using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string SecretsContainerName = "Secrets";
    public const string SecretsPartitionKeyName = nameof(RepoSecret.OwnerId);

    private static PartitionKey GetSecretPartitionKey(string ownerId) => new(ownerId);

    public async Task<RepoSecret> UpsertSecretAsync(RepoSecret secret)
    {
        if (string.IsNullOrEmpty(secret.id))
            secret.id = $"{secret.OwnerId}:{secret.Name}";
        secret.UpdatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(SecretsContainerName);
        var response = await container.UpsertItemAsync(secret, GetSecretPartitionKey(secret.OwnerId));
        return response.Resource;
    }

    public async Task<List<RepoSecret>> GetSecretsAsync(string ownerId)
    {
        var container = await GetContainerAsync(SecretsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OwnerId = @ownerId AND c.Type = 'RepoSecret' ORDER BY c.Name")
            .WithParameter("@ownerId", ownerId);

        var results = new List<RepoSecret>();
        using var iterator = container.GetItemQueryIterator<RepoSecret>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetSecretPartitionKey(ownerId) });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public async Task<RepoSecret?> GetSecretAsync(string ownerId, string name)
    {
        var container = await GetContainerAsync(SecretsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.OwnerId = @ownerId AND c.Name = @name AND c.Type = 'RepoSecret'")
            .WithParameter("@ownerId", ownerId)
            .WithParameter("@name", name);

        using var iterator = container.GetItemQueryIterator<RepoSecret>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetSecretPartitionKey(ownerId) });
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public async Task DeleteSecretAsync(string ownerId, string name)
    {
        var secret = await GetSecretAsync(ownerId, name);
        if (secret != null)
        {
            var container = await GetContainerAsync(SecretsContainerName);
            try { await container.DeleteItemAsync<RepoSecret>(secret.id, GetSecretPartitionKey(ownerId)); }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        }
    }
}
