using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using DaisiGit.Data;
using DaisiGit.Services;

namespace DaisiGit.Worker;

/// <summary>
/// Listens to an Azure Storage Queue for workflow execution messages and processes them
/// using the WorkflowEngine in an isolated container environment.
/// </summary>
public class WorkflowQueueProcessor(
    IServiceProvider serviceProvider,
    QueueClient queueClient,
    ILogger<WorkflowQueueProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMinutes(35); // longer than max script timeout

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Workflow queue processor starting, listening on queue '{Queue}'",
            queueClient.Name);

        // Ensure the queue exists
        await queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await queueClient.ReceiveMessagesAsync(
                    maxMessages: 1,
                    visibilityTimeout: VisibilityTimeout,
                    cancellationToken: stoppingToken);

                if (messages?.Value is { Length: > 0 })
                {
                    foreach (var message in messages.Value)
                    {
                        await ProcessMessageAsync(message, stoppingToken);
                        await queueClient.DeleteMessageAsync(
                            message.MessageId, message.PopReceipt, stoppingToken);
                    }
                }
                else
                {
                    // No messages — back off
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing workflow queue message");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Workflow queue processor stopping");
    }

    private async Task ProcessMessageAsync(QueueMessage message, CancellationToken ct)
    {
        WorkflowDispatchMessage? dispatch;
        try
        {
            dispatch = JsonSerializer.Deserialize<WorkflowDispatchMessage>(message.Body);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize queue message: {Body}", message.Body);
            return; // Delete the bad message
        }

        if (dispatch == null || string.IsNullOrEmpty(dispatch.ExecutionId))
        {
            logger.LogWarning("Received empty or invalid dispatch message");
            return;
        }

        logger.LogInformation("Processing workflow execution {ExecutionId} for account {AccountId}",
            dispatch.ExecutionId, dispatch.AccountId);

        using var scope = serviceProvider.CreateScope();
        var cosmo = scope.ServiceProvider.GetRequiredService<DaisiGitCosmo>();
        var engine = scope.ServiceProvider.GetRequiredService<WorkflowEngine>();

        var execution = await cosmo.GetWorkflowExecutionAsync(dispatch.ExecutionId, dispatch.AccountId);
        if (execution == null)
        {
            logger.LogWarning("Execution {ExecutionId} not found in account {AccountId}",
                dispatch.ExecutionId, dispatch.AccountId);
            return;
        }

        if (execution.Status is not ("Running" or "Dispatched"))
        {
            logger.LogInformation("Execution {ExecutionId} has status '{Status}', skipping",
                dispatch.ExecutionId, execution.Status);
            return;
        }

        // Mark as running (in case it was "Dispatched"). Clear NextRunAt so the Web's
        // poll loop (which selects Running with NextRunAt <= now) does not re-dispatch
        // an execution we are actively processing.
        if (execution.Status == "Dispatched" || execution.NextRunAt != null)
        {
            execution.Status = "Running";
            execution.NextRunAt = null;
            await cosmo.UpdateWorkflowExecutionAsync(execution);
        }

        try
        {
            if (execution.Source == "Visual" && execution.WorkflowId != null)
            {
                var workflow = await cosmo.GetWorkflowAsync(execution.WorkflowId, execution.AccountId);
                if (workflow == null)
                {
                    execution.Status = "Failed";
                    execution.Error = "Workflow definition not found";
                    await cosmo.UpdateWorkflowExecutionAsync(execution);
                    return;
                }

                await engine.ProcessExecutionAsync(execution, workflow.Steps, workflow.Env);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process execution {ExecutionId}", execution.id);
            execution.Status = "Failed";
            execution.Error = ex.Message;
            try { await cosmo.UpdateWorkflowExecutionAsync(execution); } catch { }
        }

        logger.LogInformation("Execution {ExecutionId} finished with status '{Status}'",
            execution.id, execution.Status);
    }
}
