using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages issue lifecycle — create, update, close, list.
/// </summary>
public class IssueService(DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Creates a new issue.
    /// </summary>
    public async Task<Issue> CreateAsync(
        string repositoryId, string title, string? description,
        string authorId, string authorName, List<string>? labels = null)
    {
        var issue = new Issue
        {
            RepositoryId = repositoryId,
            Title = title,
            Description = description,
            AuthorId = authorId,
            AuthorName = authorName,
            Labels = labels ?? []
        };

        return await cosmo.CreateIssueAsync(issue);
    }

    /// <summary>
    /// Gets an issue by number within a repository.
    /// </summary>
    public async Task<Issue?> GetByNumberAsync(string repositoryId, int number)
    {
        return await cosmo.GetIssueByNumberAsync(repositoryId, number);
    }

    /// <summary>
    /// Gets an issue by ID.
    /// </summary>
    public async Task<Issue?> GetAsync(string id, string repositoryId)
    {
        return await cosmo.GetIssueAsync(id, repositoryId);
    }

    /// <summary>
    /// Lists issues for a repository, optionally filtered by status.
    /// </summary>
    public async Task<List<Issue>> ListAsync(string repositoryId, IssueStatus? status = null)
    {
        return await cosmo.GetIssuesAsync(repositoryId, status);
    }

    /// <summary>
    /// Closes an issue.
    /// </summary>
    public async Task<Issue> CloseAsync(Issue issue)
    {
        issue.Status = IssueStatus.Closed;
        issue.ClosedUtc = DateTime.UtcNow;
        return await cosmo.UpdateIssueAsync(issue);
    }

    /// <summary>
    /// Reopens a closed issue.
    /// </summary>
    public async Task<Issue> ReopenAsync(Issue issue)
    {
        issue.Status = IssueStatus.Open;
        issue.ClosedUtc = null;
        return await cosmo.UpdateIssueAsync(issue);
    }

    /// <summary>
    /// Updates issue metadata.
    /// </summary>
    public async Task<Issue> UpdateAsync(Issue issue)
    {
        return await cosmo.UpdateIssueAsync(issue);
    }
}
