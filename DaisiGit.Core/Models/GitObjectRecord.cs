using DaisiGit.Core.Enums;
using System.Text.Json.Serialization;

namespace DaisiGit.Core.Models;

/// <summary>
/// Maps a git object SHA to its storage file/blob ID.
/// Stored in Cosmos DB (container: GitObjects, partition: RepositoryId).
/// </summary>
public class GitObjectRecord
{
    /// <summary>The SHA-1 hash of the git object (also used as Cosmos doc id).</summary>
    [JsonPropertyName("id")]
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(GitObjectRecord);
    public string RepositoryId { get; set; } = "";
    public string Sha { get; set; } = "";
    public string DriveFileId { get; set; } = "";
    public string ObjectType { get; set; } = ""; // "blob", "tree", "commit", "tag"
    public long SizeBytes { get; set; }

    /// <summary>
    /// Which storage backend holds this object's data.
    /// Defaults to DaisiDrive for backward compatibility with existing records.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageProvider StorageProvider { get; set; } = StorageProvider.DaisiDrive;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
