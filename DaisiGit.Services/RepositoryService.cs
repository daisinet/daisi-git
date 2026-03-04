using System.Text;
using System.Text.RegularExpressions;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Git;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages git repositories — creation, listing, deletion.
/// </summary>
public partial class RepositoryService(
    DaisiGitCosmo cosmo,
    GitObjectStore objectStore,
    GitRefService refService,
    IDriveAdapter drive)
{
    /// <summary>
    /// Creates a new git repository with an initial empty commit.
    /// </summary>
    public async Task<GitRepository> CreateRepositoryAsync(
        string accountId, string ownerId, string ownerName,
        string name, string? description, GitRepoVisibility visibility)
    {
        var slug = Slugify(name);

        // Check for duplicate slug under the same owner
        var existing = await cosmo.GetRepositoryBySlugAsync(ownerName, slug);
        if (existing != null)
            throw new InvalidOperationException($"Repository '{ownerName}/{slug}' already exists.");

        // Create a Drive repository for storage
        var driveRepoId = await drive.CreateRepositoryAsync($"git-{ownerName}-{slug}");

        var repo = await cosmo.CreateRepositoryAsync(new GitRepository
        {
            AccountId = accountId,
            OwnerId = ownerId,
            OwnerName = ownerName,
            Name = name,
            Slug = slug,
            Description = description,
            Visibility = visibility,
            DriveRepositoryId = driveRepoId,
            DefaultBranch = "main"
        });

        // Create initial empty tree + commit
        var emptyTree = new GitTree { Entries = [] };
        var treeSha = await objectStore.StoreObjectAsync(repo.id, driveRepoId, emptyTree);

        var signature = new GitSignature
        {
            Name = ownerName,
            Email = $"{ownerName}@daisigit.local",
            Timestamp = DateTimeOffset.UtcNow
        };

        var initialCommit = new GitCommit
        {
            TreeSha = treeSha,
            ParentShas = [],
            Author = signature,
            Committer = signature,
            Message = "Initial commit\n"
        };
        var commitSha = await objectStore.StoreObjectAsync(repo.id, driveRepoId, initialCommit);

        // Set up refs
        await refService.SetRefAsync(repo.id, "refs/heads/main", commitSha);
        await refService.SetHeadAsync(repo.id, "ref: refs/heads/main");

        // Mark as non-empty
        repo.IsEmpty = false;
        repo = await cosmo.UpdateRepositoryAsync(repo);

        return repo;
    }

    /// <summary>
    /// Gets a repository by owner and slug (for URL resolution).
    /// </summary>
    public async Task<GitRepository?> GetRepositoryBySlugAsync(string ownerName, string slug)
    {
        return await cosmo.GetRepositoryBySlugAsync(ownerName, slug);
    }

    /// <summary>
    /// Gets a repository by ID.
    /// </summary>
    public async Task<GitRepository?> GetRepositoryAsync(string id, string accountId)
    {
        return await cosmo.GetRepositoryAsync(id, accountId);
    }

    /// <summary>
    /// Lists all repositories for an account.
    /// </summary>
    public async Task<List<GitRepository>> GetRepositoriesAsync(string accountId)
    {
        return await cosmo.GetRepositoriesAsync(accountId);
    }

    /// <summary>
    /// Lists all repositories for an owner (public-facing).
    /// </summary>
    public async Task<List<GitRepository>> GetRepositoriesByOwnerAsync(string ownerName)
    {
        return await cosmo.GetRepositoriesByOwnerAsync(ownerName);
    }

    /// <summary>
    /// Deletes a repository and its Drive storage.
    /// </summary>
    public async Task DeleteRepositoryAsync(string id, string accountId)
    {
        var repo = await cosmo.GetRepositoryAsync(id, accountId);
        if (repo == null) return;

        // Delete Drive repository
        await drive.DeleteRepositoryAsync(repo.DriveRepositoryId);

        // Delete Cosmos records
        await cosmo.DeleteRepositoryAsync(id, accountId);
    }

    /// <summary>
    /// Updates repository metadata.
    /// </summary>
    public async Task<GitRepository> UpdateRepositoryAsync(GitRepository repo)
    {
        return await cosmo.UpdateRepositoryAsync(repo);
    }

    internal static string Slugify(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = SlugRegex().Replace(slug, "-");
        slug = slug.Trim('-');
        return slug;
    }

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex SlugRegex();
}
