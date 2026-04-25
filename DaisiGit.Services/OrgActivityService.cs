using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;

namespace DaisiGit.Services;

/// <summary>
/// Computes a per-repo, per-time-bucket commit-count matrix for an organization,
/// modeled after GitHub's contributions graph but with repos on rows and dates on
/// columns. Honors visibility/permission checks for the viewer.
/// </summary>
public class OrgActivityService(
    OrganizationService orgService,
    RepositoryService repoService,
    BrowseService browseService,
    PermissionService permissionService)
{
    /// <summary>Maximum commits walked per repo when building the matrix (safety cap).</summary>
    private const int MaxCommitsPerRepo = 1000;

    /// <summary>Maximum commits returned in the per-repo commits list (UI rendering cap).</summary>
    private const int MaxCommitsReturnedPerRepo = 300;

    public async Task<OrgActivity?> GetActivityAsync(
        string orgSlug, int days, ActivityGranularity granularity,
        bool includePrivate, string? viewerUserId)
    {
        var org = await orgService.GetBySlugAsync(orgSlug);
        if (org == null) return null;

        var allRepos = await repoService.GetRepositoriesByOwnerAsync(orgSlug);

        // Apply visibility / permission filter.
        var visibleRepos = new List<GitRepository>();
        foreach (var r in allRepos)
        {
            if (r.Visibility == GitRepoVisibility.Public)
            {
                visibleRepos.Add(r);
                continue;
            }
            if (!includePrivate || string.IsNullOrEmpty(viewerUserId)) continue;
            if (await permissionService.CanReadAsync(viewerUserId, r))
                visibleRepos.Add(r);
        }

        var (buckets, rangeStart) = BuildBuckets(days, granularity);

        var repoRows = new List<RepoActivity>();
        foreach (var repo in visibleRepos)
        {
            var row = await BuildRepoRowAsync(repo, buckets, rangeStart);
            // Skip repos with zero activity in the window so the grid stays focused.
            if (row.Counts.Sum() > 0)
                repoRows.Add(row);
        }

        repoRows = repoRows
            .OrderBy(r => r.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OrgActivity(buckets, repoRows);
    }

    private async Task<RepoActivity> BuildRepoRowAsync(
        GitRepository repo, List<ActivityBucket> buckets, DateTime rangeStart)
    {
        var counts = new int[buckets.Count];
        var commits = new List<RepoActivityCommit>();

        var headSha = await browseService.ResolveRefAsync(repo.id, repo.DefaultBranch);
        if (headSha == null)
            return new RepoActivity(repo.id, repo.OwnerName, repo.Slug, repo.Name, repo.Visibility, counts, commits);

        var log = await browseService.GetCommitLogAsync(repo.id, headSha, MaxCommitsPerRepo);

        foreach (var commit in log)
        {
            var commitDate = commit.AuthorDate.UtcDateTime;
            if (commitDate < rangeStart) continue;

            var bucketIdx = FindBucket(buckets, commitDate);
            if (bucketIdx < 0) continue;
            counts[bucketIdx]++;

            if (commits.Count < MaxCommitsReturnedPerRepo)
            {
                commits.Add(new RepoActivityCommit(
                    commit.Sha, commit.ShortSha,
                    commit.MessageFirstLine,
                    commit.AuthorName,
                    commit.AuthorDate.UtcDateTime,
                    bucketIdx));
            }
        }

        return new RepoActivity(repo.id, repo.OwnerName, repo.Slug, repo.Name, repo.Visibility, counts, commits);
    }

    private static int FindBucket(List<ActivityBucket> buckets, DateTime when)
    {
        // Binary search would be nicer; buckets are small (<= ~365) so linear is fine.
        for (var i = 0; i < buckets.Count; i++)
        {
            if (when >= buckets[i].StartUtc && when < buckets[i].EndUtc)
                return i;
        }
        return -1;
    }

    private static (List<ActivityBucket>, DateTime rangeStart) BuildBuckets(int days, ActivityGranularity granularity)
    {
        var nowUtc = DateTime.UtcNow;
        var endExclusive = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(1);
        var rangeStart = endExclusive.AddDays(-days);

        var buckets = new List<ActivityBucket>();

        if (granularity == ActivityGranularity.Month)
        {
            var cursor = new DateTime(rangeStart.Year, rangeStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            while (cursor < endExclusive)
            {
                var next = cursor.AddMonths(1);
                buckets.Add(new ActivityBucket(cursor, next, cursor.ToString("yyyy-MM")));
                cursor = next;
            }
        }
        else if (granularity == ActivityGranularity.Week)
        {
            // ISO week: start on Monday
            var cursor = rangeStart;
            var dow = (int)cursor.DayOfWeek; // Sunday = 0
            var deltaToMonday = dow == 0 ? -6 : 1 - dow;
            cursor = cursor.AddDays(deltaToMonday);
            while (cursor < endExclusive)
            {
                var next = cursor.AddDays(7);
                buckets.Add(new ActivityBucket(cursor, next, cursor.ToString("MMM dd")));
                cursor = next;
            }
        }
        else
        {
            var cursor = rangeStart;
            while (cursor < endExclusive)
            {
                var next = cursor.AddDays(1);
                buckets.Add(new ActivityBucket(cursor, next, cursor.ToString("MMM dd")));
                cursor = next;
            }
        }

        return (buckets, rangeStart);
    }
}

public enum ActivityGranularity { Day, Week, Month }

public record OrgActivity(
    List<ActivityBucket> Buckets,
    List<RepoActivity> Repos);

public record ActivityBucket(DateTime StartUtc, DateTime EndUtc, string Label);

public record RepoActivity(
    string Id,
    string OwnerName,
    string Slug,
    string Name,
    GitRepoVisibility Visibility,
    int[] Counts,
    List<RepoActivityCommit> Commits);

public record RepoActivityCommit(
    string Sha,
    string ShortSha,
    string MessageFirstLine,
    string AuthorName,
    DateTime AuthorDateUtc,
    int BucketIndex);
