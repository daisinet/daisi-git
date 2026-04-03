using Azure.Storage.Blobs;
using DaisiGit.Core.Enums;

namespace DaisiGit.Services;

/// <summary>
/// Implements git object storage using Azure Blob Storage.
/// Each git repository maps to a blob container; objects are stored as blobs.
/// </summary>
public class AzureBlobStorageAdapter(BlobServiceClient blobServiceClient) : IStorageAdapter
{
    public StorageProvider ProviderType => StorageProvider.AzureBlob;

    /// <summary>
    /// Creates a blob container for a repository. Returns the container name (used as the repository ID).
    /// </summary>
    public async Task<string> CreateRepositoryAsync(string name)
    {
        var containerName = SanitizeContainerName(name);
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync();
        return containerName;
    }

    /// <summary>
    /// Uploads a blob. Returns a composite ID "container/blobPath" used for download.
    /// </summary>
    public async Task<string> UploadAsync(string repositoryId, string path, byte[] data)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(repositoryId);
        var blobPath = path.TrimStart('/');
        var blobClient = containerClient.GetBlobClient(blobPath);
        using var stream = new MemoryStream(data);
        await blobClient.UploadAsync(stream, overwrite: true);
        return $"{repositoryId}/{blobPath}";
    }

    /// <summary>
    /// Downloads a blob by its composite ID "container/blobPath".
    /// </summary>
    public async Task<byte[]> DownloadAsync(string fileId)
    {
        var separatorIndex = fileId.IndexOf('/');
        var containerName = fileId[..separatorIndex];
        var blobPath = fileId[(separatorIndex + 1)..];

        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobPath);

        using var stream = new MemoryStream();
        await blobClient.DownloadToAsync(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Deletes the blob container for a repository.
    /// </summary>
    public async Task DeleteRepositoryAsync(string repositoryId)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(repositoryId);
        await containerClient.DeleteIfExistsAsync();
    }

    /// <summary>
    /// Azure container names must be 3-63 chars, lowercase alphanumeric + hyphens,
    /// no leading/trailing hyphens, no consecutive hyphens.
    /// </summary>
    private static string SanitizeContainerName(string name)
    {
        var sanitized = name.ToLowerInvariant();
        var chars = new List<char>();
        foreach (var c in sanitized)
        {
            if (char.IsLetterOrDigit(c))
                chars.Add(c);
            else if (c == '-' && chars.Count > 0 && chars[^1] != '-')
                chars.Add('-');
        }

        var result = new string(chars.ToArray()).TrimEnd('-');
        if (result.Length < 3)
            result = result.PadRight(3, '0');
        if (result.Length > 63)
            result = result[..63].TrimEnd('-');
        return result;
    }
}
