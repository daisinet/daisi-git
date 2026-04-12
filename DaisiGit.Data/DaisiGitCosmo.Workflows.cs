using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string WorkflowsContainerName = "Workflows";
    public const string WorkflowsPartitionKeyName = nameof(GitWorkflow.AccountId);

    private static PartitionKey GetWorkflowPartitionKey(string accountId) => new(accountId);

    public async Task<GitWorkflow> CreateWorkflowAsync(GitWorkflow workflow)
    {
        if (string.IsNullOrEmpty(workflow.id))
            workflow.id = GenerateId("wf");
        workflow.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(WorkflowsContainerName);
        var response = await container.CreateItemAsync(workflow, GetWorkflowPartitionKey(workflow.AccountId));
        return response.Resource;
    }

    public async Task<GitWorkflow?> GetWorkflowAsync(string id, string accountId)
    {
        try
        {
            var container = await GetContainerAsync(WorkflowsContainerName);
            var response = await container.ReadItemAsync<GitWorkflow>(id, GetWorkflowPartitionKey(accountId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<GitWorkflow> UpdateWorkflowAsync(GitWorkflow workflow)
    {
        workflow.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(WorkflowsContainerName);
        var response = await container.UpsertItemAsync(workflow, GetWorkflowPartitionKey(workflow.AccountId));
        return response.Resource;
    }

    public async Task DeleteWorkflowAsync(string id, string accountId)
    {
        var container = await GetContainerAsync(WorkflowsContainerName);
        await container.DeleteItemAsync<GitWorkflow>(id, GetWorkflowPartitionKey(accountId));
    }

    public async Task<List<GitWorkflow>> GetWorkflowsAsync(string accountId)
    {
        var container = await GetContainerAsync(WorkflowsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.AccountId = @accountId AND c.Type = 'GitWorkflow' AND c.Status = 'Active' ORDER BY c.CreatedUtc DESC")
            .WithParameter("@accountId", accountId);

        var results = new List<GitWorkflow>();
        using var iterator = container.GetItemQueryIterator<GitWorkflow>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetWorkflowPartitionKey(accountId) });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    /// <summary>
    /// Gets all enabled Scheduled workflows that are due to run (NextScheduledRunUtc &lt;= now).
    /// Cross-partition query since the background worker processes all accounts.
    /// </summary>
    public async Task<List<GitWorkflow>> GetDueScheduledWorkflowsAsync(int limit = 50)
    {
        var container = await GetContainerAsync(WorkflowsContainerName);
        var now = DateTime.UtcNow;
        var query = new QueryDefinition(
            "SELECT TOP @limit * FROM c WHERE c.Type = 'GitWorkflow' AND c.Status = 'Active' AND c.IsEnabled = true AND c.TriggerType = 'Scheduled' AND c.NextScheduledRunUtc != null AND c.NextScheduledRunUtc <= @now")
            .WithParameter("@limit", limit)
            .WithParameter("@now", now);

        var results = new List<GitWorkflow>();
        using var iterator = container.GetItemQueryIterator<GitWorkflow>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    /// <summary>
    /// Gets all enabled workflows matching a trigger type for an account.
    /// Includes both account-wide (RepositoryId=null) and repo-specific workflows.
    /// </summary>
    public async Task<List<GitWorkflow>> GetWorkflowsByTriggerAsync(string accountId, GitTriggerType triggerType)
    {
        var container = await GetContainerAsync(WorkflowsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.AccountId = @accountId AND c.Type = 'GitWorkflow' AND c.Status = 'Active' AND c.IsEnabled = true AND c.TriggerType = @triggerType")
            .WithParameter("@accountId", accountId)
            .WithParameter("@triggerType", triggerType.ToString());

        var results = new List<GitWorkflow>();
        using var iterator = container.GetItemQueryIterator<GitWorkflow>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetWorkflowPartitionKey(accountId) });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }
}
