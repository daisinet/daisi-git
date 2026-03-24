namespace DaisiGit.Core.Enums;

/// <summary>
/// Events that can trigger workflow execution.
/// </summary>
public enum GitTriggerType
{
    PushToRef,
    BranchCreated,
    BranchDeleted,
    TagCreated,
    TagDeleted,
    PullRequestCreated,
    PullRequestUpdated,
    PullRequestClosed,
    PullRequestMerged,
    IssueCreated,
    IssueClosed,
    IssueReopened,
    CommentCreated,
    ReviewSubmitted,
    ReviewDismissed,
    RepositoryForked
}
