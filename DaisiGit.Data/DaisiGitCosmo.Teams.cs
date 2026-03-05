using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string TeamIdPrefix = "team";
    public const string TeamsContainerName = "Teams";
    public const string TeamsPartitionKeyName = nameof(Team.OrganizationId);

    public PartitionKey GetTeamPartitionKey(string organizationId) => new(organizationId);

    public virtual async Task<Team> CreateTeamAsync(Team team)
    {
        if (string.IsNullOrEmpty(team.id))
            team.id = GenerateId(TeamIdPrefix);
        team.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(TeamsContainerName);
        var response = await container.CreateItemAsync(team, GetTeamPartitionKey(team.OrganizationId));
        return response.Resource;
    }

    public virtual async Task<Team?> GetTeamAsync(string id, string organizationId)
    {
        try
        {
            var container = await GetContainerAsync(TeamsContainerName);
            var response = await container.ReadItemAsync<Team>(id, GetTeamPartitionKey(organizationId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public virtual async Task<Team?> GetTeamBySlugAsync(string organizationId, string slug)
    {
        var container = await GetContainerAsync(TeamsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OrganizationId = @orgId AND c.Slug = @slug AND c.Type = 'Team'")
            .WithParameter("@orgId", organizationId)
            .WithParameter("@slug", slug);

        using var iterator = container.GetItemQueryIterator<Team>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetTeamPartitionKey(organizationId)
        });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<List<Team>> GetTeamsAsync(string organizationId)
    {
        var container = await GetContainerAsync(TeamsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OrganizationId = @orgId AND c.Type = 'Team' ORDER BY c.Name ASC")
            .WithParameter("@orgId", organizationId);

        var results = new List<Team>();
        using var iterator = container.GetItemQueryIterator<Team>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetTeamPartitionKey(organizationId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<Team> UpdateTeamAsync(Team team)
    {
        team.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(TeamsContainerName);
        var response = await container.UpsertItemAsync(team, GetTeamPartitionKey(team.OrganizationId));
        return response.Resource;
    }

    public virtual async Task DeleteTeamAsync(string id, string organizationId)
    {
        var container = await GetContainerAsync(TeamsContainerName);
        await container.DeleteItemAsync<Team>(id, GetTeamPartitionKey(organizationId));
    }
}
