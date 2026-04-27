using System.Text;
using DaisiGit.Core.Git;
using DaisiGit.Core.Models;

namespace DaisiGit.Services;

/// <summary>
/// Service for browsing repository content: tree traversal, path resolution, commit history.
/// </summary>
public class BrowseService(GitObjectStore objectStore, GitRefService refService)
{
    /// <summary>
    /// Resolves a branch name or SHA to a commit SHA.
    /// </summary>
    public async Task<string?> ResolveRefAsync(string repositoryId, string refOrSha)
    {
        // Try as branch
        var sha = await refService.GetRefAsync(repositoryId, $"refs/heads/{refOrSha}");
        if (sha != null) return sha;

        // Try as tag
        sha = await refService.GetRefAsync(repositoryId, $"refs/tags/{refOrSha}");
        if (sha != null) return sha;

        // Try as full ref
        sha = await refService.GetRefAsync(repositoryId, refOrSha);
        if (sha != null) return sha;

        // Assume it's a SHA (or prefix) — validate it exists
        var obj = await objectStore.GetObjectAsync(repositoryId, refOrSha);
        return obj != null ? refOrSha : null;
    }

    /// <summary>
    /// Gets the tree entries at a given path within a commit.
    /// Returns null if the path doesn't exist or isn't a tree.
    /// </summary>
    public async Task<BrowseResult?> GetTreeAtPathAsync(string repositoryId, string commitSha, string path)
    {
        var commit = await objectStore.GetObjectAsync(repositoryId, commitSha) as GitCommit;
        if (commit == null) return null;

        var treeSha = commit.TreeSha;

        // Walk the path segments
        if (!string.IsNullOrEmpty(path) && path != "/")
        {
            var segments = path.Trim('/').Split('/');
            foreach (var segment in segments)
            {
                var tree = await objectStore.GetObjectAsync(repositoryId, treeSha) as GitTree;
                if (tree == null) return null;

                var entry = tree.Entries.FirstOrDefault(e => e.Name == segment);
                if (entry == null) return null;

                if (!entry.IsTree)
                {
                    // This is a file — return it as a blob result
                    return new BrowseResult
                    {
                        IsFile = true,
                        FileSha = entry.Sha,
                        FileName = entry.Name,
                        FileMode = entry.Mode,
                        CommitSha = commitSha,
                        Commit = commit,
                        Path = path
                    };
                }

                treeSha = entry.Sha;
            }
        }

        // Get the tree at the resolved path
        var finalTree = await objectStore.GetObjectAsync(repositoryId, treeSha) as GitTree;
        if (finalTree == null) return null;

        return new BrowseResult
        {
            IsFile = false,
            TreeSha = treeSha,
            Entries = finalTree.Entries.OrderBy(e => !e.IsTree).ThenBy(e => e.Name).ToList(),
            CommitSha = commitSha,
            Commit = commit,
            Path = path
        };
    }

    /// <summary>
    /// Gets the content of a blob (file) by SHA.
    /// </summary>
    public async Task<FileContent?> GetFileContentAsync(string repositoryId, string blobSha)
    {
        var blob = await objectStore.GetObjectAsync(repositoryId, blobSha) as GitBlob;
        if (blob == null) return null;

        var isBinary = IsBinary(blob.Data);
        return new FileContent
        {
            Sha = blobSha,
            Data = blob.Data,
            SizeBytes = blob.Data.Length,
            IsBinary = isBinary,
            Text = isBinary ? null : Encoding.UTF8.GetString(blob.Data)
        };
    }

    /// <summary>
    /// Gets the commit log starting from a given SHA, walking parents.
    /// </summary>
    public async Task<List<CommitInfo>> GetCommitLogAsync(string repositoryId, string startSha, int maxCount = 50, int skip = 0)
    {
        var commits = new List<CommitInfo>();
        var visited = new HashSet<string>();
        // Priority queue: negative unix timestamp = newest first
        var queue = new PriorityQueue<(string sha, GitCommit commit), long>();

        // Load the start commit to get its timestamp
        var startObj = await objectStore.GetObjectAsync(repositoryId, startSha);
        if (startObj is GitCommit startCommit)
            queue.Enqueue((startSha, startCommit), -startCommit.Author.Timestamp.ToUnixTimeSeconds());

        var skipped = 0;

        while (queue.Count > 0 && commits.Count < maxCount)
        {
            var (sha, commit) = queue.Dequeue();
            if (!visited.Add(sha))
                continue;

            if (skipped < skip)
            {
                skipped++;
            }
            else
            {
                commits.Add(new CommitInfo
                {
                    Sha = sha,
                    ShortSha = sha[..7],
                    TreeSha = commit.TreeSha,
                    ParentShas = commit.ParentShas,
                    AuthorName = commit.Author.Name,
                    AuthorEmail = commit.Author.Email,
                    AuthorDate = commit.Author.Timestamp,
                    CommitterName = commit.Committer.Name,
                    CommitterDate = commit.Committer.Timestamp,
                    Message = commit.Message.TrimEnd(),
                    MessageFirstLine = commit.Message.Split('\n')[0].TrimEnd()
                });
            }

            // Load parents and enqueue with their own timestamps
            foreach (var parentSha in commit.ParentShas)
            {
                if (!visited.Contains(parentSha))
                {
                    var parentObj = await objectStore.GetObjectAsync(repositoryId, parentSha);
                    if (parentObj is GitCommit parentCommit)
                        queue.Enqueue((parentSha, parentCommit), -parentCommit.Author.Timestamp.ToUnixTimeSeconds());
                }
            }
        }

        return commits;
    }

