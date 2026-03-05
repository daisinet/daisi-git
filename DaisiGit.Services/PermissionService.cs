using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Evaluates permissions for a user on a repository.
/// Permission resolution order:
/// 1. Repo owner always has Admin
/// 2. Org owner/admin has Admin on org repos
/// 3. Direct user grants on the repo
/// 4. Team grants on the repo (highest role wins)
/// 5. Public repos grant Read to everyone
/// </summary>
public class PermissionService(DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Gets the effective permission level for a user on a repository.
    /// </summary>
    public async Task<GitPermissionLevel> GetEffectivePermissionAsync(
        string userId, GitRepository repo)
    {
        // 1. Repo owner always has full access
        if (repo.OwnerId == userId)
            return GitPermissionLevel.Admin;

        // 2. Check if repo belongs to an org and user is org admin/owner
        var orgPermission = await CheckOrgPermissionAsync(userId, repo);
        if (orgPermission >= GitPermissionLevel.Admin)
            return orgPermission;

        // 3. Check direct user grants on the repo
        var directGrant = await cosmo.GetPermissionAsync(repo.id, userId, "User");
        if (directGrant != null)
        {
            var directLevel = RoleToPermissionLevel(directGrant.Role);
            if (directLevel > orgPermission)
                orgPermission = directLevel;
        }

        // 4. Check team grants — find all teams the user belongs to that have access
        var teamLevel = await CheckTeamPermissionsAsync(userId, repo);
        if (teamLevel > orgPermission)
            orgPermission = teamLevel;

        // 5. Public repos grant Read to everyone
        if (orgPermission == GitPermissionLevel.None && repo.Visibility == GitRepoVisibility.Public)
            return GitPermissionLevel.Read;

        return orgPermission;
    }

    /// <summary>
    /// Checks if a user can read a repository.
    /// </summary>
    public async Task<bool> CanReadAsync(string userId, GitRepository repo)
    {
        var level = await GetEffectivePermissionAsync(userId, repo);
        return level >= GitPermissionLevel.Read;
    }

    /// <summary>
    /// Checks if a user can write (push) to a repository.
    /// </summary>
    public async Task<bool> CanWriteAsync(string userId, GitRepository repo)
    {
        var level = await GetEffectivePermissionAsync(userId, repo);
        return level >= GitPermissionLevel.Write;
    }

    /// <summary>
    /// Checks if a user has admin access to a repository.
    /// </summary>
    public async Task<bool> CanAdminAsync(string userId, GitRepository repo)
    {
        var level = await GetEffectivePermissionAsync(userId, repo);
        return level >= GitPermissionLevel.Admin;
    }

    /// <summary>
    /// Grants a team or user access to a repository.
    /// </summary>
    public async Task<RepoPermission> GrantAccessAsync(
        string repositoryId, string organizationId,
        string granteeId, string granteeType, string granteeName, GitRole role)
    {
        // Upsert — if already exists, update the role
        var existing = await cosmo.GetPermissionAsync(repositoryId, granteeId, granteeType);
        if (existing != null)
        {
            existing.Role = role;
            return await cosmo.UpsertPermissionAsync(existing);
        }

        return await cosmo.CreatePermissionAsync(new RepoPermission
        {
            RepositoryId = repositoryId,
            OrganizationId = organizationId,
            GranteeId = granteeId,
            GranteeType = granteeType,
            GranteeName = granteeName,
            Role = role
        });
    }

    /// <summary>
    /// Revokes access from a team or user on a repository.
    /// </summary>
    public async Task RevokeAccessAsync(string repositoryId, string granteeId, string granteeType)
    {
        var permission = await cosmo.GetPermissionAsync(repositoryId, granteeId, granteeType);
        if (permission != null)
            await cosmo.DeletePermissionAsync(permission.id, repositoryId);
    }

    /// <summary>
    /// Lists all permission grants on a repository.
    /// </summary>
    public async Task<List<RepoPermission>> GetRepoPermissionsAsync(string repositoryId)
    {
        return await cosmo.GetPermissionsForRepoAsync(repositoryId);
    }

    private async Task<GitPermissionLevel> CheckOrgPermissionAsync(string userId, GitRepository repo)
    {
        // Check if the repo's owner name corresponds to an organization
        var org = await cosmo.GetOrganizationBySlugAsync(repo.OwnerName);
        if (org == null)
            return GitPermissionLevel.None;

        var member = await cosmo.GetOrgMemberAsync(org.id, userId);
        if (member == null)
            return GitPermissionLevel.None;

        // Org Owner and Admin get full admin on all org repos
        if (member.Role >= GitRole.Admin)
            return GitPermissionLevel.Admin;

        // Other org members get at least Read on org repos
        return GitPermissionLevel.Read;
    }

    private async Task<GitPermissionLevel> CheckTeamPermissionsAsync(string userId, GitRepository repo)
    {
        // Get all permission grants on this repo
        var permissions = await cosmo.GetPermissionsForRepoAsync(repo.id);
        var teamPermissions = permissions.Where(p => p.GranteeType == "Team").ToList();

        if (teamPermissions.Count == 0)
            return GitPermissionLevel.None;

        var highestLevel = GitPermissionLevel.None;

        foreach (var teamPerm in teamPermissions)
        {
            // Check if user is a member of this team
            var teamMember = await cosmo.GetTeamMemberAsync(
                teamPerm.OrganizationId, teamPerm.GranteeId, userId);

            if (teamMember != null)
            {
                var level = RoleToPermissionLevel(teamPerm.Role);
                if (level > highestLevel)
                    highestLevel = level;
            }
        }

        return highestLevel;
    }

    private static GitPermissionLevel RoleToPermissionLevel(GitRole role) => role switch
    {
        GitRole.Read => GitPermissionLevel.Read,
        GitRole.Write => GitPermissionLevel.Write,
        GitRole.Maintain => GitPermissionLevel.Admin,
        GitRole.Admin => GitPermissionLevel.Admin,
        GitRole.Owner => GitPermissionLevel.Admin,
        _ => GitPermissionLevel.None
    };
}
