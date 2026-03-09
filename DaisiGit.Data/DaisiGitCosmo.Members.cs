using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string MemberIdPrefix = "mbr";
    public const string MembersContainerName = "Members";
    public const string MembersPartitionKeyName = nameof(OrgMember.OrganizationId);

    public PartitionKey GetMemberPartitionKey(string organizationId) => new(organizationId);

    // ── Org Members ──

    public virtual async Task<OrgMember> CreateOrgMemberAsync(OrgMember member)
    {
        if (string.IsNullOrEmpty(member.id))
            member.id = GenerateId(MemberIdPrefix);
        member.JoinedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(MembersContainerName);
        var response = await container.CreateItemAsync(member, GetMemberPartitionKey(member.OrganizationId));
        return response.Resource;
    }

    public virtual async Task<OrgMember?> GetOrgMemberAsync(string organizationId, string userId)
    {
        var container = await GetContainerAsync(MembersContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OrganizationId = @orgId AND c.UserId = @userId AND c.Type = 'OrgMember'")
            .WithParameter("@orgId", organizationId)
            .WithParameter("@userId", userId);

        using var iterator = container.GetItemQueryIterator<OrgMember>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetMemberPartitionKey(organizationId)
        });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<List<OrgMember>> GetOrgMembersAsync(string organizationId)
    {
        var container = await GetContainerAsync(MembersContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OrganizationId = @orgId AND c.Type = 'OrgMember' ORDER BY c.UserName ASC")
            .WithParameter("@orgId", organizationId);

        var results = new List<OrgMember>();
        using var iterator = container.GetItemQueryIterator<OrgMember>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetMemberPartitionKey(organizationId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    /// <summary>
    /// Gets all organizations a user belongs to (cross-partition query).
    /// </summary>
    public virtual async Task<List<OrgMember>> GetUserMembershipsAsync(string userId)
    {
        var container = await GetContainerAsync(MembersContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.UserId = @userId AND c.Type = 'OrgMember'")
            .WithParameter("@userId", userId);

        var results = new List<OrgMember>();
        using var iterator = container.GetItemQueryIterator<OrgMember>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<OrgMember> UpdateOrgMemberAsync(OrgMember member)
    {
        var container = await GetContainerAsync(MembersContainerName);
        var response = await container.UpsertItemAsync(member, GetMemberPartitionKey(member.OrganizationId));
        return response.Resource;
    }

    public virtual async Task DeleteOrgMemberAsync(string id, string organizationId)
    {
        var container = await GetContainerAsync(MembersContainerName);
        await container.DeleteItemAsync<OrgMember>(id, GetMemberPartitionKey(organizationId));
    }

    // ── Team Members ──

    public virtual async Task<TeamMember> CreateTeamMemberAsync(TeamMember member)
    {
        if (string.IsNullOrEmpty(member.id))
            member.id = GenerateId(MemberIdPrefix);
        member.JoinedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(MembersContainerName);
        var response = await container.CreateItemAsync(member, GetMemberPartitionKey(member.OrganizationId));
        return response.Resource;
    }

    public virtual async Task<TeamMember?> GetTeamMemberAsync(string organizationId, string teamId, string userId)
    {
        var container = await GetContainerAsync(MembersContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OrganizationId = @orgId AND c.TeamId = @teamId AND c.UserId = @userId AND c.Type = 'TeamMember'")
            .WithParameter("@orgId", organizationId)
            .WithParameter("@teamId", teamId)
            .WithParameter("@userId", userId);

        using var iterator = container.GetItemQueryIterator<TeamMember>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetMemberPartitionKey(organizationId)
        });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<List<TeamMember>> GetTeamMembersAsync(string organizationId, string teamId)
    {
        var container = await GetContainerAsync(MembersContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OrganizationId = @orgId AND c.TeamId = @teamId AND c.Type = 'TeamMember' ORDER BY c.UserName ASC")
            .WithParameter("@orgId", organizationId)
            .WithParameter("@teamId", teamId);

        var results = new List<TeamMember>();
        using var iterator = container.GetItemQueryIterator<TeamMember>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetMemberPartitionKey(organizationId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    /// <summary>
    /// Gets all teams a user belongs to within an organization.
    /// </summary>
    public virtual async Task<List<TeamMember>> GetUserTeamsAsync(string organizationId, string userId)
    {
        var container = await GetContainerAsync(MembersContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OrganizationId = @orgId AND c.UserId = @userId AND c.Type = 'TeamMember'")
            .WithParameter("@orgId", organizationId)
            .WithParameter("@userId", userId);

        var results = new List<TeamMember>();
        using var iterator = container.GetItemQueryIterator<TeamMember>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetMemberPartitionKey(organizationId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task DeleteTeamMemberAsync(string id, string organizationId)
    {
        var container = await GetContainerAsync(MembersContainerName);
        await container.DeleteItemAsync<TeamMember>(id, GetMemberPartitionKey(organizationId));
    }
}
