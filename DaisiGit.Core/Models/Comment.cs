namespace DaisiGit.Core.Models;

/// <summary>
/// Comment on a pull request or issue, stored in Cosmos DB.
/// </summary>
public class Comment
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(Comment);
    public string RepositoryId { get; set; } = "";

    /// <summary>
    /// The ID of the parent PR or Issue this comment belongs to.
    /// </summary>
    public string ParentId { get; set; } = "";

    /// <summary>
    /// "PullRequest" or "Issue" — discriminator for the parent type.
    /// </summary>
    public string ParentType { get; set; } = "";

    public string Body { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
