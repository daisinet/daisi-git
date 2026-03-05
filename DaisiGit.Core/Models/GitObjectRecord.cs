namespace DaisiGit.Core.Models;

/// <summary>
/// Maps a git object SHA to its Drive file ID.
/// Stored in Cosmos DB (container: GitObjects, partition: RepositoryId).
/// </summary>
public class GitObjectRecord
{
    /// <summary>The SHA-1 hash of the git object (also used as Cosmos doc id).</summary>
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(GitObjectRecord);
    public string RepositoryId { get; set; } = "";
    public string DriveFileId { get; set; } = "";
    public string ObjectType { get; set; } = ""; // "blob", "tree", "commit", "tag"
    public long SizeBytes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
