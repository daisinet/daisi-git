using DaisiGit.Core.Enums;
using System.Text.Json.Serialization;

namespace DaisiGit.Core.Models;

/// <summary>
/// Repository metadata stored in Cosmos DB.
/// </summary>
public class GitRepository
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(GitRepository);
    public string AccountId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public GitRepoVisibility Visibility { get; set; } = GitRepoVisibility.Private;
    public string DriveRepositoryId { get; set; } = "";

    /// <summary>
    /// Storage backend for this repository's git objects.
    /// Null means "use the account default" (which itself defaults to DaisiDrive).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageProvider? StorageProvider { get; set; }
    public bool IsEmpty { get; set; } = true;
    public string? ForkedFromId { get; set; }
    public string? ForkedFromOwnerName { get; set; }
    public string? ForkedFromSlug { get; set; }
    public int StarCount { get; set; }
    public int ForkCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
