namespace DaisiGit.Core.Models;

/// <summary>
/// Represents a user starring a repository. Stored in Cosmos DB (container: Stars, partition: RepositoryId).
/// </summary>
public class RepoStar
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(RepoStar);
    public string RepositoryId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
