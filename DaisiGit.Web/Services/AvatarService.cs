using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace DaisiGit.Web.Services;

/// <summary>
/// Manages avatar uploads to Azure Blob Storage.
/// Avatars stored in a dedicated "avatars" container as {type}/{id}.{ext}
/// </summary>
public class AvatarService
{
    private readonly BlobContainerClient? _container;
    private const string ContainerName = "avatars";

    public AvatarService(IConfiguration configuration)
    {
        var connectionString = configuration["AzureBlob:ConnectionString"];
        if (!string.IsNullOrEmpty(connectionString))
        {
            var blobServiceClient = new BlobServiceClient(connectionString);
            _container = blobServiceClient.GetBlobContainerClient(ContainerName);
            try { _container.CreateIfNotExists(PublicAccessType.Blob); }
            catch { _container.CreateIfNotExists(); }
        }
    }

    public bool IsAvailable => _container != null;

    /// <summary>
    /// Uploads an avatar image. Returns the public URL.
    /// </summary>
    public async Task<string?> UploadAvatarAsync(string type, string id, Stream imageStream, string contentType)
    {
        if (_container == null) return null;

        var ext = contentType switch
        {
            "image/png" => "png",
            "image/gif" => "gif",
            "image/webp" => "webp",
            _ => "jpg"
        };

        var blobName = $"{type}/{id}.{ext}";
        var blobClient = _container.GetBlobClient(blobName);

        await blobClient.UploadAsync(imageStream, new BlobHttpHeaders { ContentType = contentType });
        return blobClient.Uri.ToString();
    }

    /// <summary>
    /// Deletes an avatar.
    /// </summary>
    public async Task DeleteAvatarAsync(string type, string id)
    {
        if (_container == null) return;

        // Try all extensions
        foreach (var ext in new[] { "jpg", "png", "gif", "webp" })
        {
            var blobClient = _container.GetBlobClient($"{type}/{id}.{ext}");
            await blobClient.DeleteIfExistsAsync();
        }
    }
}
