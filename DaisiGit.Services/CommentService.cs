using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages comments on pull requests and issues.
/// </summary>
public class CommentService(DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Adds a comment to a PR or issue.
    /// </summary>
    public async Task<Comment> CreateAsync(
        string repositoryId, string parentId, string parentType,
        string body, string authorId, string authorName)
    {
        var comment = new Comment
        {
            RepositoryId = repositoryId,
            ParentId = parentId,
            ParentType = parentType,
            Body = body,
            AuthorId = authorId,
            AuthorName = authorName
        };

        var created = await cosmo.CreateCommentAsync(comment);

        // Increment comment count on the parent
        if (parentType == nameof(PullRequest))
        {
            var pr = await cosmo.GetPullRequestAsync(parentId, repositoryId);
            if (pr != null)
            {
                pr.CommentCount++;
                await cosmo.UpdatePullRequestAsync(pr);
            }
        }
        else if (parentType == nameof(Issue))
        {
            var issue = await cosmo.GetIssueAsync(parentId, repositoryId);
            if (issue != null)
            {
                issue.CommentCount++;
                await cosmo.UpdateIssueAsync(issue);
            }
        }

        return created;
    }

    /// <summary>
    /// Gets all comments for a PR or issue.
    /// </summary>
    public async Task<List<Comment>> GetCommentsAsync(string repositoryId, string parentId)
    {
        return await cosmo.GetCommentsForParentAsync(repositoryId, parentId);
    }

    /// <summary>
    /// Updates a comment's body.
    /// </summary>
    public async Task<Comment> UpdateAsync(Comment comment)
    {
        return await cosmo.UpdateCommentAsync(comment);
    }

    /// <summary>
    /// Deletes a comment.
    /// </summary>
    public async Task DeleteAsync(string id, string repositoryId, string parentId, string parentType)
    {
        await cosmo.DeleteCommentAsync(id, repositoryId);

        // Decrement comment count on the parent
        if (parentType == nameof(PullRequest))
        {
            var pr = await cosmo.GetPullRequestAsync(parentId, repositoryId);
            if (pr != null)
            {
                pr.CommentCount = Math.Max(0, pr.CommentCount - 1);
                await cosmo.UpdatePullRequestAsync(pr);
            }
        }
        else if (parentType == nameof(Issue))
        {
            var issue = await cosmo.GetIssueAsync(parentId, repositoryId);
            if (issue != null)
            {
                issue.CommentCount = Math.Max(0, issue.CommentCount - 1);
                await cosmo.UpdateIssueAsync(issue);
            }
        }
    }
}
