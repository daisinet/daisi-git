using DaisiGit.Data;
using DaisiGit.Services;

namespace DaisiGit.Web.Services;

/// <summary>
/// Background worker that polls for pending workflow executions and processes them.
/// Adapted from CRM CrmBackgroundWorker.
/// </summary>
public class GitWorkflowBackgroundWorker(
    IServiceProvider serviceProvider,
    ILogger<GitWorkflowBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before first poll to let the app start up
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingExecutionsAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing workflow executions");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task ProcessPendingExecutionsAsync()
    {
        using var scope = serviceProvider.CreateScope();
        var cosmo = scope.ServiceProvider.GetRequiredService<DaisiGitCosmo>();
        var engine = scope.ServiceProvider.GetRequiredService<WorkflowEngine>();

        var pending = await cosmo.GetPendingWorkflowExecutionsAsync();

        foreach (var execution in pending)
        {
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
                        continue;
                    }

                    await engine.ProcessExecutionAsync(execution, workflow.Steps);
                }
                // File-based workflow processing will be added in Phase 6
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process execution {ExecutionId}", execution.id);
                execution.Status = "Failed";
                execution.Error = ex.Message;
                await cosmo.UpdateWorkflowExecutionAsync(execution);
            }
        }
    }
}
