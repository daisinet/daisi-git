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
}

/// <summary>
/// A branch within a condition step (If / Else If / Else).
/// </summary>
public class WorkflowConditionBranch
{
    public string? Expression { get; set; }
    public List<WorkflowStep> Steps { get; set; } = [];
}
