using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
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
                await FireDueScheduledWorkflowsAsync();
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

    /// <summary>
    /// Finds Scheduled workflows that are due, creates execution records, and advances NextScheduledRunUtc.
    /// </summary>
    private async Task FireDueScheduledWorkflowsAsync()
    {
        using var scope = serviceProvider.CreateScope();
        var cosmo = scope.ServiceProvider.GetRequiredService<DaisiGitCosmo>();

        var dueWorkflows = await cosmo.GetDueScheduledWorkflowsAsync();

        foreach (var wf in dueWorkflows)
        {
            try
            {
                var schedule = wf.TriggerFilters?.GetValueOrDefault("schedule");

                // Create a minimal execution context
                var context = new Dictionary<string, string>
                {
                    ["trigger"] = "scheduled",
                    ["schedule"] = schedule ?? "",
                    ["scheduled_at"] = (wf.NextScheduledRunUtc ?? DateTime.UtcNow).ToString("o")
                };

                if (!string.IsNullOrEmpty(wf.RepositoryId))
                    context["repo.id"] = wf.RepositoryId;

                await cosmo.CreateWorkflowExecutionAsync(new WorkflowExecution
                {
                    AccountId = wf.AccountId,
                    RepositoryId = wf.RepositoryId ?? "",
                    WorkflowId = wf.id,
                    WorkflowName = wf.Name,
                    Source = "Visual",
                    TriggerType = GitTriggerType.Scheduled,
                    Context = context,
                    CurrentStepIndex = 0,
                    TotalSteps = WorkflowTriggerService.CountSteps(wf.Steps),
                    NextRunAt = DateTime.UtcNow,
                    Status = "Running"
                });

                // Advance to next scheduled run
                wf.NextScheduledRunUtc = CronScheduleService.GetNextRunUtc(schedule);
                await cosmo.UpdateWorkflowAsync(wf);

                logger.LogInformation("Fired scheduled workflow {WorkflowId} ({Name}), next run at {NextRun}",
                    wf.id, wf.Name, wf.NextScheduledRunUtc);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fire scheduled workflow {WorkflowId}", wf.id);
            }
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
                    // Look up workflow runtime for image selection
                    var runtime = "minimal";
                    if (execution.WorkflowId != null)
                    {
                        var wf = await cosmo.GetWorkflowAsync(execution.WorkflowId, execution.AccountId);
                        if (wf != null)
                            runtime = wf.Runtime.ToString().ToLowerInvariant();
                    }

                    await dispatch.DispatchAsync(execution.id, execution.AccountId, runtime);
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

            await engine.ProcessExecutionAsync(execution, workflow, workflow.Env);
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
