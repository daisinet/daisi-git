using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Resolves the correct <see cref="IStorageAdapter"/> for a given repository,
/// based on the repo's StorageProvider override or the account-level default.
/// </summary>
public class StorageAdapterFactory(
    IEnumerable<IStorageAdapter> adapters,
    DaisiGitCosmo cosmo)
{
    private readonly Dictionary<StorageProvider, IStorageAdapter> _adapters =
        adapters.ToDictionary(a => a.ProviderType);

    /// <summary>
    /// Gets the storage adapter for a specific repository.
    /// Uses the repo's StorageProvider if set, otherwise falls back to the account default.
    /// </summary>
    public async Task<IStorageAdapter> GetAdapterAsync(GitRepository repo)
    {
        var provider = repo.StorageProvider
            ?? await GetAccountDefaultAsync(repo.AccountId);
        return GetAdapter(provider);
    }

    /// <summary>
    /// Gets the storage adapter for a given provider enum value.
    /// </summary>
    public IStorageAdapter GetAdapter(StorageProvider provider)
    {
        if (_adapters.TryGetValue(provider, out var adapter))
            return adapter;
        return _adapters[StorageProvider.DaisiDrive];
    }

    /// <summary>
    /// Gets the account-level default storage provider.
    /// Returns DaisiDrive if no account settings exist.
    /// </summary>
    public async Task<StorageProvider> GetAccountDefaultAsync(string accountId)
    {
        var settings = await cosmo.GetAccountSettingsAsync(accountId);
        return settings?.DefaultStorageProvider ?? StorageProvider.DaisiDrive;
    }
}
