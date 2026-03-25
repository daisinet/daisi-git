using DaisiGit.Core.Enums;

namespace DaisiGit.IntegrationTests;

/// <summary>
/// End-to-end integration tests against a running DaisiGit dev server.
/// These tests exercise the full API flow: repo creation, issues, PRs, forks, stars, workflows.
///
/// Prerequisites:
/// - DaisiGit.Web running on https://localhost:5003 in Development mode
/// - Set env var DAISIGIT_API_KEY to the Daisi:SecretKey value (default: secret-debug)
/// </summary>
[Collection("Integration")]
public class FullWorkflowTests : IAsyncLifetime
{
    private readonly TestClient _client;
    private readonly string _testId = DateTime.UtcNow.ToString("HHmmss");
    private string _repoOwner = "";
    private string _repoSlug = "";

    public FullWorkflowTests()
    {
        if (!TestConfig.IsConfigured)
            throw new InvalidOperationException(
                "Set DAISIGIT_API_KEY env var to a valid personal access token (dg_...)");
        _client = new TestClient(TestConfig.ServerUrl, TestConfig.ApiKey);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Cleanup: try to delete the test repo
        // (may fail if already deleted by tests, that's OK)
        _client.Dispose();
    }

    // ── 1. Repository Creation ──

    [Fact]
    public async Task T01_CreateRepository()
    {
        var repo = await _client.CreateRepoAsync($"test-repo-{_testId}", "Integration test repo", GitRepoVisibility.Public);

        Assert.NotNull(repo);
        Assert.Equal($"test-repo-{_testId}", repo.Name);
        Assert.Equal(GitRepoVisibility.Public, repo.Visibility);
        Assert.Equal("main", repo.DefaultBranch);

        _repoOwner = repo.OwnerName;
        _repoSlug = repo.Slug;
    }

    [Fact]
    public async Task T02_ListRepositories()
    {
        // Create a repo first
        var repo = await _client.CreateRepoAsync($"list-test-{_testId}");
        var repos = await _client.ListReposAsync();

        Assert.NotEmpty(repos);
        Assert.Contains(repos, r => r.Slug == repo.Slug);
    }

    [Fact]
    public async Task T03_GetRepository()
    {
        var created = await _client.CreateRepoAsync($"get-test-{_testId}");
        var repo = await _client.GetRepoAsync(created.OwnerName, created.Slug);

        Assert.NotNull(repo);
        Assert.Equal(created.Slug, repo.Slug);
    }

    // ── 2. Branches and Commits ──

    [Fact]
    public async Task T04_ListBranches()
    {
        var repo = await _client.CreateRepoAsync($"branch-test-{_testId}");
        var branches = await _client.ListBranchesAsync(repo.OwnerName, repo.Slug);

        Assert.NotEmpty(branches);
        Assert.Contains(branches, b => b.Name == "main");
    }

    [Fact]
    public async Task T05_ListCommits()
    {
        var repo = await _client.CreateRepoAsync($"commit-test-{_testId}");
        var commits = await _client.ListCommitsAsync(repo.OwnerName, repo.Slug);

        Assert.NotEmpty(commits);
        Assert.Equal("Initial commit", commits[0].MessageFirstLine);
    }

    [Fact]
    public async Task T06_BrowseTree()
    {
        var repo = await _client.CreateRepoAsync($"tree-test-{_testId}");
        var tree = await _client.GetTreeAsync(repo.OwnerName, repo.Slug);

        // New repo has empty tree (initial commit has no files)
        Assert.NotNull(tree);
        Assert.False(tree.IsFile);
    }

    // ── 3. Issues ──

    [Fact]
    public async Task T07_CreateIssue()
    {
        var repo = await _client.CreateRepoAsync($"issue-test-{_testId}");
        var issue = await _client.CreateIssueAsync(repo.OwnerName, repo.Slug,
            "Test Issue", "This is a test issue");

        Assert.NotNull(issue);
        Assert.Equal("Test Issue", issue.Title);
        Assert.Equal(IssueStatus.Open, issue.Status);
        Assert.True(issue.Number > 0);
    }

