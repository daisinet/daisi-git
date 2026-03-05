using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// Membership record linking a user to a team.
/// Stored in Cosmos DB, partitioned by OrganizationId.
/// </summary>
public class TeamMember
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(TeamMember);
    public string OrganizationId { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public GitRole Role { get; set; } = GitRole.Read;
    public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;
}
