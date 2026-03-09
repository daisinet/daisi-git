using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string PermissionIdPrefix = "perm";
    public const string PermissionsContainerName = "Permissions";
    public const string PermissionsPartitionKeyName = nameof(RepoPermission.RepositoryId);

    public PartitionKey GetPermissionPartitionKey(string repositoryId) => new(repositoryId);

    public virtual async Task<RepoPermission> CreatePermissionAsync(RepoPermission permission)
    {
        if (string.IsNullOrEmpty(permission.id))
            permission.id = GenerateId(PermissionIdPrefix);
        permission.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(PermissionsContainerName);
        var response = await container.CreateItemAsync(permission, GetPermissionPartitionKey(permission.RepositoryId));
        return response.Resource;
    }

    public virtual async Task<List<RepoPermission>> GetPermissionsForRepoAsync(string repositoryId)
    {
        var container = await GetContainerAsync(PermissionsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.Type = 'RepoPermission'")
            .WithParameter("@repoId", repositoryId);

        var results = new List<RepoPermission>();
        using var iterator = container.GetItemQueryIterator<RepoPermission>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetPermissionPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    /// <summary>
    /// Gets the permission for a specific grantee (team or user) on a repository.
    /// </summary>
    public virtual async Task<RepoPermission?> GetPermissionAsync(string repositoryId, string granteeId, string granteeType)
    {
        var container = await GetContainerAsync(PermissionsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.GranteeId = @granteeId AND c.GranteeType = @granteeType AND c.Type = 'RepoPermission'")
            .WithParameter("@repoId", repositoryId)
            .WithParameter("@granteeId", granteeId)
            .WithParameter("@granteeType", granteeType);

        using var iterator = container.GetItemQueryIterator<RepoPermission>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetPermissionPartitionKey(repositoryId)
        });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public virtual async Task<RepoPermission> UpsertPermissionAsync(RepoPermission permission)
    {
        var container = await GetContainerAsync(PermissionsContainerName);
        var response = await container.UpsertItemAsync(permission, GetPermissionPartitionKey(permission.RepositoryId));
        return response.Resource;
    }

    public virtual async Task DeletePermissionAsync(string id, string repositoryId)
    {
        var container = await GetContainerAsync(PermissionsContainerName);
        await container.DeleteItemAsync<RepoPermission>(id, GetPermissionPartitionKey(repositoryId));
    }
}
