using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DaisiGit.Services;

/// <summary>
/// Parses .daisigit/workflows/*.yml files into workflow definitions.
/// Supports a GitHub Actions-inspired YAML format.
/// </summary>
public static class WorkflowYamlParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Parses a YAML workflow definition. Returns null if invalid.
    /// </summary>
    public static ParsedFileWorkflow? Parse(string yamlContent)
    {
        try
        {
            var raw = Deserializer.Deserialize<RawYamlWorkflow>(yamlContent);
            if (raw == null) return null;

            var triggers = ParseTriggers(raw.On);
            var steps = ParseJobs(raw.Jobs);

            return new ParsedFileWorkflow
            {
                Name = raw.Name ?? "Unnamed workflow",
                Triggers = triggers,
                Steps = steps
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if a parsed workflow should fire for the given event.
    /// </summary>
    public static bool MatchesTrigger(ParsedFileWorkflow workflow, GitTriggerType eventType,
        Dictionary<string, string> context)
    {
        foreach (var trigger in workflow.Triggers)
        {
            if (trigger.EventType != eventType)
                continue;

            // Check branch filter
            if (trigger.Branches is { Count: > 0 })
            {
                var branch = context.GetValueOrDefault("push.branch", "");
                if (!trigger.Branches.Any(b => string.Equals(b, branch, StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            return true;
        }
        return false;
    }

    // ── Parsing helpers ──

    private static List<WorkflowTrigger> ParseTriggers(Dictionary<string, object?>? on)
    {
        var triggers = new List<WorkflowTrigger>();
        if (on == null) return triggers;

        foreach (var (key, value) in on)
        {
            var eventTypes = MapEventName(key);

            if (value is Dictionary<object, object> config)
            {
                var types = GetStringList(config, "types");
                var branches = GetStringList(config, "branches");

                if (types.Count > 0)
                {
                    // Each type is a sub-event (e.g. pull_request.merged)
                    foreach (var type in types)
                    {
                        var subEventTypes = MapEventName($"{key}.{type}");
                        foreach (var et in subEventTypes)
                            triggers.Add(new WorkflowTrigger { EventType = et, Branches = branches });
                    }
                }
                else
                {
                    foreach (var et in eventTypes)
                        triggers.Add(new WorkflowTrigger { EventType = et, Branches = branches });
                }
            }
            else
            {
                foreach (var et in eventTypes)
                    triggers.Add(new WorkflowTrigger { EventType = et });
            }
        }

        return triggers;
    }

    private static List<GitTriggerType> MapEventName(string name) => name.ToLowerInvariant() switch
    {
        "push" => [GitTriggerType.PushToRef],
        "pull_request" => [GitTriggerType.PullRequestCreated, GitTriggerType.PullRequestClosed, GitTriggerType.PullRequestMerged],
        "pull_request.opened" or "pull_request.created" => [GitTriggerType.PullRequestCreated],
        "pull_request.closed" => [GitTriggerType.PullRequestClosed],
        "pull_request.merged" => [GitTriggerType.PullRequestMerged],
        "issues" or "issue" => [GitTriggerType.IssueCreated, GitTriggerType.IssueClosed],
        "issues.opened" or "issue.created" => [GitTriggerType.IssueCreated],
        "issues.closed" or "issue.closed" => [GitTriggerType.IssueClosed],
        "issue_comment" or "comment" => [GitTriggerType.CommentCreated],
        "pull_request_review" or "review" => [GitTriggerType.ReviewSubmitted],
        "fork" => [GitTriggerType.RepositoryForked],
        "create" => [GitTriggerType.BranchCreated, GitTriggerType.TagCreated],
        "delete" => [GitTriggerType.BranchDeleted, GitTriggerType.TagDeleted],
        _ => []
    };

    private static List<WorkflowStep> ParseJobs(Dictionary<string, RawJob>? jobs)
    {
        var steps = new List<WorkflowStep>();
        if (jobs == null) return steps;

        var order = 0;
        foreach (var (_, job) in jobs)
        {
            if (job.Steps == null) continue;
            foreach (var rawStep in job.Steps)
            {
                var step = ParseStep(rawStep, order++);
                if (step != null)
                    steps.Add(step);
            }
        }
        return steps;
    }

    private static WorkflowStep? ParseStep(RawStep raw, int order)
    {
        var stepType = MapStepType(raw.Uses);
        if (stepType == null) return null;

        var step = new WorkflowStep
        {
            Order = order,
            Name = raw.Name ?? raw.Uses ?? "",
            StepType = stepType.Value
        };

        var with = raw.With ?? new Dictionary<string, string>();

        switch (step.StepType)
        {
            case WorkflowStepType.HttpRequest:
                step.HttpUrl = with.GetValueOrDefault("url");
                step.HttpMethod = with.GetValueOrDefault("method", "GET");
                step.HttpBody = with.GetValueOrDefault("body");
                step.HttpContentType = with.GetValueOrDefault("content-type", "application/json");
                break;
            case WorkflowStepType.SetLabel:
            case WorkflowStepType.RemoveLabel:
                step.LabelName = with.GetValueOrDefault("label");
                break;
            case WorkflowStepType.AddComment:
                step.CommentBody = with.GetValueOrDefault("body");
                break;
            case WorkflowStepType.RequireReview:
                if (int.TryParse(with.GetValueOrDefault("approvals", "1"), out var approvals))
                    step.RequiredApprovals = approvals;
                break;
            case WorkflowStepType.Wait:
                if (int.TryParse(with.GetValueOrDefault("minutes"), out var mins))
                    step.WaitMinutes = mins;
                if (int.TryParse(with.GetValueOrDefault("hours"), out var hrs))
                    step.WaitHours = hrs;
                if (int.TryParse(with.GetValueOrDefault("days"), out var days))
                    step.WaitDays = days;
                break;
        }

        // Simple condition from `if:`
        if (!string.IsNullOrEmpty(raw.If))
        {
            step.ConditionExpression = raw.If;
        }

        return step;
    }

    private static WorkflowStepType? MapStepType(string? uses) => uses?.ToLowerInvariant() switch
    {
        "http-request" or "webhook" => WorkflowStepType.HttpRequest,
        "set-label" or "add-label" => WorkflowStepType.SetLabel,
        "remove-label" => WorkflowStepType.RemoveLabel,
        "close-issue" => WorkflowStepType.CloseIssue,
        "close-pr" or "close-pull-request" => WorkflowStepType.ClosePullRequest,
        "add-comment" or "comment" => WorkflowStepType.AddComment,
        "require-review" => WorkflowStepType.RequireReview,
        "wait" => WorkflowStepType.Wait,
        _ => null
    };

    private static List<string> GetStringList(Dictionary<object, object> config, string key)
    {
        if (!config.TryGetValue(key, out var value))
            return [];

        if (value is List<object> list)
            return list.Select(o => o?.ToString() ?? "").Where(s => s != "").ToList();

        if (value is string s)
            return [s];

        return [];
    }

    // ── Raw YAML models ──

    private class RawYamlWorkflow
    {
        public string? Name { get; set; }
        public Dictionary<string, object?>? On { get; set; }
        public Dictionary<string, RawJob>? Jobs { get; set; }
    }

    private class RawJob
    {
        public List<RawStep>? Steps { get; set; }
    }

    private class RawStep
    {
        public string? Name { get; set; }
        public string? Uses { get; set; }
        public string? If { get; set; }
        public Dictionary<string, string>? With { get; set; }
    }
}

/// <summary>
/// A parsed file-based workflow ready for trigger matching and execution.
/// </summary>
public class ParsedFileWorkflow
{
    public string Name { get; set; } = "";
    public List<WorkflowTrigger> Triggers { get; set; } = [];
    public List<WorkflowStep> Steps { get; set; } = [];
}

/// <summary>
/// A single trigger definition from the `on:` section.
/// </summary>
public class WorkflowTrigger
{
    public GitTriggerType EventType { get; set; }
    public List<string>? Branches { get; set; }
}
