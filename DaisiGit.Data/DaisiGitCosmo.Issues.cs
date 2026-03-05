using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string IssueIdPrefix = "iss";
    public const string IssuesContainerName = "Issues";
    public const string IssuesPartitionKeyName = nameof(Issue.RepositoryId);

    public PartitionKey GetIssuePartitionKey(string repositoryId) => new(repositoryId);

    public virtual async Task<Issue> CreateIssueAsync(Issue issue)
    {
        if (string.IsNullOrEmpty(issue.id))
            issue.id = GenerateId(IssueIdPrefix);
        issue.CreatedUtc = DateTime.UtcNow;

        if (issue.Number == 0)
            issue.Number = await GetNextIssueNumberAsync(issue.RepositoryId);

        var container = await GetContainerAsync(IssuesContainerName);
        var response = await container.CreateItemAsync(issue, GetIssuePartitionKey(issue.RepositoryId));
        return response.Resource;
    }

    public virtual async Task<Issue?> GetIssueAsync(string id, string repositoryId)
    {
        try
        {
            var container = await GetContainerAsync(IssuesContainerName);
            var response = await container.ReadItemAsync<Issue>(id, GetIssuePartitionKey(repositoryId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public virtual async Task<Issue?> GetIssueByNumberAsync(string repositoryId, int number)
    {
        var container = await GetContainerAsync(IssuesContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.Number = @number AND c.Type = 'Issue'")
            .WithParameter("@repoId", repositoryId)
            .WithParameter("@number", number);

        using var iterator = container.GetItemQueryIterator<Issue>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetIssuePartitionKey(repositoryId)
        });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<List<Issue>> GetIssuesAsync(string repositoryId, IssueStatus? status = null)
    {
        var container = await GetContainerAsync(IssuesContainerName);
        var sql = "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.Type = 'Issue'";
        if (status.HasValue)
            sql += " AND c.Status = @status";
        sql += " ORDER BY c.CreatedUtc DESC";

        var queryDef = new QueryDefinition(sql)
            .WithParameter("@repoId", repositoryId);
        if (status.HasValue)
            queryDef = queryDef.WithParameter("@status", (int)status.Value);

        var results = new List<Issue>();
        using var iterator = container.GetItemQueryIterator<Issue>(queryDef, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetIssuePartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<Issue> UpdateIssueAsync(Issue issue)
    {
        issue.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(IssuesContainerName);
        var response = await container.UpsertItemAsync(issue, GetIssuePartitionKey(issue.RepositoryId));
        return response.Resource;
    }

    private async Task<int> GetNextIssueNumberAsync(string repositoryId)
    {
        var container = await GetContainerAsync(IssuesContainerName);
        var query = new QueryDefinition(
            "SELECT VALUE MAX(c.Number) FROM c WHERE c.RepositoryId = @repoId AND c.Type = 'Issue'")
            .WithParameter("@repoId", repositoryId);

        using var iterator = container.GetItemQueryIterator<int?>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetIssuePartitionKey(repositoryId)
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
