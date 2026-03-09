using DaisiGit.Core.Models;
using Microsoft.Azure.Cosmos;

namespace DaisiGit.Data;

public partial class DaisiGitCosmo
{
    public const string LabelIdPrefix = "lbl";
    public const string LabelsContainerName = "Labels";
    public const string LabelsPartitionKeyName = nameof(Label.RepositoryId);

    public PartitionKey GetLabelPartitionKey(string repositoryId) => new(repositoryId);

    public virtual async Task<Label> CreateLabelAsync(Label label)
    {
        if (string.IsNullOrEmpty(label.id))
            label.id = GenerateId(LabelIdPrefix);
        label.CreatedUtc = DateTime.UtcNow;

        var container = await GetContainerAsync(LabelsContainerName);
        var response = await container.CreateItemAsync(label, GetLabelPartitionKey(label.RepositoryId));
        return response.Resource;
    }

    public virtual async Task<List<Label>> GetLabelsAsync(string repositoryId)
    {
        var container = await GetContainerAsync(LabelsContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.RepositoryId = @repoId AND c.Type = 'Label' ORDER BY c.Name ASC")
            .WithParameter("@repoId", repositoryId);

        var results = new List<Label>();
        using var iterator = container.GetItemQueryIterator<Label>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = GetLabelPartitionKey(repositoryId)
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public virtual async Task DeleteLabelAsync(string id, string repositoryId)
    {
        var container = await GetContainerAsync(LabelsContainerName);
        await container.DeleteItemAsync<Label>(id, GetLabelPartitionKey(repositoryId));
    }
}
