using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DaisiGit.Core.Enums;

namespace DaisiGit.IntegrationTests;

/// <summary>
/// Integration tests for PAT authentication on git smart HTTP protocol endpoints
/// and the dg CLI tool (auth, repo, push, pull, clone).
/// Creates a test repo on startup and cleans up on teardown.
/// </summary>
[Collection("Integration")]
public class GitAuthTests : IAsyncLifetime
{
    private readonly TestClient _client;
    private readonly string _testId = $"{DateTime.UtcNow:HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
    private readonly List<(string owner, string slug)> _createdRepos = [];
    private readonly HttpClient _http;
    private RepoDto _repo = null!;

    public GitAuthTests()
    {
        if (!TestConfig.IsConfigured)
            throw new InvalidOperationException("Set DAISIGIT_API_KEY env var");
        _client = new TestClient(TestConfig.ServerUrl, TestConfig.ApiKey);
        _http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        })
        { BaseAddress = new Uri(TestConfig.ServerUrl.TrimEnd('/') + "/") };
    }

    public async Task InitializeAsync()
    {
        _repo = await _client.CreateRepoAsync($"auth-test-{_testId}", "Git auth integration tests",
            GitRepoVisibility.Public);
        _createdRepos.Add((_repo.OwnerName, _repo.Slug));
    }

    public async Task DisposeAsync()
    {
        foreach (var (owner, slug) in _createdRepos)
            try { await _client.DeleteRepoAsync(owner, slug); } catch { }
        _client.Dispose();
        _http.Dispose();
    }

    // ── Helper: build Basic auth header ──

    private static AuthenticationHeaderValue BasicAuth(string username, string password)
    {
        var bytes = Encoding.UTF8.GetBytes($"{username}:{password}");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }

    // ═══════════════════════════════════════════════════════════
    // Git Smart HTTP — PAT Authentication
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task T01_ReceivePack_WithValidPAT_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_repo.OwnerName}/{_repo.Slug}.git/info/refs?service=git-receive-pack");
        request.Headers.Authorization = BasicAuth("token", TestConfig.ApiKey);

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task T02_UploadPack_WithValidPAT_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_repo.OwnerName}/{_repo.Slug}.git/info/refs?service=git-upload-pack");
        request.Headers.Authorization = BasicAuth("token", TestConfig.ApiKey);

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task T03_ReceivePack_WithoutAuth_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_repo.OwnerName}/{_repo.Slug}.git/info/refs?service=git-receive-pack");

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task T04_UploadPack_PublicRepo_WithoutAuth_Returns200()
    {
        // Public repos must be readable without authentication so anonymous
        // git clone works. _repo is created Public in InitializeAsync.
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_repo.OwnerName}/{_repo.Slug}.git/info/refs?service=git-upload-pack");

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task T04b_UploadPack_PrivateRepo_WithoutAuth_Returns401WithChallenge()
    {
        // Private repos must still demand auth, with WWW-Authenticate so
        // git clients prompt for credentials.
        var priv = await _client.CreateRepoAsync($"private-clone-{_testId}",
            "Private repo auth test", GitRepoVisibility.Private);
        _createdRepos.Add((priv.OwnerName, priv.Slug));

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{priv.OwnerName}/{priv.Slug}.git/info/refs?service=git-upload-pack");

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate,
            h => h.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task T04c_UploadPack_InternalRepo_WithoutAuth_Returns401()
    {
        // Internal repos require auth — anonymous bypass is Public-only.
        var internalRepo = await _client.CreateRepoAsync($"internal-clone-{_testId}",
            "Internal repo auth test", GitRepoVisibility.Internal);
        _createdRepos.Add((internalRepo.OwnerName, internalRepo.Slug));

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{internalRepo.OwnerName}/{internalRepo.Slug}.git/info/refs?service=git-upload-pack");

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task T04d_UploadPackPost_PublicRepo_WithoutAuth_NotChallenged()
    {
        // POST git-upload-pack on a public repo must not 401. The body here
        // is empty so the endpoint will respond with NAK + flush, but the auth
        // middleware should let the request through.
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{_repo.OwnerName}/{_repo.Slug}.git/git-upload-pack")
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/x-git-upload-pack-request") }
            }
        };

        var response = await _http.SendAsync(request);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task T04e_UploadPackPost_PrivateRepo_WithoutAuth_Returns401()
    {
        var priv = await _client.CreateRepoAsync($"private-uploadpost-{_testId}",
            "Private uploadpost test", GitRepoVisibility.Private);
        _createdRepos.Add((priv.OwnerName, priv.Slug));

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{priv.OwnerName}/{priv.Slug}.git/git-upload-pack")
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/x-git-upload-pack-request") }
            }
        };

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task T04f_ReceivePack_PublicRepo_WithoutAuth_Returns401()
    {
        // Push must always require auth, even on public repos. T03 already
        // covers this for info/refs?service=git-receive-pack; this asserts
        // the same for the POST form.
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{_repo.OwnerName}/{_repo.Slug}.git/git-receive-pack")
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/x-git-receive-pack-request") }
            }
        };

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate,
            h => h.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task T05_ReceivePack_WithInvalidPAT_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_repo.OwnerName}/{_repo.Slug}.git/info/refs?service=git-receive-pack");
        request.Headers.Authorization = BasicAuth("token", "dg_invalid_token_12345678901234");

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task T06_ReceivePack_WithPATAsUsername_Returns200()
    {
        // Some git clients send the PAT as the username instead of password
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_repo.OwnerName}/{_repo.Slug}.git/info/refs?service=git-receive-pack");
        request.Headers.Authorization = BasicAuth(TestConfig.ApiKey, "x");

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task T07_ReceivePack_ContentType_IsCorrect()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_repo.OwnerName}/{_repo.Slug}.git/info/refs?service=git-receive-pack");
        request.Headers.Authorization = BasicAuth("token", TestConfig.ApiKey);

        var response = await _http.SendAsync(request);
        Assert.Equal("application/x-git-receive-pack-advertisement",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task T08_UploadPack_ContentType_IsCorrect()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_repo.OwnerName}/{_repo.Slug}.git/info/refs?service=git-upload-pack");
        request.Headers.Authorization = BasicAuth("token", TestConfig.ApiKey);

        var response = await _http.SendAsync(request);
        Assert.Equal("application/x-git-upload-pack-advertisement",
            response.Content.Headers.ContentType?.MediaType);
    }

    // ═══════════════════════════════════════════════════════════
    // REST API — PAT Authentication
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task T09_ApiKey_Header_Authenticates()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/git/repos");
        request.Headers.Add("X-Api-Key", TestConfig.ApiKey);

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task T10_Bearer_Header_Authenticates()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/git/repos");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestConfig.ApiKey);

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task T11_BasicAuth_PAT_Authenticates_Api()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/git/auth/whoami");
        request.Headers.Authorization = BasicAuth("token", TestConfig.ApiKey);

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task T12_InvalidApiKey_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/git/repos");
        request.Headers.Add("X-Api-Key", "dg_totally_invalid_key_here_123");

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task T13_NoAuth_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/git/repos");

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════
    // CLI Endpoints
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task T14_CliVersion_ReturnsVersion()
    {
        var response = await _http.GetAsync("cli/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var version = (await response.Content.ReadAsStringAsync()).Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
    }

    [Fact]
    public async Task T15_CliDownload_InvalidBinary_Returns404()
    {
        var response = await _http.GetAsync("cli/download/malicious-file.exe");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════
    // Full Repo Lifecycle via API with PAT
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task T16_FullRepoLifecycle()
    {
        // Create
        var repo = await _client.CreateRepoAsync($"lifecycle-{_testId}",
            "Lifecycle test", GitRepoVisibility.Private);
        _createdRepos.Add((repo.OwnerName, repo.Slug));
        Assert.Equal("Private", repo.Visibility);

        // Get
        var fetched = await _client.GetRepoAsync(repo.OwnerName, repo.Slug);
        Assert.NotNull(fetched);
        Assert.Equal(repo.Slug, fetched.Slug);

        // Branches
        var branches = await _client.ListBranchesAsync(repo.OwnerName, repo.Slug);
        Assert.Contains(branches, b => b.Name == "main");

        // Commits (initial commit)
        var commits = await _client.ListCommitsAsync(repo.OwnerName, repo.Slug);
        Assert.NotEmpty(commits);

        // Browse tree
        var tree = await _client.GetTreeAsync(repo.OwnerName, repo.Slug);
        Assert.NotNull(tree);

        // Star
        await _client.StarRepoAsync(repo.OwnerName, repo.Slug);
        var starred = await _client.GetRepoAsync(repo.OwnerName, repo.Slug);
        Assert.True(starred!.StarCount >= 1);

        // Unstar
        await _client.UnstarRepoAsync(repo.OwnerName, repo.Slug);

        // Issue lifecycle
        var issue = await _client.CreateIssueAsync(repo.OwnerName, repo.Slug,
            "Lifecycle Issue", "Test description");
        Assert.Equal("Lifecycle Issue", issue.Title);

        var comment = await _client.AddIssueCommentAsync(repo.OwnerName, repo.Slug,
            issue.Number, "Test comment");
        Assert.Equal("Test comment", comment.Body);

        var closed = await _client.CloseIssueAsync(repo.OwnerName, repo.Slug, issue.Number);
        Assert.Equal(IssueStatus.Closed, closed.Status);

        // PR lifecycle
        var pr = await _client.CreatePrAsync(repo.OwnerName, repo.Slug,
            "Lifecycle PR", "main", "main");
        Assert.Equal("Lifecycle PR", pr.Title);

        var review = await _client.SubmitReviewAsync(repo.OwnerName, repo.Slug,
            pr.Number, "approved", "Looks good");
        Assert.Equal("Approved", review.State);

        // Events
        await Task.Delay(500);
        var events = await _client.ListEventsAsync(repo.OwnerName, repo.Slug);
        Assert.NotEmpty(events);

        // Delete (cleanup tracked in _createdRepos)
        await _client.DeleteRepoAsync(repo.OwnerName, repo.Slug);
        _createdRepos.RemoveAll(r => r.owner == repo.OwnerName && r.slug == repo.Slug);

        var deleted = await _client.GetRepoAsync(repo.OwnerName, repo.Slug);
        Assert.Null(deleted);
    }

    // ═══════════════════════════════════════════════════════════
    // Fork Lifecycle with Cleanup
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task T17_ForkLifecycle()
    {
        // Create source repo
        var src = await _client.CreateRepoAsync($"fork-src-{_testId}",
            "Fork source", GitRepoVisibility.Public);
        _createdRepos.Add((src.OwnerName, src.Slug));

        // Fork it
        var fork = await _client.ForkRepoAsync(src.OwnerName, src.Slug);
        _createdRepos.Add((fork.OwnerName, fork.Slug));

        // Verify fork metadata
        Assert.Equal(src.id, fork.ForkedFromId);

        // Verify fork has same commits
        var srcCommits = await _client.ListCommitsAsync(src.OwnerName, src.Slug);
        var forkCommits = await _client.ListCommitsAsync(fork.OwnerName, fork.Slug);
        Assert.Equal(srcCommits[0].Sha, forkCommits[0].Sha);

        // Verify fork branches
        var forkBranches = await _client.ListBranchesAsync(fork.OwnerName, fork.Slug);
        Assert.Contains(forkBranches, b => b.Name == "main");
    }

    // ═══════════════════════════════════════════════════════════
    // Git Smart HTTP — Permission Enforcement
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task T18_ReceivePack_NonexistentRepo_Returns404Or403()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "nonexistent-owner/nonexistent-repo.git/info/refs?service=git-receive-pack");
        request.Headers.Authorization = BasicAuth("token", TestConfig.ApiKey);

        var response = await _http.SendAsync(request);
        // Server should return 403 or 404 for repos that don't exist
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden ||
                    response.StatusCode == HttpStatusCode.NotFound,
            $"Expected 403 or 404, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task T19_ReceivePack_MalformedBasicAuth_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_repo.OwnerName}/{_repo.Slug}.git/info/refs?service=git-receive-pack");
        // Send Basic auth with no colon separator
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("no-colon-here")));

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
