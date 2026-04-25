namespace DaisiGit.Core.Models;

/// <summary>
/// Persisted record of a single bulk-import run from GitHub. Items track per-repo
/// progress; the job as a whole is "complete" when FinishedUtc is set.
/// </summary>
public class GitHubImportJob
{
    /// <summary>Cosmos id (mirrors the in-memory key).</summary>
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(GitHubImportJob);

    /// <summary>Convenience alias for code that historically used Id.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Id { get => id; set => id = value; }

    public string DaisiOrgId { get; set; } = "";
    public string DaisiOrgSlug { get; set; } = "";
    public string GithubOrg { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedUtc { get; set; }
    public List<GitHubImportItem> Items { get; set; } = [];

    public int CompletedCount => Items.Count(i => i.Status is "Imported" or "Updated" or "Skipped" or "Failed");
    public int FailedCount => Items.Count(i => i.Status == "Failed");
    public int ImportedCount => Items.Count(i => i.Status == "Imported");
    public int UpdatedCount => Items.Count(i => i.Status == "Updated");
    public bool IsComplete => FinishedUtc.HasValue;
}

public class GitHubImportItem
{
    public string Name { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public bool IsPrivate { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Error { get; set; }
    public string? LastMessage { get; set; }
    public string? DaisiRepoSlug { get; set; }
}
