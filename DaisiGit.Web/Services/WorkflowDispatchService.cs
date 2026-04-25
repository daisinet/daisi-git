using System.Text.Json;
using Azure.Storage.Queues;

namespace DaisiGit.Web.Services;

/// <summary>
/// Dispatches workflow executions to per-runtime Azure Storage Queues so each runtime
/// can have its own dedicated Container Apps Job + image. Falls back to a single legacy
/// queue when the per-runtime queues are not configured.
/// </summary>
public class WorkflowDispatchService(
    Dictionary<string, QueueClient> runtimeQueues,
    QueueClient? legacyQueue,
    ILogger<WorkflowDispatchService> logger)
{
    /// <summary>Whether queue-based dispatch is configured. False -> in-process fallback.</summary>
    public bool IsEnabled => runtimeQueues.Count > 0 || legacyQueue != null;

    /// <summary>
    /// Enqueues a workflow execution onto the queue that matches the requested runtime.
    /// Unknown runtimes fall back to "minimal", and if no per-runtime queue is configured
    /// at all, the message goes to the legacy single queue (backward-compat).
    /// </summary>
    public async Task DispatchAsync(string executionId, string accountId, string runtime = "minimal")
    {
        var key = string.IsNullOrWhiteSpace(runtime) ? "minimal" : runtime.ToLowerInvariant();
        if (!runtimeQueues.TryGetValue(key, out var queue) && !runtimeQueues.TryGetValue("minimal", out queue))
            queue = legacyQueue;

        if (queue == null)
            throw new InvalidOperationException("Workflow dispatch queue is not configured");

        var message = JsonSerializer.Serialize(new
        {
            ExecutionId = executionId,
            AccountId = accountId,
            Runtime = key
        });

        await queue.SendMessageAsync(message);
        logger.LogInformation("Dispatched execution {ExecutionId} to {Queue} (runtime {Runtime})",
            executionId, queue.Name, key);
    }
}
