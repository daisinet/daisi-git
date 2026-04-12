using System.Diagnostics;
using System.Text;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Git;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Imports a public git repository from an external URL by cloning it locally,
/// then storing all objects and refs into DaisiGit's storage.
/// </summary>
public class ImportService(
    RepositoryService repoService,
    GitObjectStore objectStore,
    GitRefService refService,
    BrowseService browseService,
    DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Imports a public git repo from a URL. Creates the DaisiGit repo and populates it.
    /// </summary>
    public async Task<GitRepository> ImportAsync(
        string sourceUrl, string accountId, string ownerId, string ownerName,
        string? repoName = null, string? description = null,
        GitRepoVisibility visibility = GitRepoVisibility.Private,
        StorageProvider? storageProvider = null,
        Action<string>? onProgress = null)
    {
        // Derive repo name from URL if not specified
        if (string.IsNullOrEmpty(repoName))
        {
            var uri = new Uri(sourceUrl);
            repoName = uri.Segments.LastOrDefault()?.TrimEnd('/').Replace(".git", "") ?? "imported-repo";
        }

        onProgress?.Invoke($"Creating repository {ownerName}/{repoName}...");

        // Create the DaisiGit repo
        var repo = await repoService.CreateRepositoryAsync(
            accountId, ownerId, ownerName, repoName, description, visibility, storageProvider);

        // Store the normalized import source URL for re-import detection
        repo.ImportedFromUrl = NormalizeImportUrl(sourceUrl);
        repo = await repoService.UpdateRepositoryAsync(repo);

        // Clone to temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"daisigit-import-{Guid.NewGuid():N}");
        try
        {
            onProgress?.Invoke($"Cloning {sourceUrl}...");

            var cloneResult = await RunGitAsync($"clone --bare \"{sourceUrl}\" \"{tempDir}\"");
            if (cloneResult.ExitCode != 0)
                throw new InvalidOperationException($"Git clone failed: {cloneResult.Error}");

            onProgress?.Invoke("Reading objects...");

            // Read all loose objects and pack files from the bare clone
            await ImportObjectsAsync(tempDir, repo, onProgress);

            onProgress?.Invoke("Importing refs...");

            // Import refs
            await ImportRefsAsync(tempDir, repo);

            // Mark as non-empty
            repo.IsEmpty = false;
            repo = await repoService.UpdateRepositoryAsync(repo);

            onProgress?.Invoke("Import complete.");
        }
        finally
        {
            // Cleanup temp directory
            try { Directory.Delete(tempDir, true); } catch { }
        }

        return repo;
    }

    /// <summary>
    /// Finds repositories in the account that were previously imported from the given URL.
    /// </summary>
    public async Task<List<GitRepository>> FindExistingImportsAsync(string accountId, string sourceUrl)
    {
        var normalized = NormalizeImportUrl(sourceUrl);
        return await cosmo.GetRepositoriesByImportUrlAsync(accountId, normalized);
    }

    /// <summary>
    /// Re-imports (pulls latest changes) from the original source URL into an existing repository.
    /// </summary>
    public async Task<GitRepository> ReimportAsync(
        GitRepository repo,
        Action<string>? onProgress = null)
    {
        var sourceUrl = repo.ImportedFromUrl;
        if (string.IsNullOrEmpty(sourceUrl))
            throw new InvalidOperationException("This repository was not imported from an external URL.");

        var tempDir = Path.Combine(Path.GetTempPath(), $"daisigit-reimport-{Guid.NewGuid():N}");
        try
        {
            onProgress?.Invoke($"Cloning latest from {sourceUrl}...");

            var cloneResult = await RunGitAsync($"clone --bare \"{sourceUrl}\" \"{tempDir}\"");
            if (cloneResult.ExitCode != 0)
                throw new InvalidOperationException($"Git clone failed: {cloneResult.Error}");

            onProgress?.Invoke("Reading objects...");

            await ImportObjectsAsync(tempDir, repo, onProgress);

            onProgress?.Invoke("Updating refs...");

            await ImportRefsAsync(tempDir, repo);

            repo.IsEmpty = false;
            repo = await repoService.UpdateRepositoryAsync(repo);

            onProgress?.Invoke("Re-import complete.");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        return repo;
    }

    /// <summary>
    /// Normalizes a git URL for consistent comparison.
    /// Trims whitespace/trailing slashes, removes trailing .git suffix.
    /// </summary>
    private static string NormalizeImportUrl(string url)
    {
        var normalized = url.Trim().TrimEnd('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];
        return normalized;
    }

    private async Task ImportObjectsAsync(string bareRepoPath, GitRepository repo, Action<string>? onProgress)
    {
        // Use git cat-file --batch to read all objects
        // First, get all object SHAs
        var listResult = await RunGitAsync(
            $"--git-dir \"{bareRepoPath}\" rev-list --all --objects", timeoutMs: 120000);
        if (listResult.ExitCode != 0)
            throw new InvalidOperationException($"Failed to list objects: {listResult.Error}");

        var lines = listResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var shas = lines.Select(l => l.Split(' ')[0]).Where(s => s.Length == 40).Distinct().ToList();

        onProgress?.Invoke($"Importing {shas.Count} objects...");

        var count = 0;
        foreach (var sha in shas)
        {
            // Read the raw object
            var catResult = await RunGitAsync(
                $"--git-dir \"{bareRepoPath}\" cat-file -t {sha}");
            if (catResult.ExitCode != 0) continue;

            var objectType = catResult.Output.Trim();

            // Get raw content
            var rawResult = await RunGitRawAsync(
                $"--git-dir \"{bareRepoPath}\" cat-file {objectType} {sha}");
            if (rawResult.Data == null) continue;

            // Build the full git object (header + content)
            var header = Encoding.ASCII.GetBytes($"{objectType} {rawResult.Data.Length}\0");
            var raw = new byte[header.Length + rawResult.Data.Length];
            Buffer.BlockCopy(header, 0, raw, 0, header.Length);
            Buffer.BlockCopy(rawResult.Data, 0, raw, header.Length, rawResult.Data.Length);

            await objectStore.StoreRawObjectAsync(repo, raw, objectType);

            count++;
            if (count % 100 == 0)
                onProgress?.Invoke($"Imported {count}/{shas.Count} objects...");
        }

        onProgress?.Invoke($"Imported {count} objects.");
    }

    private async Task ImportRefsAsync(string bareRepoPath, GitRepository repo)
    {
        // Read all refs
        var refsResult = await RunGitAsync(
            $"--git-dir \"{bareRepoPath}\" show-ref");

        if (refsResult.ExitCode == 0)
        {
            var lines = refsResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(' ', 2);
                if (parts.Length == 2)
                {
                    var sha = parts[0];
                    var refName = parts[1];
                    await refService.SetRefAsync(repo.id, refName, sha);
                }
            }
        }

        // Read HEAD
        var headResult = await RunGitAsync(
            $"--git-dir \"{bareRepoPath}\" symbolic-ref HEAD");
        if (headResult.ExitCode == 0)
        {
            var headRef = headResult.Output.Trim();
            await refService.SetHeadAsync(repo.id, $"ref: {headRef}");

            // Set default branch from HEAD
            if (headRef.StartsWith("refs/heads/"))
            {
                var defaultBranch = headRef["refs/heads/".Length..];
                if (defaultBranch != repo.DefaultBranch)
                {
                    repo.DefaultBranch = defaultBranch;
                    await repoService.UpdateRepositoryAsync(repo);
                }
            }
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunGitAsync(
        string arguments, int timeoutMs = 60000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "", "Failed to start git");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        var completed = process.WaitForExit(timeoutMs);
        if (!completed)
        {
            process.Kill();
            return (-1, "", "Git command timed out");
        }

        return (process.ExitCode, output, error);
    }

    private static async Task<(int ExitCode, byte[]? Data)> RunGitRawAsync(
        string arguments, int timeoutMs = 30000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, null);

        using var ms = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(ms);

        var completed = process.WaitForExit(timeoutMs);
        if (!completed)
        {
            process.Kill();
            return (-1, null);
        }

        return (process.ExitCode, ms.ToArray());
    }
}
