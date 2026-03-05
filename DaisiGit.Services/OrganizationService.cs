using System.Text.RegularExpressions;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages organization lifecycle — create, update, delete, member management.
/// </summary>
public partial class OrganizationService(DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Creates a new organization and adds the creator as Owner.
    /// </summary>
    public async Task<GitOrganization> CreateAsync(
        string accountId, string name, string? description,
        string creatorUserId, string creatorUserName)
    {
        var slug = Slugify(name);

        var existing = await cosmo.GetOrganizationBySlugAsync(slug);
        if (existing != null)
            throw new InvalidOperationException($"Organization '{slug}' already exists.");

        var org = await cosmo.CreateOrganizationAsync(new GitOrganization
        {
            AccountId = accountId,
            Name = name,
            Slug = slug,
            Description = description,
            CreatedByUserId = creatorUserId,
            CreatedByUserName = creatorUserName,
            MemberCount = 1
        });

        // Add creator as Owner
        await cosmo.CreateOrgMemberAsync(new OrgMember
        {
            OrganizationId = org.id,
            UserId = creatorUserId,
            UserName = creatorUserName,
            Role = GitRole.Owner
        });

        return org;
    }

    /// <summary>
    /// Gets an organization by slug.
    /// </summary>
    public async Task<GitOrganization?> GetBySlugAsync(string slug)
    {
        return await cosmo.GetOrganizationBySlugAsync(slug);
    }

    /// <summary>
    /// Lists all organizations for an account.
    /// </summary>
    public async Task<List<GitOrganization>> ListAsync(string accountId)
    {
        return await cosmo.GetOrganizationsAsync(accountId);
    }

    /// <summary>
    /// Lists organizations a user belongs to.
    /// </summary>
    public async Task<List<GitOrganization>> GetUserOrganizationsAsync(string userId, string accountId)
    {
        var memberships = await cosmo.GetUserMembershipsAsync(userId);
        var orgs = new List<GitOrganization>();
        foreach (var m in memberships)
        {
            var org = await cosmo.GetOrganizationAsync(m.OrganizationId, accountId);
            if (org != null)
                orgs.Add(org);
        }
        return orgs;
    }

    /// <summary>
    /// Updates organization metadata.
    /// </summary>
    public async Task<GitOrganization> UpdateAsync(GitOrganization org)
    {
        return await cosmo.UpdateOrganizationAsync(org);
    }

    /// <summary>
    /// Adds a member to an organization.
    /// </summary>
    public async Task<OrgMember> AddMemberAsync(string organizationId, string userId, string userName, GitRole role)
    {
        var existing = await cosmo.GetOrgMemberAsync(organizationId, userId);
        if (existing != null)
            throw new InvalidOperationException($"User '{userName}' is already a member.");

        var member = await cosmo.CreateOrgMemberAsync(new OrgMember
        {
            OrganizationId = organizationId,
            UserId = userId,
            UserName = userName,
            Role = role
        });

        // Update member count
        // Note: in production, use a transaction or atomic increment
        var org = await GetOrgByIdFromMemberAsync(organizationId);
        if (org != null)
        {
            org.MemberCount++;
            await cosmo.UpdateOrganizationAsync(org);
        }

        return member;
    }

    /// <summary>
    /// Removes a member from an organization.
    /// </summary>
    public async Task RemoveMemberAsync(string organizationId, string memberId)
    {
        await cosmo.DeleteOrgMemberAsync(memberId, organizationId);

        var org = await GetOrgByIdFromMemberAsync(organizationId);
        if (org != null)
        {
            org.MemberCount = Math.Max(0, org.MemberCount - 1);
            await cosmo.UpdateOrganizationAsync(org);
        }
    }

    /// <summary>
    /// Updates a member's role.
    /// </summary>
    public async Task<OrgMember> UpdateMemberRoleAsync(OrgMember member, GitRole newRole)
    {
        member.Role = newRole;
        return await cosmo.UpdateOrgMemberAsync(member);
    }

    /// <summary>
    /// Gets all members of an organization.
    /// </summary>
    public async Task<List<OrgMember>> GetMembersAsync(string organizationId)
    {
        return await cosmo.GetOrgMembersAsync(organizationId);
    }

    /// <summary>
    /// Gets a specific member record.
    /// </summary>
    public async Task<OrgMember?> GetMemberAsync(string organizationId, string userId)
    {
        return await cosmo.GetOrgMemberAsync(organizationId, userId);
    }

    private async Task<GitOrganization?> GetOrgByIdFromMemberAsync(string organizationId)
    {
        // Cross-partition lookup by org ID
        var container = await cosmo.GetContainerAsync(DaisiGitCosmo.OrganizationsContainerName);
        var query = new Microsoft.Azure.Cosmos.QueryDefinition(
            "SELECT * FROM c WHERE c.id = @id AND c.Type = 'GitOrganization'")
            .WithParameter("@id", organizationId);

        using var iterator = container.GetItemQueryIterator<GitOrganization>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
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
