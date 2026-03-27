using DaisiGit.Data;
using DaisiGit.Services;

namespace DaisiGit.Web.Services;

/// <summary>
/// Background worker that polls for pending workflow executions and processes them.
/// </summary>
public class GitWorkflowBackgroundWorker(
    IServiceProvider serviceProvider,
    ILogger<GitWorkflowBackgroundWorker> logger) : BackgroundService
{
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
        }
    }
}
