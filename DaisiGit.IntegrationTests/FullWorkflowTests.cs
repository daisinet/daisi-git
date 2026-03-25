using DaisiGit.Core.Enums;

namespace DaisiGit.IntegrationTests;

/// <summary>
/// End-to-end integration tests against a running DaisiGit dev server.
/// Uses a single shared repo for most tests to minimize Cosmos/Blob usage.
/// </summary>
[Collection("Integration")]
public class FullWorkflowTests : IAsyncLifetime
{
    private readonly TestClient _client;
    private readonly string _testId = DateTime.UtcNow.ToString("HHmmss");
    private readonly List<(string owner, string slug)> _createdRepos = [];
    private RepoDto _repo = null!;

    public FullWorkflowTests()
    {
        if (!TestConfig.IsConfigured)
            throw new InvalidOperationException("Set DAISIGIT_API_KEY env var");
        _client = new TestClient(TestConfig.ServerUrl, TestConfig.ApiKey);
    }

    public async Task InitializeAsync()
    {
        _repo = await _client.CreateRepoAsync($"test-{_testId}", "Integration test repo",
            GitRepoVisibility.Public);
        _createdRepos.Add((_repo.OwnerName, _repo.Slug));
    }

    public async Task DisposeAsync()
    {
        foreach (var (owner, slug) in _createdRepos)
            try { await _client.DeleteRepoAsync(owner, slug); } catch { }
        _client.Dispose();
    }

    // ── Repository ──

    [Fact]
    public void T01_RepoCreated()
    {
        Assert.NotNull(_repo);
        Assert.Equal("main", _repo.DefaultBranch);
        Assert.Equal("Public", _repo.Visibility);
    }

    [Fact]
    public async Task T02_GetRepository()
    {
        var fetched = await _client.GetRepoAsync(_repo.OwnerName, _repo.Slug);
        Assert.NotNull(fetched);
        Assert.Equal(_repo.Slug, fetched.Slug);
    }

    // ── Branches and Commits ──

    [Fact]
    public async Task T03_ListBranches()
    {
        var branches = await _client.ListBranchesAsync(_repo.OwnerName, _repo.Slug);
        Assert.NotEmpty(branches);
        Assert.Contains(branches, b => b.Name == "main");
    }

    [Fact]
    public async Task T04_ListCommits()
    {
        var commits = await _client.ListCommitsAsync(_repo.OwnerName, _repo.Slug);
        Assert.NotEmpty(commits);
        Assert.Equal("Initial commit", commits[0].MessageFirstLine);
    }

    [Fact]
    public async Task T05_BrowseTree()
    {
        var tree = await _client.GetTreeAsync(_repo.OwnerName, _repo.Slug);
        Assert.NotNull(tree);
        Assert.False(tree.IsFile);
    }

    // ── Issues ──

    [Fact]
    public async Task T06_IssueLifecycle()
    {
        var issue = await _client.CreateIssueAsync(_repo.OwnerName, _repo.Slug,
            "Test Issue", "Description");
        Assert.Equal("Test Issue", issue.Title);
        Assert.Equal(IssueStatus.Open, issue.Status);

        var issues = await _client.ListIssuesAsync(_repo.OwnerName, _repo.Slug);
        Assert.Contains(issues, i => i.Number == issue.Number);

        var closed = await _client.CloseIssueAsync(_repo.OwnerName, _repo.Slug, issue.Number);
        Assert.Equal(IssueStatus.Closed, closed.Status);
    }

    [Fact]
    public async Task T07_IssueComment()
    {
        var issue = await _client.CreateIssueAsync(_repo.OwnerName, _repo.Slug, "Comment Test");
        var comment = await _client.AddIssueCommentAsync(_repo.OwnerName, _repo.Slug,
            issue.Number, "Test comment");
        Assert.Equal("Test comment", comment.Body);
    }

    // ── Pull Requests ──

    [Fact]
    public async Task T08_PullRequestLifecycle()
    {
        var pr = await _client.CreatePrAsync(_repo.OwnerName, _repo.Slug,
            "Test PR", "main", "main");
        Assert.Equal("Test PR", pr.Title);
        Assert.Equal(PullRequestStatus.Open, pr.Status);

        var prs = await _client.ListPrsAsync(_repo.OwnerName, _repo.Slug);
        Assert.Contains(prs, p => p.Number == pr.Number);
    }

    [Fact]
    public async Task T09_ReviewOnPR()
    {
        var pr = await _client.CreatePrAsync(_repo.OwnerName, _repo.Slug, "Review PR", "main", "main");
        var review = await _client.SubmitReviewAsync(_repo.OwnerName, _repo.Slug,
            pr.Number, "approved", "LGTM");
        Assert.Equal("Approved", review.State);
    }

    // ── Stars ──

    [Fact]
    public async Task T10_StarAndUnstar()
    {
        await _client.StarRepoAsync(_repo.OwnerName, _repo.Slug);
        var starred = await _client.GetRepoAsync(_repo.OwnerName, _repo.Slug);
        Assert.True(starred!.StarCount > 0);

        await _client.UnstarRepoAsync(_repo.OwnerName, _repo.Slug);
    }

    // ── Forks ──

    [Fact]
    public async Task T11_ForkRepository()
    {
        // Create a second repo to fork (can't fork to same owner)
        var src = await _client.CreateRepoAsync($"fork-src-{_testId}", visibility: GitRepoVisibility.Public);
        _createdRepos.Add((src.OwnerName, src.Slug));

        var fork = await _client.ForkRepoAsync(src.OwnerName, src.Slug);
        _createdRepos.Add((fork.OwnerName, fork.Slug));
        Assert.Equal(src.id, fork.ForkedFromId);
    }

    // ── Workflows ──

    [Fact]
    public async Task T12_WorkflowLifecycle()
    {
        var wf = await _client.CreateWorkflowAsync(_repo.OwnerName, _repo.Slug,
            "Test Workflow", GitTriggerType.PushToRef);
        Assert.Equal("Test Workflow", wf.Name);

        var wfs = await _client.ListWorkflowsAsync(_repo.OwnerName, _repo.Slug);
        Assert.Contains(wfs, w => w.Name == "Test Workflow");
    }

    // ── Events ──

    [Fact]
    public async Task T13_EventsEmitted()
    {
        await _client.CreateIssueAsync(_repo.OwnerName, _repo.Slug, "Event Test");
        await Task.Delay(500);

        var events = await _client.ListEventsAsync(_repo.OwnerName, _repo.Slug);
        Assert.NotEmpty(events);
    }

    // ── Duplicate Prevention ──

    [Fact]
    public async Task T14_DuplicateRepoFails()
    {
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await _client.CreateRepoAsync(_repo.Name);
        });
    }
}
