using System.Text;
using DaisiGit.Core.Git;
using DaisiGit.Core.Models;

namespace DaisiGit.Services;

public class FileWriteService(GitObjectStore objectStore, BrowseService browseService, GitRefService refService)
{
    public async Task<(string BlobSha, string CommitSha)> WriteFileAsync(
        GitRepository repo, string branch, string path, string content,
        string commitMessage, string userName)
    {
        var branchSha = await browseService.ResolveRefAsync(repo.id, branch)
            ?? throw new InvalidOperationException($"Branch '{branch}' not found.");

        var commit = await objectStore.GetObjectAsync(repo.id, branchSha) as GitCommit
            ?? throw new InvalidOperationException($"Could not resolve commit for branch '{branch}'.");

        var blob = new GitBlob { Data = Encoding.UTF8.GetBytes(content) };
        var blobSha = await objectStore.StoreObjectAsync(repo, blob);

        var segments = path.Trim('/').Split('/');
        var newTreeSha = await BuildTreeWithFileAsync(repo, commit.TreeSha, segments, 0, blobSha);

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
            Message = commitMessage
        };

        var commitSha = await objectStore.StoreObjectAsync(repo, newCommit);
        await refService.SetRefAsync(repo.id, $"refs/heads/{branch}", commitSha);

        return (blobSha, commitSha);
    }

    private async Task<string> BuildTreeWithFileAsync(
        GitRepository repo, string currentTreeSha, string[] pathSegments, int depth, string blobSha)
    {
        var tree = await objectStore.GetObjectAsync(repo.id, currentTreeSha) as GitTree;
        var entries = tree?.Entries.ToList() ?? [];

        var segment = pathSegments[depth];
        var isLastSegment = depth == pathSegments.Length - 1;

        if (isLastSegment)
        {
            var existingIdx = entries.FindIndex(e => e.Name == segment);
            var entry = new GitTreeEntry { Name = segment, Mode = "100644", Sha = blobSha };
            if (existingIdx >= 0) entries[existingIdx] = entry;
            else entries.Add(entry);
        }
        else
        {
            var existingIdx = entries.FindIndex(e => e.Name == segment);
            string subTreeSha;

            if (existingIdx >= 0 && entries[existingIdx].IsTree)
            {
                subTreeSha = await BuildTreeWithFileAsync(
                    repo, entries[existingIdx].Sha, pathSegments, depth + 1, blobSha);
                entries[existingIdx] = new GitTreeEntry { Name = segment, Mode = "40000", Sha = subTreeSha };
            }
            else
            {
                var emptyTree = new GitTree();
                var emptySha = await objectStore.StoreObjectAsync(repo, emptyTree);
                subTreeSha = await BuildTreeWithFileAsync(repo, emptySha, pathSegments, depth + 1, blobSha);

                if (existingIdx >= 0)
                    entries[existingIdx] = new GitTreeEntry { Name = segment, Mode = "40000", Sha = subTreeSha };
                else
                    entries.Add(new GitTreeEntry { Name = segment, Mode = "40000", Sha = subTreeSha });
            }
        }

        entries.Sort((a, b) =>
        {
            var aName = a.IsTree ? a.Name + "/" : a.Name;
            var bName = b.IsTree ? b.Name + "/" : b.Name;
            return string.Compare(aName, bName, StringComparison.Ordinal);
        });

        var newTree = new GitTree { Entries = entries };
        return await objectStore.StoreObjectAsync(repo, newTree);
    }
}
