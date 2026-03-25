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
    private readonly List<(string owner, string slug)> _createdRepos = [];

    public FullWorkflowTests()
    {
        if (!TestConfig.IsConfigured)
            throw new InvalidOperationException(
                "Set DAISIGIT_API_KEY env var to a valid personal access token (dg_...)");
        _client = new TestClient(TestConfig.ServerUrl, TestConfig.ApiKey);
    }

    /// <summary>Creates a repo and tracks it for cleanup.</summary>
    private async Task<RepoDto> CreateAndTrackRepoAsync(string name,
        string? description = null, GitRepoVisibility visibility = GitRepoVisibility.Private)
    {
        var repo = await _client.CreateRepoAsync(name, description, visibility);
        _createdRepos.Add((repo.OwnerName, repo.Slug));
        return repo;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Delete all repos created during this test run
        foreach (var (owner, slug) in _createdRepos)
        {
            try { await _client.DeleteRepoAsync(owner, slug); } catch { }
        }
        _client.Dispose();
    }

    // ── 1. Repository Creation ──

    [Fact]
    public async Task T01_CreateRepository()
    {
        var repo = await CreateAndTrackRepoAsync($"test-repo-{_testId}", "Integration test repo", GitRepoVisibility.Public);

        Assert.NotNull(repo);
        Assert.Equal($"test-repo-{_testId}", repo.Name);
        Assert.Equal("Public", repo.Visibility);
        Assert.Equal("main", repo.DefaultBranch);
    }

    [Fact]
    public async Task T02_ListRepositories()
    {
        // Create a repo, then verify it's retrievable by owner/slug
        var repo = await CreateAndTrackRepoAsync($"list-test-{_testId}");
        var fetched = await _client.GetRepoAsync(repo.OwnerName, repo.Slug);

        Assert.NotNull(fetched);
        Assert.Equal(repo.Slug, fetched.Slug);
    }

    [Fact]
    public async Task T03_GetRepository()
    {
        var created = await CreateAndTrackRepoAsync($"get-test-{_testId}");
        var repo = await _client.GetRepoAsync(created.OwnerName, created.Slug);

        Assert.NotNull(repo);
        Assert.Equal(created.Slug, repo.Slug);
    }

    // ── 2. Branches and Commits ──

    [Fact]
    public async Task T04_ListBranches()
    {
        var repo = await CreateAndTrackRepoAsync($"branch-test-{_testId}");
        var branches = await _client.ListBranchesAsync(repo.OwnerName, repo.Slug);

        Assert.NotEmpty(branches);
        Assert.Contains(branches, b => b.Name == "main");
    }

    [Fact]
    public async Task T05_ListCommits()
    {
        var repo = await CreateAndTrackRepoAsync($"commit-test-{_testId}");
        var commits = await _client.ListCommitsAsync(repo.OwnerName, repo.Slug);

        Assert.NotEmpty(commits);
        Assert.Equal("Initial commit", commits[0].MessageFirstLine);
    }

    [Fact]
    public async Task T06_BrowseTree()
    {
        var repo = await CreateAndTrackRepoAsync($"tree-test-{_testId}");
        var tree = await _client.GetTreeAsync(repo.OwnerName, repo.Slug);

        // New repo has empty tree (initial commit has no files)
        Assert.NotNull(tree);
        Assert.False(tree.IsFile);
    }

    // ── 3. Issues ──

    [Fact]
    public async Task T07_CreateIssue()
    {
        var repo = await CreateAndTrackRepoAsync($"issue-test-{_testId}");
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
        var repo = await CreateAndTrackRepoAsync($"issuelist-{_testId}");
        await _client.CreateIssueAsync(repo.OwnerName, repo.Slug, "Issue A");
        await _client.CreateIssueAsync(repo.OwnerName, repo.Slug, "Issue B");

        var issues = await _client.ListIssuesAsync(repo.OwnerName, repo.Slug);
        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public async Task T09_CloseIssue()
    {
        var repo = await CreateAndTrackRepoAsync($"issueclose-{_testId}");
        var issue = await _client.CreateIssueAsync(repo.OwnerName, repo.Slug, "Close Me");

        var closed = await _client.CloseIssueAsync(repo.OwnerName, repo.Slug, issue.Number);
        Assert.Equal(IssueStatus.Closed, closed.Status);
    }

    [Fact]
    public async Task T10_IssueComment()
    {
        var repo = await CreateAndTrackRepoAsync($"issuecomment-{_testId}");
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
        var repo = await CreateAndTrackRepoAsync($"pr-test-{_testId}");

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
        var repo = await CreateAndTrackRepoAsync($"prlist-{_testId}");
        await _client.CreatePrAsync(repo.OwnerName, repo.Slug, "PR A", "main", "main");
        await _client.CreatePrAsync(repo.OwnerName, repo.Slug, "PR B", "main", "main");

        var prs = await _client.ListPrsAsync(repo.OwnerName, repo.Slug);
        Assert.Equal(2, prs.Count);
    }

    // ── 5. Stars ──

    [Fact]
    public async Task T13_StarAndUnstar()
    {
        var repo = await CreateAndTrackRepoAsync($"star-test-{_testId}");

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
        var repo = await CreateAndTrackRepoAsync($"fork-src-{_testId}", visibility: GitRepoVisibility.Public);

        var fork = await _client.ForkRepoAsync(repo.OwnerName, repo.Slug);
        _createdRepos.Add((fork.OwnerName, fork.Slug));
        Assert.NotNull(fork);
        Assert.Equal(repo.id, fork.ForkedFromId);
    }

    // ── 7. Workflows ──

    [Fact]
    public async Task T15_CreateWorkflow()
    {
        var repo = await CreateAndTrackRepoAsync($"workflow-test-{_testId}");

        var wf = await _client.CreateWorkflowAsync(repo.OwnerName, repo.Slug,
            "Test Workflow", GitTriggerType.PushToRef);

        Assert.NotNull(wf);
        Assert.Equal("Test Workflow", wf.Name);
        Assert.True(wf.IsEnabled);
    }

    [Fact]
    public async Task T16_ListWorkflows()
    {
        var repo = await CreateAndTrackRepoAsync($"wflist-{_testId}");
        await _client.CreateWorkflowAsync(repo.OwnerName, repo.Slug, "WF A", GitTriggerType.PushToRef);
        await _client.CreateWorkflowAsync(repo.OwnerName, repo.Slug, "WF B", GitTriggerType.IssueCreated);

        var wfs = await _client.ListWorkflowsAsync(repo.OwnerName, repo.Slug);
        Assert.Equal(2, wfs.Count);
    }

    // ── 8. Events ──

    [Fact]
    public async Task T17_EventsEmitted()
    {
        var repo = await CreateAndTrackRepoAsync($"events-test-{_testId}");

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
        var repo = await CreateAndTrackRepoAsync($"dup-test-{_testId}");

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await CreateAndTrackRepoAsync($"dup-test-{_testId}");
        });
    }

    // ── 10. Reviews ──

    [Fact]
    public async Task T19_SubmitReview()
    {
        var repo = await CreateAndTrackRepoAsync($"review-test-{_testId}");
        var pr = await _client.CreatePrAsync(repo.OwnerName, repo.Slug, "Review PR", "main", "main");

        var review = await _client.SubmitReviewAsync(repo.OwnerName, repo.Slug,
            pr.Number, "approved", "Looks good!");

        Assert.NotNull(review);
        Assert.Equal("Approved", review.State);
    }
}
