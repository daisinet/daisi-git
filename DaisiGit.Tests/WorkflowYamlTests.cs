using DaisiGit.Core.Enums;
using DaisiGit.Services;

namespace DaisiGit.Tests;

public class WorkflowYamlTests
{
    private const string SimpleWorkflow = """
        name: Notify on merge
        on:
          pull_request:
            types: [merged]
            branches: [main]
        jobs:
          notify:
            steps:
              - name: Post webhook
                uses: http-request
                with:
                  url: https://hooks.example.com/merged
                  method: POST
                  body: '{"pr": "{{pr.title}}"}'
              - name: Add shipped label
                uses: set-label
                with:
                  label: shipped
        """;

    private const string MultiTriggerWorkflow = """
        name: CI
        on:
          push:
            branches: [main, dev]
          pull_request:
            types: [opened]
        jobs:
          build:
            steps:
              - name: Trigger build
                uses: http-request
                with:
                  url: https://ci.example.com/build
        """;

    [Fact]
    public void Parse_SimpleWorkflow_ReturnsCorrectStructure()
    {
        var parsed = WorkflowYamlParser.Parse(SimpleWorkflow);

        Assert.NotNull(parsed);
        Assert.Equal("Notify on merge", parsed.Name);
        Assert.Single(parsed.Triggers);
        Assert.Equal(GitTriggerType.PullRequestMerged, parsed.Triggers[0].EventType);
        Assert.Equal(["main"], parsed.Triggers[0].Branches);
        Assert.Equal(2, parsed.Steps.Count);

        Assert.Equal(WorkflowStepType.HttpRequest, parsed.Steps[0].StepType);
        Assert.Equal("https://hooks.example.com/merged", parsed.Steps[0].HttpUrl);
        Assert.Equal("POST", parsed.Steps[0].HttpMethod);

        Assert.Equal(WorkflowStepType.SetLabel, parsed.Steps[1].StepType);
        Assert.Equal("shipped", parsed.Steps[1].LabelName);
    }

    [Fact]
    public void Parse_MultiTrigger_ExpandsTriggers()
    {
        var parsed = WorkflowYamlParser.Parse(MultiTriggerWorkflow);

        Assert.NotNull(parsed);
        Assert.True(parsed.Triggers.Count >= 2);

        var pushTrigger = parsed.Triggers.First(t => t.EventType == GitTriggerType.PushToRef);
        Assert.NotNull(pushTrigger.Branches);
        Assert.Contains("main", pushTrigger.Branches);
        Assert.Contains("dev", pushTrigger.Branches);

        var prTrigger = parsed.Triggers.First(t => t.EventType == GitTriggerType.PullRequestCreated);
        Assert.NotNull(prTrigger);
    }

    [Fact]
    public void Parse_InvalidYaml_ReturnsNull()
    {
        Assert.Null(WorkflowYamlParser.Parse("{{invalid yaml"));
        Assert.Null(WorkflowYamlParser.Parse(""));
    }

    [Fact]
    public void MatchesTrigger_CorrectEvent_ReturnsTrue()
    {
        var parsed = WorkflowYamlParser.Parse(SimpleWorkflow)!;
        var context = new Dictionary<string, string> { ["push.branch"] = "main" };

        Assert.True(WorkflowYamlParser.MatchesTrigger(parsed, GitTriggerType.PullRequestMerged, context));
    }

    [Fact]
    public void MatchesTrigger_WrongEvent_ReturnsFalse()
    {
        var parsed = WorkflowYamlParser.Parse(SimpleWorkflow)!;
        Assert.False(WorkflowYamlParser.MatchesTrigger(parsed, GitTriggerType.IssueCreated, new()));
    }

    [Fact]
    public void MatchesTrigger_BranchFilter_Applies()
    {
        var parsed = WorkflowYamlParser.Parse(MultiTriggerWorkflow)!;

        var mainCtx = new Dictionary<string, string> { ["push.branch"] = "main" };
        Assert.True(WorkflowYamlParser.MatchesTrigger(parsed, GitTriggerType.PushToRef, mainCtx));

        var featureCtx = new Dictionary<string, string> { ["push.branch"] = "feature-x" };
        Assert.False(WorkflowYamlParser.MatchesTrigger(parsed, GitTriggerType.PushToRef, featureCtx));
    }

    [Fact]
    public void Parse_StepTypes_MapCorrectly()
    {
        var yaml = """
            name: All steps
            on:
              push: {}
            jobs:
              test:
                steps:
                  - uses: http-request
                    with:
                      url: https://example.com
                  - uses: set-label
                    with:
                      label: ready
                  - uses: remove-label
                    with:
                      label: wip
                  - uses: add-comment
                    with:
                      body: Hello
                  - uses: close-issue
                  - uses: close-pr
                  - uses: require-review
                    with:
                      approvals: "2"
                  - uses: wait
                    with:
                      minutes: "5"
            """;

        var parsed = WorkflowYamlParser.Parse(yaml);
        Assert.NotNull(parsed);
        Assert.Equal(8, parsed.Steps.Count);
        Assert.Equal(WorkflowStepType.HttpRequest, parsed.Steps[0].StepType);
        Assert.Equal(WorkflowStepType.SetLabel, parsed.Steps[1].StepType);
        Assert.Equal(WorkflowStepType.RemoveLabel, parsed.Steps[2].StepType);
        Assert.Equal(WorkflowStepType.AddComment, parsed.Steps[3].StepType);
        Assert.Equal(WorkflowStepType.CloseIssue, parsed.Steps[4].StepType);
        Assert.Equal(WorkflowStepType.ClosePullRequest, parsed.Steps[5].StepType);
        Assert.Equal(WorkflowStepType.RequireReview, parsed.Steps[6].StepType);
        Assert.Equal(2, parsed.Steps[6].RequiredApprovals);
        Assert.Equal(WorkflowStepType.Wait, parsed.Steps[7].StepType);
        Assert.Equal(5, parsed.Steps[7].WaitMinutes);
    }
}
