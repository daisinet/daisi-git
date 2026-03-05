using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages code reviews on pull requests — submit, list, dismiss, and inline diff comments.
/// </summary>
public class ReviewService(DaisiGitCosmo cosmo)
{
    /// <summary>
    /// Submits a review with optional inline diff comments. Creates the Review document
    /// and all DiffComment documents in sequence.
    /// </summary>
    public async Task<Review> SubmitReviewAsync(
        string repositoryId, string pullRequestId, int prNumber,
        string authorId, string authorName,
        ReviewState state, string? body,
        List<DiffComment>? diffComments = null)
    {
        var review = new Review
        {
            RepositoryId = repositoryId,
            PullRequestId = pullRequestId,
            PullRequestNumber = prNumber,
            State = state,
            Body = body,
            AuthorId = authorId,
            AuthorName = authorName
        };

        review = await cosmo.CreateReviewAsync(review);

        if (diffComments != null)
        {
            foreach (var dc in diffComments)
            {
                dc.RepositoryId = repositoryId;
                dc.ReviewId = review.id;
                dc.PullRequestId = pullRequestId;
                dc.PullRequestNumber = prNumber;
                dc.AuthorId = authorId;
                dc.AuthorName = authorName;
                await cosmo.CreateDiffCommentAsync(dc);
            }
        }

        return review;
    }

    /// <summary>
    /// Lists all reviews for a pull request.
    /// </summary>
    public async Task<List<Review>> ListReviewsAsync(string repositoryId, int prNumber)
    {
        return await cosmo.ListReviewsForPrAsync(repositoryId, prNumber);
    }

    /// <summary>
    /// Gets a single review by ID, including its diff comments.
    /// </summary>
    public async Task<(Review? Review, List<DiffComment> DiffComments)> GetReviewAsync(string repositoryId, string reviewId)
    {
        var review = await cosmo.GetReviewAsync(reviewId, repositoryId);
        if (review == null)
            return (null, []);

        var comments = await cosmo.GetDiffCommentsForReviewAsync(repositoryId, reviewId);
        return (review, comments);
    }

    /// <summary>
    /// Gets all inline diff comments for a pull request (across all reviews).
    /// </summary>
    public async Task<List<DiffComment>> GetDiffCommentsAsync(string repositoryId, int prNumber)
    {
        return await cosmo.GetDiffCommentsForPrAsync(repositoryId, prNumber);
    }

    /// <summary>
    /// Dismisses a review by setting its state to Dismissed.
    /// </summary>
    public async Task<Review?> DismissReviewAsync(string repositoryId, string reviewId)
    {
        var review = await cosmo.GetReviewAsync(reviewId, repositoryId);
        if (review == null)
            return null;

        review.State = ReviewState.Dismissed;
        return await cosmo.UpdateReviewAsync(review);
    }

    /// <summary>
    /// Returns a summary of review states for a pull request.
    /// </summary>
    public async Task<ReviewSummary> GetReviewSummaryAsync(string repositoryId, int prNumber)
    {
        var reviews = await cosmo.ListReviewsForPrAsync(repositoryId, prNumber);
        return new ReviewSummary
        {
            Approvals = reviews.Count(r => r.State == ReviewState.Approved),
            ChangesRequested = reviews.Count(r => r.State == ReviewState.ChangesRequested),
            Commented = reviews.Count(r => r.State == ReviewState.Commented)
        };
    }
}

/// <summary>
/// Summary counts of review states for a pull request.
/// </summary>
public class ReviewSummary
{
    public int Approvals { get; set; }
    public int ChangesRequested { get; set; }
    public int Commented { get; set; }
}
