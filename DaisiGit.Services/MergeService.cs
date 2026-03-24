using System.Text;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Git;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Handles merging pull requests — creates merge commits and updates refs.
/// </summary>
public class MergeService(
    DaisiGitCosmo cosmo,
    GitObjectStore objectStore,
    GitRefService refService,
    BrowseService browseService)
{
    /// <summary>
    /// Merges a pull request. Creates a merge commit on the target branch.
    /// Currently supports merge commits only (not squash or rebase).
    /// </summary>
    public async Task<MergeResult> MergeAsync(
        PullRequest pr, GitRepository repo,
        string mergerName, string mergerEmail,
        MergeStrategy strategy = MergeStrategy.Merge)
    {
        if (pr.Status != PullRequestStatus.Open)
            return new MergeResult { Success = false, Error = "Pull request is not open." };

        // Resolve source and target branch SHAs
        var sourceSha = await browseService.ResolveRefAsync(repo.id, pr.SourceBranch);
        var targetSha = await browseService.ResolveRefAsync(repo.id, pr.TargetBranch);

        if (sourceSha == null)
            return new MergeResult { Success = false, Error = $"Source branch '{pr.SourceBranch}' not found." };
        if (targetSha == null)
            return new MergeResult { Success = false, Error = $"Target branch '{pr.TargetBranch}' not found." };

        if (sourceSha == targetSha)
            return new MergeResult { Success = false, Error = "Source and target branches are identical." };

        // Get source commit's tree
        var sourceCommit = await objectStore.GetObjectAsync(repo.id, sourceSha) as GitCommit;
        if (sourceCommit == null)
            return new MergeResult { Success = false, Error = "Could not read source commit." };

        string mergeCommitSha;

        if (strategy == MergeStrategy.Squash)
        {
            // Squash: create a single new commit with the source tree on top of target
            var signature = new GitSignature
            {
                Name = mergerName,
                Email = mergerEmail,
                Timestamp = DateTimeOffset.UtcNow
            };

            var squashCommit = new GitCommit
            {
                TreeSha = sourceCommit.TreeSha,
                ParentShas = [targetSha],
                Author = signature,
                Committer = signature,
                Message = $"{pr.Title} (#{pr.Number})\n\nSquash merge of '{pr.SourceBranch}' into '{pr.TargetBranch}'\n"
            };
            mergeCommitSha = await objectStore.StoreObjectAsync(repo, squashCommit);
        }
        else
        {
            // Merge commit: two parents (target + source)
            var signature = new GitSignature
            {
                Name = mergerName,
                Email = mergerEmail,
                Timestamp = DateTimeOffset.UtcNow
            };

            var mergeCommit = new GitCommit
            {
                TreeSha = sourceCommit.TreeSha,
                ParentShas = [targetSha, sourceSha],
                Author = signature,
                Committer = signature,
                Message = $"Merge pull request #{pr.Number} from {pr.SourceBranch}\n\n{pr.Title}\n"
            };
            mergeCommitSha = await objectStore.StoreObjectAsync(repo, mergeCommit);
        }

        // Update target branch ref
        var updated = await refService.UpdateRefAsync(repo.id, $"refs/heads/{pr.TargetBranch}", mergeCommitSha, targetSha);
        if (!updated)
            return new MergeResult { Success = false, Error = "Target branch was updated concurrently. Please retry." };

        // Mark PR as merged
        pr.Status = PullRequestStatus.Merged;
        pr.MergeCommitSha = mergeCommitSha;
        pr.MergeStrategy = strategy;
        pr.MergedUtc = DateTime.UtcNow;
        await cosmo.UpdatePullRequestAsync(pr);

        return new MergeResult { Success = true, MergeCommitSha = mergeCommitSha };
    }

    /// <summary>
    /// Checks if a PR can be merged (branches exist and differ).
    /// </summary>
    public async Task<bool> CanMergeAsync(string repositoryId, string sourceBranch, string targetBranch)
    {
        var sourceSha = await browseService.ResolveRefAsync(repositoryId, sourceBranch);
        var targetSha = await browseService.ResolveRefAsync(repositoryId, targetBranch);
        return sourceSha != null && targetSha != null && sourceSha != targetSha;
    }
}

/// <summary>
/// Result of a merge operation.
/// </summary>
public class MergeResult
{
    public bool Success { get; set; }
    public string? MergeCommitSha { get; set; }
    public string? Error { get; set; }
}
