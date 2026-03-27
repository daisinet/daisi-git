using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string UserProfilesContainerName = "UserProfiles";
    public const string UserProfilesPartitionKeyName = nameof(UserProfile.AccountId);

    private static PartitionKey GetUserProfilePartitionKey(string accountId) => new(accountId);

    public async Task<UserProfile?> GetUserProfileAsync(string userId, string accountId)
    {
        var container = await GetContainerAsync(UserProfilesContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.UserId = @userId AND c.Type = 'UserProfile'")
            .WithParameter("@userId", userId);

        using var iterator = container.GetItemQueryIterator<UserProfile>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetUserProfilePartitionKey(accountId) });

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public async Task<UserProfile?> GetUserProfileByHandleAsync(string handle)
    {
        var container = await GetContainerAsync(UserProfilesContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.Handle = @handle AND c.Type = 'UserProfile'")
            .WithParameter("@handle", handle);

        // Cross-partition — handles are globally unique
        using var iterator = container.GetItemQueryIterator<UserProfile>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    public async Task<UserProfile> UpsertUserProfileAsync(UserProfile profile)
    {
        if (string.IsNullOrEmpty(profile.id))
            profile.id = $"profile-{profile.UserId}";
        profile.UpdatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(UserProfilesContainerName);
        var response = await container.UpsertItemAsync(profile,
            GetUserProfilePartitionKey(profile.AccountId));
        return response.Resource;
    }
}