    /// <summary>
    /// Gets a single commit's details.
    /// </summary>
    public async Task<CommitInfo?> GetCommitAsync(string repositoryId, string commitSha)
    {
        var obj = await objectStore.GetObjectAsync(repositoryId, commitSha);
        if (obj is not GitCommit commit) return null;

        return new CommitInfo
        {
            Sha = commitSha,
            ShortSha = commitSha[..7],
            TreeSha = commit.TreeSha,
            ParentShas = commit.ParentShas,
            AuthorName = commit.Author.Name,
            AuthorEmail = commit.Author.Email,
            AuthorDate = commit.Author.Timestamp,
            CommitterName = commit.Committer.Name,
            CommitterDate = commit.Committer.Timestamp,
            Message = commit.Message.TrimEnd(),
            MessageFirstLine = commit.Message.Split('\n')[0].TrimEnd()
        };
    }

    /// <summary>
    /// Computes the diff between two commits (or a commit and its parent).
    /// Returns a list of changed files with their diffs.
    /// </summary>
    public async Task<List<FileDiff>> GetCommitDiffAsync(string repositoryId, string commitSha)
    {
        var commit = await objectStore.GetObjectAsync(repositoryId, commitSha) as GitCommit;
        if (commit == null) return [];

        string? parentTreeSha = null;
        if (commit.ParentShas.Count > 0)
        {
            var parent = await objectStore.GetObjectAsync(repositoryId, commit.ParentShas[0]) as GitCommit;
            parentTreeSha = parent?.TreeSha;
        }

        return await DiffTreesAsync(repositoryId, parentTreeSha, commit.TreeSha, "");
    }

    /// <summary>
    /// Gets all branches with their tip commit info.
    /// </summary>
    public async Task<List<BranchInfo>> GetBranchesAsync(string repositoryId, string defaultBranch)
    {
        var refs = await refService.GetAllRefsAsync(repositoryId);
        var branches = new List<BranchInfo>();

        foreach (var (refName, sha) in refs.Where(r => r.Key.StartsWith("refs/heads/")))
        {
            var branchName = refName["refs/heads/".Length..];
            var commit = await GetCommitAsync(repositoryId, sha);

            branches.Add(new BranchInfo
            {
                Name = branchName,
                Sha = sha,
                IsDefault = branchName == defaultBranch,
                LastCommit = commit
            });
        }

        return branches.OrderByDescending(b => b.IsDefault)
                       .ThenByDescending(b => b.LastCommit?.AuthorDate)
                       .ToList();
    }

    /// <summary>
    /// Returns the file paths that differ between two commits — additions, modifications,
    /// and deletions. Used by the push pipeline to populate push.changedPaths context for
    /// path-based trigger filters. Cheaper than GetCommitDiffAsync because it skips
    /// content comparison.
    /// </summary>
    public async Task<List<string>> GetChangedPathsAsync(string repositoryId, string? oldSha, string newSha)
    {
        var newCommit = await objectStore.GetObjectAsync(repositoryId, newSha) as GitCommit;
        if (newCommit == null) return [];

        string? oldTreeSha = null;
        if (!string.IsNullOrEmpty(oldSha) && !oldSha.All(c => c == '0'))
        {
            var oldCommit = await objectStore.GetObjectAsync(repositoryId, oldSha) as GitCommit;
            oldTreeSha = oldCommit?.TreeSha;
        }

        var diffs = await DiffTreesAsync(repositoryId, oldTreeSha, newCommit.TreeSha, "");
        return diffs.Select(d => d.Path).Distinct().ToList();
    }

