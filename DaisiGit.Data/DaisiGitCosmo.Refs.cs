using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string RefsContainerName = "Refs";
    public const string RefsPartitionKeyName = nameof(GitRef.RepositoryId);

    public PartitionKey GetRefPartitionKey(string repositoryId) => new(repositoryId);

    private static string MakeRefId(string repositoryId, string refName) => $"{repositoryId}:{refName}";

    public virtual async Task<GitRef> UpsertRefAsync(GitRef gitRef)
    {
        gitRef.id = MakeRefId(gitRef.RepositoryId, gitRef.Name);
        gitRef.UpdatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(RefsContainerName);
        var response = await container.UpsertItemAsync(gitRef, GetRefPartitionKey(gitRef.RepositoryId));
        return response.Resource;
    }

    public virtual async Task<GitRef?> GetRefAsync(string repositoryId, string refName)
    {
        try
        {
            var container = await GetContainerAsync(RefsContainerName);
            var id = MakeRefId(repositoryId, refName);
            var response = await container.ReadItemAsync<GitRef>(id, GetRefPartitionKey(repositoryId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public virtual async Task<List<GitRef>> GetAllRefsAsync(string repositoryId)
    {
        var container = await GetContainerAsync(RefsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.Type = 'GitRef'")
            .WithParameter("@repoId", repositoryId);

        var results = new List<GitRef>();
        using var iterator = container.GetItemQueryIterator<GitRef>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetRefPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task DeleteRefAsync(string repositoryId, string refName)
    {
        try
        {
            var container = await GetContainerAsync(RefsContainerName);
            var id = MakeRefId(repositoryId, refName);
            await container.DeleteItemAsync<GitRef>(id, GetRefPartitionKey(repositoryId));
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone
        }
    }
}
