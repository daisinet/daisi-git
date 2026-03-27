using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Finds matching workflows when an event fires and creates execution records.
/// </summary>
public class WorkflowTriggerService(DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Evaluates all workflows matching the trigger and creates execution records.
    /// </summary>
    public async Task FireTriggerAsync(
        string accountId, string repositoryId,
        GitTriggerType triggerType, Dictionary<string, string> context)
    {
        // Visual workflows
        var workflows = await cosmo.GetWorkflowsByTriggerAsync(accountId, triggerType);

        foreach (var wf in workflows)
        {
            // Check repo scope
            if (wf.RepositoryId != null && wf.RepositoryId != repositoryId)
                continue;

            // Check trigger filters
            if (!MatchesFilters(wf.TriggerFilters, context))
                continue;

            await cosmo.CreateWorkflowExecutionAsync(new WorkflowExecution
            {
                AccountId = accountId,
                RepositoryId = repositoryId,
                WorkflowId = wf.id,
                WorkflowName = wf.Name,
                Source = "Visual",
                TriggerType = triggerType,
                Context = context,
                CurrentStepIndex = 0,
                TotalSteps = CountSteps(wf.Steps),
                NextRunAt = DateTime.UtcNow,
                Status = "Running"
            });
        }

        // File-based workflows will be added in Phase 6 (YAML parser)
    }

    internal static bool MatchesFilters(Dictionary<string, string>? filters, Dictionary<string, string> context)
    {
        if (filters == null || filters.Count == 0)
            return true;

        foreach (var (key, value) in filters)
        {
            // Filter key maps to context key (e.g. "branch" → "push.branch")
            var contextKey = key switch
            {
                "branch" => "push.branch",
                "tag" => "push.tag",
                "label" => "issue.label",
                _ => key
            };

            if (!context.TryGetValue(contextKey, out var contextValue))
                return false;

            // Support comma-separated values (e.g. "main,dev")
            var allowedValues = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!allowedValues.Any(v => string.Equals(v, contextValue, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    internal static int CountSteps(List<WorkflowStep> steps)
    {
        var count = 0;
        foreach (var step in steps)
        {
            count++;
            if (step.Branches != null)
            {
                foreach (var branch in step.Branches)
                    count += CountSteps(branch.Steps);
            }
        }
        return count;
    }
}
