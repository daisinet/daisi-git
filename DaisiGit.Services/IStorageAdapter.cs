using DaisiGit.Core.Enums;

namespace DaisiGit.Services;

/// <summary>
/// Abstracts blob storage operations for git object storage.
/// Implementations include Daisi Drive and Azure Blob Storage.
/// </summary>
public interface IStorageAdapter
{
    /// <summary>Which storage provider this adapter implements.</summary>
    StorageProvider ProviderType { get; }

    /// <summary>Creates a new storage container/repository. Returns the container ID.</summary>
    Task<string> CreateRepositoryAsync(string name);

    /// <summary>Uploads data at the given path. Returns the file/blob ID.</summary>
    Task<string> UploadAsync(string repositoryId, string path, byte[] data);

    /// <summary>Downloads data by file/blob ID.</summary>
    Task<byte[]> DownloadAsync(string fileId);

    /// <summary>Deletes a storage container/repository.</summary>
    Task DeleteRepositoryAsync(string repositoryId);
}
