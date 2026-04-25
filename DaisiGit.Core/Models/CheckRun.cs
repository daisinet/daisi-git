namespace DaisiGit.Core.Models;

/// <summary>
/// A status-check posted by a workflow run against a specific commit. Surfaced on PRs
/// and commits so reviewers can see whether automation has passed before merging.
/// </summary>
public class CheckRun
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(CheckRun);

    /// <summary>Cosmos partition.</summary>
    public string RepositoryId { get; set; } = "";

    /// <summary>Commit SHA the check applies to. For PR-triggered runs, this is the head SHA.</summary>
    public string HeadSha { get; set; } = "";

    /// <summary>If this check was raised on a PR, the PR number; otherwise 0.</summary>
    public int PullRequestNumber { get; set; }

    /// <summary>Display name (typically the workflow name).</summary>
    public string Name { get; set; } = "";

    /// <summary>"queued" | "in_progress" | "completed".</summary>
    public string Status { get; set; } = "queued";

    /// <summary>Set when Status=="completed". "success" | "failure" | "cancelled" | "skipped".</summary>
    public string? Conclusion { get; set; }

    /// <summary>Workflow execution that produced this check, for deep-linking.</summary>
    public string? ExecutionId { get; set; }
    public string? WorkflowId { get; set; }

    /// <summary>Optional summary line shown next to the check on PR pages.</summary>
    public string? Summary { get; set; }

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
}
