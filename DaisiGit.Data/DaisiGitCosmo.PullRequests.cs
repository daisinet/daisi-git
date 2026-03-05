using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string PullRequestIdPrefix = "pr";
    public const string PullRequestsContainerName = "PullRequests";
    public const string PullRequestsPartitionKeyName = nameof(PullRequest.RepositoryId);

    public PartitionKey GetPullRequestPartitionKey(string repositoryId) => new(repositoryId);

    public virtual async Task<PullRequest> CreatePullRequestAsync(PullRequest pr)
    {
        if (string.IsNullOrEmpty(pr.id))
            pr.id = GenerateId(PullRequestIdPrefix);
        pr.CreatedUtc = DateTime.UtcNow;

        // Assign next PR number for this repo
        if (pr.Number == 0)
            pr.Number = await GetNextPullRequestNumberAsync(pr.RepositoryId);

        var container = await GetContainerAsync(PullRequestsContainerName);
        var response = await container.CreateItemAsync(pr, GetPullRequestPartitionKey(pr.RepositoryId));
        return response.Resource;
    }

    public virtual async Task<PullRequest?> GetPullRequestAsync(string id, string repositoryId)
    {
        try
        {
            var container = await GetContainerAsync(PullRequestsContainerName);
            var response = await container.ReadItemAsync<PullRequest>(id, GetPullRequestPartitionKey(repositoryId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public virtual async Task<PullRequest?> GetPullRequestByNumberAsync(string repositoryId, int number)
    {
        var container = await GetContainerAsync(PullRequestsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.Number = @number AND c.Type = 'PullRequest'")
            .WithParameter("@repoId", repositoryId)
            .WithParameter("@number", number);

        using var iterator = container.GetItemQueryIterator<PullRequest>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetPullRequestPartitionKey(repositoryId)
        });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<List<PullRequest>> GetPullRequestsAsync(string repositoryId, PullRequestStatus? status = null)
    {
        var container = await GetContainerAsync(PullRequestsContainerName);
        var sql = "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.Type = 'PullRequest'";
        if (status.HasValue)
            sql += " AND c.Status = @status";
        sql += " ORDER BY c.CreatedUtc DESC";

        var queryDef = new QueryDefinition(sql)
            .WithParameter("@repoId", repositoryId);
        if (status.HasValue)
            queryDef = queryDef.WithParameter("@status", (int)status.Value);

        var results = new List<PullRequest>();
        using var iterator = container.GetItemQueryIterator<PullRequest>(queryDef, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetPullRequestPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<PullRequest> UpdatePullRequestAsync(PullRequest pr)
    {
        pr.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(PullRequestsContainerName);
        var response = await container.UpsertItemAsync(pr, GetPullRequestPartitionKey(pr.RepositoryId));
        return response.Resource;
    }

    private async Task<int> GetNextPullRequestNumberAsync(string repositoryId)
    {
        // Get the max PR/Issue number in this repo and increment
        var container = await GetContainerAsync(PullRequestsContainerName);
        var query = new QueryDefinition(
            "SELECT VALUE MAX(c.Number) FROM c WHERE c.RepositoryId = @repoId AND c.Type = 'PullRequest'")
            .WithParameter("@repoId", repositoryId);

        using var iterator = container.GetItemQueryIterator<int?>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetPullRequestPartitionKey(repositoryId)
        });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            var max = response.FirstOrDefault();
            return (max ?? 0) + 1;
        }
        return 1;
    }
}
