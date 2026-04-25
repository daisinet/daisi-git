using System.Text.Json;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Git;
using DaisiGit.Core.Models;
using DaisiGit.Data;
using DaisiGit.Services;

namespace DaisiGit.Web.Endpoints;

/// <summary>
/// REST API endpoints for DaisiGit. Used by the SDK and secure tool provider.
/// All repo-scoped endpoints verify the authenticated user has appropriate permissions.
/// </summary>
public static class DaisiGitApiEndpoints
{
    public static void MapDaisiGitApiEndpoints(this WebApplication app)
    {
        // Authenticated endpoints (mutations + user-specific)
        var api = app.MapGroup("/api/git").RequireAuthorization();

        // Repositories (create/delete require auth; get/list allow anonymous for public repos)
        api.MapPost("/repos", CreateRepository);
        api.MapPost("/repos/import", ImportRepository);
        api.MapGet("/repos/import/check", CheckExistingImport);
        api.MapPost("/repos/{owner}/{slug}/reimport", ReimportRepository);
        api.MapGet("/repos", ListRepositories);
        api.MapDelete("/repos/{owner}/{slug}", DeleteRepository);

        // Branches (create requires write access)
        api.MapPost("/repos/{owner}/{slug}/branches", CreateBranch);

        // File contents (write requires write access)
        api.MapPut("/repos/{owner}/{slug}/contents/{**path}", WriteFile);

        // Issues (create/update require auth)
        api.MapPost("/repos/{owner}/{slug}/issues", CreateIssue);
        api.MapPatch("/repos/{owner}/{slug}/issues/{number:int}", UpdateIssue);

        // Pull requests (create/update/merge require auth)
        api.MapPost("/repos/{owner}/{slug}/pulls", CreatePullRequest);
        api.MapPatch("/repos/{owner}/{slug}/pulls/{number:int}", UpdatePullRequest);
        api.MapPost("/repos/{owner}/{slug}/pulls/{number:int}/merge", MergePullRequest);

        // Comments (create requires auth)
        api.MapPost("/repos/{owner}/{slug}/issues/{number:int}/comments", CreateIssueComment);
        api.MapPost("/repos/{owner}/{slug}/pulls/{number:int}/comments", CreatePrComment);

        // Reviews (submit requires auth)
        api.MapPost("/repos/{owner}/{slug}/pulls/{number:int}/reviews", SubmitReview);

        // Forks (create requires auth)
        api.MapPost("/repos/{owner}/{slug}/forks", ForkRepository);

        // Secrets
        api.MapPut("/repos/{owner}/{slug}/secrets/{name}", SetSecret);
        api.MapGet("/repos/{owner}/{slug}/secrets", ListSecrets);
        api.MapDelete("/repos/{owner}/{slug}/secrets/{name}", DeleteSecret);

        // Stars (require auth)
        api.MapPut("/repos/{owner}/{slug}/star", StarRepository);
        api.MapDelete("/repos/{owner}/{slug}/star", UnstarRepository);

        // Organizations
        api.MapPost("/orgs", CreateOrg);
        api.MapGet("/orgs", ListOrgs);
        api.MapDelete("/orgs/{slug}", DeleteOrg);

        // Repo groups (org-level repo organizing)
        api.MapPost("/orgs/{slug}/groups", CreateRepoGroup);
        api.MapPut("/orgs/{slug}/groups/{id}", UpdateRepoGroup);
        api.MapDelete("/orgs/{slug}/groups/{id}", DeleteRepoGroup);
        api.MapPut("/repos/{owner}/{slug}/group", SetRepoGroup);

        // Vars (non-secret config)
        api.MapGet("/orgs/{slug}/vars", GetOrgVars);
        api.MapPut("/orgs/{slug}/vars", SetOrgVars);
        api.MapGet("/repos/{owner}/{slug}/vars", GetRepoVars);
        api.MapPut("/repos/{owner}/{slug}/vars", SetRepoVars);

        // GitHub bulk import (org-scoped)
        api.MapPost("/orgs/{slug}/import-from-github", StartGitHubImport);
        api.MapGet("/orgs/{slug}/import-from-github/{jobId}", GetGitHubImportJob);

        // Org activity is intentionally on the public group — anonymous viewers see
        // only public-repo data, authenticated viewers can opt into private repos.

        // Account settings (require auth)
        api.MapGet("/account/settings", GetAccountSettings);
        api.MapPut("/account/settings/storage", SetDefaultStorageProvider);
        api.MapPut("/account/settings/merge-policy", SetAccountMergePolicy);
        api.MapPut("/repos/{owner}/{slug}/merge-policy", SetRepoMergePolicy);

#if DEBUG
        // Debug endpoints (auth required, dev only)
        api.MapGet("/debug/repos/{owner}/{slug}/objects", DebugListObjects);
        api.MapGet("/debug/repos/{owner}/{slug}/objects/{sha}", DebugGetObject);
#endif

        // Anonymous-allowed endpoints (read-only; permission check handles public vs private)
        var pub = app.MapGroup("/api/git");

        pub.MapGet("/repos/{owner}/{slug}", GetRepository);
        pub.MapGet("/orgs/{slug}/groups", ListRepoGroups);
        pub.MapGet("/orgs/{slug}/activity", GetOrgActivity);
        pub.MapGet("/repos/{owner}/{slug}/commits-in-range", GetCommitsInRange);
        pub.MapGet("/repos/{owner}/{slug}/branches", ListBranches);
        pub.MapGet("/repos/{owner}/{slug}/tree/{branch}/{**path}", GetTree);
        pub.MapGet("/repos/{owner}/{slug}/blob/{branch}/{**path}", GetBlob);
        // Alternative: path as query param (workaround for IIS blocking .cs/.config in URL paths)
        pub.MapGet("/repos/{owner}/{slug}/blob/{branch}", GetBlobQuery);
        pub.MapGet("/repos/{owner}/{slug}/commits/{branch}", ListCommits);
        pub.MapGet("/repos/{owner}/{slug}/commit/{sha}", GetCommit);
        pub.MapGet("/repos/{owner}/{slug}/issues", ListIssues);
        pub.MapGet("/repos/{owner}/{slug}/issues/{number:int}", GetIssue);
        pub.MapGet("/repos/{owner}/{slug}/issues/{number:int}/comments", ListIssueComments);
        pub.MapGet("/repos/{owner}/{slug}/pulls", ListPullRequests);
        pub.MapGet("/repos/{owner}/{slug}/pulls/{number:int}", GetPullRequest);
        pub.MapGet("/repos/{owner}/{slug}/pulls/{number:int}/comments", ListPrComments);
        pub.MapGet("/repos/{owner}/{slug}/pulls/{number:int}/reviews", ListReviews);
        pub.MapGet("/repos/{owner}/{slug}/pulls/{number:int}/diff-comments", ListDiffComments);
        pub.MapGet("/repos/{owner}/{slug}/forks", ListForks);
        pub.MapGet("/explore", ExploreRepositories);

        // API keys
        api.MapPost("/auth/keys", CreateApiKey);
        api.MapGet("/auth/keys", ListApiKeys);
        api.MapDelete("/auth/keys/{id}", RevokeApiKey);
        api.MapGet("/auth/whoami", WhoAmI);
    }

