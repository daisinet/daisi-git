using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// Membership record linking a user to an organization.
/// Stored in Cosmos DB, partitioned by OrganizationId.
/// </summary>
public class OrgMember
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(OrgMember);
    public string OrganizationId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public GitRole Role { get; set; } = GitRole.Read;
    public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;
}
