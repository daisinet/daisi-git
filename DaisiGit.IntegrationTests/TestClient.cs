using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;

namespace DaisiGit.IntegrationTests;

/// <summary>
/// HTTP client for integration tests using the X-Test-ApiKey auth bypass.
/// </summary>
public class TestClient : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TestClient(string serverUrl, string apiKey)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    // ── Repos ──

    public async Task<GitRepository> CreateRepoAsync(string name, string? description = null,
        GitRepoVisibility visibility = GitRepoVisibility.Private)
    {
        var response = await _http.PostAsJsonAsync("api/git/repos",
            new { name, description, visibility }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GitRepository>(JsonOptions))!;
    }

    public async Task<RepoDto?> GetRepoAsync(string owner, string slug)
    {
        var response = await _http.GetAsync($"api/git/repos/{owner}/{slug}");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<RepoDto>(JsonOptions);
    }

    public async Task<List<RepoDto>> ListReposAsync()
    {
        return (await _http.GetFromJsonAsync<List<RepoDto>>("api/git/repos", JsonOptions))!;
    }

    public async Task DeleteRepoAsync(string owner, string slug)
    {
        // Delete via the settings page API — not yet a direct API endpoint
        // For now we'll skip cleanup or call the internal endpoint
    }

    // ── Branches ──

    public async Task<List<BranchDto>> ListBranchesAsync(string owner, string slug)
    {
        return (await _http.GetFromJsonAsync<List<BranchDto>>($"api/git/repos/{owner}/{slug}/branches", JsonOptions))!;
    }

    // ── Issues ──

    public async Task<Issue> CreateIssueAsync(string owner, string slug, string title, string? description = null)
    {
        var response = await _http.PostAsJsonAsync($"api/git/repos/{owner}/{slug}/issues",
            new { title, description }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Issue>(JsonOptions))!;
    }

    public async Task<List<Issue>> ListIssuesAsync(string owner, string slug, string? status = null)
    {
        var path = $"api/git/repos/{owner}/{slug}/issues";
        if (status != null) path += $"?status={status}";
        return (await _http.GetFromJsonAsync<List<Issue>>(path, JsonOptions))!;
    }

    public async Task<Issue> CloseIssueAsync(string owner, string slug, int number)
    {
        var response = await _http.PatchAsJsonAsync($"api/git/repos/{owner}/{slug}/issues/{number}",
            new { action = "close" }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Issue>(JsonOptions))!;
    }

    // ── Pull Requests ──

    public async Task<PullRequest> CreatePrAsync(string owner, string slug,
        string title, string sourceBranch, string targetBranch = "main", string? description = null)
    {
        var response = await _http.PostAsJsonAsync($"api/git/repos/{owner}/{slug}/pulls",
            new { title, description, sourceBranch, targetBranch }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PullRequest>(JsonOptions))!;
    }

    public async Task<List<PullRequest>> ListPrsAsync(string owner, string slug, string? status = null)
    {
        var path = $"api/git/repos/{owner}/{slug}/pulls";
        if (status != null) path += $"?status={status}";
        return (await _http.GetFromJsonAsync<List<PullRequest>>(path, JsonOptions))!;
    }

    public async Task<MergeResultDto> MergePrAsync(string owner, string slug, int number, string strategy = "merge")
    {
        var response = await _http.PostAsJsonAsync($"api/git/repos/{owner}/{slug}/pulls/{number}/merge",
            new { strategy }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MergeResultDto>(JsonOptions))!;
    }

    // ── Comments ──

    public async Task<Comment> AddIssueCommentAsync(string owner, string slug, int issueNumber, string body)
    {
        var response = await _http.PostAsJsonAsync($"api/git/repos/{owner}/{slug}/issues/{issueNumber}/comments",
            new { body }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Comment>(JsonOptions))!;
    }

    // ── Reviews ──

    public async Task<ReviewDto> SubmitReviewAsync(string owner, string slug, int prNumber,
        string state, string? body = null)
    {
        var response = await _http.PostAsJsonAsync($"api/git/repos/{owner}/{slug}/pulls/{prNumber}/reviews",
            new { state, body }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReviewDto>(JsonOptions))!;
    }

    // ── Forks ──

    public async Task<GitRepository> ForkRepoAsync(string owner, string slug)
    {
        var response = await _http.PostAsJsonAsync($"api/git/repos/{owner}/{slug}/forks", new { }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GitRepository>(JsonOptions))!;
    }

    // ── Stars ──

    public async Task StarRepoAsync(string owner, string slug)
    {
        var response = await _http.PutAsync($"api/git/repos/{owner}/{slug}/star", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task UnstarRepoAsync(string owner, string slug)
    {
        var response = await _http.DeleteAsync($"api/git/repos/{owner}/{slug}/star");
        response.EnsureSuccessStatusCode();
    }

    // ── Commits ──

    public async Task<List<CommitDto>> ListCommitsAsync(string owner, string slug, string branch = "main")
    {
        return (await _http.GetFromJsonAsync<List<CommitDto>>($"api/git/repos/{owner}/{slug}/commits/{branch}", JsonOptions))!;
    }

    // ── File browsing ──

    public async Task<TreeResultDto?> GetTreeAsync(string owner, string slug, string branch = "main", string path = "")
    {
        var response = await _http.GetAsync($"api/git/repos/{owner}/{slug}/tree/{branch}/{path}");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TreeResultDto>(JsonOptions);
    }

    // ── Workflows ──

    public async Task<GitWorkflow> CreateWorkflowAsync(string owner, string slug,
        string name, GitTriggerType triggerType)
    {
        var response = await _http.PostAsJsonAsync($"api/git/repos/{owner}/{slug}/workflows",
            new { name, triggerType, steps = new List<object>() }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GitWorkflow>(JsonOptions))!;
    }

    public async Task<List<GitWorkflow>> ListWorkflowsAsync(string owner, string slug)
    {
        return (await _http.GetFromJsonAsync<List<GitWorkflow>>($"api/git/repos/{owner}/{slug}/workflows", JsonOptions))!;
    }

    // ── Events ──

    public async Task<List<GitEvent>> ListEventsAsync(string owner, string slug)
    {
        return (await _http.GetFromJsonAsync<List<GitEvent>>($"api/git/repos/{owner}/{slug}/events", JsonOptions))!;
    }

    public void Dispose() => _http.Dispose();
}

// DTOs for deserialization
public class BranchDto { public string Name { get; set; } = ""; public string Sha { get; set; } = ""; public bool IsDefault { get; set; } }
public class CommitDto { public string Sha { get; set; } = ""; public string AuthorName { get; set; } = ""; public string MessageFirstLine { get; set; } = ""; }
public class TreeResultDto { public string Path { get; set; } = ""; public bool IsFile { get; set; } public List<TreeEntryDto> Entries { get; set; } = []; }
public class TreeEntryDto { public string Name { get; set; } = ""; public string Mode { get; set; } = ""; }
public class MergeResultDto { public bool Success { get; set; } public string? MergeCommitSha { get; set; } public string? Error { get; set; } }
public class ReviewDto { public string Id { get; set; } = ""; public string State { get; set; } = ""; }
public class RepoDto { public string id { get; set; } = ""; public string Name { get; set; } = ""; public string Slug { get; set; } = ""; public string OwnerName { get; set; } = ""; public string? Description { get; set; } public string DefaultBranch { get; set; } = "main"; public string? Visibility { get; set; } public int StarCount { get; set; } public int ForkCount { get; set; } public string? ForkedFromId { get; set; } }
