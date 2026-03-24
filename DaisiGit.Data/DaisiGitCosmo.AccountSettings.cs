using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string AccountSettingsContainerName = "AccountSettings";
    public const string AccountSettingsPartitionKeyName = nameof(AccountSettings.AccountId);

    private static PartitionKey GetAccountSettingsPartitionKey(string accountId) => new(accountId);

    public async Task<AccountSettings?> GetAccountSettingsAsync(string accountId)
    {
        var container = await GetContainerAsync(AccountSettingsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.AccountId = @accountId AND c.Type = 'AccountSettings'")
            .WithParameter("@accountId", accountId);

        using var iterator = container.GetItemQueryIterator<AccountSettings>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetAccountSettingsPartitionKey(accountId) });

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public async Task<AccountSettings> UpsertAccountSettingsAsync(AccountSettings settings)
    {
        var container = await GetContainerAsync(AccountSettingsContainerName);
        settings.UpdatedUtc = DateTime.UtcNow;
        var response = await container.UpsertItemAsync(settings,
            GetAccountSettingsPartitionKey(settings.AccountId));
        return response.Resource;
    }
}
