using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string OrganizationIdPrefix = "org";
    public const string OrganizationsContainerName = "Organizations";
    public const string OrganizationsPartitionKeyName = nameof(GitOrganization.AccountId);

    public PartitionKey GetOrganizationPartitionKey(string accountId) => new(accountId);

    public virtual async Task<GitOrganization> CreateOrganizationAsync(GitOrganization org)
    {
        if (string.IsNullOrEmpty(org.id))
            org.id = GenerateId(OrganizationIdPrefix);
        org.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(OrganizationsContainerName);
        var response = await container.CreateItemAsync(org, GetOrganizationPartitionKey(org.AccountId));
        return response.Resource;
    }

    public virtual async Task<GitOrganization?> GetOrganizationAsync(string id, string accountId)
    {
        try
        {
            var container = await GetContainerAsync(OrganizationsContainerName);
            var response = await container.ReadItemAsync<GitOrganization>(id, GetOrganizationPartitionKey(accountId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public virtual async Task<GitOrganization?> GetOrganizationBySlugAsync(string slug)
    {
        var container = await GetContainerAsync(OrganizationsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.Slug = @slug AND c.Type = 'GitOrganization'")
            .WithParameter("@slug", slug);

        using var iterator = container.GetItemQueryIterator<GitOrganization>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<List<GitOrganization>> GetOrganizationsAsync(string accountId)
    {
        var container = await GetContainerAsync(OrganizationsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.AccountId = @accountId AND c.Type = 'GitOrganization' ORDER BY c.Name ASC")
            .WithParameter("@accountId", accountId);

        var results = new List<GitOrganization>();
        using var iterator = container.GetItemQueryIterator<GitOrganization>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetOrganizationPartitionKey(accountId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<GitOrganization> UpdateOrganizationAsync(GitOrganization org)
    {
        org.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(OrganizationsContainerName);
        var response = await container.UpsertItemAsync(org, GetOrganizationPartitionKey(org.AccountId));
        return response.Resource;
    }

    public virtual async Task DeleteOrganizationAsync(string id, string accountId)
    {
        var container = await GetContainerAsync(OrganizationsContainerName);
        await container.DeleteItemAsync<GitOrganization>(id, GetOrganizationPartitionKey(accountId));
    }
}
