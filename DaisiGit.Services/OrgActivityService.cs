using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;

namespace DaisiGit.Services;

/// <summary>
/// Computes the per-repo, per-time-bucket commit-count matrix for an organization.
/// Reads pre-aggregated counts from <see cref="GitRepository.CommitCountsByDate"/>, which
/// is maintained incrementally by <see cref="RepoActivityRollupService"/>. On the first
/// load for a repo with no rollup yet, it falls back to a one-time backfill.
/// </summary>
public class OrgActivityService(
    OrganizationService orgService,
    RepositoryService repoService,
    PermissionService permissionService,
    RepoActivityRollupService rollupService)
{
    public async Task<OrgActivity?> GetActivityAsync(
        string orgSlug, int days, ActivityGranularity granularity,
        bool includePrivate, string? viewerUserId)
    {
        var org = await orgService.GetBySlugAsync(orgSlug);
        if (org == null) return null;

        var allRepos = await repoService.GetRepositoriesByOwnerAsync(orgSlug);

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
            // Backfill once for legacy repos that haven't been hit by an incremental update yet.
            if (repo.CommitRollupBackfilledUtc == null)
            {
                try { await rollupService.BackfillAsync(repo); }
                catch { /* keep going with whatever rollup we have */ }
            }

            var counts = BucketCounts(repo.CommitCountsByDate, buckets);
            // Skip repos with zero activity in the window so the grid stays focused.
            if (counts.Sum() == 0) continue;

            repoRows.Add(new RepoActivity(
                repo.id, repo.OwnerName, repo.Slug, repo.Name, repo.Visibility, counts));
        }

        repoRows = repoRows
            .OrderBy(r => r.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OrgActivity(buckets, repoRows);
    }

    private static int[] BucketCounts(Dictionary<string, int>? rollup, List<ActivityBucket> buckets)
    {
        var result = new int[buckets.Count];
        if (rollup == null || rollup.Count == 0) return result;

        // For day granularity the rollup keys map directly. For week/month, sum the daily
        // keys that fall inside each bucket. The rollup is small enough to walk per call.
        foreach (var (key, count) in rollup)
        {
            if (!DateTime.TryParse(key, out var date)) continue;
            date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            for (var i = 0; i < buckets.Count; i++)
            {
                if (date >= buckets[i].StartUtc && date < buckets[i].EndUtc)
                {
                    result[i] += count;
                    break;
                }
            }
        }
        return result;
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
            var cursor = rangeStart;
            var dow = (int)cursor.DayOfWeek;
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
    int[] Counts);
