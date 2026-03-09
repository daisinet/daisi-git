using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string GitObjectsContainerName = "GitObjects";
    public const string GitObjectsPartitionKeyName = nameof(GitObjectRecord.RepositoryId);

    public PartitionKey GetObjectPartitionKey(string repositoryId) => new(repositoryId);

    /// <summary>
    /// Upserts a SHA → Drive file ID mapping.
    /// </summary>
    public virtual async Task<GitObjectRecord> UpsertObjectRecordAsync(GitObjectRecord record)
    {
        if (string.IsNullOrEmpty(record.id))
            record.id = record.RepositoryId + ":" + record.DriveFileId;

        var container = await GetContainerAsync(GitObjectsContainerName);
        var response = await container.UpsertItemAsync(record, GetObjectPartitionKey(record.RepositoryId));
        return response.Resource;
    }

    /// <summary>
    /// Gets the Drive file ID for a git object by SHA within a repository.
    /// </summary>
    public virtual async Task<GitObjectRecord?> GetObjectRecordAsync(string sha, string repositoryId)
    {
        var container = await GetContainerAsync(GitObjectsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.id = @id AND c.Type = 'GitObjectRecord'")
            .WithParameter("@repoId", repositoryId)
            .WithParameter("@id", sha);

        using var iterator = container.GetItemQueryIterator<GitObjectRecord>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetObjectPartitionKey(repositoryId)
        });

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }

    /// <summary>
    /// Checks which SHAs already exist in the repository (for push optimization).
    /// </summary>
    public virtual async Task<HashSet<string>> GetExistingShasAsync(string repositoryId, IEnumerable<string> shas)
    {
        var container = await GetContainerAsync(GitObjectsContainerName);
        var shaList = shas.ToList();
        if (shaList.Count == 0) return [];

        // Batch by 50 to avoid huge IN clauses
        var existing = new HashSet<string>();
        foreach (var batch in shaList.Chunk(50))
        {
            var paramNames = batch.Select((_, i) => $"@sha{i}").ToList();
            var inClause = string.Join(",", paramNames);
            var queryDef = new QueryDefinition(
                $"SELECT c.id FROM c WHERE c.RepositoryId = @repoId AND c.id IN ({inClause})")
                .WithParameter("@repoId", repositoryId);

            for (var i = 0; i < batch.Length; i++)
                queryDef = queryDef.WithParameter(paramNames[i], batch[i]);

            using var iterator = container.GetItemQueryIterator<GitObjectRecord>(queryDef, requestOptions: new QueryRequestOptions
            {
                PartitionKey = GetObjectPartitionKey(repositoryId)
            });
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                    existing.Add(item.id);
            }
        }

        return existing;
    }

    /// <summary>
    /// Gets all object records for a repository (used for full enumeration).
    /// </summary>
    public virtual async Task<List<GitObjectRecord>> GetAllObjectRecordsAsync(string repositoryId)
    {
        var container = await GetContainerAsync(GitObjectsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.Type = 'GitObjectRecord'")
            .WithParameter("@repoId", repositoryId);

        var results = new List<GitObjectRecord>();
        using var iterator = container.GetItemQueryIterator<GitObjectRecord>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetObjectPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }
}
