using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// CRUD wrapper for workflow management.
/// </summary>
public class WorkflowService(DaisiGitCosmo cosmo)
{
    public async Task<GitWorkflow> CreateAsync(GitWorkflow workflow)
    {
        ApplySchedule(workflow);
        return await cosmo.CreateWorkflowAsync(workflow);
    }

    public async Task<GitWorkflow?> GetAsync(string id, string accountId)
        => await cosmo.GetWorkflowAsync(id, accountId);

    public async Task<GitWorkflow> UpdateAsync(GitWorkflow workflow)
    {
        ApplySchedule(workflow);
        return await cosmo.UpdateWorkflowAsync(workflow);
    }

    public async Task DeleteAsync(string id, string accountId)
        => await cosmo.DeleteWorkflowAsync(id, accountId);

    public async Task<List<GitWorkflow>> ListAsync(string accountId)
        => await cosmo.GetWorkflowsAsync(accountId);

    public async Task<List<WorkflowExecution>> ListExecutionsAsync(
        string accountId, string? workflowId = null, string? repositoryId = null,
        int take = 50, int skip = 0)
        => await cosmo.GetWorkflowExecutionsAsync(accountId, workflowId, repositoryId, take, skip);

    public async Task<List<GitEvent>> ListEventsAsync(string repositoryId, int take = 50)
        => await cosmo.GetRecentEventsAsync(repositoryId, take);

    /// <summary>
    /// Creates a manual execution for a workflow (Run Now).
    /// Populates a minimal context with repo info and the actor who triggered it.
    /// </summary>
    public async Task<WorkflowExecution> RunNowAsync(GitWorkflow workflow, GitRepository repo,
        string actorId, string actorName, string? actorEmail = null,
        Dictionary<string, string>? inputs = null)
    {
        var context = new Dictionary<string, string>
        {
            ["repo.id"] = repo.id,
            ["repo.name"] = repo.Name,
            ["repo.slug"] = repo.Slug,
            ["repo.owner"] = repo.OwnerName,
            ["repo.defaultBranch"] = repo.DefaultBranch,
            ["push.branch"] = repo.DefaultBranch,
            ["actor.id"] = actorId,
            ["actor.name"] = actorName,
            ["_orgId"] = repo.AccountId,
            ["_manual"] = "true"
        };
        if (!string.IsNullOrEmpty(actorEmail))
            context["actor.email"] = actorEmail;

        var nameParts = actorName.Split(' ', 2, StringSplitOptions.TrimEntries);
        context["actor.firstName"] = nameParts[0];
        context["actor.lastName"] = nameParts.Length > 1 ? nameParts[1] : "";

        // Resolve and validate declared inputs. Missing required inputs throw; missing
        // optional inputs fall back to Default. Unknown supplied inputs are ignored
        // (mirrors GitHub Actions behavior — keeps callers forward-compatible).
        if (workflow.Inputs is { Count: > 0 })
        {
            foreach (var def in workflow.Inputs)
            {
                if (string.IsNullOrWhiteSpace(def.Name)) continue;
                inputs ??= new();
                inputs.TryGetValue(def.Name, out var supplied);
                var value = supplied ?? def.Default;
                if (string.IsNullOrEmpty(value))
                {
                    if (def.Required)
                        throw new ArgumentException($"Input '{def.Name}' is required.");
                    continue;
                }
                if (def.Type == "choice" && def.Choices is { Count: > 0 } && !def.Choices.Contains(value))
                    throw new ArgumentException($"Input '{def.Name}' must be one of: {string.Join(", ", def.Choices)}");
                context[$"inputs.{def.Name}"] = value;
            }
        }

        return await cosmo.CreateWorkflowExecutionAsync(new WorkflowExecution
        {
            AccountId = workflow.AccountId,
            RepositoryId = repo.id,
            WorkflowId = workflow.id,
            WorkflowName = workflow.Name,
            Source = "Visual",
            TriggerType = workflow.TriggerType,
            Context = context,
            CurrentStepIndex = 0,
            TotalSteps = WorkflowTriggerService.CountSteps(workflow.Steps),
            NextRunAt = DateTime.UtcNow,
            Status = "Running"
        });
    }

    /// <summary>
    /// For Scheduled workflows, computes NextScheduledRunUtc from the schedule filter.
    /// Clears it for non-scheduled or disabled workflows.
    /// </summary>
    private static void ApplySchedule(GitWorkflow workflow)
    {
        if (workflow.TriggerType == GitTriggerType.Scheduled && workflow.IsEnabled)
        {
            var schedule = workflow.TriggerFilters?.GetValueOrDefault("schedule");
            workflow.NextScheduledRunUtc = CronScheduleService.GetNextRunUtc(schedule);
        }
        else
        {
            workflow.NextScheduledRunUtc = null;
        }
    }
}
