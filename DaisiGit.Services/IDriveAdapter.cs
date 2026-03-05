namespace DaisiGit.Services;

/// <summary>
/// Abstracts Drive operations for git object storage.
/// Allows the service layer to work with Drive without directly depending on gRPC/SDK types.
/// </summary>
public interface IDriveAdapter
{
    /// <summary>Creates a new Drive repository. Returns the repository ID.</summary>
    Task<string> CreateRepositoryAsync(string name);

    /// <summary>Uploads data to a Drive repository at the given path. Returns the file ID.</summary>
    Task<string> UploadAsync(string repositoryId, string path, byte[] data);

    /// <summary>Downloads file data by file ID.</summary>
    Task<byte[]> DownloadAsync(string fileId);

    /// <summary>Deletes a Drive repository.</summary>
    Task DeleteRepositoryAsync(string repositoryId);
}
