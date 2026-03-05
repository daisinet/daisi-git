using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// Team within an organization. Teams have a default permission level
/// and can be granted access to specific repositories.
/// Stored in Cosmos DB, partitioned by OrganizationId.
/// </summary>
public class Team
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(Team);
    public string OrganizationId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public GitRole DefaultPermission { get; set; } = GitRole.Read;
    public int MemberCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
