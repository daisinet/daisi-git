using DaisiGit.Core.Git;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Maintains a per-day commit-count rollup on each repository so the org activity view
/// reads pre-aggregated values instead of walking commit history on every request.
/// Hooked from the smart-protocol push handler and the PR merge endpoint.
/// </summary>
public class RepoActivityRollupService(
    DaisiGitCosmo cosmo,
    GitObjectStore objectStore,
    BrowseService browseService)
{
    /// <summary>Cap on how many commits we walk in a single push update (safety bound).</summary>
    private const int MaxCommitsPerPushUpdate = 5000;

    /// <summary>Cap on the total log we walk during a one-time backfill.</summary>
    private const int MaxCommitsForBackfill = 50_000;

    /// <summary>
    /// Increments the rollup for every commit reachable from <paramref name="newSha"/>
    /// but not from <paramref name="oldSha"/>. Called from the push pipeline. Both SHAs may
    /// be empty/null on branch create — in that case we count everything reachable from newSha.
    /// </summary>
    public async Task ApplyPushAsync(GitRepository repo, string? oldSha, string? newSha)
    {
        if (string.IsNullOrEmpty(newSha) || newSha.All(c => c == '0')) return; // delete
        var newCommits = await CollectNewCommitsAsync(repo.id, oldSha, newSha, MaxCommitsPerPushUpdate);
        if (newCommits.Count == 0) return;
        ApplyCommitsToRollup(repo, newCommits);
        await cosmo.UpdateRepositoryAsync(repo);
    }

    /// <summary>
    /// Reconciles the rollup from scratch by walking the default-branch commit log. Use this
    /// for repos with no rollup yet (legacy data) or after a known divergence.
    /// </summary>
    public async Task BackfillAsync(GitRepository repo)
    {
        var headSha = await browseService.ResolveRefAsync(repo.id, repo.DefaultBranch);
        if (headSha == null)
        {
            repo.CommitRollupBackfilledUtc = DateTime.UtcNow;
            await cosmo.UpdateRepositoryAsync(repo);
            return;
        }

        var log = await browseService.GetCommitLogAsync(repo.id, headSha, MaxCommitsForBackfill);
        var newCounts = new Dictionary<string, int>();
        foreach (var c in log)
        {
            var key = c.AuthorDate.UtcDateTime.ToString("yyyy-MM-dd");
            newCounts[key] = newCounts.GetValueOrDefault(key) + 1;
        }
        repo.CommitCountsByDate = newCounts;
        repo.CommitRollupBackfilledUtc = DateTime.UtcNow;
        await cosmo.UpdateRepositoryAsync(repo);
    }

    private void ApplyCommitsToRollup(GitRepository repo, IEnumerable<DateTime> commitDatesUtc)
    {
        repo.CommitCountsByDate ??= new Dictionary<string, int>();
        foreach (var d in commitDatesUtc)
        {
            var key = d.ToString("yyyy-MM-dd");
            repo.CommitCountsByDate[key] = repo.CommitCountsByDate.GetValueOrDefault(key) + 1;
        }
    }

    private async Task<List<DateTime>> CollectNewCommitsAsync(string repoId, string? oldSha, string newSha, int cap)
    {
        // Walk parents from newSha. Stop when we hit oldSha or its ancestors (or the cap).
        var dates = new List<DateTime>();
        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(newSha);

        // Pre-load oldSha and its ancestors as a "stop set" so we don't double-count
        // history that was already on the branch before this push.
        var stopSet = new HashSet<string>();
        if (!string.IsNullOrEmpty(oldSha) && !oldSha.All(c => c == '0'))
        {
            var stopQueue = new Queue<string>();
            stopQueue.Enqueue(oldSha);
            while (stopQueue.Count > 0 && stopSet.Count < cap)
            {
                var s = stopQueue.Dequeue();
                if (!stopSet.Add(s)) continue;
                var obj = await objectStore.GetObjectAsync(repoId, s);
                if (obj is GitCommit c)
                    foreach (var p in c.ParentShas) stopQueue.Enqueue(p);
            }
        }

        while (queue.Count > 0 && dates.Count < cap)
        {
            var sha = queue.Dequeue();
            if (stopSet.Contains(sha)) continue;
            if (!visited.Add(sha)) continue;

            var obj = await objectStore.GetObjectAsync(repoId, sha);
            if (obj is not GitCommit commit) continue;

            dates.Add(commit.Author.Timestamp.UtcDateTime);
            foreach (var p in commit.ParentShas) queue.Enqueue(p);
        }

        return dates;
    }

    private void ApplyCommitsToRollup(GitRepository repo, List<DateTime> commitDatesUtc)
        => ApplyCommitsToRollup(repo, (IEnumerable<DateTime>)commitDatesUtc);
}