    private async Task<List<FileDiff>> DiffTreesAsync(string repositoryId, string? oldTreeSha, string? newTreeSha, string basePath)
    {
        var diffs = new List<FileDiff>();

        var oldEntries = new Dictionary<string, GitTreeEntry>();
        var newEntries = new Dictionary<string, GitTreeEntry>();

        if (oldTreeSha != null)
        {
            var oldTree = await objectStore.GetObjectAsync(repositoryId, oldTreeSha) as GitTree;
            if (oldTree != null)
                foreach (var e in oldTree.Entries)
                    oldEntries[e.Name] = e;
        }

        if (newTreeSha != null)
        {
            var newTree = await objectStore.GetObjectAsync(repositoryId, newTreeSha) as GitTree;
            if (newTree != null)
                foreach (var e in newTree.Entries)
                    newEntries[e.Name] = e;
        }

        var allNames = oldEntries.Keys.Union(newEntries.Keys).OrderBy(n => n);

        foreach (var name in allNames)
        {
            var path = string.IsNullOrEmpty(basePath) ? name : $"{basePath}/{name}";
            var hasOld = oldEntries.TryGetValue(name, out var oldEntry);
            var hasNew = newEntries.TryGetValue(name, out var newEntry);

            if (hasOld && hasNew && oldEntry!.Sha == newEntry!.Sha)
                continue; // Unchanged

            var isOldTree = hasOld && oldEntry!.IsTree;
            var isNewTree = hasNew && newEntry!.IsTree;

            if (isOldTree || isNewTree)
            {
                // Recurse into subtrees
                var subDiffs = await DiffTreesAsync(
                    repositoryId,
                    isOldTree ? oldEntry!.Sha : null,
                    isNewTree ? newEntry!.Sha : null,
                    path);
                diffs.AddRange(subDiffs);
            }
            else
            {
                // File diff
                var diff = new FileDiff { Path = path };

                if (!hasOld)
                {
                    diff.Status = DiffStatus.Added;
                    diff.NewSha = newEntry!.Sha;
                    var content = await GetFileContentAsync(repositoryId, newEntry.Sha);
                    if (content is { IsBinary: false })
                        diff.NewContent = content.Text;
                }
                else if (!hasNew)
                {
                    diff.Status = DiffStatus.Deleted;
                    diff.OldSha = oldEntry!.Sha;
                    var content = await GetFileContentAsync(repositoryId, oldEntry.Sha);
                    if (content is { IsBinary: false })
                        diff.OldContent = content.Text;
                }
                else
                {
                    diff.Status = DiffStatus.Modified;
                    diff.OldSha = oldEntry!.Sha;
                    diff.NewSha = newEntry!.Sha;
                    var oldContent = await GetFileContentAsync(repositoryId, oldEntry.Sha);
                    var newContent = await GetFileContentAsync(repositoryId, newEntry.Sha);
                    if (oldContent is { IsBinary: false })
                        diff.OldContent = oldContent.Text;
                    if (newContent is { IsBinary: false })
                        diff.NewContent = newContent.Text;
                }

                diffs.Add(diff);
            }
        }

        return diffs;
    }

    private static bool IsBinary(byte[] data)
    {
        // Check first 8KB for null bytes (common heuristic)
        var checkLength = Math.Min(data.Length, 8192);
        for (var i = 0; i < checkLength; i++)
        {
            if (data[i] == 0)
                return true;
        }
        return false;
    }
}

// ── Result types ──

public class BrowseResult
{
    public bool IsFile { get; set; }
    public string Path { get; set; } = "";
    public string CommitSha { get; set; } = "";
    public GitCommit? Commit { get; set; }

    // Tree result
    public string? TreeSha { get; set; }
    public List<GitTreeEntry> Entries { get; set; } = [];

    // File result
    public string? FileSha { get; set; }
    public string? FileName { get; set; }
    public string? FileMode { get; set; }
}

public class FileContent
{
    public string Sha { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public int SizeBytes { get; set; }
    public bool IsBinary { get; set; }
    public string? Text { get; set; }
}

public class CommitInfo
{
    public string Sha { get; set; } = "";
    public string ShortSha { get; set; } = "";
    public string TreeSha { get; set; } = "";
    public List<string> ParentShas { get; set; } = [];
    public string AuthorName { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public DateTimeOffset AuthorDate { get; set; }
    public string CommitterName { get; set; } = "";
    public DateTimeOffset CommitterDate { get; set; }
    public string Message { get; set; } = "";
    public string MessageFirstLine { get; set; } = "";
}

public class FileDiff
{
    public string Path { get; set; } = "";
    public DiffStatus Status { get; set; }
    public string? OldSha { get; set; }
    public string? NewSha { get; set; }
    public string? OldContent { get; set; }
    public string? NewContent { get; set; }
}

public enum DiffStatus
{
    Added,
    Modified,
    Deleted
}

public class BranchInfo
{
    public string Name { get; set; } = "";
    public string Sha { get; set; } = "";
    public bool IsDefault { get; set; }
    public CommitInfo? LastCommit { get; set; }
}
