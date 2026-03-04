using Daisi.SDK.Clients.V1.Orc;
using DaisiGit.Services;

namespace DaisiGit.Web.Services;

/// <summary>
/// Adapts the Daisi Drive SDK client for git object storage.
/// </summary>
public class DaisiDriveAdapter(DriveClient driveClient) : IDriveAdapter
{
    public async Task<string> CreateRepositoryAsync(string name)
    {
        var response = await driveClient.CreateRepositoryAsync(name);
        return response.Repository.Id;
    }

    public async Task<string> UploadAsync(string repositoryId, string path, byte[] data)
    {
        using var stream = new MemoryStream(data);
        var fileName = Path.GetFileName(path);
        var response = await driveClient.UploadFileAsync(
            stream, fileName, repositoryId, null, path,
            "application/octet-stream", isSystemFile: true);
        return response.File.Id;
    }

    public async Task<byte[]> DownloadAsync(string fileId)
    {
        return await driveClient.DownloadFileAsync(fileId);
    }

    public async Task DeleteRepositoryAsync(string repositoryId)
    {
        await driveClient.DeleteRepositoryAsync(repositoryId);
    }
}
