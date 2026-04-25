using DaisiGit.Core.Enums;
using System.Text.Json.Serialization;

namespace DaisiGit.Core.Models;

/// <summary>
/// Per-account settings stored in Cosmos DB, partitioned by AccountId.
/// Controls account-level defaults such as the storage provider for new repositories.
/// </summary>
public class AccountSettings
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(AccountSettings);
    public string AccountId { get; set; } = "";

    /// <summary>
    /// Default storage provider for new repositories created under this account.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageProvider DefaultStorageProvider { get; set; } = StorageProvider.DaisiDrive;

    /// <summary>When true, PRs created in this account auto-merge as soon as they're mergeable.</summary>
    public bool AutoMergeEnabled { get; set; } = false;

    /// <summary>When true, the source branch is deleted after a successful merge (unless it's the default branch).</summary>
    public bool DeleteBranchOnMerge { get; set; } = false;

    /// <summary>When true, the merge-commit strategy is allowed for PRs.</summary>
    public bool AllowMergeCommit { get; set; } = true;

    /// <summary>When true, the squash-merge strategy is allowed for PRs.</summary>
    public bool AllowSquashMerge { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
