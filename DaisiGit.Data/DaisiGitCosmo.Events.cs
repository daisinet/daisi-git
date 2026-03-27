using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string EventsContainerName = "Events";
    public const string EventsPartitionKeyName = nameof(GitEvent.RepositoryId);

    private static PartitionKey GetEventPartitionKey(string repositoryId) => new(repositoryId);

    public async Task<GitEvent> CreateEventAsync(GitEvent gitEvent)
    {
        if (string.IsNullOrEmpty(gitEvent.id))
            gitEvent.id = GenerateId("evt");
        gitEvent.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(EventsContainerName);
        var response = await container.CreateItemAsync(gitEvent,
            GetEventPartitionKey(gitEvent.RepositoryId));
        return response.Resource;
    }

    public async Task<List<GitEvent>> GetRecentEventsAsync(string repositoryId, int take = 50)
    {
        var container = await GetContainerAsync(EventsContainerName);
        var query = new QueryDefinition(
            "SELECT TOP @take * FROM c WHERE c.RepositoryId = @repositoryId AND c.Type = 'GitEvent' ORDER BY c.CreatedUtc DESC")
            .WithParameter("@repositoryId", repositoryId)
            .WithParameter("@take", take);

        var results = new List<GitEvent>();
        using var iterator = container.GetItemQueryIterator<GitEvent>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = GetEventPartitionKey(repositoryId) });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }
}
