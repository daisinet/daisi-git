using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// Code review on a pull request, stored in Cosmos DB.
/// </summary>
public class Review
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(Review);
    public string RepositoryId { get; set; } = "";
    public string PullRequestId { get; set; } = "";
    public int PullRequestNumber { get; set; }
    public ReviewState State { get; set; } = ReviewState.Commented;
    public string? Body { get; set; }
    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