    // ── Auth/API key endpoints ──

    private static async Task<IResult> CreateApiKey(
        HttpContext ctx, CreateApiKeyRequest req, ApiKeyService keyService)
    {
        var (key, rawToken) = await keyService.CreateKeyAsync(
            GetAccountId(ctx), GetUserId(ctx), GetUserName(ctx), req.Name);
        return Results.Created($"/api/git/auth/keys/{key.id}", new
        {
            key.id,
            key.Name,
            key.TokenPrefix,
            Token = rawToken,
            key.CreatedUtc,
            key.ExpiresUtc,
            Warning = "This token will only be shown once. Copy it now."
        });
    }

    private static async Task<IResult> ListApiKeys(
        HttpContext ctx, ApiKeyService keyService)
    {
        var keys = await keyService.ListKeysAsync(GetAccountId(ctx), GetUserId(ctx));
        return Results.Ok(keys.Select(k => new
        {
            k.id, k.Name, k.TokenPrefix, k.CreatedUtc, k.ExpiresUtc, k.LastUsedUtc
        }));
    }

    private static async Task<IResult> RevokeApiKey(
        HttpContext ctx, string id, ApiKeyService keyService)
    {
        await keyService.RevokeKeyAsync(id, GetAccountId(ctx));
        return Results.NoContent();
    }

    private static IResult WhoAmI(HttpContext ctx)
    {
        return Results.Ok(new
        {
            UserId = GetUserId(ctx),
            UserName = GetUserName(ctx),
            AccountId = GetAccountId(ctx)
        });
    }

    // ── Permission helpers ──

    private static async Task<(GitRepository? Repo, IResult? Error)> RequireRead(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return (null, Results.NotFound());
        if (!await permissionService.CanReadAsync(GetUserId(ctx), repo))
            return (null, Results.Forbid());
        return (repo, null);
    }

    private static async Task<(GitRepository? Repo, IResult? Error)> RequireWrite(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return (null, Results.NotFound());
        if (!await permissionService.CanWriteAsync(GetUserId(ctx), repo))
            return (null, Results.Forbid());
        return (repo, null);
    }

    // ── Repository endpoints ──

    private static async Task<IResult> ListRepositories(
        HttpContext ctx, string? owner, RepositoryService repoService)
    {
        if (!string.IsNullOrEmpty(owner))
        {
            var repos = await repoService.GetRepositoriesByOwnerAsync(owner);
            return Results.Ok(repos.Select(RepoDto));
        }

        // Default: list all repos in the account
        var accountId = GetAccountId(ctx);
        var allRepos = await repoService.GetRepositoriesAsync(accountId);
        return Results.Ok(allRepos.Select(RepoDto));
    }

    private static async Task<IResult> GetRepository(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;
        return Results.Ok(RepoDto(repo!));
    }

    private static async Task<IResult> CreateRepository(
        HttpContext ctx, CreateRepoRequest req,
        RepositoryService repoService, UserProfileService profileService,
        OrganizationService orgService)
    {
        var userId = GetUserId(ctx);
        var accountId = GetAccountId(ctx);
        string ownerId;
        string ownerName;

        if (!string.IsNullOrEmpty(req.Owner))
        {
            // Check if owner is an org
            var org = await orgService.GetBySlugAsync(req.Owner);
            if (org != null)
            {
                ownerId = org.id;
                ownerName = org.Slug;
            }
            else
            {
                // Must be the user's own handle
                var profile = await profileService.GetProfileAsync(userId, accountId);
                if (profile == null || profile.Handle != req.Owner)
                    return Results.BadRequest("Invalid owner. Specify your personal handle or an organization slug.");
                ownerId = userId;
                ownerName = profile.Handle;
            }
        }
        else
        {
            // No owner specified — use personal handle
            var profile = await profileService.GetProfileAsync(userId, accountId);
            if (profile == null || string.IsNullOrEmpty(profile.Handle))
                return Results.BadRequest("You must set up a personal handle before creating personal repositories. Go to Settings > Personal Profile.");
            ownerId = userId;
            ownerName = profile.Handle;
        }

        var repo = await repoService.CreateRepositoryAsync(
            accountId, ownerId, ownerName,
            req.Name, req.Description, req.Visibility, req.StorageProvider);
        return Results.Created($"/api/git/repos/{repo.OwnerName}/{repo.Slug}", RepoDto(repo));
    }

    private static async Task<IResult> ImportRepository(
        HttpContext ctx, ImportRepoRequest req,
        ImportService importService, UserProfileService profileService,
        OrganizationService orgService)
    {
        if (string.IsNullOrWhiteSpace(req.Url))
            return Results.BadRequest("Source URL is required.");

        var userId = GetUserId(ctx);
        var accountId = GetAccountId(ctx);
        string ownerId, ownerName;

        if (!string.IsNullOrEmpty(req.Owner))
        {
            var org = await orgService.GetBySlugAsync(req.Owner);
            if (org != null) { ownerId = org.id; ownerName = org.Slug; }
            else
            {
                var profile = await profileService.GetProfileAsync(userId, accountId);
                if (profile == null || profile.Handle != req.Owner)
                    return Results.BadRequest("Invalid owner.");
                ownerId = userId; ownerName = profile.Handle;
            }
        }
        else
        {
            var profile = await profileService.GetProfileAsync(userId, accountId);
            if (profile == null || string.IsNullOrEmpty(profile.Handle))
                return Results.BadRequest("Set up your personal handle first.");
            ownerId = userId; ownerName = profile.Handle;
        }

        try
        {
            var repo = await importService.ImportAsync(
                req.Url, accountId, ownerId, ownerName,
                req.Name, req.Description, req.Visibility);
            return Results.Created($"/api/git/repos/{repo.OwnerName}/{repo.Slug}", RepoDto(repo));
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Import failed: {ex.Message}");
        }
    }

    private static async Task<IResult> CheckExistingImport(
        HttpContext ctx, string url,
        ImportService importService)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Results.BadRequest("URL is required.");

        var accountId = GetAccountId(ctx);
        var existing = await importService.FindExistingImportsAsync(accountId, url);
        if (existing.Count == 0)
            return Results.Ok(new { found = false, repositories = Array.Empty<object>() });

