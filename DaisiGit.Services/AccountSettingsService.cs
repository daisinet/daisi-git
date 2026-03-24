using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages per-account settings like default storage provider.
/// </summary>
public class AccountSettingsService(DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Gets account settings. Returns defaults if none exist.
    /// </summary>
    public async Task<AccountSettings> GetSettingsAsync(string accountId)
    {
        var settings = await cosmo.GetAccountSettingsAsync(accountId);
        return settings ?? new AccountSettings
        {
            id = $"settings-{accountId}",
            AccountId = accountId
        };
    }

    /// <summary>
    /// Updates the default storage provider for an account.
    /// </summary>
    public async Task<AccountSettings> SetDefaultStorageProviderAsync(string accountId, StorageProvider provider)
    {
        var settings = await cosmo.GetAccountSettingsAsync(accountId);
        if (settings == null)
        {
            settings = new AccountSettings
            {
                id = $"settings-{accountId}",
                AccountId = accountId
            };
        }

        settings.DefaultStorageProvider = provider;
        return await cosmo.UpsertAccountSettingsAsync(settings);
    }
}