    [Fact]
    public async Task T08_ListIssues()
    {
        var repo = await _client.CreateRepoAsync($"issuelist-{_testId}");
        await _client.CreateIssueAsync(repo.OwnerName, repo.Slug, "Issue A");
        await _client.CreateIssueAsync(repo.OwnerName, repo.Slug, "Issue B");

        var issues = await _client.ListIssuesAsync(repo.OwnerName, repo.Slug);
        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public async Task T09_CloseIssue()
    {
        var repo = await _client.CreateRepoAsync($"issueclose-{_testId}");
        var issue = await _client.CreateIssueAsync(repo.OwnerName, repo.Slug, "Close Me");

        var closed = await _client.CloseIssueAsync(repo.OwnerName, repo.Slug, issue.Number);
        Assert.Equal(IssueStatus.Closed, closed.Status);
    }

    [Fact]
    public async Task T10_IssueComment()
    {
        var repo = await _client.CreateRepoAsync($"issuecomment-{_testId}");
        var issue = await _client.CreateIssueAsync(repo.OwnerName, repo.Slug, "Comment Test");

        var comment = await _client.AddIssueCommentAsync(repo.OwnerName, repo.Slug,
            issue.Number, "This is a comment");

        Assert.NotNull(comment);
        Assert.Equal("This is a comment", comment.Body);
    }

    // ── 4. Pull Requests ──

    [Fact]
    public async Task T11_CreatePullRequest()
    {
        var repo = await _client.CreateRepoAsync($"pr-test-{_testId}");

        // PR needs two different branches — create a PR from main to main won't work
        // but the API should still accept the creation (validation happens at merge)
        var pr = await _client.CreatePrAsync(repo.OwnerName, repo.Slug,
            "Test PR", "main", "main", "Test PR description");

        Assert.NotNull(pr);
        Assert.Equal("Test PR", pr.Title);
        Assert.Equal(PullRequestStatus.Open, pr.Status);
    }

    [Fact]
    public async Task T12_ListPullRequests()
    {
        var repo = await _client.CreateRepoAsync($"prlist-{_testId}");
        await _client.CreatePrAsync(repo.OwnerName, repo.Slug, "PR A", "main", "main");
        await _client.CreatePrAsync(repo.OwnerName, repo.Slug, "PR B", "main", "main");

        var prs = await _client.ListPrsAsync(repo.OwnerName, repo.Slug);
        Assert.Equal(2, prs.Count);
    }

    // ── 5. Stars ──

    [Fact]
    public async Task T13_StarAndUnstar()
    {
        var repo = await _client.CreateRepoAsync($"star-test-{_testId}");

        await _client.StarRepoAsync(repo.OwnerName, repo.Slug);
        var starred = await _client.GetRepoAsync(repo.OwnerName, repo.Slug);
        Assert.Equal(1, starred!.StarCount);

        await _client.UnstarRepoAsync(repo.OwnerName, repo.Slug);
        var unstarred = await _client.GetRepoAsync(repo.OwnerName, repo.Slug);
        Assert.Equal(0, unstarred!.StarCount);
    }

    // ── 6. Forks ──

    [Fact]
    public async Task T14_ForkRepository()
    {
        var repo = await _client.CreateRepoAsync($"fork-src-{_testId}", visibility: GitRepoVisibility.Public);

        var fork = await _client.ForkRepoAsync(repo.OwnerName, repo.Slug);
        Assert.NotNull(fork);
        Assert.Equal(repo.id, fork.ForkedFromId);
    }

    // ── 7. Workflows ──

    [Fact]
    public async Task T15_CreateWorkflow()
    {
        var repo = await _client.CreateRepoAsync($"workflow-test-{_testId}");

        var wf = await _client.CreateWorkflowAsync(repo.OwnerName, repo.Slug,
            "Test Workflow", GitTriggerType.PushToRef);

        Assert.NotNull(wf);
        Assert.Equal("Test Workflow", wf.Name);
        Assert.True(wf.IsEnabled);
    }

    [Fact]
    public async Task T16_ListWorkflows()
    {
        var repo = await _client.CreateRepoAsync($"wflist-{_testId}");
        await _client.CreateWorkflowAsync(repo.OwnerName, repo.Slug, "WF A", GitTriggerType.PushToRef);
        await _client.CreateWorkflowAsync(repo.OwnerName, repo.Slug, "WF B", GitTriggerType.IssueCreated);

        var wfs = await _client.ListWorkflowsAsync(repo.OwnerName, repo.Slug);
        Assert.Equal(2, wfs.Count);
    }

    // ── 8. Events ──

    [Fact]
    public async Task T17_EventsEmitted()
    {
        var repo = await _client.CreateRepoAsync($"events-test-{_testId}");

        // Create an issue — should emit an event
        await _client.CreateIssueAsync(repo.OwnerName, repo.Slug, "Event Test Issue");

        // Wait a moment for async event processing
        await Task.Delay(500);

        var events = await _client.ListEventsAsync(repo.OwnerName, repo.Slug);
        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.EventType == GitTriggerType.IssueCreated);
    }

    // ── 9. Duplicate Prevention ──

    [Fact]
    public async Task T18_DuplicateRepoNameFails()
    {
        var repo = await _client.CreateRepoAsync($"dup-test-{_testId}");

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await _client.CreateRepoAsync($"dup-test-{_testId}");
        });
    }

    // ── 10. Reviews ──

    [Fact]
    public async Task T19_SubmitReview()
    {
        var repo = await _client.CreateRepoAsync($"review-test-{_testId}");
        var pr = await _client.CreatePrAsync(repo.OwnerName, repo.Slug, "Review PR", "main", "main");

        var review = await _client.SubmitReviewAsync(repo.OwnerName, repo.Slug,
            pr.Number, "approved", "Looks good!");

        Assert.NotNull(review);
        Assert.Equal("Approved", review.State);
    }
}