        return Results.Ok(new
        {
            found = true,
            repositories = existing.Select(RepoDto).ToArray()
        });
    }

    private static async Task<IResult> ReimportRepository(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, PermissionService permissionService,
        ImportService importService)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        if (string.IsNullOrEmpty(repo!.ImportedFromUrl))
            return Results.BadRequest("This repository was not imported from an external URL.");

        try
        {
            var updated = await importService.ReimportAsync(repo);
            return Results.Ok(RepoDto(updated));
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Re-import failed: {ex.Message}");
        }
    }

    private static async Task<IResult> DeleteRepository(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, PermissionService permissionService)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        await repoService.DeleteRepositoryAsync(repo!.id, repo.AccountId);
        return Results.NoContent();
    }

    // ── Branch endpoints ──

    private static async Task<IResult> ListBranches(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, BrowseService browseService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;
        var branches = await browseService.GetBranchesAsync(repo!.id, repo.DefaultBranch);
        return Results.Ok(branches);
    }

    // ── File browsing endpoints ──

    private static async Task<IResult> GetTree(
        HttpContext ctx, string owner, string slug, string branch, string? path,
        RepositoryService repoService, BrowseService browseService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var sha = await browseService.ResolveRefAsync(repo!.id, branch);
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
        HttpContext ctx, string owner, string slug, string branch, string? path,
        RepositoryService repoService, BrowseService browseService, PermissionService permissionService)
    {
        return await GetBlobInternal(ctx, owner, slug, branch, path, repoService, browseService, permissionService);
    }

    /// <summary>
    /// Alternative blob endpoint: path as ?path= query parameter.
    /// Workaround for IIS/Azure App Service request filtering that blocks
    /// extensions like .cs, .config, .asax in URL path segments.
    /// </summary>
    private static async Task<IResult> GetBlobQuery(
        HttpContext ctx, string owner, string slug, string branch, string? path,
        RepositoryService repoService, BrowseService browseService, PermissionService permissionService)
    {
        // path comes from query string: ?path=code-reviewer/module.cs
        return await GetBlobInternal(ctx, owner, slug, branch, path, repoService, browseService, permissionService);
    }

    private static async Task<IResult> GetBlobInternal(
        HttpContext ctx, string owner, string slug, string branch, string? path,
        RepositoryService repoService, BrowseService browseService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var sha = await browseService.ResolveRefAsync(repo!.id, branch);
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
        HttpContext ctx, string owner, string slug, string branch,
        int? skip, int? take,
        RepositoryService repoService, BrowseService browseService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var sha = await browseService.ResolveRefAsync(repo!.id, branch);
        if (sha == null) return Results.NotFound("Branch not found");

        var commits = await browseService.GetCommitLogAsync(repo.id, sha, take ?? 50, skip ?? 0);
        return Results.Ok(commits);
    }

    private static async Task<IResult> GetCommit(
        HttpContext ctx, string owner, string slug, string sha,
        RepositoryService repoService, BrowseService browseService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var commit = await browseService.GetCommitAsync(repo!.id, sha);
        if (commit == null) return Results.NotFound();

        var diffs = await browseService.GetCommitDiffAsync(repo.id, sha);
        return Results.Ok(new { Commit = commit, Files = diffs });
    }

    // ── Issue endpoints ──

    private static async Task<IResult> ListIssues(
        HttpContext ctx, string owner, string slug, string? status,
        RepositoryService repoService, IssueService issueService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        IssueStatus? filter = status?.ToLowerInvariant() switch
        {
            "open" => IssueStatus.Open,
            "closed" => IssueStatus.Closed,
            _ => null
        };

        var issues = await issueService.ListAsync(repo!.id, filter);
        return Results.Ok(issues);
    }

    private static async Task<IResult> GetIssue(
        HttpContext ctx, string owner, string slug, int number,
        RepositoryService repoService, IssueService issueService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var issue = await issueService.GetByNumberAsync(repo!.id, number);
        return issue == null ? Results.NotFound() : Results.Ok(issue);
    }

    private static async Task<IResult> CreateIssue(
        HttpContext ctx, string owner, string slug, CreateIssueRequest req,
        RepositoryService repoService, IssueService issueService, PermissionService permissionService,
        GitEventService events)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var issue = await issueService.CreateAsync(
            repo!.id, req.Title, req.Description,
            GetUserId(ctx), GetUserName(ctx), req.Labels);

        await events.EmitAsync(repo.AccountId, repo.id, GitTriggerType.IssueCreated,
            GetUserId(ctx), GetUserName(ctx), new Dictionary<string, string>
            {
                ["issue.number"] = issue.Number.ToString(),
                ["issue.title"] = issue.Title
            });

        return Results.Created($"/api/git/repos/{owner}/{slug}/issues/{issue.Number}", issue);
    }

    private static async Task<IResult> UpdateIssue(
        HttpContext ctx, string owner, string slug, int number, UpdateIssueRequest req,
        RepositoryService repoService, IssueService issueService, PermissionService permissionService,
        GitEventService events)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var issue = await issueService.GetByNumberAsync(repo!.id, number);
        if (issue == null) return Results.NotFound();

        if (req.Action?.ToLowerInvariant() == "close")
        {
            issue = await issueService.CloseAsync(issue);
            await events.EmitAsync(repo.AccountId, repo.id, GitTriggerType.IssueClosed,
                GetUserId(ctx), GetUserName(ctx), new Dictionary<string, string>
                {
                    ["issue.number"] = issue.Number.ToString(),
                    ["issue.title"] = issue.Title
                });
        }
        else if (req.Action?.ToLowerInvariant() == "reopen")
        {
            issue = await issueService.ReopenAsync(issue);
            await events.EmitAsync(repo.AccountId, repo.id, GitTriggerType.IssueReopened,
                GetUserId(ctx), GetUserName(ctx), new Dictionary<string, string>
                {
                    ["issue.number"] = issue.Number.ToString(),
                    ["issue.title"] = issue.Title
                });
        }

        if (req.Title != null) issue.Title = req.Title;
        if (req.Description != null) issue.Description = req.Description;

        issue = await issueService.UpdateAsync(issue);
        return Results.Ok(issue);
    }

    // ── Pull Request endpoints ──

    private static async Task<IResult> ListPullRequests(
        HttpContext ctx, string owner, string slug, string? status,
        RepositoryService repoService, PullRequestService prService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        PullRequestStatus? filter = status?.ToLowerInvariant() switch
        {
            "open" => PullRequestStatus.Open,
            "closed" => PullRequestStatus.Closed,
            "merged" => PullRequestStatus.Merged,
            _ => null
        };

        var prs = await prService.ListAsync(repo!.id, filter);
        return Results.Ok(prs);
    }

    private static async Task<IResult> GetPullRequest(
        HttpContext ctx, string owner, string slug, int number,
        RepositoryService repoService, PullRequestService prService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var pr = await prService.GetByNumberAsync(repo!.id, number);
        return pr == null ? Results.NotFound() : Results.Ok(pr);
    }

    private static async Task<IResult> CreatePullRequest(
        HttpContext ctx, string owner, string slug, CreatePrRequest req,
        RepositoryService repoService, PullRequestService prService,
        MergeService mergeService, GitRefService refService,
        AccountSettingsService settingsService, RepoActivityRollupService rollupService,
        PermissionService permissionService, GitEventService events)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var pr = await prService.CreateAsync(
            repo!.id, req.Title, req.Description,
            req.SourceBranch, req.TargetBranch,
            GetUserId(ctx), GetUserName(ctx), req.Labels);

        await events.EmitAsync(repo.AccountId, repo.id, GitTriggerType.PullRequestCreated,
            GetUserId(ctx), GetUserName(ctx), new Dictionary<string, string>
            {
                ["pr.number"] = pr.Number.ToString(),
                ["pr.title"] = pr.Title,
                ["pr.sourceBranch"] = pr.SourceBranch,
                ["pr.targetBranch"] = pr.TargetBranch
            });

        // Auto-merge: if the effective policy says so, try to merge immediately. Failures
        // (e.g. branch protections, non-fast-forward) are silent — the PR is still created
        // and the user can merge manually. Strategy preference: merge-commit, fall back to
        // squash if merge-commit is disabled.
        var policy = await settingsService.GetMergePolicyAsync(repo);
        if (policy.AutoMergeEnabled)
        {
            var strategy = policy.AllowMergeCommit ? MergeStrategy.Merge
                : policy.AllowSquashMerge ? MergeStrategy.Squash
                : (MergeStrategy?)null;
            if (strategy != null)
            {
                _ = await PerformMergeAsync(pr, repo, GetUserName(ctx), GetUserId(ctx),
                    strategy.Value, policy, mergeService, refService, events, rollupService);
                pr = await prService.GetByNumberAsync(repo.id, pr.Number) ?? pr;
            }
        }

        return Results.Created($"/api/git/repos/{owner}/{slug}/pulls/{pr.Number}", pr);
    }

    private static async Task<IResult> UpdatePullRequest(
        HttpContext ctx, string owner, string slug, int number, UpdatePrRequest req,
        RepositoryService repoService, PullRequestService prService, PermissionService permissionService,
        GitEventService events)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var pr = await prService.GetByNumberAsync(repo!.id, number);
        if (pr == null) return Results.NotFound();

        if (req.Action?.ToLowerInvariant() == "close")
        {
            pr = await prService.CloseAsync(pr);
            await events.EmitAsync(repo.AccountId, repo.id, GitTriggerType.PullRequestClosed,
                GetUserId(ctx), GetUserName(ctx), new Dictionary<string, string>
                {
                    ["pr.number"] = pr.Number.ToString(),
                    ["pr.title"] = pr.Title
                });
        }
        else if (req.Action?.ToLowerInvariant() == "reopen")
        {
            pr = await prService.ReopenAsync(pr);
        }

        if (req.Title != null) pr.Title = req.Title;
        if (req.Description != null) pr.Description = req.Description;

        pr = await prService.UpdateAsync(pr);
        return Results.Ok(pr);
    }

    private static async Task<IResult> MergePullRequest(
        HttpContext ctx, string owner, string slug, int number,
        MergePrRequest? req,
        RepositoryService repoService, PullRequestService prService, MergeService mergeService,
        GitRefService refService, AccountSettingsService settingsService,
        RepoActivityRollupService rollupService,
        PermissionService permissionService, GitEventService events)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var pr = await prService.GetByNumberAsync(repo!.id, number);
        if (pr == null) return Results.NotFound();

        var strategy = req?.Strategy?.ToLowerInvariant() switch
        {
            "squash" => MergeStrategy.Squash,
            _ => MergeStrategy.Merge
        };

        var policy = await settingsService.GetMergePolicyAsync(repo);
        if (strategy == MergeStrategy.Merge && !policy.AllowMergeCommit)
            return Results.Conflict(new MergeResult { Success = false, Error = "Merge commits are disabled for this repository." });
        if (strategy == MergeStrategy.Squash && !policy.AllowSquashMerge)
            return Results.Conflict(new MergeResult { Success = false, Error = "Squash merges are disabled for this repository." });

        var userName = GetUserName(ctx);
        var result = await PerformMergeAsync(pr, repo, userName, GetUserId(ctx), strategy,
            policy, mergeService, refService, events, rollupService);

        return result.Success
            ? Results.Ok(result)
            : Results.Conflict(result);
    }

    /// <summary>
    /// Performs the merge, fires the PullRequestMerged + PushToRef events, and applies the
    /// delete-branch-on-merge policy. Shared by the explicit merge endpoint and auto-merge.
    /// </summary>
    private static async Task<MergeResult> PerformMergeAsync(
        PullRequest pr, GitRepository repo, string userName, string userId,
        MergeStrategy strategy, MergePolicy policy,
        MergeService mergeService, GitRefService refService, GitEventService events,
        RepoActivityRollupService rollupService)
    {
        var result = await mergeService.MergeAsync(pr, repo, userName, $"{userName}@daisinet", strategy);
        if (!result.Success) return result;

        await events.EmitAsync(repo.AccountId, repo.id, GitTriggerType.PullRequestMerged,
            userId, userName, new Dictionary<string, string>
            {
                ["pr.number"] = pr.Number.ToString(),
                ["pr.title"] = pr.Title,
                ["pr.sourceBranch"] = pr.SourceBranch,
                ["pr.targetBranch"] = pr.TargetBranch,
                ["pr.mergeCommitSha"] = result.MergeCommitSha ?? "",
                ["pr.mergeStrategy"] = strategy.ToString()
            });

        await events.EmitAsync(repo.AccountId, repo.id, GitTriggerType.PushToRef,
            userId, userName, new Dictionary<string, string>
            {
                ["push.ref"] = $"refs/heads/{pr.TargetBranch}",
                ["push.branch"] = pr.TargetBranch,
                ["push.tag"] = "",
                ["push.oldSha"] = "",
                ["push.newSha"] = result.MergeCommitSha ?? "",
                ["push.isCreate"] = "False",
                ["push.isDelete"] = "False",
                ["push.source"] = "merge",
                ["pr.number"] = pr.Number.ToString()
            });

        // Bump the per-day commit rollup with just the commits the merge added to the
        // target branch (merge commit + any source-branch commits not yet present).
        try { await rollupService.ApplyPushAsync(repo, oldSha: result.PreviousTargetSha, newSha: result.MergeCommitSha); }
        catch { /* rollup is best-effort */ }

        // Delete the source branch when policy says so. Never delete the default branch
        // (a PR sourced from the default branch is unusual but possible — be defensive).
        if (policy.DeleteBranchOnMerge
            && !string.Equals(pr.SourceBranch, repo.DefaultBranch, StringComparison.OrdinalIgnoreCase))
        {
            try { await refService.DeleteRefAsync(repo.id, $"refs/heads/{pr.SourceBranch}"); }
            catch { /* best-effort; the merge already succeeded */ }
        }

        return result;
    }

    // ── Comment endpoints ──

    private static async Task<IResult> ListIssueComments(
        HttpContext ctx, string owner, string slug, int number,
        RepositoryService repoService, IssueService issueService, CommentService commentService,
        PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var issue = await issueService.GetByNumberAsync(repo!.id, number);
        if (issue == null) return Results.NotFound();

        var comments = await commentService.GetCommentsAsync(repo.id, issue.id);
        return Results.Ok(comments);
    }

    private static async Task<IResult> CreateIssueComment(
        HttpContext ctx, string owner, string slug, int number, CreateCommentRequest req,
        RepositoryService repoService, IssueService issueService, CommentService commentService,
        PermissionService permissionService, GitEventService events)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var issue = await issueService.GetByNumberAsync(repo!.id, number);
        if (issue == null) return Results.NotFound();

        var comment = await commentService.CreateAsync(
            repo.id, issue.id, nameof(Issue),
            req.Body, GetUserId(ctx), GetUserName(ctx));

        await events.EmitAsync(repo.AccountId, repo.id, GitTriggerType.CommentCreated,
            GetUserId(ctx), GetUserName(ctx), new Dictionary<string, string>
            {
                ["comment.parentType"] = "Issue",
                ["comment.parentNumber"] = number.ToString(),
                ["comment.body"] = req.Body
            });

        return Results.Created("", comment);
    }

    private static async Task<IResult> ListPrComments(
        HttpContext ctx, string owner, string slug, int number,
        RepositoryService repoService, PullRequestService prService, CommentService commentService,
        PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var pr = await prService.GetByNumberAsync(repo!.id, number);
        if (pr == null) return Results.NotFound();

        var comments = await commentService.GetCommentsAsync(repo.id, pr.id);
        return Results.Ok(comments);
    }

    private static async Task<IResult> CreatePrComment(
        HttpContext ctx, string owner, string slug, int number, CreateCommentRequest req,
        RepositoryService repoService, PullRequestService prService, CommentService commentService,
        PermissionService permissionService, GitEventService events)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var pr = await prService.GetByNumberAsync(repo!.id, number);
        if (pr == null) return Results.NotFound();

        var comment = await commentService.CreateAsync(
            repo.id, pr.id, nameof(PullRequest),
            req.Body, GetUserId(ctx), GetUserName(ctx));

        await events.EmitAsync(repo.AccountId, repo.id, GitTriggerType.CommentCreated,
            GetUserId(ctx), GetUserName(ctx), new Dictionary<string, string>
            {
                ["comment.parentType"] = "PullRequest",
                ["comment.parentNumber"] = number.ToString(),
                ["comment.body"] = req.Body
            });

        return Results.Created("", comment);
    }

    // ── Review endpoints ──

    private static async Task<IResult> ListReviews(
        HttpContext ctx, string owner, string slug, int number,
        RepositoryService repoService, PullRequestService prService, ReviewService reviewService,
        PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var pr = await prService.GetByNumberAsync(repo!.id, number);
        if (pr == null) return Results.NotFound();

        var reviews = await reviewService.ListReviewsAsync(repo.id, number);
        return Results.Ok(reviews);
    }

    private static async Task<IResult> SubmitReview(
        HttpContext ctx, string owner, string slug, int number, SubmitReviewRequest req,
        RepositoryService repoService, PullRequestService prService, ReviewService reviewService,
        PermissionService permissionService, GitEventService events)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var pr = await prService.GetByNumberAsync(repo!.id, number);
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

        await events.EmitAsync(repo.AccountId, repo.id, GitTriggerType.ReviewSubmitted,
            GetUserId(ctx), GetUserName(ctx), new Dictionary<string, string>
            {
                ["review.state"] = state.ToString(),
                ["review.prNumber"] = number.ToString()
            });

        return Results.Created($"/api/git/repos/{owner}/{slug}/pulls/{number}/reviews", review);
    }

    private static async Task<IResult> ListDiffComments(
        HttpContext ctx, string owner, string slug, int number,
        RepositoryService repoService, PullRequestService prService, ReviewService reviewService,
        PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var pr = await prService.GetByNumberAsync(repo!.id, number);
        if (pr == null) return Results.NotFound();

        var comments = await reviewService.GetDiffCommentsAsync(repo.id, number);
        return Results.Ok(comments);
    }

    // ── Fork endpoints ──

    private static async Task<IResult> ForkRepository(
        HttpContext ctx, string owner, string slug, RepositoryService repoService,
        PermissionService permissionService, GitEventService events,
        UserProfileService profileService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        // Resolve the user's handle for fork ownership
        var profile = await profileService.GetProfileAsync(GetUserId(ctx), GetAccountId(ctx));
        if (profile == null || string.IsNullOrEmpty(profile.Handle))
            return Results.BadRequest("You must set up a personal handle before forking. Go to Settings > Personal Profile.");

        var fork = await repoService.ForkRepositoryAsync(
            GetAccountId(ctx), GetUserId(ctx), profile.Handle, repo!);

        await events.EmitAsync(repo.AccountId, repo.id, GitTriggerType.RepositoryForked,
            GetUserId(ctx), GetUserName(ctx), new Dictionary<string, string>
            {
                ["fork.ownerName"] = fork.OwnerName,
                ["fork.slug"] = fork.Slug
            });

        return Results.Created($"/api/git/repos/{fork.OwnerName}/{fork.Slug}", RepoDto(fork));
    }

    private static async Task<IResult> ListForks(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var forks = await repoService.GetForksAsync(repo!.id);
        return Results.Ok(forks.Select(RepoDto));
    }

    // ── Secret endpoints ──

    private static async Task<IResult> SetSecret(
        HttpContext ctx, string owner, string slug, string name, SetSecretRequest req,
        RepositoryService repoService, PermissionService permissionService, SecretService secretService)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        await secretService.SetSecretAsync(repo!.id, name, req.Value);
        return Results.NoContent();
    }

    private static async Task<IResult> ListSecrets(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, PermissionService permissionService, SecretService secretService)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var names = await secretService.ListSecretNamesAsync(repo!.id);
        return Results.Ok(names.Select(n => new { Name = n }));
    }

    private static async Task<IResult> DeleteSecret(
        HttpContext ctx, string owner, string slug, string name,
        RepositoryService repoService, PermissionService permissionService, SecretService secretService)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        await secretService.DeleteSecretAsync(repo!.id, name);
        return Results.NoContent();
    }

    // ── Star endpoints ──

    private static async Task<IResult> StarRepository(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        await repoService.StarAsync(GetUserId(ctx), GetUserName(ctx), repo!.id);
        return Results.NoContent();
    }

    private static async Task<IResult> UnstarRepository(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        await repoService.UnstarAsync(GetUserId(ctx), repo!.id);
        return Results.NoContent();
    }

    // ── Explore endpoint ──

    private static async Task<IResult> ExploreRepositories(
        int? skip, int? take, RepositoryService repoService)
    {
        var repos = await repoService.GetPublicReposAsync(skip ?? 0, take ?? 20);
        return Results.Ok(repos.Select(RepoDto));
    }

    // ── Organization endpoints ──

    private static async Task<IResult> CreateOrg(
        HttpContext ctx, CreateOrgRequest req, OrganizationService orgService)
    {
        var org = await orgService.CreateAsync(
            GetAccountId(ctx), req.Name, req.Description,
            GetUserId(ctx), GetUserName(ctx), req.Handle);
        return Results.Created($"/api/git/orgs/{org.Slug}", org);
    }

    private static async Task<IResult> ListOrgs(
        HttpContext ctx, OrganizationService orgService)
    {
        var orgs = await orgService.ListAsync(GetAccountId(ctx));
        return Results.Ok(orgs);
    }

    private static async Task<IResult> DeleteOrg(
        HttpContext ctx, string slug, OrganizationService orgService,
        RepositoryService repoService, DaisiGit.Web.Services.AvatarService avatarService)
    {
        var org = await orgService.GetBySlugAsync(slug);
        if (org == null) return Results.NotFound();
        await orgService.DeleteAsync(org, repoService);
        try { await avatarService.DeleteAvatarAsync("org", org.id); } catch { }
        return Results.NoContent();
    }

    // ── Repo groups ──

    private static async Task<IResult> ListRepoGroups(
        string slug, OrganizationService orgService, RepoGroupService groupService)
    {
        var org = await orgService.GetBySlugAsync(slug);
        if (org == null) return Results.NotFound();
        return Results.Ok(await groupService.ListAsync(org.id));
    }

    private static async Task<IResult> CreateRepoGroup(
        HttpContext ctx, string slug, RepoGroupRequest req,
        OrganizationService orgService, RepoGroupService groupService)
    {
        var org = await orgService.GetBySlugAsync(slug);
        if (org == null) return Results.NotFound();
        var created = await groupService.CreateAsync(org.id, GetAccountId(ctx), req.Name ?? "", req.Description);
        return Results.Created($"/api/git/orgs/{slug}/groups/{created.id}", created);
    }

    private static async Task<IResult> UpdateRepoGroup(
        HttpContext ctx, string slug, string id, RepoGroupRequest req,
        OrganizationService orgService, RepoGroupService groupService)
    {
        var org = await orgService.GetBySlugAsync(slug);
        if (org == null) return Results.NotFound();
        var updated = await groupService.UpdateAsync(id, org.id, req.Name, req.Description, req.SortOrder);
        return Results.Ok(updated);
    }

    private static async Task<IResult> DeleteRepoGroup(
        HttpContext ctx, string slug, string id,
        OrganizationService orgService, RepoGroupService groupService)
    {
        var org = await orgService.GetBySlugAsync(slug);
        if (org == null) return Results.NotFound();
        await groupService.DeleteAsync(id, org.id);
        return Results.NoContent();
    }

    private static async Task<IResult> SetRepoGroup(
        HttpContext ctx, string owner, string slug, SetRepoGroupRequest req,
        RepositoryService repoService, PermissionService permissionService)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        repo!.GroupId = string.IsNullOrEmpty(req.GroupId) ? null : req.GroupId;
        await repoService.UpdateRepositoryAsync(repo);
        return Results.Ok(new { repo.GroupId });
    }

    // ── Vars ──

    private static async Task<IResult> GetOrgVars(string slug, OrganizationService orgService)
    {
        var org = await orgService.GetBySlugAsync(slug);
        return org == null ? Results.NotFound() : Results.Ok(org.Vars ?? new());
    }

    private static async Task<IResult> SetOrgVars(string slug, Dictionary<string, string> req, OrganizationService orgService)
    {
        var org = await orgService.GetBySlugAsync(slug);
        if (org == null) return Results.NotFound();
        org.Vars = SanitizeVarMap(req);
        await orgService.UpdateAsync(org);
        return Results.Ok(org.Vars);
    }

    private static async Task<IResult> GetRepoVars(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;
        return Results.Ok(repo!.Vars ?? new());
    }

    private static async Task<IResult> SetRepoVars(
        HttpContext ctx, string owner, string slug, Dictionary<string, string> req,
        RepositoryService repoService, PermissionService permissionService)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;
        repo!.Vars = SanitizeVarMap(req);
        await repoService.UpdateRepositoryAsync(repo);
        return Results.Ok(repo.Vars);
    }

    private static Dictionary<string, string> SanitizeVarMap(Dictionary<string, string>? input)
    {
        var result = new Dictionary<string, string>();
        if (input == null) return result;
        foreach (var (k, v) in input)
        {
            var key = (k ?? "").Trim();
            if (string.IsNullOrEmpty(key)) continue;
            // Restrict to ENV-safe identifiers so {{vars.X}} usage stays predictable.
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, @"^[A-Za-z_][A-Za-z0-9_]*$")) continue;
            result[key] = v ?? "";
        }
        return result;
    }

    // ── GitHub bulk import ──

    private static async Task<IResult> StartGitHubImport(
        HttpContext ctx, string slug, GitHubImportRequest req,
        OrganizationService orgService, GitHubBulkImportService importer)
    {
        if (string.IsNullOrWhiteSpace(req.GithubOrg))
            return Results.BadRequest(new { error = "githubOrg is required." });

        var org = await orgService.GetBySlugAsync(slug);
        if (org == null) return Results.NotFound();

        try
        {
            var job = await importer.StartAsync(
                daisiOrgId: org.id,
                daisiOrgSlug: org.Slug,
                accountId: GetAccountId(ctx),
                actorUserId: GetUserId(ctx),
                actorUserName: GetUserName(ctx),
                githubOrg: req.GithubOrg.Trim(),
                githubToken: string.IsNullOrWhiteSpace(req.GithubToken) ? null : req.GithubToken.Trim(),
                includePrivate: req.IncludePrivate,
                defaultPublicVisibility: req.PublicVisibility ?? GitRepoVisibility.Public,
                defaultPrivateVisibility: req.PrivateVisibility ?? GitRepoVisibility.Private,
                storageProvider: req.StorageProvider);
            return Results.Ok(job);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static IResult GetGitHubImportJob(string slug, string jobId, GitHubBulkImportService importer)
    {
        var job = importer.GetJob(jobId);
        if (job == null) return Results.NotFound();
        if (!string.Equals(job.DaisiOrgSlug, slug, StringComparison.OrdinalIgnoreCase))
            return Results.NotFound();
        return Results.Ok(job);
    }

    // ── Org activity ──

    private static async Task<IResult> GetOrgActivity(
        HttpContext ctx, string slug,
        int? days, string? granularity, string? type, bool? includePrivate,
        OrgActivityService activityService)
    {
        var d = Math.Clamp(days ?? 30, 1, 365);
        var g = granularity?.ToLowerInvariant() switch
        {
            "month" => ActivityGranularity.Month,
            "week" => ActivityGranularity.Week,
            _ => ActivityGranularity.Day
        };
        var t = type?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(t) && t != "commits")
            return Results.BadRequest(new { error = $"Unsupported activity type '{t}'. Currently supported: commits." });

        var viewerId = ctx.Items["userId"] as string;
        var includePriv = (includePrivate ?? false) && !string.IsNullOrEmpty(viewerId);

        var activity = await activityService.GetActivityAsync(slug, d, g, includePriv, viewerId);
        return activity == null ? Results.NotFound() : Results.Ok(activity);
    }

    private static async Task<IResult> GetCommitsInRange(
        HttpContext ctx, string owner, string slug,
        DateTime from, DateTime to, int? limit,
        RepositoryService repoService, BrowseService browseService,
        PermissionService permissionService)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var headSha = await browseService.ResolveRefAsync(repo!.id, repo.DefaultBranch);
        if (headSha == null) return Results.Ok(new List<object>());

        var cap = Math.Clamp(limit ?? 200, 1, 500);
        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to, DateTimeKind.Utc);

        // Walk newest-to-oldest, stop when we've gone past the range or hit the cap.
        var log = await browseService.GetCommitLogAsync(repo.id, headSha, cap * 4);
        var hits = new List<object>();
        foreach (var c in log)
        {
            var when = c.AuthorDate.UtcDateTime;
            if (when >= toUtc) continue;
            if (when < fromUtc) break;
            hits.Add(new
            {
                sha = c.Sha,
                shortSha = c.ShortSha,
                messageFirstLine = c.MessageFirstLine,
                authorName = c.AuthorName,
                authorDateUtc = when
            });
            if (hits.Count >= cap) break;
        }
        return Results.Ok(hits);
    }

    // ── Account settings endpoints ──

    private static async Task<IResult> GetAccountSettings(
        HttpContext ctx, AccountSettingsService settingsService)
    {
        var settings = await settingsService.GetSettingsAsync(GetAccountId(ctx));
        return Results.Ok(new
        {
            settings.DefaultStorageProvider,
            settings.AutoMergeEnabled,
            settings.DeleteBranchOnMerge,
            settings.AllowMergeCommit,
            settings.AllowSquashMerge,
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

    private static async Task<IResult> SetAccountMergePolicy(
        HttpContext ctx, SetMergePolicyRequest req, AccountSettingsService settingsService)
    {
        if (!req.AllowMergeCommit && !req.AllowSquashMerge)
            return Results.BadRequest(new { error = "At least one merge strategy must be allowed." });

        var settings = await settingsService.SetMergePolicyAsync(
            GetAccountId(ctx),
            req.AutoMergeEnabled, req.DeleteBranchOnMerge,
            req.AllowMergeCommit, req.AllowSquashMerge);
        return Results.Ok(new
        {
            settings.AutoMergeEnabled,
            settings.DeleteBranchOnMerge,
            settings.AllowMergeCommit,
            settings.AllowSquashMerge,
            settings.UpdatedUtc
        });
    }

    private static async Task<IResult> SetRepoMergePolicy(
        HttpContext ctx, string owner, string slug, SetRepoMergePolicyRequest req,
        RepositoryService repoService, PermissionService permissionService)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        // If both strategies are explicitly disabled, refuse — would brick merging.
        if (req.AllowMergeCommit == false && req.AllowSquashMerge == false)
            return Results.BadRequest(new { error = "At least one merge strategy must be allowed." });

        repo!.AutoMergeEnabled = req.AutoMergeEnabled;
        repo.DeleteBranchOnMerge = req.DeleteBranchOnMerge;
        repo.AllowMergeCommit = req.AllowMergeCommit;
        repo.AllowSquashMerge = req.AllowSquashMerge;
        await repoService.UpdateRepositoryAsync(repo);
        return Results.Ok(new
        {
            repo.AutoMergeEnabled,
            repo.DeleteBranchOnMerge,
            repo.AllowMergeCommit,
            repo.AllowSquashMerge
        });
    }

    // ── Branch creation ──

    private static async Task<IResult> CreateBranch(
        HttpContext ctx, string owner, string slug, CreateBranchRequest req,
        RepositoryService repoService, PermissionService permissionService,
        BrowseService browseService, GitRefService refService)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest("Branch name is required.");

        // Resolve the source branch/ref
        var fromRef = req.From ?? repo!.DefaultBranch;
        var sourceSha = await browseService.ResolveRefAsync(repo!.id, fromRef);
        if (sourceSha == null)
            return Results.BadRequest($"Source ref '{fromRef}' not found.");

        // Check if branch already exists
        var existing = await refService.GetRefAsync(repo.id, $"refs/heads/{req.Name}");
        if (existing != null)
            return Results.Conflict($"Branch '{req.Name}' already exists.");

        // Create the branch ref
        await refService.SetRefAsync(repo.id, $"refs/heads/{req.Name}", sourceSha);

        return Results.Ok(new { name = req.Name, sha = sourceSha });
    }

    // ── File write/commit ──

    private static async Task<IResult> WriteFile(
        HttpContext ctx, string owner, string slug, string path, WriteFileRequest req,
        RepositoryService repoService, PermissionService permissionService,
        BrowseService browseService, GitObjectStore objectStore,
        GitRefService refService)
    {
        var (repo, error) = await RequireWrite(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(path))
            return Results.BadRequest("File path is required.");
        if (req.Content == null)
            return Results.BadRequest("Content is required.");

        var branch = req.Branch ?? repo!.DefaultBranch;
        var message = req.Message ?? $"Update {path}";

        // Resolve the branch to a commit
        var branchSha = await browseService.ResolveRefAsync(repo!.id, branch);
        if (branchSha == null)
            return Results.BadRequest($"Branch '{branch}' not found.");

        var commit = await objectStore.GetObjectAsync(repo.id, branchSha) as GitCommit;
        if (commit == null)
            return Results.BadRequest($"Could not resolve commit for branch '{branch}'.");

        // Create the blob
        var blob = new GitBlob { Data = System.Text.Encoding.UTF8.GetBytes(req.Content) };
        var blobSha = await objectStore.StoreObjectAsync(repo, blob);

        // Build the new tree by walking the path and replacing/inserting the entry
        var segments = path.Trim('/').Split('/');
        var newTreeSha = await BuildTreeWithFileAsync(
            repo, objectStore, commit.TreeSha, segments, 0, blobSha);

        // Create the commit
        var userName = GetUserName(ctx);
        var userId = GetUserId(ctx);
        var sig = new GitSignature
        {
            Name = userName,
            Email = $"{userName}@daisi.ai",
            Timestamp = DateTimeOffset.UtcNow
        };

        var newCommit = new GitCommit
        {
            TreeSha = newTreeSha,
            ParentShas = [branchSha],
            Author = sig,
            Committer = sig,
            Message = message
        };

        var commitSha = await objectStore.StoreObjectAsync(repo, newCommit);

        // Update the branch ref
        await refService.SetRefAsync(repo.id, $"refs/heads/{branch}", commitSha);

        return Results.Ok(new { sha = blobSha, commitSha, branch });
    }

    /// <summary>
    /// Recursively builds a new tree with a file inserted/updated at the given path.
    /// </summary>
    private static async Task<string> BuildTreeWithFileAsync(
        GitRepository repo, GitObjectStore objectStore,
        string currentTreeSha, string[] pathSegments, int depth, string blobSha)
    {
        var tree = await objectStore.GetObjectAsync(repo.id, currentTreeSha) as GitTree;
        var entries = tree?.Entries.ToList() ?? [];

        var segment = pathSegments[depth];
        var isLastSegment = depth == pathSegments.Length - 1;

        if (isLastSegment)
        {
            // Replace or insert the blob entry
            var existingIdx = entries.FindIndex(e => e.Name == segment);
            var entry = new GitTreeEntry { Name = segment, Mode = "100644", Sha = blobSha };

            if (existingIdx >= 0)
                entries[existingIdx] = entry;
            else
                entries.Add(entry);
        }
        else
        {
            // Recurse into subtree — create it if it doesn't exist
            var existingIdx = entries.FindIndex(e => e.Name == segment);
            string subTreeSha;

            if (existingIdx >= 0 && entries[existingIdx].IsTree)
            {
                subTreeSha = await BuildTreeWithFileAsync(
                    repo, objectStore, entries[existingIdx].Sha, pathSegments, depth + 1, blobSha);
                entries[existingIdx] = new GitTreeEntry { Name = segment, Mode = "40000", Sha = subTreeSha };
            }
            else
            {
                // Create a new empty subtree and recurse
                var emptyTree = new GitTree();
                var emptySha = await objectStore.StoreObjectAsync(repo, emptyTree);
                subTreeSha = await BuildTreeWithFileAsync(
                    repo, objectStore, emptySha, pathSegments, depth + 1, blobSha);

                if (existingIdx >= 0)
                    entries[existingIdx] = new GitTreeEntry { Name = segment, Mode = "40000", Sha = subTreeSha };
                else
                    entries.Add(new GitTreeEntry { Name = segment, Mode = "40000", Sha = subTreeSha });
            }
        }

        // Sort entries: trees first, then blobs, alphabetically within each group
        entries.Sort((a, b) =>
        {
            // Git sorts tree entries with trailing '/' for directories
            var aName = a.IsTree ? a.Name + "/" : a.Name;
            var bName = b.IsTree ? b.Name + "/" : b.Name;
            return string.Compare(aName, bName, StringComparison.Ordinal);
        });

        var newTree = new GitTree { Entries = entries };
        return await objectStore.StoreObjectAsync(repo, newTree);
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
        r.ImportedFromUrl,
        r.CreatedUtc
    };

#if DEBUG
    // ── Debug endpoints ──

    private static async Task<IResult> DebugListObjects(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, PermissionService permissionService,
        DaisiGitCosmo cosmo)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var records = await cosmo.GetAllObjectRecordsAsync(repo!.id);
        return Results.Ok(records.Select(r => new
        {
            r.id,
            r.Sha,
            r.RepositoryId,
            r.ObjectType,
            r.SizeBytes,
            r.DriveFileId,
            StorageProvider = r.StorageProvider.ToString()
        }));
    }

    private static async Task<IResult> DebugGetObject(
        HttpContext ctx, string owner, string slug, string sha,
        RepositoryService repoService, PermissionService permissionService,
        GitObjectStore objectStore)
    {
        var (repo, error) = await RequireRead(ctx, owner, slug, repoService, permissionService);
        if (error != null) return error;

        var obj = await objectStore.GetObjectAsync(repo!.id, sha);
        if (obj == null) return Results.NotFound($"Object {sha} not found");

        var content = obj.SerializeContent();
        var raw = obj.Serialize();
        var computedSha = ObjectHasher.HashRaw(raw);

        return Results.Ok(new
        {
            StoredSha = sha,
            ComputedSha = computedSha,
            Match = sha == computedSha,
            Type = obj.TypeString,
            ContentSize = content.Length,
            RawSize = raw.Length,
            ContentHex = content.Length <= 2048 ? Convert.ToHexString(content) : "too large"
        });
    }
