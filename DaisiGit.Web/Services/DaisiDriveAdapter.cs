using Daisi.SDK.Clients.V1.Orc;
using DaisiGit.Core.Enums;
using DaisiGit.Services;

namespace DaisiGit.Web.Services;

/// <summary>
/// Adapts the Daisi Drive SDK client for git object storage.
/// </summary>
public class DaisiDriveAdapter(DriveClientFactory driveClientFactory) : IStorageAdapter
{
    public StorageProvider ProviderType => StorageProvider.DaisiDrive;
    private readonly DriveClient _driveClient = driveClientFactory.Create();

    public async Task<string> CreateRepositoryAsync(string name)
    {
        var response = await _driveClient.CreateRepositoryAsync(name);
        return response.Repository.Id;
    }

    public async Task<string> UploadAsync(string repositoryId, string path, byte[] data)
    {
        using var stream = new MemoryStream(data);
        var fileName = Path.GetFileName(path);
        var response = await _driveClient.UploadFileAsync(
            stream, fileName, repositoryId, null, path,
            "application/octet-stream", isSystemFile: true);
        return response.File.Id;
    }

    public async Task<byte[]> DownloadAsync(string fileId)
    {
        return await _driveClient.DownloadFileAsync(fileId);
    }

    public async Task DeleteRepositoryAsync(string repositoryId)
    {
        await _driveClient.DeleteRepositoryAsync(repositoryId);
    }
}
