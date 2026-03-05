using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages pull request lifecycle — create, update, close, list.
/// </summary>
public class PullRequestService(DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Creates a new pull request.
    /// </summary>
    public async Task<PullRequest> CreateAsync(
        string repositoryId, string title, string? description,
        string sourceBranch, string targetBranch,
        string authorId, string authorName, List<string>? labels = null)
    {
        var pr = new PullRequest
        {
            RepositoryId = repositoryId,
            Title = title,
            Description = description,
            SourceBranch = sourceBranch,
            TargetBranch = targetBranch,
            AuthorId = authorId,
            AuthorName = authorName,
            Labels = labels ?? []
        };

        return await cosmo.CreatePullRequestAsync(pr);
    }

    /// <summary>
    /// Gets a pull request by number within a repository.
    /// </summary>
    public async Task<PullRequest?> GetByNumberAsync(string repositoryId, int number)
    {
        return await cosmo.GetPullRequestByNumberAsync(repositoryId, number);
    }

    /// <summary>
    /// Gets a pull request by ID.
    /// </summary>
    public async Task<PullRequest?> GetAsync(string id, string repositoryId)
    {
        return await cosmo.GetPullRequestAsync(id, repositoryId);
    }

    /// <summary>
    /// Lists pull requests for a repository, optionally filtered by status.
    /// </summary>
    public async Task<List<PullRequest>> ListAsync(string repositoryId, PullRequestStatus? status = null)
    {
        return await cosmo.GetPullRequestsAsync(repositoryId, status);
    }

    /// <summary>
    /// Closes a pull request without merging.
    /// </summary>
    public async Task<PullRequest> CloseAsync(PullRequest pr)
    {
        pr.Status = PullRequestStatus.Closed;
        pr.ClosedUtc = DateTime.UtcNow;
        return await cosmo.UpdatePullRequestAsync(pr);
    }

    /// <summary>
    /// Reopens a closed pull request.
    /// </summary>
    public async Task<PullRequest> ReopenAsync(PullRequest pr)
    {
        pr.Status = PullRequestStatus.Open;
        pr.ClosedUtc = null;
        return await cosmo.UpdatePullRequestAsync(pr);
    }

    /// <summary>
    /// Updates pull request metadata (title, description, labels).
    /// </summary>
    public async Task<PullRequest> UpdateAsync(PullRequest pr)
    {
        return await cosmo.UpdatePullRequestAsync(pr);
    }
}
