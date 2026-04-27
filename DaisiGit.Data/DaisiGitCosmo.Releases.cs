using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string ReleasesContainerName = "Releases";
    public const string ReleasesPartitionKeyName = nameof(Release.RepositoryId);

    public PartitionKey GetReleasePartitionKey(string repositoryId) => new(repositoryId);

    public virtual async Task<Release> UpsertReleaseAsync(Release release)
    {
        if (string.IsNullOrEmpty(release.id)) release.id = GenerateId("rel");
        release.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(ReleasesContainerName);
        var response = await container.UpsertItemAsync(release, GetReleasePartitionKey(release.RepositoryId));
        return response.Resource;
    }

    public virtual async Task<Release?> GetReleaseAsync(string id, string repositoryId)
    {
        try
        {
            var container = await GetContainerAsync(ReleasesContainerName);
            var response = await container.ReadItemAsync<Release>(id, GetReleasePartitionKey(repositoryId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public virtual async Task<Release?> GetReleaseByTagAsync(string repositoryId, string tag)
    {
        var container = await GetContainerAsync(ReleasesContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @rid AND c.Tag = @tag AND c.Type = 'Release'")
            .WithParameter("@rid", repositoryId).WithParameter("@tag", tag);
        using var iterator = container.GetItemQueryIterator<Release>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetReleasePartitionKey(repositoryId)
        });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<List<Release>> GetReleasesAsync(string repositoryId)
    {
        var container = await GetContainerAsync(ReleasesContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @rid AND c.Type = 'Release' ORDER BY c.CreatedUtc DESC")
            .WithParameter("@rid", repositoryId);
        var results = new List<Release>();
        using var iterator = container.GetItemQueryIterator<Release>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetReleasePartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task DeleteReleaseAsync(string id, string repositoryId)
    {
        var container = await GetContainerAsync(ReleasesContainerName);
        await container.DeleteItemAsync<Release>(id, GetReleasePartitionKey(repositoryId));
    }
}
