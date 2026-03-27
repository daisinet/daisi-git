using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string ReviewIdPrefix = "rev";
    public const string DiffCommentIdPrefix = "dc";
    public const string ReviewsContainerName = "Reviews";
    public const string ReviewsPartitionKeyName = nameof(Review.RepositoryId);

    public PartitionKey GetReviewPartitionKey(string repositoryId) => new(repositoryId);

    public virtual async Task<Review> CreateReviewAsync(Review review)
    {
        if (string.IsNullOrEmpty(review.id))
            review.id = GenerateId(ReviewIdPrefix);
        review.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(ReviewsContainerName);
        var response = await container.CreateItemAsync(review, GetReviewPartitionKey(review.RepositoryId));
        return response.Resource;
    }

    public virtual async Task<Review?> GetReviewAsync(string id, string repositoryId)
    {
        try
        {
            var container = await GetContainerAsync(ReviewsContainerName);
            var response = await container.ReadItemAsync<Review>(id, GetReviewPartitionKey(repositoryId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public virtual async Task<List<Review>> ListReviewsForPrAsync(string repositoryId, int prNumber)
    {
        var container = await GetContainerAsync(ReviewsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.PullRequestNumber = @prNumber AND c.Type = 'Review' ORDER BY c.CreatedUtc DESC")
            .WithParameter("@repoId", repositoryId)
            .WithParameter("@prNumber", prNumber);

        var results = new List<Review>();
        using var iterator = container.GetItemQueryIterator<Review>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetReviewPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<Review> UpdateReviewAsync(Review review)
    {
        review.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(ReviewsContainerName);
        var response = await container.UpsertItemAsync(review, GetReviewPartitionKey(review.RepositoryId));
        return response.Resource;
    }

    public virtual async Task<DiffComment> CreateDiffCommentAsync(DiffComment comment)
    {
        if (string.IsNullOrEmpty(comment.id))
            comment.id = GenerateId(DiffCommentIdPrefix);
        comment.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(ReviewsContainerName);
        var response = await container.CreateItemAsync(comment, GetReviewPartitionKey(comment.RepositoryId));
        return response.Resource;
    }

    public virtual async Task<List<DiffComment>> GetDiffCommentsForReviewAsync(string repositoryId, string reviewId)
    {
        var container = await GetContainerAsync(ReviewsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.ReviewId = @reviewId AND c.Type = 'DiffComment' ORDER BY c.Path, c.Line")
            .WithParameter("@repoId", repositoryId)
            .WithParameter("@reviewId", reviewId);

        var results = new List<DiffComment>();
        using var iterator = container.GetItemQueryIterator<DiffComment>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetReviewPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<List<DiffComment>> GetDiffCommentsForPrAsync(string repositoryId, int prNumber)
    {
        var container = await GetContainerAsync(ReviewsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.PullRequestNumber = @prNumber AND c.Type = 'DiffComment' ORDER BY c.Path, c.Line")
            .WithParameter("@repoId", repositoryId)
            .WithParameter("@prNumber", prNumber);

        var results = new List<DiffComment>();
        using var iterator = container.GetItemQueryIterator<DiffComment>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetReviewPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<List<Review>> GetAllReviewsAsync(string repositoryId)
    {
        var container = await GetContainerAsync(ReviewsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND (c.Type = 'Review' OR c.Type = 'DiffComment')")
            .WithParameter("@repoId", repositoryId);

        var results = new List<Review>();
        using var iterator = container.GetItemQueryIterator<Review>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetReviewPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task DeleteReviewAsync(string id, string repositoryId)
    {
        try
        {
            var container = await GetContainerAsync(ReviewsContainerName);
            await container.DeleteItemAsync<Review>(id, GetReviewPartitionKey(repositoryId));
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
    }
}
