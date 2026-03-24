using System.Text.Json;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Services;

namespace DaisiGit.Web.Endpoints;

/// <summary>
/// REST API endpoints for DaisiGit. Used by the SDK and secure tool provider.
/// All endpoints are prefixed with /api/git/.
/// </summary>
public static class DaisiGitApiEndpoints
{
    public static void MapDaisiGitApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/git").RequireAuthorization();

        // Repositories
        api.MapGet("/repos", ListRepositories);
        api.MapGet("/repos/{owner}/{slug}", GetRepository);
        api.MapPost("/repos", CreateRepository);

        // Branches
        api.MapGet("/repos/{owner}/{slug}/branches", ListBranches);

        // File browsing
        api.MapGet("/repos/{owner}/{slug}/tree/{branch}/{**path}", GetTree);
        api.MapGet("/repos/{owner}/{slug}/blob/{branch}/{**path}", GetBlob);

        // Commits
        api.MapGet("/repos/{owner}/{slug}/commits/{branch}", ListCommits);
        api.MapGet("/repos/{owner}/{slug}/commit/{sha}", GetCommit);

        // Issues
        api.MapGet("/repos/{owner}/{slug}/issues", ListIssues);
        api.MapGet("/repos/{owner}/{slug}/issues/{number:int}", GetIssue);
        api.MapPost("/repos/{owner}/{slug}/issues", CreateIssue);
        api.MapPatch("/repos/{owner}/{slug}/issues/{number:int}", UpdateIssue);

        // Pull requests
        api.MapGet("/repos/{owner}/{slug}/pulls", ListPullRequests);
        api.MapGet("/repos/{owner}/{slug}/pulls/{number:int}", GetPullRequest);
        api.MapPost("/repos/{owner}/{slug}/pulls", CreatePullRequest);
        api.MapPatch("/repos/{owner}/{slug}/pulls/{number:int}", UpdatePullRequest);
        api.MapPost("/repos/{owner}/{slug}/pulls/{number:int}/merge", MergePullRequest);

        // Comments (on issues and PRs)
        api.MapGet("/repos/{owner}/{slug}/issues/{number:int}/comments", ListIssueComments);
        api.MapPost("/repos/{owner}/{slug}/issues/{number:int}/comments", CreateIssueComment);
        api.MapGet("/repos/{owner}/{slug}/pulls/{number:int}/comments", ListPrComments);
        api.MapPost("/repos/{owner}/{slug}/pulls/{number:int}/comments", CreatePrComment);

        // Reviews
        api.MapGet("/repos/{owner}/{slug}/pulls/{number:int}/reviews", ListReviews);
        api.MapPost("/repos/{owner}/{slug}/pulls/{number:int}/reviews", SubmitReview);
        api.MapGet("/repos/{owner}/{slug}/pulls/{number:int}/diff-comments", ListDiffComments);

        // Forks
        api.MapPost("/repos/{owner}/{slug}/forks", ForkRepository);
        api.MapGet("/repos/{owner}/{slug}/forks", ListForks);

        // Stars
        api.MapPut("/repos/{owner}/{slug}/star", StarRepository);
        api.MapDelete("/repos/{owner}/{slug}/star", UnstarRepository);

        // Explore
        api.MapGet("/explore", ExploreRepositories);

