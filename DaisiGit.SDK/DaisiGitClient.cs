using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;

namespace DaisiGit.SDK;

/// <summary>
/// Client SDK for programmatic access to the DaisiGit REST API.
/// </summary>
public class DaisiGitClient : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Creates a new DaisiGit client pointing at the given server URL.
    /// </summary>
    /// <param name="baseUrl">DaisiGit server base URL (e.g., https://git.daisinet.app)</param>
    /// <param name="apiKeyOrSession">API key (dg_...) or session token for authentication</param>
    public DaisiGitClient(string baseUrl, string apiKeyOrSession)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        if (apiKeyOrSession.StartsWith("dg_"))
            _http.DefaultRequestHeaders.Add("X-Api-Key", apiKeyOrSession);
        else
            _http.DefaultRequestHeaders.Add("X-Session-Id", apiKeyOrSession);
    }

    /// <summary>
    /// Creates a new DaisiGit client using an existing HttpClient.
    /// </summary>
    public DaisiGitClient(HttpClient httpClient)
    {
        _http = httpClient;
    }

    // ── Repositories ──

    /// <summary>
    /// Lists repositories accessible to the current user.
    /// </summary>
    public async Task<List<GitRepository>> ListRepositoriesAsync()
    {
        return await GetAsync<List<GitRepository>>("api/git/repos");
    }

    /// <summary>
    /// Gets a repository by owner and slug.
    /// </summary>
    public async Task<GitRepository?> GetRepositoryAsync(string owner, string slug)
    {
        return await GetAsync<GitRepository?>($"api/git/repos/{owner}/{slug}");
    }

    /// <summary>
    /// Creates a new repository.
    /// </summary>
    public async Task<GitRepository> CreateRepositoryAsync(string name, string? description = null, GitRepoVisibility visibility = GitRepoVisibility.Private)
    {
        return await PostAsync<GitRepository>("api/git/repos", new { name, description, visibility });
    }

    // ── Branches ──

    /// <summary>
    /// Lists branches in a repository.
    /// </summary>
    public async Task<List<BranchDto>> ListBranchesAsync(string owner, string slug)
    {
        return await GetAsync<List<BranchDto>>($"api/git/repos/{owner}/{slug}/branches");
    }

    /// <summary>
    /// Creates a new branch from an existing ref.
    /// </summary>
    public async Task<BranchDto> CreateBranchAsync(string owner, string slug, string name, string from = "main")
    {
        return await PostAsync<BranchDto>($"api/git/repos/{owner}/{slug}/branches",
            new { name, from });
    }

    /// <summary>
    /// Writes a file and creates a commit on the specified branch.
    /// Creates parent directories as needed.
    /// </summary>
    public async Task<FileCommitResult> WriteFileAsync(
        string owner, string slug, string path, string content,
        string message, string branch = "main")
    {
        return await PutAsync<FileCommitResult>($"api/git/repos/{owner}/{slug}/contents/{path}",
            new { content, message, branch });
    }

    // ── File Browsing ──

    /// <summary>
    /// Browses the file tree at a given branch and path.
    /// </summary>
    public async Task<TreeResult> GetTreeAsync(string owner, string slug, string branch = "main", string path = "")
    {
        return await GetAsync<TreeResult>($"api/git/repos/{owner}/{slug}/tree/{branch}/{path}");
    }

    /// <summary>
    /// Reads a file's content at a given branch and path.
    /// </summary>
    public async Task<FileResult> GetFileAsync(string owner, string slug, string path, string branch = "main")
    {
        return await GetAsync<FileResult>($"api/git/repos/{owner}/{slug}/blob/{branch}/{path}");
    }

    // ── Commits ──

    /// <summary>
    /// Lists commits on a branch.
    /// </summary>
    public async Task<List<CommitDto>> ListCommitsAsync(string owner, string slug, string branch = "main", int take = 50, int skip = 0)
    {
        return await GetAsync<List<CommitDto>>($"api/git/repos/{owner}/{slug}/commits/{branch}?take={take}&skip={skip}");
    }

    /// <summary>
    /// Gets a single commit with file diffs.
    /// </summary>
    public async Task<CommitDetailDto> GetCommitAsync(string owner, string slug, string sha)
    {
        return await GetAsync<CommitDetailDto>($"api/git/repos/{owner}/{slug}/commit/{sha}");
    }

    // ── Issues ──

    /// <summary>
    /// Lists issues in a repository.
    /// </summary>
    public async Task<List<Issue>> ListIssuesAsync(string owner, string slug, string? status = null)
    {
        var path = $"api/git/repos/{owner}/{slug}/issues";
        if (status != null) path += $"?status={status}";
        return await GetAsync<List<Issue>>(path);
    }

    /// <summary>
    /// Gets a single issue by number.
    /// </summary>
    public async Task<Issue?> GetIssueAsync(string owner, string slug, int number)
    {
        return await GetAsync<Issue?>($"api/git/repos/{owner}/{slug}/issues/{number}");
    }

    /// <summary>
    /// Creates a new issue.
    /// </summary>
    public async Task<Issue> CreateIssueAsync(string owner, string slug, string title, string? description = null)
    {
        return await PostAsync<Issue>($"api/git/repos/{owner}/{slug}/issues", new { title, description });
    }

    /// <summary>
    /// Closes an issue.
    /// </summary>
    public async Task<Issue> CloseIssueAsync(string owner, string slug, int number)
    {
        return await PatchAsync<Issue>($"api/git/repos/{owner}/{slug}/issues/{number}", new { action = "close" });
    }

    /// <summary>
    /// Reopens an issue.
    /// </summary>
    public async Task<Issue> ReopenIssueAsync(string owner, string slug, int number)
    {
        return await PatchAsync<Issue>($"api/git/repos/{owner}/{slug}/issues/{number}", new { action = "reopen" });
    }

    // ── Pull Requests ──

    /// <summary>
    /// Lists pull requests in a repository.
    /// </summary>
    public async Task<List<PullRequest>> ListPullRequestsAsync(string owner, string slug, string? status = null)
    {
        var path = $"api/git/repos/{owner}/{slug}/pulls";
        if (status != null) path += $"?status={status}";
        return await GetAsync<List<PullRequest>>(path);
    }

    /// <summary>
    /// Gets a single pull request by number.
    /// </summary>
    public async Task<PullRequest?> GetPullRequestAsync(string owner, string slug, int number)
    {
        return await GetAsync<PullRequest?>($"api/git/repos/{owner}/{slug}/pulls/{number}");
    }

    /// <summary>
    /// Creates a new pull request.
    /// </summary>
    public async Task<PullRequest> CreatePullRequestAsync(
        string owner, string slug, string title, string sourceBranch,
        string targetBranch = "main", string? description = null)
    {
        return await PostAsync<PullRequest>($"api/git/repos/{owner}/{slug}/pulls",
            new { title, description, sourceBranch, targetBranch });
    }

    /// <summary>
    /// Merges a pull request.
    /// </summary>
    public async Task<MergeResultDto> MergePullRequestAsync(string owner, string slug, int number, string strategy = "merge")
    {
        return await PostAsync<MergeResultDto>($"api/git/repos/{owner}/{slug}/pulls/{number}/merge",
            new { strategy });
    }

    // ── Reviews ──

    /// <summary>
    /// Lists reviews on a pull request.
    /// </summary>
    public async Task<List<ReviewDto>> ListReviewsAsync(string owner, string slug, int prNumber)
    {
        return await GetAsync<List<ReviewDto>>($"api/git/repos/{owner}/{slug}/pulls/{prNumber}/reviews");
    }

    /// <summary>
    /// Submits a review on a pull request.
    /// </summary>
    public async Task<ReviewDto> SubmitReviewAsync(
        string owner, string slug, int prNumber,
        string state, string? body = null, List<DiffCommentInput>? diffComments = null)
    {
        return await PostAsync<ReviewDto>($"api/git/repos/{owner}/{slug}/pulls/{prNumber}/reviews",
            new { state, body, diffComments });
    }

    /// <summary>
    /// Lists inline diff comments on a pull request.
    /// </summary>
    public async Task<List<DiffCommentDto>> ListDiffCommentsAsync(string owner, string slug, int prNumber)
    {
        return await GetAsync<List<DiffCommentDto>>($"api/git/repos/{owner}/{slug}/pulls/{prNumber}/diff-comments");
    }

    // ── Forks ──

    /// <summary>
    /// Forks a repository to the current user's account.
    /// </summary>
    public async Task<GitRepository> ForkRepositoryAsync(string owner, string slug)
    {
        return await PostAsync<GitRepository>($"api/git/repos/{owner}/{slug}/forks", new { });
    }

    /// <summary>
    /// Lists forks of a repository.
    /// </summary>
    public async Task<List<GitRepository>> ListForksAsync(string owner, string slug)
    {
        return await GetAsync<List<GitRepository>>($"api/git/repos/{owner}/{slug}/forks");
    }

    // ── Stars ──

    /// <summary>
    /// Stars a repository. Idempotent.
    /// </summary>
    public async Task StarRepositoryAsync(string owner, string slug)
    {
        await PutAsync($"api/git/repos/{owner}/{slug}/star");
    }

    /// <summary>
    /// Unstars a repository.
    /// </summary>
    public async Task UnstarRepositoryAsync(string owner, string slug)
    {
        await DeleteAsync($"api/git/repos/{owner}/{slug}/star");
    }

    // ── Explore ──

    /// <summary>
    /// Lists public repositories sorted by star count.
    /// </summary>
    public async Task<List<GitRepository>> ExploreRepositoriesAsync(int skip = 0, int take = 20)
    {
        return await GetAsync<List<GitRepository>>($"api/git/explore?skip={skip}&take={take}");
    }

    // ── Comments ──

    /// <summary>
    /// Lists comments on an issue.
    /// </summary>
    public async Task<List<Comment>> ListIssueCommentsAsync(string owner, string slug, int issueNumber)
    {
        return await GetAsync<List<Comment>>($"api/git/repos/{owner}/{slug}/issues/{issueNumber}/comments");
    }

    /// <summary>
    /// Adds a comment to an issue.
    /// </summary>
    public async Task<Comment> AddIssueCommentAsync(string owner, string slug, int issueNumber, string body)
    {
        return await PostAsync<Comment>($"api/git/repos/{owner}/{slug}/issues/{issueNumber}/comments",
            new { body });
    }

    /// <summary>
    /// Lists comments on a pull request.
    /// </summary>
    public async Task<List<Comment>> ListPrCommentsAsync(string owner, string slug, int prNumber)
    {
        return await GetAsync<List<Comment>>($"api/git/repos/{owner}/{slug}/pulls/{prNumber}/comments");
    }

    /// <summary>
    /// Adds a comment to a pull request.
    /// </summary>
    public async Task<Comment> AddPrCommentAsync(string owner, string slug, int prNumber, string body)
    {
        return await PostAsync<Comment>($"api/git/repos/{owner}/{slug}/pulls/{prNumber}/comments",
            new { body });
    }

    // ── HTTP helpers ──

    private async Task<T> GetAsync<T>(string path)
    {
        var response = await _http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<T> PostAsync<T>(string path, object body)
    {
        var response = await _http.PostAsJsonAsync(path, body, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<T> PatchAsync<T>(string path, object body)
    {
        var response = await _http.PatchAsJsonAsync(path, body, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task PutAsync(string path)
    {
        var response = await _http.PutAsync(path, null);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> PutAsync<T>(string path, object body)
    {
        var response = await _http.PutAsJsonAsync(path, body, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task DeleteAsync(string path)
    {
        var response = await _http.DeleteAsync(path);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}

// ── SDK DTOs ──

/// <summary>
/// Branch information returned by the API.
/// </summary>
public class BranchDto
{
    public string Name { get; set; } = "";
    public string Sha { get; set; } = "";
    public bool IsDefault { get; set; }
}

/// <summary>
/// Tree browsing result returned by the API.
/// </summary>
public class TreeResult
{
    public string Path { get; set; } = "";
    public string CommitSha { get; set; } = "";
    public string? TreeSha { get; set; }
    public bool IsFile { get; set; }
    public List<TreeEntryDto> Entries { get; set; } = [];
}

/// <summary>
/// A single entry in a tree listing.
/// </summary>
public class TreeEntryDto
{
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Sha { get; set; } = "";
}

/// <summary>
/// File content result returned by the API.
/// </summary>
public class FileResult
{
    public string? FileName { get; set; }
    public string Sha { get; set; } = "";
    public int SizeBytes { get; set; }
    public bool IsBinary { get; set; }
    public string? Text { get; set; }
}

/// <summary>
/// Commit info returned by the API.
/// </summary>
public class CommitDto
{
    public string Sha { get; set; } = "";
    public string ShortSha { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public DateTimeOffset AuthorDate { get; set; }
    public string Message { get; set; } = "";
    public string MessageFirstLine { get; set; } = "";
}

/// <summary>
/// Commit detail with file diffs returned by the API.
/// </summary>
public class CommitDetailDto
{
    public CommitDto Commit { get; set; } = new();
    public List<FileDiffDto> Files { get; set; } = [];
}

/// <summary>
/// File diff info returned by the API.
/// </summary>
public class FileDiffDto
{
    public string Path { get; set; } = "";
    public string Status { get; set; } = "";
    public string? OldContent { get; set; }
    public string? NewContent { get; set; }
}

/// <summary>
/// Merge result returned by the API.
/// </summary>
public class MergeResultDto
{
    public bool Success { get; set; }
    public string? MergeCommitSha { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Review information returned by the API.
/// </summary>
public class ReviewDto
{
    public string Id { get; set; } = "";
    public string State { get; set; } = "";
    public string? Body { get; set; }
    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public int PullRequestNumber { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// Inline diff comment returned by the API.
/// </summary>
public class DiffCommentDto
{
    public string Id { get; set; } = "";
    public string ReviewId { get; set; } = "";
    public string Path { get; set; } = "";
    public int Line { get; set; }
    public string Side { get; set; } = "";
    public string Body { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// Input for creating an inline diff comment as part of a review submission.
/// </summary>
public class DiffCommentInput
{
    public string Path { get; set; } = "";
    public int Line { get; set; }
    public string Body { get; set; } = "";
    public string? Side { get; set; }
}

/// <summary>
/// Result of writing a file via the contents API.
/// </summary>
public class FileCommitResult
{
    public string Sha { get; set; } = "";
    public string CommitSha { get; set; } = "";
    public string Branch { get; set; } = "";
}
