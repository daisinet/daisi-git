using DaisiGit.Data;
using DaisiGit.Services;

namespace DaisiGit.Web.Services;

/// <summary>
/// Background worker that polls for pending workflow executions.
/// When a dispatch queue is configured, enqueues executions for isolated processing.
/// Otherwise falls back to in-process execution (local dev).
/// Also acts as a watchdog for stuck dispatched executions.
/// </summary>
public class GitWorkflowBackgroundWorker(
    IServiceProvider serviceProvider,
    ILogger<GitWorkflowBackgroundWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StuckDispatchTimeout = TimeSpan.FromMinutes(35);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the app to fully start
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingExecutionsAsync();
                await FailStuckDispatchesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing workflow executions");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ProcessPendingExecutionsAsync()
    {
        using var scope = serviceProvider.CreateScope();
        var cosmo = scope.ServiceProvider.GetRequiredService<DaisiGitCosmo>();
        var dispatch = scope.ServiceProvider.GetRequiredService<WorkflowDispatchService>();

        var pending = await cosmo.GetPendingWorkflowExecutionsAsync();

        foreach (var execution in pending)
        {
            try
            {
                if (dispatch.IsEnabled)
                {
                    // Dispatch to isolated worker via queue
                    await dispatch.DispatchAsync(execution.id, execution.AccountId);
                    execution.Status = "Dispatched";
                    await cosmo.UpdateWorkflowExecutionAsync(execution);
                }
                else
                {
                    // Fallback: process in-process (local development)
                    await ProcessInProcessAsync(scope, cosmo, execution);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process execution {ExecutionId}", execution.id);
                execution.Status = "Failed";
                execution.Error = ex.Message;
                try { await cosmo.UpdateWorkflowExecutionAsync(execution); } catch { }
            }
        }
    }

    private static async Task ProcessInProcessAsync(IServiceScope scope, DaisiGitCosmo cosmo, DaisiGit.Core.Models.WorkflowExecution execution)
    {
        var engine = scope.ServiceProvider.GetRequiredService<WorkflowEngine>();

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

    /// <summary>
    /// Watchdog: fails executions stuck in "Dispatched" for too long.
    /// This handles cases where the worker container crashed or timed out
    /// without writing results back.
    /// </summary>
    private async Task FailStuckDispatchesAsync()
    {
        using var scope = serviceProvider.CreateScope();
        var cosmo = scope.ServiceProvider.GetRequiredService<DaisiGitCosmo>();

        var stuck = await cosmo.GetStuckDispatchedExecutionsAsync(StuckDispatchTimeout);
        foreach (var execution in stuck)
        {
            logger.LogWarning("Failing stuck dispatched execution {ExecutionId} (dispatched at {UpdatedUtc})",
                execution.id, execution.UpdatedUtc);
            execution.Status = "Failed";
            execution.Error = "Execution timed out waiting for worker";
            await cosmo.UpdateWorkflowExecutionAsync(execution);
        }
    }
}
