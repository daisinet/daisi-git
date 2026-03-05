using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// Inline comment on a specific line in a pull request diff, stored in Cosmos DB.
/// </summary>
public class DiffComment
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(DiffComment);
    public string RepositoryId { get; set; } = "";
    public string ReviewId { get; set; } = "";
    public string PullRequestId { get; set; } = "";
    public int PullRequestNumber { get; set; }
    public string Path { get; set; } = "";
    public int Line { get; set; }
    public DiffSide Side { get; set; } = DiffSide.Right;
    public string Body { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
