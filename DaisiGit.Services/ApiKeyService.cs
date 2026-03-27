using System.Security.Cryptography;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages personal access tokens for API/CLI authentication.
/// Tokens are hashed with SHA-256 — the raw token is only returned once at creation.
/// Token format: dg_XXXXXXXX (32 random chars with "dg_" prefix).
/// </summary>
public class ApiKeyService(DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Creates a new API key. Returns the raw token (only shown once).
    /// </summary>
    public async Task<(ApiKey Key, string RawToken)> CreateKeyAsync(
        string accountId, string userId, string userName, string name)
    {
        var rawToken = GenerateToken();
        var hash = HashToken(rawToken);

        var key = await cosmo.CreateApiKeyAsync(new ApiKey
        {
            AccountId = accountId,
            UserId = userId,
            UserName = userName,
            Name = name,
            TokenHash = hash,
            TokenPrefix = rawToken[..7]
        });

        return (key, rawToken);
    }

    /// <summary>
    /// Validates a raw token and returns the associated API key, or null if invalid.
    /// </summary>
    public async Task<ApiKey?> ValidateTokenAsync(string rawToken)
    {
        if (string.IsNullOrEmpty(rawToken) || !rawToken.StartsWith("dg_"))
            return null;

        var hash = HashToken(rawToken);
        var key = await cosmo.GetApiKeyByHashAsync(hash);

        if (key == null || key.IsRevoked)
            return null;

        if (key.ExpiresUtc.HasValue && key.ExpiresUtc < DateTime.UtcNow)
            return null;

        // Update last used
        key.LastUsedUtc = DateTime.UtcNow;
        await cosmo.UpdateApiKeyAsync(key);

        return key;
    }

    /// <summary>
    /// Lists all active (non-revoked) keys for a user.
    /// </summary>
    public async Task<List<ApiKey>> ListKeysAsync(string accountId, string userId)
    {
        return await cosmo.GetApiKeysAsync(accountId, userId);
    }

    /// <summary>
    /// Revokes an API key.
    /// </summary>
    public async Task RevokeKeyAsync(string keyId, string accountId)
    {
        var keys = await cosmo.GetApiKeysAsync(accountId, "");
        var key = keys.FirstOrDefault(k => k.id == keyId);
        if (key != null)
        {
            key.IsRevoked = true;
            await cosmo.UpdateApiKeyAsync(key);
        }
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return "dg_" + Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "")[..29];
    }

    private static string HashToken(string token)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
