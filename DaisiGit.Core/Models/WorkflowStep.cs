using System.Text.Json.Serialization;
using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// A single step within a workflow definition.
/// </summary>
public class WorkflowStep
{
    public int Order { get; set; }
    public string Name { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowStepType StepType { get; set; }

    // ── HttpRequest ──
    public string? HttpUrl { get; set; }
    public string? HttpMethod { get; set; }
    public string? HttpBody { get; set; }
    public string? HttpContentType { get; set; }
    public Dictionary<string, string>? HttpHeaders { get; set; }

    // ── SetLabel / RemoveLabel ──
    public string? LabelName { get; set; }

    // ── AddComment ──
    public string? CommentBody { get; set; }

    // ── RequireReview ──
    public int? RequiredApprovals { get; set; }

    // ── Wait ──
    public int? WaitDays { get; set; }
    public int? WaitHours { get; set; }
    public int? WaitMinutes { get; set; }

    // ── Condition ──
    public string? ConditionExpression { get; set; }
    public List<WorkflowConditionBranch>? Branches { get; set; }

    // ── DeployAzureWebApp ──
    public string? AzureAppName { get; set; }
    public string? AzureWorkDir { get; set; }
    public string? AzureDeployPath { get; set; }
    public string? AzureUsernameSecret { get; set; }
    public string? AzurePasswordSecret { get; set; }
    /// <summary>
    /// Optional override for the Kudu/SCM host (without scheme). Use when Azure has
    /// assigned a region-hashed hostname (e.g. "myapp-abc123.scm.centralus-01.azurewebsites.net")
    /// instead of the legacy "{appName}.scm.azurewebsites.net".
    /// </summary>
    public string? AzureScmHost { get; set; }

    // ── Checkout ──
    public string? CheckoutRepo { get; set; }
    public string? CheckoutBranch { get; set; }
    public string? CheckoutPath { get; set; }

    // ── RunScript ──
    public string? ScriptCommand { get; set; }
    public string? ScriptWorkDir { get; set; }
    public int? ScriptTimeoutSeconds { get; set; }

    // ── SendEmail ──
    public string? EmailTo { get; set; }
    public string? EmailSubject { get; set; }
    public string? EmailBody { get; set; }

    // ── ImportFromUrl ──
    public string? ImportUrl { get; set; }

    // ── RunMinion ──
    /// <summary>Inline prompt. Mutually exclusive with <see cref="MinionInstructionsFile"/>.</summary>
    public string? MinionInstructions { get; set; }

    /// <summary>Workspace-relative path to a file whose contents become the prompt.</summary>
    public string? MinionInstructionsFile { get; set; }

    /// <summary>Workspace-relative subdir used as the minion's working directory. Default = workspace root.</summary>
    public string? MinionWorkingDirectory { get; set; }

    /// <summary>Daisinet model name. Empty/null falls back to the ORC's default text-gen model.</summary>
    public string? MinionModel { get; set; }

    public int? MinionContextSize { get; set; }
    public int? MinionMaxTokens { get; set; }

    /// <summary>Goal-mode iteration limit. Null = minion's default (20).</summary>
    public int? MinionMaxIterations { get; set; }

    public string? MinionRole { get; set; }
    public string? MinionKvQuant { get; set; }
    public bool? MinionJsonOutput { get; set; }
    public bool? MinionGrammar { get; set; }

    /// <summary>Subprocess timeout. Null = 1500 s default. Hard-capped at 1800 s by the engine.</summary>
    public int? MinionTimeoutSeconds { get; set; }

    /// <summary>Optional ORC address override (host:port). If unset, SDK defaults apply.</summary>
    public string? MinionOrcAddress { get; set; }
}

/// <summary>
/// A branch within a condition step (If / Else If / Else).
/// </summary>
public class WorkflowConditionBranch
{
    public string? Expression { get; set; }
    public List<WorkflowStep> Steps { get; set; } = [];
}
