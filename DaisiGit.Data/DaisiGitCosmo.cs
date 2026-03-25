using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo(IConfiguration configuration, string connectionStringConfigurationName = "Cosmo:ConnectionString")
{
    private readonly Lazy<CosmosClient> _client = new(() =>
    {
        var connectionString = configuration[connectionStringConfigurationName];
        var options = new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null
            }
        };
        return new CosmosClient(connectionString, options);
    });

    private readonly ConcurrentDictionary<string, Container> _containerCache = new();

    public static string GenerateId(string prefix)
    {
        return $"{prefix}-{DateTime.UtcNow:yyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    private const string DatabaseName = "daisi-git";

    private Database? _database;

    public CosmosClient GetCosmoClient() => _client.Value;

    public async Task<Database> GetDatabaseAsync()
    {
        if (_database != null)
            return _database;

        var response = await GetCosmoClient().CreateDatabaseIfNotExistsAsync(DatabaseName);
        _database = response.Database;
        return _database;
    }

    public async Task<Container> GetContainerAsync(string containerName)
    {
        if (_containerCache.TryGetValue(containerName, out var cached))
            return cached;

        string partitionKeyPath = "/" + containerName switch
        {
            RepositoriesContainerName => RepositoriesPartitionKeyName,
            GitObjectsContainerName => GitObjectsPartitionKeyName,
            RefsContainerName => RefsPartitionKeyName,
            PullRequestsContainerName => PullRequestsPartitionKeyName,
            IssuesContainerName => IssuesPartitionKeyName,
            CommentsContainerName => CommentsPartitionKeyName,
            LabelsContainerName => LabelsPartitionKeyName,
            OrganizationsContainerName => OrganizationsPartitionKeyName,
            TeamsContainerName => TeamsPartitionKeyName,
            MembersContainerName => MembersPartitionKeyName,
            PermissionsContainerName => PermissionsPartitionKeyName,
            ReviewsContainerName => ReviewsPartitionKeyName,
            StarsContainerName => StarsPartitionKeyName,
            AccountSettingsContainerName => AccountSettingsPartitionKeyName,
            WorkflowsContainerName => WorkflowsPartitionKeyName,
            WorkflowExecutionsContainerName => WorkflowExecutionsPartitionKeyName,
            EventsContainerName => EventsPartitionKeyName,
            UserProfilesContainerName => UserProfilesPartitionKeyName,
            ApiKeysContainerName => ApiKeysPartitionKeyName,
            _ => "id"
        };

        var db = await GetDatabaseAsync();
        var container = await db.CreateContainerIfNotExistsAsync(containerName, partitionKeyPath);
        _containerCache.TryAdd(containerName, container);
        return container;
    }
}
