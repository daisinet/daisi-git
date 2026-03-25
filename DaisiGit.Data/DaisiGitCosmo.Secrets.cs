using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string SecretsContainerName = "Secrets";
    public const string SecretsPartitionKeyName = nameof(RepoSecret.RepositoryId);

    private static PartitionKey GetSecretPartitionKey(string repositoryId) => new(repositoryId);

    public async Task<RepoSecret> UpsertSecretAsync(RepoSecret secret)
    {
        if (string.IsNullOrEmpty(secret.id))
            secret.id = $"{secret.RepositoryId}:{secret.Name}";
        secret.UpdatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(SecretsContainerName);
        var response = await container.UpsertItemAsync(secret, GetSecretPartitionKey(secret.RepositoryId));
        return response.Resource;
    }

    public async Task<List<RepoSecret>> GetSecretsAsync(string repositoryId)
    {
        var container = await GetContainerAsync(SecretsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.Type = 'RepoSecret' ORDER BY c.Name")
            .WithParameter("@repoId", repositoryId);

        var results = new List<RepoSecret>();
        using var iterator = container.GetItemQueryIterator<RepoSecret>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetSecretPartitionKey(repositoryId) });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public async Task<RepoSecret?> GetSecretAsync(string repositoryId, string name)
    {
        var container = await GetContainerAsync(SecretsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.Name = @name AND c.Type = 'RepoSecret'")
            .WithParameter("@repoId", repositoryId)
            .WithParameter("@name", name);

        using var iterator = container.GetItemQueryIterator<RepoSecret>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetSecretPartitionKey(repositoryId) });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public async Task DeleteSecretAsync(string repositoryId, string name)
    {
        var secret = await GetSecretAsync(repositoryId, name);
        if (secret != null)
        {
            var container = await GetContainerAsync(SecretsContainerName);
            try { await container.DeleteItemAsync<RepoSecret>(secret.id, GetSecretPartitionKey(repositoryId)); }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        }
    }
}
