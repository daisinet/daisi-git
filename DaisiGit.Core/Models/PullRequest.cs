using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// Pull request stored in Cosmos DB.
/// </summary>
public class PullRequest
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(PullRequest);
    public string RepositoryId { get; set; } = "";
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public PullRequestStatus Status { get; set; } = PullRequestStatus.Open;
    public string SourceBranch { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public string? MergeCommitSha { get; set; }
    public MergeStrategy? MergeStrategy { get; set; }
    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public int CommentCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
    public DateTime? MergedUtc { get; set; }
    public DateTime? ClosedUtc { get; set; }
    public List<string> Labels { get; set; } = [];
}
