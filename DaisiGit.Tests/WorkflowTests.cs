using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Services;

namespace DaisiGit.Tests;

public class WorkflowTests
{
    // ── Model defaults ──

    [Fact]
    public void GitWorkflow_DefaultsAreCorrect()
    {
        var wf = new GitWorkflow();
        Assert.Equal("GitWorkflow", wf.Type);
        Assert.True(wf.IsEnabled);
        Assert.Equal("Active", wf.Status);
        Assert.Empty(wf.Steps);
    }

    [Fact]
    public void WorkflowExecution_DefaultsAreCorrect()
    {
        var exec = new WorkflowExecution();
        Assert.Equal("WorkflowExecution", exec.Type);
        Assert.Equal("Running", exec.Status);
        Assert.Equal("Visual", exec.Source);
        Assert.Equal(0, exec.CurrentStepIndex);
        Assert.Empty(exec.StepResults);
    }

    [Fact]
    public void GitEvent_DefaultsAreCorrect()
    {
        var evt = new GitEvent();
        Assert.Equal("GitEvent", evt.Type);
        Assert.Empty(evt.Payload);
    }

    // ── Merge field rendering ──

    [Fact]
    public void MergeService_Render_ReplacesFields()
    {
        var ctx = new Dictionary<string, string>
        {
            ["repo.name"] = "my-repo",
            ["pr.title"] = "Fix bug"
        };

        var result = WorkflowMergeService.Render("PR '{{pr.title}}' in {{repo.name}}", ctx);
        Assert.Equal("PR 'Fix bug' in my-repo", result);
    }

    [Fact]
    public void MergeService_Render_LeavesUnknownFieldsIntact()
    {
        var ctx = new Dictionary<string, string> { ["known"] = "value" };
        var result = WorkflowMergeService.Render("{{known}} and {{unknown}}", ctx);
        Assert.Equal("value and {{unknown}}", result);
    }

    [Fact]
    public void MergeService_Render_HandlesEmptyTemplate()
    {
        Assert.Equal("", WorkflowMergeService.Render("", new()));
        Assert.Null(WorkflowMergeService.Render(null!, new()));
    }

    [Fact]
    public void MergeService_BuildContext_IncludesRepoAndActorFields()
    {
        var repo = new GitRepository
        {
            id = "repo-1",
            Name = "test-repo",
            Slug = "test-repo",
            OwnerName = "alice",
            DefaultBranch = "main",
            Visibility = GitRepoVisibility.Private
        };

        var ctx = WorkflowMergeService.BuildContext(repo, "user-1", "alice",
            new() { ["pr.title"] = "My PR" });

        Assert.Equal("test-repo", ctx["repo.name"]);
        Assert.Equal("alice", ctx["actor.name"]);
        Assert.Equal("My PR", ctx["pr.title"]);
    }

    // ── Trigger filter matching ──

    [Fact]
    public void TriggerFilter_EmptyFilters_AlwaysMatch()
    {
        Assert.True(WorkflowTriggerService.MatchesFilters(null, new()));
        Assert.True(WorkflowTriggerService.MatchesFilters(new(), new()));
    }

    [Fact]
    public void TriggerFilter_BranchFilter_Matches()
    {
        var filters = new Dictionary<string, string> { ["branch"] = "main" };
        var context = new Dictionary<string, string> { ["push.branch"] = "main" };
        Assert.True(WorkflowTriggerService.MatchesFilters(filters, context));
    }

    [Fact]
    public void TriggerFilter_BranchFilter_NoMatch()
    {
        var filters = new Dictionary<string, string> { ["branch"] = "main" };
        var context = new Dictionary<string, string> { ["push.branch"] = "dev" };
        Assert.False(WorkflowTriggerService.MatchesFilters(filters, context));
    }

    [Fact]
    public void TriggerFilter_CommaSeparated_Matches()
    {
        var filters = new Dictionary<string, string> { ["branch"] = "main,dev" };
        var context = new Dictionary<string, string> { ["push.branch"] = "dev" };
        Assert.True(WorkflowTriggerService.MatchesFilters(filters, context));
    }

    [Fact]
    public void TriggerFilter_MissingContextKey_NoMatch()
    {
        var filters = new Dictionary<string, string> { ["branch"] = "main" };
        Assert.False(WorkflowTriggerService.MatchesFilters(filters, new()));
    }

    // ── Step counting ──

    [Fact]
    public void CountSteps_FlatList()
    {
        var steps = new List<WorkflowStep>
        {
            new() { StepType = WorkflowStepType.HttpRequest },
            new() { StepType = WorkflowStepType.AddComment }
        };
        Assert.Equal(2, WorkflowTriggerService.CountSteps(steps));
    }

    [Fact]
    public void CountSteps_WithConditionBranches()
    {
        var steps = new List<WorkflowStep>
        {
            new()
            {
                StepType = WorkflowStepType.Condition,
                Branches =
                [
                    new() { Steps = [new() { StepType = WorkflowStepType.AddComment }] },
                    new() { Steps = [new() { StepType = WorkflowStepType.CloseIssue }] }
                ]
            }
        };
        Assert.Equal(3, WorkflowTriggerService.CountSteps(steps)); // 1 condition + 2 branch steps
    }

    // ── Simple condition evaluator ──

    [Fact]
    public void SimpleCondition_Equals()
    {
        var ctx = new Dictionary<string, string> { ["push.branch"] = "main" };
        Assert.True(WorkflowEngine.EvaluateSimpleCondition("push.branch == main", ctx));
        Assert.False(WorkflowEngine.EvaluateSimpleCondition("push.branch == dev", ctx));
    }

    [Fact]
    public void SimpleCondition_NotEquals()
    {
        var ctx = new Dictionary<string, string> { ["push.branch"] = "main" };
        Assert.True(WorkflowEngine.EvaluateSimpleCondition("push.branch != dev", ctx));
        Assert.False(WorkflowEngine.EvaluateSimpleCondition("push.branch != main", ctx));
    }

    [Fact]
    public void SimpleCondition_Contains()
    {
        var ctx = new Dictionary<string, string> { ["pr.title"] = "Fix critical bug in auth" };
        Assert.True(WorkflowEngine.EvaluateSimpleCondition("pr.title contains bug", ctx));
        Assert.False(WorkflowEngine.EvaluateSimpleCondition("pr.title contains feature", ctx));
    }

    [Fact]
    public void SimpleCondition_NullOrEmpty_AlwaysTrue()
    {
        Assert.True(WorkflowEngine.EvaluateSimpleCondition(null, new()));
        Assert.True(WorkflowEngine.EvaluateSimpleCondition("", new()));
    }

    [Fact]
    public void SimpleCondition_KeyExists()
    {
        var ctx = new Dictionary<string, string> { ["push.branch"] = "main" };
        Assert.True(WorkflowEngine.EvaluateSimpleCondition("push.branch", ctx));
        Assert.False(WorkflowEngine.EvaluateSimpleCondition("nonexistent", ctx));
    }
}
