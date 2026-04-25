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
            // A filter key maps to one or more context keys. For "branch", we check the push
            // branch (push events) AND the PR target branch (pr-* events) so the same
            // "branch: main" filter works for both trigger families.
            var contextKeys = key switch
            {
                "branch" => new[] { "push.branch", "pr.targetBranch", "branch.name" },
                "tag" => new[] { "push.tag" },
                "label" => new[] { "issue.label" },
                _ => new[] { key }
            };

            string? contextValue = null;
            foreach (var ck in contextKeys)
            {
                if (context.TryGetValue(ck, out var v) && !string.IsNullOrEmpty(v))
                {
                    contextValue = v;
                    break;
                }
            }
            if (contextValue == null) return false;

            // Support comma-separated values (e.g. "main,dev")
            var allowedValues = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!allowedValues.Any(v => string.Equals(v, contextValue, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    public static int CountSteps(List<WorkflowStep> steps)
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
