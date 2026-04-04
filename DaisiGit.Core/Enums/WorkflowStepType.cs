namespace DaisiGit.Core.Enums;

/// <summary>
/// Types of steps that can be executed in a workflow.
/// </summary>
public enum WorkflowStepType
{
    HttpRequest,
    SetLabel,
    RemoveLabel,
    CloseIssue,
    ClosePullRequest,
    AddComment,
    RequireReview,
    Condition,
    Wait,
    DeployAzureWebApp,
    Checkout,
    RunScript,
    SendEmail
}
