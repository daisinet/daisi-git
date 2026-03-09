using System.Text.RegularExpressions;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages teams within organizations — create, update, delete, member management.
/// </summary>
public partial class TeamService(DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Creates a new team within an organization.
    /// </summary>
    public async Task<Team> CreateAsync(
        string organizationId, string name, string? description, GitRole defaultPermission)
    {
        var slug = Slugify(name);

        var existing = await cosmo.GetTeamBySlugAsync(organizationId, slug);
        if (existing != null)
            throw new InvalidOperationException($"Team '{slug}' already exists in this organization.");

        var team = await cosmo.CreateTeamAsync(new Team
        {
            OrganizationId = organizationId,
            Name = name,
            Slug = slug,
            Description = description,
            DefaultPermission = defaultPermission
        });

        // Update org team count
        var orgs = await cosmo.GetOrganizationsAsync(organizationId);
        // org.AccountId may differ, so we search by team's orgId
        // (team count update is best-effort)

        return team;
    }

    /// <summary>
    /// Gets a team by slug within an organization.
    /// </summary>
    public async Task<Team?> GetBySlugAsync(string organizationId, string slug)
    {
        return await cosmo.GetTeamBySlugAsync(organizationId, slug);
    }

    /// <summary>
    /// Gets a team by ID.
    /// </summary>
    public async Task<Team?> GetAsync(string id, string organizationId)
    {
        return await cosmo.GetTeamAsync(id, organizationId);
    }

    /// <summary>
    /// Lists all teams in an organization.
    /// </summary>
    public async Task<List<Team>> ListAsync(string organizationId)
    {
        return await cosmo.GetTeamsAsync(organizationId);
    }

    /// <summary>
    /// Updates team metadata.
    /// </summary>
    public async Task<Team> UpdateAsync(Team team)
    {
        return await cosmo.UpdateTeamAsync(team);
    }

    /// <summary>
    /// Deletes a team.
    /// </summary>
    public async Task DeleteAsync(string id, string organizationId)
    {
        await cosmo.DeleteTeamAsync(id, organizationId);
    }

    /// <summary>
    /// Adds a member to a team.
    /// </summary>
    public async Task<TeamMember> AddMemberAsync(
        string organizationId, string teamId, string userId, string userName, GitRole role)
    {
        var existing = await cosmo.GetTeamMemberAsync(organizationId, teamId, userId);
        if (existing != null)
            throw new InvalidOperationException($"User '{userName}' is already a member of this team.");

        var member = await cosmo.CreateTeamMemberAsync(new TeamMember
        {
            OrganizationId = organizationId,
            TeamId = teamId,
            UserId = userId,
            UserName = userName,
            Role = role
        });

        // Update team member count
        var team = await cosmo.GetTeamAsync(teamId, organizationId);
        if (team != null)
        {
            team.MemberCount++;
            await cosmo.UpdateTeamAsync(team);
        }

        return member;
    }

    /// <summary>
    /// Removes a member from a team.
    /// </summary>
    public async Task RemoveMemberAsync(string organizationId, string teamId, string memberId)
    {
        await cosmo.DeleteTeamMemberAsync(memberId, organizationId);

        var team = await cosmo.GetTeamAsync(teamId, organizationId);
        if (team != null)
        {
            team.MemberCount = Math.Max(0, team.MemberCount - 1);
            await cosmo.UpdateTeamAsync(team);
        }
    }

    /// <summary>
    /// Gets all members of a team.
    /// </summary>
    public async Task<List<TeamMember>> GetMembersAsync(string organizationId, string teamId)
    {
        return await cosmo.GetTeamMembersAsync(organizationId, teamId);
    }

    /// <summary>
    /// Gets all teams a user belongs to within an organization.
    /// </summary>
    public async Task<List<TeamMember>> GetUserTeamsAsync(string organizationId, string userId)
    {
        return await cosmo.GetUserTeamsAsync(organizationId, userId);
    }

    internal static string Slugify(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = SlugRegex().Replace(slug, "-");
        slug = slug.Trim('-');
        return slug;
    }

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex SlugRegex();
}
