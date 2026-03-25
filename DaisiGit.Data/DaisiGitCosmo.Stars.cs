using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string StarsContainerName = "Stars";
    public const string StarsPartitionKeyName = nameof(RepoStar.RepositoryId);
    public const string StarIdPrefix = "star";

    public PartitionKey GetStarPartitionKey(string repositoryId) => new(repositoryId);

    public virtual async Task<RepoStar> CreateStarAsync(RepoStar star)
    {
        if (string.IsNullOrEmpty(star.id))
            star.id = GenerateId(StarIdPrefix);
        star.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(StarsContainerName);
        var response = await container.CreateItemAsync(star, GetStarPartitionKey(star.RepositoryId));
        return response.Resource;
    }

    public virtual async Task DeleteStarAsync(string id, string repositoryId)
    {
        var container = await GetContainerAsync(StarsContainerName);
        await container.DeleteItemAsync<RepoStar>(id, GetStarPartitionKey(repositoryId));
    }

    public virtual async Task<RepoStar?> GetStarAsync(string repositoryId, string userId)
    {
        var container = await GetContainerAsync(StarsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.UserId = @userId AND c.Type = 'RepoStar'")
            .WithParameter("@repoId", repositoryId)
            .WithParameter("@userId", userId);

        using var iterator = container.GetItemQueryIterator<RepoStar>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetStarPartitionKey(repositoryId)
        });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<List<RepoStar>> GetStarsForRepoAsync(string repositoryId)
    {
        var container = await GetContainerAsync(StarsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.Type = 'RepoStar'")
            .WithParameter("@repoId", repositoryId);

        var results = new List<RepoStar>();
        using var iterator = container.GetItemQueryIterator<RepoStar>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetStarPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<List<RepoStar>> GetStarsByUserAsync(string userId)
    {
        var container = await GetContainerAsync(StarsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.UserId = @userId AND c.Type = 'RepoStar' ORDER BY c.CreatedUtc DESC")
            .WithParameter("@userId", userId);

        var results = new List<RepoStar>();
        using var iterator = container.GetItemQueryIterator<RepoStar>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }
}
