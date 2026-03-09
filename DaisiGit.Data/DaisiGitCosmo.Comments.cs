using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string CommentIdPrefix = "cmt";
    public const string CommentsContainerName = "Comments";
    public const string CommentsPartitionKeyName = nameof(Comment.RepositoryId);

    public PartitionKey GetCommentPartitionKey(string repositoryId) => new(repositoryId);

    public virtual async Task<Comment> CreateCommentAsync(Comment comment)
    {
        if (string.IsNullOrEmpty(comment.id))
            comment.id = GenerateId(CommentIdPrefix);
        comment.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(CommentsContainerName);
        var response = await container.CreateItemAsync(comment, GetCommentPartitionKey(comment.RepositoryId));
        return response.Resource;
    }

    public virtual async Task<Comment?> GetCommentAsync(string id, string repositoryId)
    {
        try
        {
            var container = await GetContainerAsync(CommentsContainerName);
            var response = await container.ReadItemAsync<Comment>(id, GetCommentPartitionKey(repositoryId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public virtual async Task<List<Comment>> GetCommentsForParentAsync(string repositoryId, string parentId)
    {
        var container = await GetContainerAsync(CommentsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.ParentId = @parentId AND c.Type = 'Comment' ORDER BY c.CreatedUtc ASC")
            .WithParameter("@repoId", repositoryId)
            .WithParameter("@parentId", parentId);

        var results = new List<Comment>();
        using var iterator = container.GetItemQueryIterator<Comment>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetCommentPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task<Comment> UpdateCommentAsync(Comment comment)
    {
        comment.UpdatedUtc = DateTime.UtcNow;
        var container = await GetContainerAsync(CommentsContainerName);
        var response = await container.UpsertItemAsync(comment, GetCommentPartitionKey(comment.RepositoryId));
        return response.Resource;
    }

    public virtual async Task DeleteCommentAsync(string id, string repositoryId)
    {
        var container = await GetContainerAsync(CommentsContainerName);
        await container.DeleteItemAsync<Comment>(id, GetCommentPartitionKey(repositoryId));
    }
}
