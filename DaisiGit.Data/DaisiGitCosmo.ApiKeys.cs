using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string ApiKeysContainerName = "ApiKeys";
    public const string ApiKeysPartitionKeyName = nameof(ApiKey.AccountId);

    private static PartitionKey GetApiKeyPartitionKey(string accountId) => new(accountId);

    public async Task<ApiKey> CreateApiKeyAsync(ApiKey key)
    {
        if (string.IsNullOrEmpty(key.id))
            key.id = GenerateId("key");
        key.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(ApiKeysContainerName);
        var response = await container.CreateItemAsync(key, GetApiKeyPartitionKey(key.AccountId));
        return response.Resource;
    }

    /// <summary>
    /// Finds an API key by token hash. Cross-partition query since the caller doesn't know the account.
    /// </summary>
    public async Task<ApiKey?> GetApiKeyByHashAsync(string tokenHash)
    {
        var container = await GetContainerAsync(ApiKeysContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.TokenHash = @hash AND c.Type = 'ApiKey' AND c.IsRevoked = false")
            .WithParameter("@hash", tokenHash);

        using var iterator = container.GetItemQueryIterator<ApiKey>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public async Task<List<ApiKey>> GetApiKeysAsync(string accountId, string userId)
    {
        var container = await GetContainerAsync(ApiKeysContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.AccountId = @accountId AND c.UserId = @userId AND c.Type = 'ApiKey' AND c.IsRevoked = false ORDER BY c.CreatedUtc DESC")
            .WithParameter("@accountId", accountId)
            .WithParameter("@userId", userId);

        var results = new List<ApiKey>();
        using var iterator = container.GetItemQueryIterator<ApiKey>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetApiKeyPartitionKey(accountId) });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public async Task<ApiKey> UpdateApiKeyAsync(ApiKey key)
    {
        var container = await GetContainerAsync(ApiKeysContainerName);
        var response = await container.UpsertItemAsync(key, GetApiKeyPartitionKey(key.AccountId));
        return response.Resource;
    }
}