#endif
}

// ── Request DTOs ──

public record CreateRepoRequest(string Name, string? Description, string? Owner = null, GitRepoVisibility Visibility = GitRepoVisibility.Private, StorageProvider? StorageProvider = null);
public record SetStorageProviderRequest(StorageProvider Provider);

public record RepoGroupRequest(string? Name, string? Description, int? SortOrder);

public record SetRepoGroupRequest(string? GroupId);

public record GitHubImportRequest(
    string? GithubOrg,
    string? GithubToken,
    bool IncludePrivate,
    GitRepoVisibility? PublicVisibility,
    GitRepoVisibility? PrivateVisibility,
    StorageProvider? StorageProvider);

public record SetMergePolicyRequest(
    bool AutoMergeEnabled,
    bool DeleteBranchOnMerge,
    bool AllowMergeCommit,
    bool AllowSquashMerge);

public record SetRepoMergePolicyRequest(
    bool? AutoMergeEnabled,
    bool? DeleteBranchOnMerge,
    bool? AllowMergeCommit,
    bool? AllowSquashMerge);
public record CreateIssueRequest(string Title, string? Description, List<string>? Labels = null);
public record UpdateIssueRequest(string? Title = null, string? Description = null, string? Action = null);
public record CreatePrRequest(string Title, string? Description, string SourceBranch, string TargetBranch, List<string>? Labels = null);
public record UpdatePrRequest(string? Title = null, string? Description = null, string? Action = null);
public record MergePrRequest(string? Strategy = null);
public record CreateApiKeyRequest(string Name);
public record CreateOrgRequest(string Name, string Handle, string? Description = null);
public record ImportRepoRequest(string Url, string? Name = null, string? Owner = null, string? Description = null, GitRepoVisibility Visibility = GitRepoVisibility.Private);
public record SetSecretRequest(string Value);
public record CreateCommentRequest(string Body);
public record SubmitReviewRequest(string? State, string? Body, List<DiffCommentRequest>? DiffComments = null);
public record DiffCommentRequest(string Path, int Line, string Body, string? Side = null);
public record CreateBranchRequest(string Name, string? From = null);
public record WriteFileRequest(string? Content, string? Message = null, string? Branch = null, string? Sha = null);
