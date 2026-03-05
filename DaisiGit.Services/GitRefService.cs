using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages git refs (branches, tags, HEAD) stored in Cosmos DB.
/// </summary>
public class GitRefService(DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Gets a ref value (SHA) by name. Returns null if not found.
    /// </summary>
    public async Task<string?> GetRefAsync(string repositoryId, string refName)
    {
        var gitRef = await cosmo.GetRefAsync(repositoryId, refName);
        return gitRef?.Target;
    }

    /// <summary>
    /// Sets a ref to point to a SHA.
    /// </summary>
    public async Task SetRefAsync(string repositoryId, string refName, string sha)
    {
        await cosmo.UpsertRefAsync(new GitRef
        {
            RepositoryId = repositoryId,
            Name = refName,
            Target = sha,
            IsSymbolic = false
        });
    }

    /// <summary>
    /// Gets the HEAD value for a repository.
    /// </summary>
    public async Task<string?> GetHeadAsync(string repositoryId)
    {
        var head = await cosmo.GetRefAsync(repositoryId, "HEAD");
        return head?.Target;
    }

    /// <summary>
    /// Sets HEAD (typically a symbolic ref like "ref: refs/heads/main").
    /// </summary>
    public async Task SetHeadAsync(string repositoryId, string target, bool isSymbolic = true)
    {
        await cosmo.UpsertRefAsync(new GitRef
        {
            RepositoryId = repositoryId,
            Name = "HEAD",
            Target = target,
            IsSymbolic = isSymbolic
        });
    }

    /// <summary>
    /// Resolves HEAD to a SHA (follows symbolic refs).
    /// </summary>
    public async Task<string?> ResolveHeadAsync(string repositoryId)
    {
        var head = await GetHeadAsync(repositoryId);
        if (head == null) return null;

        if (head.StartsWith("ref: "))
        {
            var refName = head[5..]; // e.g., "refs/heads/main"
            return await GetRefAsync(repositoryId, refName);
        }

        return head; // Detached HEAD — direct SHA
    }

    /// <summary>
    /// Gets all refs (branches + tags) as a dictionary of refName → SHA.
    /// </summary>
    public async Task<Dictionary<string, string>> GetAllRefsAsync(string repositoryId)
    {
        var refs = await cosmo.GetAllRefsAsync(repositoryId);
        var result = new Dictionary<string, string>();
        foreach (var r in refs)
        {
            if (r.Name == "HEAD") continue; // HEAD is handled separately
            result[r.Name] = r.Target;
        }
        return result;
    }

    /// <summary>
    /// Deletes a ref.
    /// </summary>
    public async Task DeleteRefAsync(string repositoryId, string refName)
    {
        await cosmo.DeleteRefAsync(repositoryId, refName);
    }

    /// <summary>
    /// Updates a ref atomically (compare-and-swap). Returns true if successful.
    /// </summary>
    public async Task<bool> UpdateRefAsync(string repositoryId, string refName, string newSha, string? expectedOldSha)
    {
        if (expectedOldSha != null)
        {
            var current = await GetRefAsync(repositoryId, refName);
            if (current != expectedOldSha)
                return false;
        }

        await SetRefAsync(repositoryId, refName, newSha);
        return true;
    }
}
