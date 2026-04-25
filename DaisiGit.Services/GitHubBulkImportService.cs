using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DaisiGit.Services;

/// <summary>
/// Drives a bulk import of every repository in a GitHub organization into a daisi-git org.
/// Singleton so jobs survive across the polling requests; results held in memory and retired
/// after a short retention window.
/// </summary>
public class GitHubBulkImportService(
    IHttpClientFactory httpFactory,
    IServiceScopeFactory scopeFactory,
    DaisiGit.Data.DaisiGitCosmo cosmo,
    ILogger<GitHubBulkImportService> logger)
{
    private static readonly TimeSpan JobRetention = TimeSpan.FromHours(1);
    private const int MaxReposPerJob = 200;

    private readonly ConcurrentDictionary<string, GitHubImportJob> _jobs = new();
    private bool _orphanScanDone;
    private readonly SemaphoreSlim _orphanScanLock = new(1, 1);

    /// <summary>
    /// On the first call after process start, marks any persisted job that's still in
    /// "running" state as orphaned. The actual import work runs in Task.Run and dies with
    /// the process, so a not-finished job in Cosmos after a restart is by definition dead.
    /// We don't auto-resume because the GitHub PAT was never persisted.
    /// </summary>
    private async Task EnsureOrphanScanAsync()
    {
        if (_orphanScanDone) return;
        await _orphanScanLock.WaitAsync();
        try
        {
            if (_orphanScanDone) return;
            try
            {
                var incomplete = await cosmo.GetIncompleteGitHubImportJobsAsync();
                foreach (var job in incomplete)
                {
                    job.FinishedUtc = DateTime.UtcNow;
                    foreach (var item in job.Items)
                    {
                        if (item.Status is "Pending" or "InProgress")
                        {
                            item.Status = "Failed";
                            item.Error = "Server restarted before this import finished. Re-run the import to resume.";
                        }
                    }
                    await cosmo.UpsertGitHubImportJobAsync(job);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Orphan scan for GitHub import jobs failed");
            }
            _orphanScanDone = true;
        }
        finally { _orphanScanLock.Release(); }
    }

    private async Task PersistAsync(GitHubImportJob job)
    {
        try { await cosmo.UpsertGitHubImportJobAsync(job); }
        catch (Exception ex) { logger.LogWarning(ex, "Persist of import job {JobId} failed", job.Id); }
    }

    /// <summary>
    /// Starts a job. Returns a job id that can be polled via <see cref="GetJob"/>.
    /// </summary>
    /// <summary>
    /// GitHub source orgs whose name is reserved — only members of the matching daisi-git
    /// org can use them as a source. Prevents a stranger creating a new daisi-git org and
    /// bulk-cloning every public daisinet repo into it.
    /// </summary>
    private static readonly HashSet<string> ProtectedSourceOrgs = new(StringComparer.OrdinalIgnoreCase)
    {
        "daisinet"
    };

    public async Task<GitHubImportJob> StartAsync(
        string daisiOrgId, string daisiOrgSlug, string accountId, string actorUserId, string actorUserName,
        string githubOrg, string? githubToken, bool includePrivate,
        GitRepoVisibility defaultPublicVisibility, GitRepoVisibility defaultPrivateVisibility,
        StorageProvider? storageProvider)
    {
        await EnsureOrphanScanAsync();

        // Restrict imports from a protected source org to admins of the matching daisi-git
        // org. Anyone else (or anyone trying to land it in a different daisi-git org) is
        // refused. Prevents a stranger creating a new daisi-git org and bulk-cloning every
        // public daisinet repo into it.
        if (ProtectedSourceOrgs.Contains(githubOrg))
        {
            if (!string.Equals(daisiOrgSlug, githubOrg, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Importing from GitHub org '{githubOrg}' is reserved for the matching daisi-git org.");

            using var scope = scopeFactory.CreateScope();
            var orgService = scope.ServiceProvider.GetRequiredService<OrganizationService>();
            var members = await orgService.GetMembersAsync(daisiOrgId);
            var actor = members.FirstOrDefault(m => m.UserId == actorUserId);
            if (actor == null || (actor.Role != GitRole.Admin && actor.Role != GitRole.Owner))
                throw new InvalidOperationException(
                    $"Only admins of the '{githubOrg}' org may import from GitHub org '{githubOrg}'.");
        }

        // Refuse to start a second job against the same daisi-git org. The caller can use
        // GetMostRecentJobFor to surface the running one in the UI.
        if (await HasActiveJobForAsync(daisiOrgId))
            throw new InvalidOperationException(
                "An import is already running for this organization. Wait for it to finish before starting another.");

        var repos = await ListGitHubReposAsync(githubOrg, githubToken, includePrivate);
        if (repos.Count > MaxReposPerJob)
            repos = repos.Take(MaxReposPerJob).ToList();

        var job = new GitHubImportJob
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            DaisiOrgId = daisiOrgId,
            DaisiOrgSlug = daisiOrgSlug,
            GithubOrg = githubOrg,
            StartedUtc = DateTime.UtcNow,
            Items = repos.Select(r => new GitHubImportItem
            {
                Name = r.Name,
                SourceUrl = r.CloneUrl,
                IsPrivate = r.Private,
                Description = r.Description,
                Status = "Pending"
            }).ToList()
        };
        _jobs[job.Id] = job;
        await PersistAsync(job);

        // Run in background; the request returns immediately with the job's planned items.
        _ = Task.Run(() => RunJobAsync(job, accountId, actorUserId, actorUserName,
            githubToken, defaultPublicVisibility, defaultPrivateVisibility, storageProvider));

        Cleanup();
        return job;
    }

    public GitHubImportJob? GetJob(string id)
    {
        Cleanup();
        return _jobs.TryGetValue(id, out var job) ? job : null;
    }

    /// <summary>
    /// Returns the most recent job for an org. Checks the in-memory cache first, then
    /// Cosmos so jobs from before a process restart are still visible (read-only, since
    /// orphans get marked Failed by EnsureOrphanScanAsync).
    /// </summary>
    public async Task<GitHubImportJob?> GetMostRecentJobForAsync(string daisiOrgId)
    {
        await EnsureOrphanScanAsync();
        Cleanup();
        var inMem = _jobs.Values
            .Where(j => j.DaisiOrgId == daisiOrgId)
            .OrderByDescending(j => j.StartedUtc)
            .FirstOrDefault();
        if (inMem != null) return inMem;
        try { return await cosmo.GetMostRecentGitHubImportJobAsync(daisiOrgId); }
        catch { return null; }
    }

    /// <summary>True if a job is currently running for the org.</summary>
    public async Task<bool> HasActiveJobForAsync(string daisiOrgId)
    {
        var most = await GetMostRecentJobForAsync(daisiOrgId);
        return most != null && !most.IsComplete;
    }

    private async Task RunJobAsync(
        GitHubImportJob job, string accountId, string actorUserId, string actorUserName,
        string? githubToken,
        GitRepoVisibility defaultPublicVisibility, GitRepoVisibility defaultPrivateVisibility,
        StorageProvider? storageProvider)
    {
        foreach (var item in job.Items)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var importService = scope.ServiceProvider.GetRequiredService<ImportService>();
                var repoService = scope.ServiceProvider.GetRequiredService<RepositoryService>();

                // Map GitHub's profile-repo convention (".github") to ours (".daisi").
                var targetName = string.Equals(item.Name, ".github", StringComparison.OrdinalIgnoreCase)
                    ? ".daisi"
                    : item.Name;

                item.Status = "InProgress";
                if (!string.Equals(targetName, item.Name, StringComparison.OrdinalIgnoreCase))
                    item.LastMessage = $"Importing as '{targetName}'...";
                job.UpdatedUtc = DateTime.UtcNow;

                // Embed token in clone URL so private repos work. Token-only auth is sufficient
                // for HTTPS clone (GitHub accepts username = "x-access-token" or any non-empty).
                var cloneUrl = item.SourceUrl;
                if (!string.IsNullOrEmpty(githubToken))
                    cloneUrl = cloneUrl.Replace("https://", $"https://x-access-token:{githubToken}@");

                var visibility = item.IsPrivate ? defaultPrivateVisibility : defaultPublicVisibility;

                var existing = await repoService.GetRepositoryBySlugAsync(job.DaisiOrgSlug, Slugify(targetName));

                GitRepository imported;
                if (existing != null)
                {
                    // Already in daisi-git (could be a real repo or the empty shell left by
                    // a previous failed import) — merge GitHub history in rather than skipping.
                    imported = await importService.MergeFromUrlAsync(existing, cloneUrl,
                        onProgress: msg => { item.LastMessage = msg; job.UpdatedUtc = DateTime.UtcNow; });
                    item.Status = "Updated";
                }
                else
                {
                    // Owner of the new repo is the daisi-git org itself (slug). The actor's
                    // id/name is not used for ownership — the org owns it regardless of who
                    // kicked off the import.
                    imported = await importService.ImportAsync(
                        cloneUrl, accountId,
                        ownerId: job.DaisiOrgId,
                        ownerName: job.DaisiOrgSlug,
                        repoName: targetName, description: item.Description,
                        visibility: visibility, storageProvider: storageProvider,
                        onProgress: msg => { item.LastMessage = msg; job.UpdatedUtc = DateTime.UtcNow; });
                    item.Status = "Imported";
                }
                item.DaisiRepoSlug = imported.Slug;
                item.LastMessage = null;
                job.UpdatedUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Import of {Repo} failed for job {JobId}", item.Name, job.Id);
                item.Status = "Failed";
                item.Error = ex.Message;
                job.UpdatedUtc = DateTime.UtcNow;
            }

            // Persist after every item so progress survives a Web restart in read-only form.
            await PersistAsync(job);
        }

        job.FinishedUtc = DateTime.UtcNow;
        job.UpdatedUtc = DateTime.UtcNow;
        await PersistAsync(job);
    }

    private async Task<List<GitHubRepoSummary>> ListGitHubReposAsync(string org, string? token, bool includePrivate)
    {
        var http = httpFactory.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("daisi-git-importer");
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrEmpty(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var results = new List<GitHubRepoSummary>();
        var page = 1;
        while (true)
        {
            var url = $"https://api.github.com/orgs/{org}/repos?per_page=100&page={page}&type=all";
            using var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"GitHub API returned {(int)resp.StatusCode}: {(body.Length > 200 ? body[..200] : body)}");
            }

            var batch = await resp.Content.ReadFromJsonAsync<List<GitHubRepoSummary>>(JsonOptions);
            if (batch == null || batch.Count == 0) break;

            foreach (var r in batch)
            {
                if (r.Archived || r.Disabled) continue;
                if (r.Private && !includePrivate) continue;
                results.Add(r);
            }

            if (batch.Count < 100) break;
            page++;
            if (results.Count >= MaxReposPerJob) break;
        }
        return results;
    }

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow - JobRetention;
        foreach (var (id, job) in _jobs)
        {
            if (job.FinishedUtc.HasValue && job.FinishedUtc.Value < cutoff)
                _jobs.TryRemove(id, out _);
        }
    }

    private static string Slugify(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == '-' || ch == '_' || ch == '.' || ch == ' ') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "repo" : slug;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private class GitHubRepoSummary
    {
        public string Name { get; set; } = "";
        public string CloneUrl { get; set; } = "";
        public bool Private { get; set; }
        public string? Description { get; set; }
        public bool Archived { get; set; }
        public bool Disabled { get; set; }
    }
}

// GitHubImportJob and GitHubImportItem live in DaisiGit.Core.Models so the data layer
// can persist them without taking a dependency on this service.
