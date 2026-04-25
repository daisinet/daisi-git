using Daisi.Protos.V1;
using Daisi.SDK.Clients.V1.Orc;
using Daisi.SDK.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DaisiGit.Services;

/// <summary>
/// Cached lookup of enabled text-generation models available on the configured ORC.
/// Used to populate the run-minion step editor's model dropdown.
///
/// Registered as singleton (cache persists across requests). Takes <see cref="IServiceScopeFactory"/>
/// to resolve the scoped <see cref="SecretService"/> during a refresh. The underlying
/// <see cref="DaisiStaticSettings"/> is process-global, so this class assumes a single ORC endpoint
/// per deployment. A mutex serializes SECRET→CLIENT exchanges so concurrent first-hit requests don't race.
/// </summary>
public sealed class DaisinetModelCatalog(IServiceScopeFactory scopeFactory)
{
    private const string SystemSecretName = "DAISIGIT_WORKERS_SECRET_KEY";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private List<DaisinetModelInfo>? _cached;
    private DateTime _cachedAtUtc;

    public async Task<IReadOnlyList<DaisinetModelInfo>> GetTextGenModelsAsync(CancellationToken ct)
    {
        if (_cached != null && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
            return _cached;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_cached != null && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
                return _cached;

            using var scope = scopeFactory.CreateScope();
            var secretService = scope.ServiceProvider.GetRequiredService<SecretService>();
            var secret = await secretService.ResolveSystemSecretAsync(SystemSecretName);
            if (string.IsNullOrEmpty(secret))
                throw new InvalidOperationException(
                    $"System secret '{SystemSecretName}' is not configured on this deployment.");

            DaisiStaticSettings.SecretKey = secret;
            new AuthClientFactory().CreateStaticClientKey();

            var modelClient = new ModelClientFactory().Create();
            var response = await modelClient
                .GetRequiredModelsAsync(new GetRequiredModelsRequest())
                .ResponseAsync;

            _cached = response.Models
                .Where(m => m.Enabled && m.Type == AIModelTypes.TextGeneration)
                .Select(m => new DaisinetModelInfo(
                    Name: m.Name,
                    IsDefault: m.IsDefault,
                    HasReasoning: m.HasReasoning,
                    ThinkLevels: m.ThinkLevels.Select(t => t.ToString()).ToArray()))
                .OrderByDescending(m => m.IsDefault)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Clear the cache. Useful after an admin changes the system secret or ORC endpoint.</summary>
    public void Invalidate()
    {
        _cached = null;
        _cachedAtUtc = DateTime.MinValue;
    }
}

public sealed record DaisinetModelInfo(string Name, bool IsDefault, bool HasReasoning, string[] ThinkLevels);
