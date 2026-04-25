using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string RepoGroupIdPrefix = "rgrp";
    public const string RepoGroupsContainerName = "RepoGroups";
    public const string RepoGroupsPartitionKeyName = nameof(RepoGroup.OrganizationId);

    public PartitionKey GetRepoGroupPartitionKey(string organizationId) => new(organizationId);

    public virtual async Task<RepoGroup> CreateRepoGroupAsync(RepoGroup group)
    {
        if (string.IsNullOrEmpty(group.id))
            group.id = GenerateId(RepoGroupIdPrefix);
        group.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(RepoGroupsContainerName);
        var response = await container.CreateItemAsync(group, GetRepoGroupPartitionKey(group.OrganizationId));
        return response.Resource;
    }

    public virtual async Task<RepoGroup?> GetRepoGroupAsync(string id, string organizationId)
    {
        try
        {
            var container = await GetContainerAsync(RepoGroupsContainerName);
            var response = await container.ReadItemAsync<RepoGroup>(id, GetRepoGroupPartitionKey(organizationId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public virtual async Task<RepoGroup?> GetRepoGroupBySlugAsync(string organizationId, string slug)
    {
        var container = await GetContainerAsync(RepoGroupsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OrganizationId = @orgId AND c.Slug = @slug AND c.Type = 'RepoGroup'")
            .WithParameter("@orgId", organizationId)
            .WithParameter("@slug", slug);

        using var iterator = container.GetItemQueryIterator<RepoGroup>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetRepoGroupPartitionKey(organizationId)
        });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<List<RepoGroup>> GetRepoGroupsAsync(string organizationId)
    {
        var container = await GetContainerAsync(RepoGroupsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OrganizationId = @orgId AND c.Type = 'RepoGroup' ORDER BY c.SortOrder ASC")
            .WithParameter("@orgId", organizationId);

        var results = new List<RepoGroup>();
        using var iterator = container.GetItemQueryIterator<RepoGroup>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetRepoGroupPartitionKey(organizationId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<RepoGroup> UpdateRepoGroupAsync(RepoGroup group)
    {
        group.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(RepoGroupsContainerName);
        var response = await container.UpsertItemAsync(group, GetRepoGroupPartitionKey(group.OrganizationId));
        return response.Resource;
    }

    public virtual async Task DeleteRepoGroupAsync(string id, string organizationId)
    {
        var container = await GetContainerAsync(RepoGroupsContainerName);
        await container.DeleteItemAsync<RepoGroup>(id, GetRepoGroupPartitionKey(organizationId));
    }
}
