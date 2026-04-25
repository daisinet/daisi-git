using DaisiGit.Core.Enums;
using System.Text.Json.Serialization;

namespace DaisiGit.Core.Models;

/// <summary>
/// Repository metadata stored in Cosmos DB.
/// </summary>
public class GitRepository
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(GitRepository);
    public string AccountId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public GitRepoVisibility Visibility { get; set; } = GitRepoVisibility.Private;
    public string DriveRepositoryId { get; set; } = "";

    /// <summary>
    /// Storage backend for this repository's git objects.
    /// Null means "use the account default" (which itself defaults to DaisiDrive).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageProvider? StorageProvider { get; set; }
    public bool IsEmpty { get; set; } = true;
    /// <summary>
    /// The external URL this repository was originally imported from, if any.
    /// Used to detect re-imports and offer to pull latest changes.
    /// </summary>
    public string? ImportedFromUrl { get; set; }
    public string? ForkedFromId { get; set; }
    public string? ForkedFromOwnerName { get; set; }
    public string? ForkedFromSlug { get; set; }
    public int StarCount { get; set; }
    public int ForkCount { get; set; }

    /// <summary>
    /// Pre-computed commit counts per UTC date ("yyyy-MM-dd"). Maintained incrementally on
    /// push/merge so the org activity view doesn't have to walk full commit history on every
    /// load. Empty for legacy repos until backfilled on demand.
    /// </summary>
    public Dictionary<string, int> CommitCountsByDate { get; set; } = new();

    /// <summary>UTC date the rollup was last reconciled (used to detect needs-backfill).</summary>
    public DateTime? CommitRollupBackfilledUtc { get; set; }

    /// <summary>Per-repo override for the account's auto-merge default. Null = inherit.</summary>
    public bool? AutoMergeEnabled { get; set; }

    /// <summary>Per-repo override for the account's delete-branch-on-merge default. Null = inherit.</summary>
    public bool? DeleteBranchOnMerge { get; set; }

    /// <summary>Per-repo override for the account's allow-merge-commit default. Null = inherit.</summary>
    public bool? AllowMergeCommit { get; set; }

    /// <summary>Per-repo override for the account's allow-squash-merge default. Null = inherit.</summary>
    public bool? AllowSquashMerge { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
