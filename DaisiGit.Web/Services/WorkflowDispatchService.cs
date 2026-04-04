using System.Text.Json;
using Azure.Storage.Queues;

namespace DaisiGit.Web.Services;

/// <summary>
/// Dispatches workflow executions to an Azure Storage Queue for processing
/// by the isolated DaisiGit.Worker container.
/// </summary>
public class WorkflowDispatchService(QueueClient? queueClient, ILogger<WorkflowDispatchService> logger)
{
    /// <summary>Whether queue-based dispatch is configured. When false, falls back to in-process execution.</summary>
    public bool IsEnabled => queueClient != null;

    /// <summary>
    /// Enqueues a workflow execution for processing by the worker.
    /// </summary>
    public async Task DispatchAsync(string executionId, string accountId, string runtime = "minimal")
    {
        if (queueClient == null)
            throw new InvalidOperationException("Workflow dispatch queue is not configured");

        var message = JsonSerializer.Serialize(new
        {
            ExecutionId = executionId,
            AccountId = accountId,
            Runtime = runtime
        });

        await queueClient.SendMessageAsync(message);
        logger.LogInformation("Dispatched workflow execution {ExecutionId} to queue (runtime: {Runtime})",
            executionId, runtime);
    }
}
