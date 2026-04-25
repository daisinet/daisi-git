using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string WorkflowExecutionsContainerName = "WorkflowExecutions";
    public const string WorkflowExecutionsPartitionKeyName = nameof(WorkflowExecution.AccountId);

    private static PartitionKey GetWorkflowExecutionPartitionKey(string accountId) => new(accountId);

    public async Task<WorkflowExecution> CreateWorkflowExecutionAsync(WorkflowExecution execution)
    {
        if (string.IsNullOrEmpty(execution.id))
            execution.id = GenerateId("wfx");
        execution.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(WorkflowExecutionsContainerName);
        var response = await container.CreateItemAsync(execution,
            GetWorkflowExecutionPartitionKey(execution.AccountId));
        return response.Resource;
    }

    public async Task<WorkflowExecution?> GetWorkflowExecutionAsync(string id, string accountId)
    {
        var container = await GetContainerAsync(WorkflowExecutionsContainerName);
        try
        {
            var response = await container.ReadItemAsync<WorkflowExecution>(id,
                GetWorkflowExecutionPartitionKey(accountId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<WorkflowExecution> UpdateWorkflowExecutionAsync(WorkflowExecution execution)
    {
        execution.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(WorkflowExecutionsContainerName);
        var response = await container.UpsertItemAsync(execution,
            GetWorkflowExecutionPartitionKey(execution.AccountId));
        return response.Resource;
    }

    /// <summary>
    /// Gets pending executions: Running with NextRunAt set and in the past.
    /// Worker clears NextRunAt when it picks an execution up, which is what excludes
    /// it from this query while it is actively being processed.
    /// Cross-partition query since the background worker processes all accounts.
    /// </summary>
    public async Task<List<WorkflowExecution>> GetPendingWorkflowExecutionsAsync(int limit = 50)
    {
        var container = await GetContainerAsync(WorkflowExecutionsContainerName);
        var now = DateTime.UtcNow;
        var query = new QueryDefinition(
            "SELECT TOP @limit * FROM c WHERE c.Type = 'WorkflowExecution' AND c.Status = 'Running' AND c.NextRunAt <= @now")
            .WithParameter("@limit", limit)
            .WithParameter("@now", now);

        var results = new List<WorkflowExecution>();
        using var iterator = container.GetItemQueryIterator<WorkflowExecution>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    /// <summary>
    /// Gets executions stuck in "Dispatched" status for longer than the specified timeout.
    /// Used by the watchdog to fail stuck executions.
    /// </summary>
    public async Task<List<WorkflowExecution>> GetStuckDispatchedExecutionsAsync(TimeSpan timeout, int limit = 50)
    {
        var container = await GetContainerAsync(WorkflowExecutionsContainerName);
        var cutoff = DateTime.UtcNow - timeout;
        var query = new QueryDefinition(
            "SELECT TOP @limit * FROM c WHERE c.Type = 'WorkflowExecution' AND c.Status = 'Dispatched' AND c.UpdatedUtc <= @cutoff")
            .WithParameter("@limit", limit)
            .WithParameter("@cutoff", cutoff);

        var results = new List<WorkflowExecution>();
        using var iterator = container.GetItemQueryIterator<WorkflowExecution>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    /// <summary>
    /// Gets execution history for a workflow or repository.
    /// </summary>
    public async Task<List<WorkflowExecution>> GetWorkflowExecutionsAsync(
        string accountId, string? workflowId = null, string? repositoryId = null,
        int take = 50, int skip = 0)
    {
        var container = await GetContainerAsync(WorkflowExecutionsContainerName);

        var sql = "SELECT * FROM c WHERE c.AccountId = @accountId AND c.Type = 'WorkflowExecution'";
        if (workflowId != null)
            sql += " AND c.WorkflowId = @workflowId";
        if (repositoryId != null)
            sql += " AND c.RepositoryId = @repositoryId";
        sql += " ORDER BY c.CreatedUtc DESC OFFSET @skip LIMIT @take";

        var query = new QueryDefinition(sql)
            .WithParameter("@accountId", accountId)
            .WithParameter("@skip", skip)
            .WithParameter("@take", take);

        if (workflowId != null)
            query = query.WithParameter("@workflowId", workflowId);
        if (repositoryId != null)
            query = query.WithParameter("@repositoryId", repositoryId);

        var results = new List<WorkflowExecution>();
        using var iterator = container.GetItemQueryIterator<WorkflowExecution>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetWorkflowExecutionPartitionKey(accountId) });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }
}
