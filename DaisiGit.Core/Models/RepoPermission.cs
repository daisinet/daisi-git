using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// Grants a team access to a specific repository at a given role level.
/// Stored in Cosmos DB, partitioned by RepositoryId.
/// </summary>
public class RepoPermission
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(RepoPermission);
    public string RepositoryId { get; set; } = "";
    public string OrganizationId { get; set; } = "";

    /// <summary>
    /// TeamId if this is a team grant, or UserId if this is a direct user grant.
    /// </summary>
    public string GranteeId { get; set; } = "";

    /// <summary>
    /// "Team" or "User" — discriminator for the grantee type.
    /// </summary>
    public string GranteeType { get; set; } = "";

    public string GranteeName { get; set; } = "";
    public GitRole Role { get; set; } = GitRole.Read;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
