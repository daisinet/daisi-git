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
        var settings = await GetOrCreate(accountId);
        settings.DefaultStorageProvider = provider;
        return await cosmo.UpsertAccountSettingsAsync(settings);
    }

    /// <summary>
    /// Updates the account-level merge policy defaults.
    /// </summary>
    public async Task<AccountSettings> SetMergePolicyAsync(
        string accountId, bool autoMerge, bool deleteBranchOnMerge,
        bool allowMergeCommit, bool allowSquash)
    {
        var settings = await GetOrCreate(accountId);
        settings.AutoMergeEnabled = autoMerge;
        settings.DeleteBranchOnMerge = deleteBranchOnMerge;
        settings.AllowMergeCommit = allowMergeCommit;
        settings.AllowSquashMerge = allowSquash;
        return await cosmo.UpsertAccountSettingsAsync(settings);
    }

    /// <summary>
    /// Resolves the effective merge policy for a repository: repo-level overrides
    /// fall back to account-level defaults, which fall back to built-in defaults.
    /// </summary>
    public async Task<MergePolicy> GetMergePolicyAsync(GitRepository repo)
    {
        var account = await cosmo.GetAccountSettingsAsync(repo.AccountId);
        return new MergePolicy(
            AutoMergeEnabled:     repo.AutoMergeEnabled     ?? account?.AutoMergeEnabled     ?? false,
            DeleteBranchOnMerge:  repo.DeleteBranchOnMerge  ?? account?.DeleteBranchOnMerge  ?? false,
            AllowMergeCommit:     repo.AllowMergeCommit     ?? account?.AllowMergeCommit     ?? true,
            AllowSquashMerge:     repo.AllowSquashMerge     ?? account?.AllowSquashMerge     ?? true);
    }

    private async Task<AccountSettings> GetOrCreate(string accountId)
    {
        return await cosmo.GetAccountSettingsAsync(accountId)
            ?? new AccountSettings { id = $"settings-{accountId}", AccountId = accountId };
    }
}

/// <summary>Effective merge policy for a repository, after resolving repo-over-account-over-default.</summary>
public record MergePolicy(
    bool AutoMergeEnabled,
    bool DeleteBranchOnMerge,
    bool AllowMergeCommit,
    bool AllowSquashMerge);
