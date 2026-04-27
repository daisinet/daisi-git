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

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)
        .Build();

    /// <summary>
    /// Parses a YAML workflow definition. Returns null if invalid.
    /// </summary>
    public static ParsedFileWorkflow? Parse(string yamlContent)
    {
        return TryParse(yamlContent, out _, out var result) ? result : null;
    }

    /// <summary>
    /// Tries to parse a YAML workflow definition. Returns false with an error message on failure.
    /// </summary>
    public static bool TryParse(string yamlContent, out string? error, out ParsedFileWorkflow? result)
    {
        error = null;
        result = null;

        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            error = "YAML content is empty.";
            return false;
        }

        try
        {
            var raw = Deserializer.Deserialize<RawYamlWorkflow>(yamlContent);
            if (raw == null)
            {
                error = "Could not parse YAML document.";
                return false;
            }

            var triggers = ParseTriggers(raw.On);
            var inputs = ParseInputs(raw.On);
            var steps = ParseJobs(raw.Jobs);

            result = new ParsedFileWorkflow
            {
                Name = raw.Name ?? "Unnamed workflow",
                Triggers = triggers,
                Steps = steps,
                Env = raw.Env,
                Inputs = inputs
            };
            return true;
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            error = $"YAML syntax error at line {ex.Start.Line}: {ex.InnerException?.Message ?? ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Parse error: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Serializes a visual workflow definition to YAML format.
    /// </summary>
    public static string ToYaml(GitWorkflow workflow)
    {
        var doc = new Dictionary<string, object?>();
        doc["name"] = workflow.Name;

        // on: triggers
        var onSection = new Dictionary<string, object?>();
        var triggerKey = MapTriggerToEventName(workflow.TriggerType);
        if (workflow.TriggerFilters is { Count: > 0 } && workflow.TriggerFilters.TryGetValue("branch", out var branches))
        {
            var branchList = branches.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            onSection[triggerKey] = new Dictionary<string, object> { ["branches"] = branchList };
        }
        else
        {
            onSection[triggerKey] = null;
        }

        // Render declared inputs alongside the trigger as workflow_dispatch.inputs.
        if (workflow.Inputs is { Count: > 0 })
        {
            var inputsMap = new Dictionary<string, object?>();
            foreach (var input in workflow.Inputs)
            {
                if (string.IsNullOrWhiteSpace(input.Name)) continue;
                var entry = new Dictionary<string, object?>();
                if (!string.IsNullOrEmpty(input.Description)) entry["description"] = input.Description;
                if (!string.IsNullOrEmpty(input.Type) && input.Type != "string") entry["type"] = input.Type;
                if (input.Required) entry["required"] = true;
                if (!string.IsNullOrEmpty(input.Default)) entry["default"] = input.Default;
                if (input.Choices is { Count: > 0 }) entry["options"] = input.Choices;
                inputsMap[input.Name] = entry;
            }
            onSection["workflow_dispatch"] = new Dictionary<string, object?> { ["inputs"] = inputsMap };
        }

        doc["on"] = onSection;

        if (workflow.Env is { Count: > 0 })
            doc["env"] = workflow.Env;

        // jobs
        var steps = new List<Dictionary<string, object?>>();
        foreach (var step in workflow.Steps.OrderBy(s => s.Order))
        {
            var rawStep = new Dictionary<string, object?>();
            if (!string.IsNullOrEmpty(step.Name))
                rawStep["name"] = step.Name;

            rawStep["uses"] = MapStepTypeToUses(step.StepType);

            if (step.Matrix is { Count: > 0 })
            {
                rawStep["strategy"] = new Dictionary<string, object?>
                {
                    ["matrix"] = step.Matrix.ToDictionary(kv => kv.Key, kv => (object)kv.Value)
                };
            }

            var with = BuildStepWith(step);
            if (with.Count > 0)
                rawStep["with"] = with;

            steps.Add(rawStep);
        }

        doc["jobs"] = new Dictionary<string, object>
        {
            ["build"] = new Dictionary<string, object> { ["steps"] = steps }
        };

        return YamlSerializer.Serialize(doc);
    }

    private static string MapTriggerToEventName(GitTriggerType type) => type switch
    {
        GitTriggerType.PushToRef => "push",
        GitTriggerType.BranchCreated => "create",
        GitTriggerType.BranchDeleted => "delete",
        GitTriggerType.TagCreated => "create",
        GitTriggerType.TagDeleted => "delete",
        GitTriggerType.PullRequestCreated => "pull_request",
        GitTriggerType.PullRequestUpdated => "pull_request",
        GitTriggerType.PullRequestClosed => "pull_request",
        GitTriggerType.PullRequestMerged => "pull_request",
        GitTriggerType.IssueCreated => "issues",
        GitTriggerType.IssueClosed => "issues",
        GitTriggerType.IssueReopened => "issues",
        GitTriggerType.CommentCreated => "issue_comment",
        GitTriggerType.ReviewSubmitted => "pull_request_review",
        GitTriggerType.ReviewDismissed => "pull_request_review",
        GitTriggerType.RepositoryForked => "fork",
        _ => "push"
    };

    private static string MapStepTypeToUses(WorkflowStepType type) => type switch
    {
        WorkflowStepType.HttpRequest => "http-request",
        WorkflowStepType.SetLabel => "set-label",
        WorkflowStepType.RemoveLabel => "remove-label",
        WorkflowStepType.CloseIssue => "close-issue",
        WorkflowStepType.ClosePullRequest => "close-pr",
        WorkflowStepType.AddComment => "add-comment",
        WorkflowStepType.RequireReview => "require-review",
        WorkflowStepType.Wait => "wait",
        WorkflowStepType.DeployAzureWebApp => "deploy-azure-webapp",
        WorkflowStepType.Checkout => "checkout",
        WorkflowStepType.RunScript => "run",
        WorkflowStepType.SendEmail => "send-email",
        WorkflowStepType.Condition => "condition",
        WorkflowStepType.RunMinion => "run-minion",
        WorkflowStepType.AcrBuild => "acr-build",
        WorkflowStepType.NugetPush => "nuget-push",
        WorkflowStepType.DispatchWorkflow => "dispatch-workflow",
        WorkflowStepType.WaitForApproval => "wait-for-approval",
        _ => type.ToString().ToLowerInvariant()
    };

    private static Dictionary<string, string> BuildStepWith(WorkflowStep step)
    {
        var with = new Dictionary<string, string>();
        switch (step.StepType)
        {
            case WorkflowStepType.HttpRequest:
                if (!string.IsNullOrEmpty(step.HttpUrl)) with["url"] = step.HttpUrl;
                if (!string.IsNullOrEmpty(step.HttpMethod)) with["method"] = step.HttpMethod;
                if (!string.IsNullOrEmpty(step.HttpBody)) with["body"] = step.HttpBody;
                if (!string.IsNullOrEmpty(step.HttpContentType) && step.HttpContentType != "application/json")
                    with["content-type"] = step.HttpContentType;
                break;
            case WorkflowStepType.SetLabel:
            case WorkflowStepType.RemoveLabel:
                if (!string.IsNullOrEmpty(step.LabelName)) with["label"] = step.LabelName;
                break;
            case WorkflowStepType.AddComment:
                if (!string.IsNullOrEmpty(step.CommentBody)) with["body"] = step.CommentBody;
                break;
            case WorkflowStepType.RequireReview:
                if (step.RequiredApprovals.HasValue) with["approvals"] = step.RequiredApprovals.Value.ToString();
                break;
            case WorkflowStepType.Wait:
                if (step.WaitDays.HasValue) with["days"] = step.WaitDays.Value.ToString();
                if (step.WaitHours.HasValue) with["hours"] = step.WaitHours.Value.ToString();
                if (step.WaitMinutes.HasValue) with["minutes"] = step.WaitMinutes.Value.ToString();
                break;
            case WorkflowStepType.DeployAzureWebApp:
                if (!string.IsNullOrEmpty(step.AzureAppName)) with["app-name"] = step.AzureAppName;
                if (!string.IsNullOrEmpty(step.AzureWorkDir)) with["working-directory"] = step.AzureWorkDir;
                if (!string.IsNullOrEmpty(step.AzureDeployPath)) with["path"] = step.AzureDeployPath;
                if (!string.IsNullOrEmpty(step.AzureUsernameSecret)) with["username-secret"] = step.AzureUsernameSecret;
                if (!string.IsNullOrEmpty(step.AzurePasswordSecret)) with["password-secret"] = step.AzurePasswordSecret;
                if (!string.IsNullOrEmpty(step.AzureScmHost)) with["scm-host"] = step.AzureScmHost;
                if (!string.IsNullOrEmpty(step.AzureAuthMode)) with["auth-mode"] = step.AzureAuthMode;
                break;
            case WorkflowStepType.Checkout:
                if (!string.IsNullOrEmpty(step.CheckoutRepo)) with["repo"] = step.CheckoutRepo;
                if (!string.IsNullOrEmpty(step.CheckoutBranch)) with["branch"] = step.CheckoutBranch;
                if (!string.IsNullOrEmpty(step.CheckoutPath)) with["path"] = step.CheckoutPath;
                break;
            case WorkflowStepType.RunScript:
                if (!string.IsNullOrEmpty(step.ScriptCommand)) with["run"] = step.ScriptCommand;
                if (!string.IsNullOrEmpty(step.ScriptWorkDir)) with["working-directory"] = step.ScriptWorkDir;
                if (step.ScriptTimeoutSeconds.HasValue) with["timeout"] = step.ScriptTimeoutSeconds.Value.ToString();
                break;
            case WorkflowStepType.SendEmail:
                if (!string.IsNullOrEmpty(step.EmailTo)) with["to"] = step.EmailTo;
                if (!string.IsNullOrEmpty(step.EmailSubject)) with["subject"] = step.EmailSubject;
                if (!string.IsNullOrEmpty(step.EmailBody)) with["body"] = step.EmailBody;
                break;
            case WorkflowStepType.RunMinion:
                if (!string.IsNullOrEmpty(step.MinionInstructions)) with["instructions"] = step.MinionInstructions;
                if (!string.IsNullOrEmpty(step.MinionInstructionsFile)) with["instructions-file"] = step.MinionInstructionsFile;
                if (!string.IsNullOrEmpty(step.MinionWorkingDirectory)) with["working-directory"] = step.MinionWorkingDirectory;
                if (!string.IsNullOrEmpty(step.MinionModel)) with["model"] = step.MinionModel;
                if (step.MinionContextSize.HasValue) with["context"] = step.MinionContextSize.Value.ToString();
                if (step.MinionMaxTokens.HasValue) with["max-tokens"] = step.MinionMaxTokens.Value.ToString();
                if (step.MinionMaxIterations.HasValue) with["max-iterations"] = step.MinionMaxIterations.Value.ToString();
                if (!string.IsNullOrEmpty(step.MinionRole)) with["role"] = step.MinionRole;
                if (!string.IsNullOrEmpty(step.MinionKvQuant)) with["kv-quant"] = step.MinionKvQuant;
                if (step.MinionJsonOutput == true) with["json"] = "true";
                if (step.MinionGrammar == true) with["grammar"] = "true";
                if (step.MinionTimeoutSeconds.HasValue) with["timeout"] = step.MinionTimeoutSeconds.Value.ToString();
                if (!string.IsNullOrEmpty(step.MinionOrcAddress)) with["orc-address"] = step.MinionOrcAddress;
                break;
            case WorkflowStepType.AcrBuild:
                if (!string.IsNullOrEmpty(step.AcrRegistry)) with["registry"] = step.AcrRegistry;
                if (!string.IsNullOrEmpty(step.AcrImage)) with["image"] = step.AcrImage;
                if (!string.IsNullOrEmpty(step.AcrDockerfile)) with["dockerfile"] = step.AcrDockerfile;
                if (!string.IsNullOrEmpty(step.AcrContext)) with["context"] = step.AcrContext;
                if (!string.IsNullOrEmpty(step.AcrBuildArgs)) with["build-args"] = step.AcrBuildArgs;
                break;
            case WorkflowStepType.NugetPush:
                if (!string.IsNullOrEmpty(step.NugetPackagePath)) with["package"] = step.NugetPackagePath;
                if (!string.IsNullOrEmpty(step.NugetSource)) with["source"] = step.NugetSource;
                if (!string.IsNullOrEmpty(step.NugetApiKeySecret)) with["api-key-secret"] = step.NugetApiKeySecret;
                if (step.NugetSkipDuplicate == true) with["skip-duplicate"] = "true";
                break;
            case WorkflowStepType.DispatchWorkflow:
                if (!string.IsNullOrEmpty(step.DispatchRepo)) with["repo"] = step.DispatchRepo;
                if (!string.IsNullOrEmpty(step.DispatchWorkflow)) with["workflow"] = step.DispatchWorkflow;
                if (!string.IsNullOrEmpty(step.DispatchInputs)) with["inputs"] = step.DispatchInputs;
                break;
            case WorkflowStepType.WaitForApproval:
                if (!string.IsNullOrEmpty(step.ApprovalEnvironment)) with["environment"] = step.ApprovalEnvironment;
                if (!string.IsNullOrEmpty(step.ApprovalApprovers)) with["approvers"] = step.ApprovalApprovers;
                if (!string.IsNullOrEmpty(step.ApprovalMessage)) with["message"] = step.ApprovalMessage;
                break;
        }
        return with;
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

    /// <summary>
    /// Reads the inputs declared under <c>on: workflow_dispatch: inputs:</c>. Mirrors the
    /// GitHub Actions schema; ignores anything we don't understand.
    /// </summary>
    private static List<WorkflowInput> ParseInputs(Dictionary<string, object?>? on)
    {
        var result = new List<WorkflowInput>();
        if (on == null) return result;
        if (!on.TryGetValue("workflow_dispatch", out var wd) || wd is not Dictionary<object, object> wdMap) return result;
        if (!wdMap.TryGetValue("inputs", out var inputsObj) || inputsObj is not Dictionary<object, object> inputs) return result;

        foreach (var (key, value) in inputs)
        {
            var name = key?.ToString();
            if (string.IsNullOrEmpty(name)) continue;
            var def = new WorkflowInput { Name = name };

            if (value is Dictionary<object, object> map)
            {
                if (map.TryGetValue("description", out var d)) def.Description = d?.ToString();
                if (map.TryGetValue("type", out var t)) def.Type = t?.ToString() ?? "string";
                if (map.TryGetValue("default", out var dv)) def.Default = dv?.ToString();
                if (map.TryGetValue("required", out var rv) && bool.TryParse(rv?.ToString(), out var rq)) def.Required = rq;
                if (map.TryGetValue("options", out var opts) && opts is List<object> list)
                    def.Choices = list.Select(o => o?.ToString() ?? "").Where(s => s.Length > 0).ToList();
            }
            result.Add(def);
        }
        return result;
    }

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
            case WorkflowStepType.DeployAzureWebApp:
                step.AzureAppName = with.GetValueOrDefault("app-name");
                step.AzureWorkDir = with.GetValueOrDefault("working-directory");
                step.AzureDeployPath = with.GetValueOrDefault("path");
                step.AzureUsernameSecret = with.GetValueOrDefault("username-secret");
                step.AzurePasswordSecret = with.GetValueOrDefault("password-secret");
                step.AzureScmHost = with.GetValueOrDefault("scm-host");
                step.AzureAuthMode = with.GetValueOrDefault("auth-mode");
                break;
            case WorkflowStepType.Checkout:
                step.CheckoutRepo = with.GetValueOrDefault("repo");
                step.CheckoutBranch = with.GetValueOrDefault("branch");
                step.CheckoutPath = with.GetValueOrDefault("path");
                break;
            case WorkflowStepType.RunScript:
                step.ScriptCommand = with.GetValueOrDefault("run") ?? with.GetValueOrDefault("command");
                step.ScriptWorkDir = with.GetValueOrDefault("working-directory");
                if (int.TryParse(with.GetValueOrDefault("timeout"), out var secs))
                    step.ScriptTimeoutSeconds = secs;
                break;
            case WorkflowStepType.SendEmail:
                step.EmailTo = with.GetValueOrDefault("to");
                step.EmailSubject = with.GetValueOrDefault("subject");
                step.EmailBody = with.GetValueOrDefault("body");
                break;
            case WorkflowStepType.RunMinion:
                step.MinionInstructions = with.GetValueOrDefault("instructions");
                step.MinionInstructionsFile = with.GetValueOrDefault("instructions-file");
                step.MinionWorkingDirectory = with.GetValueOrDefault("working-directory");
                step.MinionModel = with.GetValueOrDefault("model");
                if (int.TryParse(with.GetValueOrDefault("context"), out var mctx)) step.MinionContextSize = mctx;
                if (int.TryParse(with.GetValueOrDefault("max-tokens"), out var mmt)) step.MinionMaxTokens = mmt;
                if (int.TryParse(with.GetValueOrDefault("max-iterations"), out var mmi)) step.MinionMaxIterations = mmi;
                step.MinionRole = with.GetValueOrDefault("role");
                step.MinionKvQuant = with.GetValueOrDefault("kv-quant");
                if (bool.TryParse(with.GetValueOrDefault("json"), out var mjson)) step.MinionJsonOutput = mjson;
                if (bool.TryParse(with.GetValueOrDefault("grammar"), out var mgram)) step.MinionGrammar = mgram;
                if (int.TryParse(with.GetValueOrDefault("timeout"), out var mto)) step.MinionTimeoutSeconds = mto;
                step.MinionOrcAddress = with.GetValueOrDefault("orc-address");

                var hasInline = !string.IsNullOrEmpty(step.MinionInstructions);
                var hasFile = !string.IsNullOrEmpty(step.MinionInstructionsFile);
                if (hasInline && hasFile)
                    throw new InvalidOperationException(
                        "run-minion: set either `instructions` or `instructions-file`, not both.");
                if (!hasInline && !hasFile)
                    throw new InvalidOperationException(
                        "run-minion: one of `instructions` or `instructions-file` is required.");
                break;
            case WorkflowStepType.AcrBuild:
                step.AcrRegistry = with.GetValueOrDefault("registry");
                step.AcrImage = with.GetValueOrDefault("image");
                step.AcrDockerfile = with.GetValueOrDefault("dockerfile");
                step.AcrContext = with.GetValueOrDefault("context");
                step.AcrBuildArgs = with.GetValueOrDefault("build-args");
                break;
            case WorkflowStepType.NugetPush:
                step.NugetPackagePath = with.GetValueOrDefault("package");
                step.NugetSource = with.GetValueOrDefault("source");
                step.NugetApiKeySecret = with.GetValueOrDefault("api-key-secret");
                if (bool.TryParse(with.GetValueOrDefault("skip-duplicate"), out var skipDup)) step.NugetSkipDuplicate = skipDup;
                break;
            case WorkflowStepType.DispatchWorkflow:
                step.DispatchRepo = with.GetValueOrDefault("repo");
                step.DispatchWorkflow = with.GetValueOrDefault("workflow");
                step.DispatchInputs = with.GetValueOrDefault("inputs");
                break;
            case WorkflowStepType.WaitForApproval:
                step.ApprovalEnvironment = with.GetValueOrDefault("environment");
                step.ApprovalApprovers = with.GetValueOrDefault("approvers");
                step.ApprovalMessage = with.GetValueOrDefault("message");
                break;
        }

        // Simple condition from `if:`
        if (!string.IsNullOrEmpty(raw.If))
        {
            step.ConditionExpression = raw.If;
        }

        // strategy.matrix → step.Matrix (each entry is a dimension and its value list).
        if (raw.Strategy?.Matrix is { Count: > 0 } matrixMap)
        {
            var dims = new Dictionary<string, List<string>>();
            foreach (var (k, v) in matrixMap)
            {
                var key = k?.ToString();
                if (string.IsNullOrEmpty(key)) continue;
                var values = new List<string>();
                if (v is List<object> list)
                {
                    foreach (var item in list)
                        if (item != null) values.Add(item.ToString() ?? "");
                }
                else if (v != null)
                {
                    values.Add(v.ToString() ?? "");
                }
                if (values.Count > 0) dims[key] = values;
            }
            if (dims.Count > 0) step.Matrix = dims;
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
        "deploy-azure-webapp" or "deploy-azure" or "azure-deploy" => WorkflowStepType.DeployAzureWebApp,
        "checkout" or "clone" => WorkflowStepType.Checkout,
        "run" or "script" or "run-script" or "shell" => WorkflowStepType.RunScript,
        "send-email" or "email" => WorkflowStepType.SendEmail,
        "run-minion" or "minion" => WorkflowStepType.RunMinion,
        "acr-build" or "docker-build" => WorkflowStepType.AcrBuild,
        "nuget-push" or "dotnet-nuget-push" => WorkflowStepType.NugetPush,
        "dispatch-workflow" or "trigger-workflow" => WorkflowStepType.DispatchWorkflow,
        "wait-for-approval" or "approval" or "manual-approval" => WorkflowStepType.WaitForApproval,
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
        public Dictionary<string, string>? Env { get; set; }
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
        public RawStrategy? Strategy { get; set; }
    }

    private class RawStrategy
    {
        public Dictionary<object, object>? Matrix { get; set; }
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
    public Dictionary<string, string>? Env { get; set; }
    public List<WorkflowInput> Inputs { get; set; } = [];
}

/// <summary>
/// A single trigger definition from the `on:` section.
/// </summary>
public class WorkflowTrigger
{
    public GitTriggerType EventType { get; set; }
    public List<string>? Branches { get; set; }
}