        // Account settings
        api.MapGet("/account/settings", GetAccountSettings);
        api.MapPut("/account/settings/storage", SetDefaultStorageProvider);
    }

    // ── Repository endpoints ──

    private static async Task<IResult> ListRepositories(
        HttpContext ctx, RepositoryService repoService)
    {
        var userId = GetUserId(ctx);
        var repos = await repoService.GetRepositoriesByOwnerAsync(userId);
        return Results.Ok(repos.Select(RepoDto));
    }

    private static async Task<IResult> GetRepository(
        string owner, string slug, RepositoryService repoService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        return repo == null ? Results.NotFound() : Results.Ok(RepoDto(repo));
    }

    private static async Task<IResult> CreateRepository(
        HttpContext ctx, CreateRepoRequest req, RepositoryService repoService)
    {
        var userId = GetUserId(ctx);
        var userName = GetUserName(ctx);
        var repo = await repoService.CreateRepositoryAsync(
            GetAccountId(ctx), userId, userName,
            req.Name, req.Description, req.Visibility, req.StorageProvider);
        return Results.Created($"/api/git/repos/{repo.OwnerName}/{repo.Slug}", RepoDto(repo));
    }

    // ── Branch endpoints ──

    private static async Task<IResult> ListBranches(
        string owner, string slug,
        RepositoryService repoService, BrowseService browseService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();
        var branches = await browseService.GetBranchesAsync(repo.id, repo.DefaultBranch);
        return Results.Ok(branches);
    }

    // ── File browsing endpoints ──

    private static async Task<IResult> GetTree(
        string owner, string slug, string branch, string? path,
        RepositoryService repoService, BrowseService browseService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var sha = await browseService.ResolveRefAsync(repo.id, branch);
        if (sha == null) return Results.NotFound("Branch not found");

        var result = await browseService.GetTreeAtPathAsync(repo.id, sha, path ?? "");
        if (result == null) return Results.NotFound();

        return Results.Ok(new
        {
            result.Path,
            result.CommitSha,
            result.TreeSha,
            result.IsFile,
            Entries = result.Entries?.Select(e => new { e.Name, e.Mode, e.Sha }) ?? []
        });
    }

    private static async Task<IResult> GetBlob(
        string owner, string slug, string branch, string? path,
        RepositoryService repoService, BrowseService browseService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var sha = await browseService.ResolveRefAsync(repo.id, branch);
        if (sha == null) return Results.NotFound("Branch not found");

        var result = await browseService.GetTreeAtPathAsync(repo.id, sha, path ?? "");
        if (result == null || !result.IsFile || result.FileSha == null)
            return Results.NotFound();

        var content = await browseService.GetFileContentAsync(repo.id, result.FileSha);
        if (content == null) return Results.NotFound();

        return Results.Ok(new
        {
            result.FileName,
            content.Sha,
            content.SizeBytes,
            content.IsBinary,
            content.Text
        });
    }

    // ── Commit endpoints ──

    private static async Task<IResult> ListCommits(
        string owner, string slug, string branch,
        int? skip, int? take,
        RepositoryService repoService, BrowseService browseService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var sha = await browseService.ResolveRefAsync(repo.id, branch);
        if (sha == null) return Results.NotFound("Branch not found");

        var commits = await browseService.GetCommitLogAsync(repo.id, sha, take ?? 50, skip ?? 0);
        return Results.Ok(commits);
    }

    private static async Task<IResult> GetCommit(
        string owner, string slug, string sha,
        RepositoryService repoService, BrowseService browseService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var commit = await browseService.GetCommitAsync(repo.id, sha);
        if (commit == null) return Results.NotFound();

        var diffs = await browseService.GetCommitDiffAsync(repo.id, sha);
        return Results.Ok(new { Commit = commit, Files = diffs });
    }

    // ── Issue endpoints ──

    private static async Task<IResult> ListIssues(
        string owner, string slug, string? status,
        RepositoryService repoService, IssueService issueService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        IssueStatus? filter = status?.ToLowerInvariant() switch
        {
            "open" => IssueStatus.Open,
            "closed" => IssueStatus.Closed,
            _ => null
        };

        var issues = await issueService.ListAsync(repo.id, filter);
        return Results.Ok(issues);
    }

    private static async Task<IResult> GetIssue(
        string owner, string slug, int number,
        RepositoryService repoService, IssueService issueService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var issue = await issueService.GetByNumberAsync(repo.id, number);
        return issue == null ? Results.NotFound() : Results.Ok(issue);
    }

    private static async Task<IResult> CreateIssue(
        HttpContext ctx, string owner, string slug, CreateIssueRequest req,
        RepositoryService repoService, IssueService issueService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var issue = await issueService.CreateAsync(
            repo.id, req.Title, req.Description,
            GetUserId(ctx), GetUserName(ctx), req.Labels);
        return Results.Created($"/api/git/repos/{owner}/{slug}/issues/{issue.Number}", issue);
    }

    private static async Task<IResult> UpdateIssue(
        HttpContext ctx, string owner, string slug, int number, UpdateIssueRequest req,
        RepositoryService repoService, IssueService issueService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var issue = await issueService.GetByNumberAsync(repo.id, number);
        if (issue == null) return Results.NotFound();

        if (req.Action?.ToLowerInvariant() == "close")
            issue = await issueService.CloseAsync(issue);
        else if (req.Action?.ToLowerInvariant() == "reopen")
            issue = await issueService.ReopenAsync(issue);

        if (req.Title != null) issue.Title = req.Title;
        if (req.Description != null) issue.Description = req.Description;

        issue = await issueService.UpdateAsync(issue);
        return Results.Ok(issue);
    }

    // ── Pull Request endpoints ──

    private static async Task<IResult> ListPullRequests(
        string owner, string slug, string? status,
        RepositoryService repoService, PullRequestService prService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        PullRequestStatus? filter = status?.ToLowerInvariant() switch
        {
            "open" => PullRequestStatus.Open,
            "closed" => PullRequestStatus.Closed,
            "merged" => PullRequestStatus.Merged,
            _ => null
        };

        var prs = await prService.ListAsync(repo.id, filter);
        return Results.Ok(prs);
    }

    private static async Task<IResult> GetPullRequest(
        string owner, string slug, int number,
        RepositoryService repoService, PullRequestService prService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var pr = await prService.GetByNumberAsync(repo.id, number);
        return pr == null ? Results.NotFound() : Results.Ok(pr);
    }

    private static async Task<IResult> CreatePullRequest(
        HttpContext ctx, string owner, string slug, CreatePrRequest req,
        RepositoryService repoService, PullRequestService prService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var pr = await prService.CreateAsync(
            repo.id, req.Title, req.Description,
            req.SourceBranch, req.TargetBranch,
            GetUserId(ctx), GetUserName(ctx), req.Labels);
        return Results.Created($"/api/git/repos/{owner}/{slug}/pulls/{pr.Number}", pr);
    }

    private static async Task<IResult> UpdatePullRequest(
        HttpContext ctx, string owner, string slug, int number, UpdatePrRequest req,
        RepositoryService repoService, PullRequestService prService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var pr = await prService.GetByNumberAsync(repo.id, number);
        if (pr == null) return Results.NotFound();

        if (req.Action?.ToLowerInvariant() == "close")
            pr = await prService.CloseAsync(pr);
        else if (req.Action?.ToLowerInvariant() == "reopen")
            pr = await prService.ReopenAsync(pr);

        if (req.Title != null) pr.Title = req.Title;
        if (req.Description != null) pr.Description = req.Description;

        pr = await prService.UpdateAsync(pr);
        return Results.Ok(pr);
    }

    private static async Task<IResult> MergePullRequest(
        HttpContext ctx, string owner, string slug, int number,
        MergePrRequest? req,
        RepositoryService repoService, PullRequestService prService, MergeService mergeService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var pr = await prService.GetByNumberAsync(repo.id, number);
        if (pr == null) return Results.NotFound();

        var strategy = req?.Strategy?.ToLowerInvariant() switch
        {
            "squash" => MergeStrategy.Squash,
            _ => MergeStrategy.Merge
        };

        var userName = GetUserName(ctx);
        var result = await mergeService.MergeAsync(pr, repo, userName, $"{userName}@daisinet", strategy);
        return result.Success
            ? Results.Ok(result)
            : Results.Conflict(result);
    }

    // ── Comment endpoints ──

    private static async Task<IResult> ListIssueComments(
        string owner, string slug, int number,
        RepositoryService repoService, IssueService issueService, CommentService commentService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var issue = await issueService.GetByNumberAsync(repo.id, number);
        if (issue == null) return Results.NotFound();

        var comments = await commentService.GetCommentsAsync(repo.id, issue.id);
        return Results.Ok(comments);
    }

    private static async Task<IResult> CreateIssueComment(
        HttpContext ctx, string owner, string slug, int number, CreateCommentRequest req,
        RepositoryService repoService, IssueService issueService, CommentService commentService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var issue = await issueService.GetByNumberAsync(repo.id, number);
        if (issue == null) return Results.NotFound();

        var comment = await commentService.CreateAsync(
            repo.id, issue.id, nameof(Issue),
            req.Body, GetUserId(ctx), GetUserName(ctx));
        return Results.Created("", comment);
    }

    private static async Task<IResult> ListPrComments(
        string owner, string slug, int number,
        RepositoryService repoService, PullRequestService prService, CommentService commentService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var pr = await prService.GetByNumberAsync(repo.id, number);
        if (pr == null) return Results.NotFound();

        var comments = await commentService.GetCommentsAsync(repo.id, pr.id);
        return Results.Ok(comments);
    }

    private static async Task<IResult> CreatePrComment(
        HttpContext ctx, string owner, string slug, int number, CreateCommentRequest req,
        RepositoryService repoService, PullRequestService prService, CommentService commentService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var pr = await prService.GetByNumberAsync(repo.id, number);
        if (pr == null) return Results.NotFound();

        var comment = await commentService.CreateAsync(
            repo.id, pr.id, nameof(PullRequest),
            req.Body, GetUserId(ctx), GetUserName(ctx));
        return Results.Created("", comment);
    }

    // ── Review endpoints ──

    private static async Task<IResult> ListReviews(
        string owner, string slug, int number,
        RepositoryService repoService, PullRequestService prService, ReviewService reviewService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var pr = await prService.GetByNumberAsync(repo.id, number);
        if (pr == null) return Results.NotFound();

        var reviews = await reviewService.ListReviewsAsync(repo.id, number);
        return Results.Ok(reviews);
    }

    private static async Task<IResult> SubmitReview(
        HttpContext ctx, string owner, string slug, int number, SubmitReviewRequest req,
        RepositoryService repoService, PullRequestService prService, ReviewService reviewService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var pr = await prService.GetByNumberAsync(repo.id, number);
        if (pr == null) return Results.NotFound();

        var state = req.State?.ToLowerInvariant() switch
        {
            "approved" or "approve" => ReviewState.Approved,
            "changes_requested" or "request_changes" => ReviewState.ChangesRequested,
            _ => ReviewState.Commented
        };

        var diffComments = req.DiffComments?.Select(dc => new DiffComment
        {
            Path = dc.Path,
            Line = dc.Line,
            Side = dc.Side?.ToLowerInvariant() == "left" ? DiffSide.Left : DiffSide.Right,
            Body = dc.Body
        }).ToList();

        var review = await reviewService.SubmitReviewAsync(
            repo.id, pr.id, number,
            GetUserId(ctx), GetUserName(ctx),
            state, req.Body, diffComments);

        return Results.Created($"/api/git/repos/{owner}/{slug}/pulls/{number}/reviews", review);
    }

    private static async Task<IResult> ListDiffComments(
        string owner, string slug, int number,
        RepositoryService repoService, PullRequestService prService, ReviewService reviewService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var pr = await prService.GetByNumberAsync(repo.id, number);
        if (pr == null) return Results.NotFound();

        var comments = await reviewService.GetDiffCommentsAsync(repo.id, number);
        return Results.Ok(comments);
    }

    // ── Fork endpoints ──

    private static async Task<IResult> ForkRepository(
        HttpContext ctx, string owner, string slug, RepositoryService repoService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var fork = await repoService.ForkRepositoryAsync(
            GetAccountId(ctx), GetUserId(ctx), GetUserName(ctx), repo);
        return Results.Created($"/api/git/repos/{fork.OwnerName}/{fork.Slug}", RepoDto(fork));
    }

    private static async Task<IResult> ListForks(
        string owner, string slug, RepositoryService repoService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        var forks = await repoService.GetForksAsync(repo.id);
        return Results.Ok(forks.Select(RepoDto));
    }

    // ── Star endpoints ──

    private static async Task<IResult> StarRepository(
        HttpContext ctx, string owner, string slug, RepositoryService repoService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        await repoService.StarAsync(GetUserId(ctx), GetUserName(ctx), repo.id);
        return Results.NoContent();
    }

    private static async Task<IResult> UnstarRepository(
        HttpContext ctx, string owner, string slug, RepositoryService repoService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        await repoService.UnstarAsync(GetUserId(ctx), repo.id);
        return Results.NoContent();
    }

    // ── Explore endpoint ──

    private static async Task<IResult> ExploreRepositories(
        int? skip, int? take, RepositoryService repoService)
    {
        var repos = await repoService.GetPublicReposAsync(skip ?? 0, take ?? 20);
        return Results.Ok(repos.Select(RepoDto));
    }

    // ── Account settings endpoints ──

    private static async Task<IResult> GetAccountSettings(
        HttpContext ctx, AccountSettingsService settingsService)
    {
        var settings = await settingsService.GetSettingsAsync(GetAccountId(ctx));
        return Results.Ok(new
        {
            settings.DefaultStorageProvider,
            settings.CreatedUtc,
            settings.UpdatedUtc
        });
    }

    private static async Task<IResult> SetDefaultStorageProvider(
        HttpContext ctx, SetStorageProviderRequest req, AccountSettingsService settingsService)
    {
        var settings = await settingsService.SetDefaultStorageProviderAsync(
            GetAccountId(ctx), req.Provider);
        return Results.Ok(new
        {
            settings.DefaultStorageProvider,
            settings.UpdatedUtc
        });
    }

    // ── Helpers ──

    private static string GetUserId(HttpContext ctx) => ctx.Items["userId"] as string ?? "";
    private static string GetUserName(HttpContext ctx) => ctx.Items["userName"] as string ?? "";
    private static string GetAccountId(HttpContext ctx) => ctx.Items["accountId"] as string ?? "";

    private static object RepoDto(GitRepository r) => new
    {
        r.id,
        r.Name,
        r.Slug,
        r.OwnerName,
        r.Description,
        r.DefaultBranch,
        Visibility = r.Visibility.ToString(),
        StorageProvider = r.StorageProvider?.ToString(),
        r.IsEmpty,
        r.StarCount,
        r.ForkCount,
        r.ForkedFromId,
        r.ForkedFromOwnerName,
        r.ForkedFromSlug,
        r.CreatedUtc
    };
}

// ── Request DTOs ──

public record CreateRepoRequest(string Name, string? Description, GitRepoVisibility Visibility = GitRepoVisibility.Private, StorageProvider? StorageProvider = null);
public record SetStorageProviderRequest(StorageProvider Provider);
public record CreateIssueRequest(string Title, string? Description, List<string>? Labels = null);
public record UpdateIssueRequest(string? Title = null, string? Description = null, string? Action = null);
public record CreatePrRequest(string Title, string? Description, string SourceBranch, string TargetBranch, List<string>? Labels = null);
public record UpdatePrRequest(string? Title = null, string? Description = null, string? Action = null);
public record MergePrRequest(string? Strategy = null);
public record CreateCommentRequest(string Body);
public record SubmitReviewRequest(string? State, string? Body, List<DiffCommentRequest>? DiffComments = null);
public record DiffCommentRequest(string Path, int Line, string Body, string? Side = null);
