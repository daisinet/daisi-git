using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string GitHubImportJobsContainerName = "GitHubImportJobs";
    public const string GitHubImportJobsPartitionKeyName = nameof(GitHubImportJob.DaisiOrgId);

    public PartitionKey GetGitHubImportJobPartitionKey(string daisiOrgId) => new(daisiOrgId);

    public virtual async Task<GitHubImportJob> UpsertGitHubImportJobAsync(GitHubImportJob job)
    {
        if (string.IsNullOrEmpty(job.id)) job.id = Guid.NewGuid().ToString("N")[..12];
        job.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(GitHubImportJobsContainerName);
        var response = await container.UpsertItemAsync(job, GetGitHubImportJobPartitionKey(job.DaisiOrgId));
        return response.Resource;
    }

    public virtual async Task<GitHubImportJob?> GetMostRecentGitHubImportJobAsync(string daisiOrgId)
    {
        var container = await GetContainerAsync(GitHubImportJobsContainerName);
        var query = new QueryDefinition(
            "SELECT TOP 1 * FROM c WHERE c.DaisiOrgId = @oid AND c.Type = 'GitHubImportJob' ORDER BY c.StartedUtc DESC")
            .WithParameter("@oid", daisiOrgId);
        using var iterator = container.GetItemQueryIterator<GitHubImportJob>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetGitHubImportJobPartitionKey(daisiOrgId)
        });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    /// <summary>Returns every job that hasn't reached a terminal state, across all orgs.</summary>
    public virtual async Task<List<GitHubImportJob>> GetIncompleteGitHubImportJobsAsync()
    {
        var container = await GetContainerAsync(GitHubImportJobsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.Type = 'GitHubImportJob' AND (NOT IS_DEFINED(c.FinishedUtc) OR c.FinishedUtc = null)");
        var results = new List<GitHubImportJob>();
        using var iterator = container.GetItemQueryIterator<GitHubImportJob>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }
}
