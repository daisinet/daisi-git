namespace DaisiGit.IntegrationTests;

/// <summary>
/// Integration tests for org-level repos and cross-owner forking.
/// Uses a single shared org repo for most tests.
/// </summary>
[Collection("Integration")]
public class OrgRepoTests : IAsyncLifetime
{
    private readonly TestClient _client;
    private readonly string _testId = DateTime.UtcNow.ToString("HHmmss");
    private readonly List<(string owner, string slug)> _createdRepos = [];
    private const string TestOrg = "daisi-test-harness";
    private RepoDto _orgRepo = null!;

    public OrgRepoTests()
    {
        if (!TestConfig.IsConfigured)
            throw new InvalidOperationException("Set DAISIGIT_API_KEY env var");
        _client = new TestClient(TestConfig.ServerUrl, TestConfig.ApiKey);
    }

    public async Task InitializeAsync()
    {
        _orgRepo = await _client.CreateRepoAsync($"org-test-{_testId}", owner: TestOrg,
            visibility: GitRepoVisibility.Public);
        _createdRepos.Add((_orgRepo.OwnerName, _orgRepo.Slug));
    }

    public async Task DisposeAsync()
    {
        foreach (var (owner, slug) in _createdRepos)
            try { await _client.DeleteRepoAsync(owner, slug); } catch { }
        _client.Dispose();
    }

    // ── Org repo basics ──

    [Fact]
    public void T01_OrgRepoCreated()
    {
        Assert.Equal(TestOrg, _orgRepo.OwnerName);
    }

    [Fact]
    public async Task T02_OrgRepoBranchAndCommit()
    {
        var branches = await _client.ListBranchesAsync(_orgRepo.OwnerName, _orgRepo.Slug);
        Assert.Contains(branches, b => b.Name == "main");

        var commits = await _client.ListCommitsAsync(_orgRepo.OwnerName, _orgRepo.Slug);
        Assert.NotEmpty(commits);
    }

    [Fact]
    public async Task T03_OrgRepoIssues()
    {
        var issue = await _client.CreateIssueAsync(_orgRepo.OwnerName, _orgRepo.Slug, "Org Issue");
        Assert.Equal("Org Issue", issue.Title);

        var closed = await _client.CloseIssueAsync(_orgRepo.OwnerName, _orgRepo.Slug, issue.Number);
        Assert.Equal(IssueStatus.Closed, closed.Status);
    }

    [Fact]
    public async Task T04_OrgRepoPR()
    {
        var pr = await _client.CreatePrAsync(_orgRepo.OwnerName, _orgRepo.Slug, "Org PR", "main", "main");
        Assert.Equal("Org PR", pr.Title);
    }

    [Fact]
    public async Task T05_OrgRepoStar()
    {
        await _client.StarRepoAsync(_orgRepo.OwnerName, _orgRepo.Slug);
        var starred = await _client.GetRepoAsync(_orgRepo.OwnerName, _orgRepo.Slug);
        Assert.True(starred!.StarCount > 0);
        await _client.UnstarRepoAsync(_orgRepo.OwnerName, _orgRepo.Slug);
    }

    [Fact]
    public async Task T06_OrgRepoWorkflow()
    {
        var wf = await _client.CreateWorkflowAsync(_orgRepo.OwnerName, _orgRepo.Slug,
            "Org WF", GitTriggerType.IssueCreated);
        Assert.Equal("Org WF", wf.Name);
    }

    [Fact]
    public async Task T07_OrgRepoEvents()
    {
        await _client.CreateIssueAsync(_orgRepo.OwnerName, _orgRepo.Slug, "Event Org");
        await Task.Delay(500);
        var events = await _client.ListEventsAsync(_orgRepo.OwnerName, _orgRepo.Slug);
        Assert.NotEmpty(events);
    }

    [Fact]
    public async Task T08_OrgRepoReview()
    {
        var pr = await _client.CreatePrAsync(_orgRepo.OwnerName, _orgRepo.Slug, "Review", "main", "main");
        var review = await _client.SubmitReviewAsync(_orgRepo.OwnerName, _orgRepo.Slug,
            pr.Number, "approved");
        Assert.Equal("Approved", review.State);
    }

    // ── Cross-owner forking ──

    [Fact]
    public async Task T09_ForkOrgToPersonal()
    {
        var fork = await _client.ForkRepoAsync(_orgRepo.OwnerName, _orgRepo.Slug);
        _createdRepos.Add((fork.OwnerName, fork.Slug));

        Assert.Equal(_orgRepo.id, fork.ForkedFromId);
        Assert.NotEqual(_orgRepo.OwnerName, fork.OwnerName);
    }

    [Fact]
    public async Task T10_ForkPreservesCommits()
    {
        var origCommits = await _client.ListCommitsAsync(_orgRepo.OwnerName, _orgRepo.Slug);

        var fork = await _client.ForkRepoAsync(_orgRepo.OwnerName, _orgRepo.Slug);
        _createdRepos.Add((fork.OwnerName, fork.Slug));

        var forkCommits = await _client.ListCommitsAsync(fork.OwnerName, fork.Slug);
        Assert.NotEmpty(forkCommits);
        Assert.Equal(origCommits[0].Sha, forkCommits[0].Sha);
    }
}
