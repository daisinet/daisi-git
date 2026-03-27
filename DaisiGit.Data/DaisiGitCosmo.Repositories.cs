using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string RepositoryIdPrefix = "repo";
    public const string RepositoriesContainerName = "Repositories";
    public const string RepositoriesPartitionKeyName = nameof(GitRepository.AccountId);

    public PartitionKey GetRepositoryPartitionKey(string accountId) => new(accountId);

    public virtual async Task<GitRepository> CreateRepositoryAsync(GitRepository repo)
    {
        if (string.IsNullOrEmpty(repo.id))
            repo.id = GenerateId(RepositoryIdPrefix);
        repo.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(RepositoriesContainerName);
        var response = await container.CreateItemAsync(repo, GetRepositoryPartitionKey(repo.AccountId));
        return response.Resource;
    }

    public virtual async Task<GitRepository?> GetRepositoryAsync(string id, string accountId)
    {
        try
        {
            var container = await GetContainerAsync(RepositoriesContainerName);
            var response = await container.ReadItemAsync<GitRepository>(id, GetRepositoryPartitionKey(accountId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public virtual async Task<GitRepository?> GetRepositoryBySlugAsync(string ownerName, string slug)
    {
        var container = await GetContainerAsync(RepositoriesContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OwnerName = @owner AND c.Slug = @slug AND c.Type = 'GitRepository'")
            .WithParameter("@owner", ownerName)
            .WithParameter("@slug", slug);

        using var iterator = container.GetItemQueryIterator<GitRepository>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<List<GitRepository>> GetRepositoriesAsync(string accountId)
    {
        var container = await GetContainerAsync(RepositoriesContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.AccountId = @accountId AND c.Type = 'GitRepository' ORDER BY c.CreatedUtc DESC")
            .WithParameter("@accountId", accountId);

        var results = new List<GitRepository>();
        using var iterator = container.GetItemQueryIterator<GitRepository>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetRepositoryPartitionKey(accountId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<List<GitRepository>> GetRepositoriesByOwnerAsync(string ownerName)
    {
        var container = await GetContainerAsync(RepositoriesContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OwnerName = @owner AND c.Type = 'GitRepository' ORDER BY c.CreatedUtc DESC")
            .WithParameter("@owner", ownerName);

        var results = new List<GitRepository>();
        using var iterator = container.GetItemQueryIterator<GitRepository>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<GitRepository> UpdateRepositoryAsync(GitRepository repo)
    {
        repo.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(RepositoriesContainerName);
        var response = await container.UpsertItemAsync(repo, GetRepositoryPartitionKey(repo.AccountId));
        return response.Resource;
    }

    public virtual async Task DeleteRepositoryAsync(string id, string accountId)
    {
        var container = await GetContainerAsync(RepositoriesContainerName);
        await container.DeleteItemAsync<GitRepository>(id, GetRepositoryPartitionKey(accountId));
    }

    public virtual async Task<List<GitRepository>> GetForksAsync(string forkedFromId)
    {
        var container = await GetContainerAsync(RepositoriesContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.ForkedFromId = @forkedFromId AND c.Type = 'GitRepository' ORDER BY c.CreatedUtc DESC")
            .WithParameter("@forkedFromId", forkedFromId);

        var results = new List<GitRepository>();
        using var iterator = container.GetItemQueryIterator<GitRepository>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<GitRepository?> GetExistingForkAsync(string forkedFromId, string ownerId)
    {
        var container = await GetContainerAsync(RepositoriesContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.ForkedFromId = @forkedFromId AND c.OwnerId = @ownerId AND c.Type = 'GitRepository'")
            .WithParameter("@forkedFromId", forkedFromId)
            .WithParameter("@ownerId", ownerId);

        using var iterator = container.GetItemQueryIterator<GitRepository>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<List<GitRepository>> GetPublicRepositoriesAsync(int skip, int take, string? search = null)
    {
        var container = await GetContainerAsync(RepositoriesContainerName);

        var sql = "SELECT * FROM c WHERE c.Visibility = 2 AND c.Type = 'GitRepository'";
        if (!string.IsNullOrWhiteSpace(search))
            sql += " AND (CONTAINS(LOWER(c.Name), @search) OR CONTAINS(LOWER(c.OwnerName), @search) OR CONTAINS(LOWER(c.Description), @search))";
        sql += " ORDER BY c.StarCount DESC OFFSET @skip LIMIT @take";

        var query = new QueryDefinition(sql)
            .WithParameter("@skip", skip)
            .WithParameter("@take", take);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.WithParameter("@search", search.Trim().ToLowerInvariant());

        var results = new List<GitRepository>();
        using var iterator = container.GetItemQueryIterator<GitRepository>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<List<GitRepository>> SearchRepositoriesAsync(string accountId, string search, int skip = 0, int take = 50)
    {
        var container = await GetContainerAsync(RepositoriesContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.AccountId = @accountId AND c.Type = 'GitRepository' AND (CONTAINS(LOWER(c.Name), @search) OR CONTAINS(LOWER(c.OwnerName), @search) OR CONTAINS(LOWER(c.Description), @search)) ORDER BY c.CreatedUtc DESC OFFSET @skip LIMIT @take")
            .WithParameter("@accountId", accountId)
            .WithParameter("@search", search.Trim().ToLowerInvariant())
            .WithParameter("@skip", skip)
            .WithParameter("@take", take);

        var results = new List<GitRepository>();
        using var iterator = container.GetItemQueryIterator<GitRepository>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetRepositoryPartitionKey(accountId) });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }
}
