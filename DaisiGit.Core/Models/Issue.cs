using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// Issue stored in Cosmos DB.
/// </summary>
public class Issue
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(Issue);
    public string RepositoryId { get; set; } = "";
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public IssueStatus Status { get; set; } = IssueStatus.Open;
    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public int CommentCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
    public DateTime? ClosedUtc { get; set; }
    public List<string> Labels { get; set; } = [];
}
