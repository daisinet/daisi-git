using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace DaisiGit.Web.Services;

/// <summary>
/// Manages avatar uploads to Azure Blob Storage.
/// Avatars stored in a private "avatars" container as {type}/{id}.{ext}
/// Served through a proxy endpoint at /api/git/avatars/{type}/{id}
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
            try { _container.CreateIfNotExists(); } catch { }
        }
    }

    public bool IsAvailable => _container != null;

    /// <summary>
    /// Uploads an avatar image. Returns the proxy URL path (not the blob URL).
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

        await blobClient.UploadAsync(imageStream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        });

        // Return proxy URL, not direct blob URL
        return $"/api/git/avatars/{type}/{id}";
    }

    /// <summary>
    /// Downloads an avatar. Returns (stream, contentType) or null if not found.
    /// </summary>
    public async Task<(Stream Stream, string ContentType)?> DownloadAvatarAsync(string type, string id)
    {
        if (_container == null) return null;

        foreach (var ext in new[] { "jpg", "png", "gif", "webp" })
        {
            var blobClient = _container.GetBlobClient($"{type}/{id}.{ext}");
            if (await blobClient.ExistsAsync())
            {
                var download = await blobClient.DownloadStreamingAsync();
                return (download.Value.Content, download.Value.Details.ContentType);
            }
        }
        return null;
    }

    /// <summary>
    /// Deletes an avatar.
    /// </summary>
    public async Task DeleteAvatarAsync(string type, string id)
    {
        if (_container == null) return;

        foreach (var ext in new[] { "jpg", "png", "gif", "webp" })
        {
            var blobClient = _container.GetBlobClient($"{type}/{id}.{ext}");
            await blobClient.DeleteIfExistsAsync();
        }
    }
}
