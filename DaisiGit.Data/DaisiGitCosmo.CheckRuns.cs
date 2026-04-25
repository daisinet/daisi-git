using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string CheckRunsContainerName = "CheckRuns";
    public const string CheckRunsPartitionKeyName = nameof(CheckRun.RepositoryId);

    public PartitionKey GetCheckRunPartitionKey(string repositoryId) => new(repositoryId);

    public virtual async Task<CheckRun> UpsertCheckRunAsync(CheckRun check)
    {
        if (string.IsNullOrEmpty(check.id)) check.id = GenerateId("chk");
        check.UpdatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(CheckRunsContainerName);
        var response = await container.UpsertItemAsync(check, GetCheckRunPartitionKey(check.RepositoryId));
        return response.Resource;
    }

    public virtual async Task<List<CheckRun>> GetCheckRunsByPullRequestAsync(string repositoryId, int prNumber)
    {
        var container = await GetContainerAsync(CheckRunsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @rid AND c.PullRequestNumber = @prn AND c.Type = 'CheckRun' ORDER BY c.StartedUtc DESC")
            .WithParameter("@rid", repositoryId)
            .WithParameter("@prn", prNumber);

        var results = new List<CheckRun>();
        using var iterator = container.GetItemQueryIterator<CheckRun>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetCheckRunPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<List<CheckRun>> GetCheckRunsByShaAsync(string repositoryId, string sha)
    {
        var container = await GetContainerAsync(CheckRunsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @rid AND c.HeadSha = @sha AND c.Type = 'CheckRun' ORDER BY c.StartedUtc DESC")
            .WithParameter("@rid", repositoryId)
            .WithParameter("@sha", sha);

        var results = new List<CheckRun>();
        using var iterator = container.GetItemQueryIterator<CheckRun>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetCheckRunPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }
}
