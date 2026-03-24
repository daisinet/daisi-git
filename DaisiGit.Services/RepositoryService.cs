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
    StorageAdapterFactory storageFactory)
{
    /// <summary>
    /// Creates a new git repository with an initial empty commit.
    /// </summary>
    public async Task<GitRepository> CreateRepositoryAsync(
        string accountId, string ownerId, string ownerName,
        string name, string? description, GitRepoVisibility visibility,
        StorageProvider? storageProviderOverride = null)
    {
        var slug = Slugify(name);

        // Check for duplicate slug under the same owner
        var existing = await cosmo.GetRepositoryBySlugAsync(ownerName, slug);
        if (existing != null)
            throw new InvalidOperationException($"Repository '{ownerName}/{slug}' already exists.");

        // Resolve storage provider: explicit override > account default
        var accountDefault = await storageFactory.GetAccountDefaultAsync(accountId);
        var provider = storageProviderOverride ?? accountDefault;
        var drive = storageFactory.GetAdapter(provider);

        // Create a storage container/repository
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
            DefaultBranch = "main",
            StorageProvider = storageProviderOverride
        });

        // Create initial empty tree + commit
        var emptyTree = new GitTree { Entries = [] };
        var treeSha = await objectStore.StoreObjectAsync(repo, emptyTree);

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
        var commitSha = await objectStore.StoreObjectAsync(repo, initialCommit);

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
    /// Deletes a repository and its storage.
    /// </summary>
    public async Task DeleteRepositoryAsync(string id, string accountId)
    {
        var repo = await cosmo.GetRepositoryAsync(id, accountId);
        if (repo == null) return;

        // Delete storage
        var drive = await storageFactory.GetAdapterAsync(repo);
        await drive.DeleteRepositoryAsync(repo.DriveRepositoryId);

        // Delete Cosmos records
        await cosmo.DeleteRepositoryAsync(id, accountId);
    }

    /// <summary>
    /// Updates repository metadata.
    /// StorageProvider and DriveRepositoryId are immutable after creation and will be
    /// restored from the persisted record to prevent accidental corruption.
    /// </summary>
    public async Task<GitRepository> UpdateRepositoryAsync(GitRepository repo)
    {
        var existing = await cosmo.GetRepositoryAsync(repo.id, repo.AccountId);
        if (existing != null)
        {
            repo.StorageProvider = existing.StorageProvider;
            repo.DriveRepositoryId = existing.DriveRepositoryId;
        }

        return await cosmo.UpdateRepositoryAsync(repo);
    }

    // ── Fork ──

    /// <summary>
    /// Forks a repository. Returns existing fork if user already forked it.
    /// Copies object records (sharing storage file IDs) and refs, then increments upstream ForkCount.
    /// </summary>
    public async Task<GitRepository> ForkRepositoryAsync(
        string accountId, string forkOwnerId, string forkOwnerName, GitRepository upstream)
    {
        // Duplicate check — return existing fork
        var existingFork = await cosmo.GetExistingForkAsync(upstream.id, forkOwnerId);
        if (existingFork != null)
            return existingFork;

        var slug = Slugify(upstream.Name);

        // Deduplicate slug under the fork owner
        var existing = await cosmo.GetRepositoryBySlugAsync(forkOwnerName, slug);
        if (existing != null)
            slug = $"{slug}-{Guid.NewGuid().ToString("N")[..4]}";

        // Fork inherits the upstream's storage provider
        var provider = upstream.StorageProvider
            ?? (await storageFactory.GetAccountDefaultAsync(upstream.AccountId));
        var drive = storageFactory.GetAdapter(provider);

        // Create storage container for the fork (future pushes go here)
        var driveRepoId = await drive.CreateRepositoryAsync($"git-{forkOwnerName}-{slug}");

        var fork = await cosmo.CreateRepositoryAsync(new GitRepository
        {
            AccountId = accountId,
            OwnerId = forkOwnerId,
            OwnerName = forkOwnerName,
            Name = upstream.Name,
            Slug = slug,
            Description = upstream.Description,
            Visibility = upstream.Visibility,
            DriveRepositoryId = driveRepoId,
            DefaultBranch = upstream.DefaultBranch,
            IsEmpty = upstream.IsEmpty,
            ForkedFromId = upstream.id,
            ForkedFromOwnerName = upstream.OwnerName,
            ForkedFromSlug = upstream.Slug,
            StorageProvider = upstream.StorageProvider
        });

        // Copy object records (same DriveFileId, new RepositoryId)
        var objects = await cosmo.GetAllObjectRecordsAsync(upstream.id);
        foreach (var obj in objects)
        {
            await cosmo.UpsertObjectRecordAsync(new GitObjectRecord
            {
                id = obj.id,
                RepositoryId = fork.id,
                DriveFileId = obj.DriveFileId,
                ObjectType = obj.ObjectType,
                SizeBytes = obj.SizeBytes
            });
        }

        // Copy refs
        var refs = await refService.GetAllRefsAsync(upstream.id);
        foreach (var (refName, sha) in refs)
        {
            await refService.SetRefAsync(fork.id, refName, sha);
        }

        // Copy HEAD
        var head = await refService.GetHeadAsync(upstream.id);
        if (head != null)
            await refService.SetHeadAsync(fork.id, head);

        // Increment upstream ForkCount
        upstream.ForkCount++;
        await cosmo.UpdateRepositoryAsync(upstream);

        return fork;
    }

    /// <summary>
    /// Lists forks of a repository.
    /// </summary>
    public async Task<List<GitRepository>> GetForksAsync(string repositoryId)
    {
        return await cosmo.GetForksAsync(repositoryId);
    }

    // ── Stars ──

    /// <summary>
    /// Stars a repository. Idempotent — does nothing if already starred.
    /// </summary>
    public async Task StarAsync(string userId, string userName, string repositoryId)
    {
        var existing = await cosmo.GetStarAsync(repositoryId, userId);
        if (existing != null)
            return;

        await cosmo.CreateStarAsync(new RepoStar
        {
            RepositoryId = repositoryId,
            UserId = userId,
            UserName = userName
        });

        await IncrementStarCountAsync(repositoryId, 1);
    }

    /// <summary>
    /// Unstars a repository. Silently succeeds if not starred.
    /// </summary>
    public async Task UnstarAsync(string userId, string repositoryId)
    {
        var star = await cosmo.GetStarAsync(repositoryId, userId);
        if (star == null)
            return;

        await cosmo.DeleteStarAsync(star.id, repositoryId);
        await IncrementStarCountAsync(repositoryId, -1);
    }

    /// <summary>
    /// Checks if a user has starred a repository.
    /// </summary>
    public async Task<bool> HasStarredAsync(string userId, string repositoryId)
    {
        var star = await cosmo.GetStarAsync(repositoryId, userId);
        return star != null;
    }

    /// <summary>
    /// Gets all repositories starred by a user, hydrated from star records.
    /// </summary>
    public async Task<List<GitRepository>> GetStarredReposAsync(string userId)
    {
        var stars = await cosmo.GetStarsByUserAsync(userId);
        var repos = new List<GitRepository>();
        foreach (var star in stars)
        {
            var repo = await GetRepositoryByIdAsync(star.RepositoryId);
            if (repo != null)
                repos.Add(repo);
        }
        return repos;
    }

    /// <summary>
    /// Lists public repositories sorted by star count for the explore page.
    /// </summary>
    public async Task<List<GitRepository>> GetPublicReposAsync(int skip = 0, int take = 20)
    {
        return await cosmo.GetPublicRepositoriesAsync(skip, take);
    }

    private async Task IncrementStarCountAsync(string repositoryId, int delta)
    {
        var repo = await GetRepositoryByIdAsync(repositoryId);
        if (repo == null) return;

        repo.StarCount = Math.Max(0, repo.StarCount + delta);
        await cosmo.UpdateRepositoryAsync(repo);
    }

    private async Task<GitRepository?> GetRepositoryByIdAsync(string repositoryId)
    {
        // Cross-partition query to find repo by id
        var container = await cosmo.GetContainerAsync(DaisiGitCosmo.RepositoriesContainerName);
        var query = new Microsoft.Azure.Cosmos.QueryDefinition(
            "SELECT * FROM c WHERE c.id = @id AND c.Type = 'GitRepository'")
            .WithParameter("@id", repositoryId);

        using var iterator = container.GetItemQueryIterator<GitRepository>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
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
