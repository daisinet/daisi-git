using DaisiGit.Core.Git;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Stores and retrieves git objects via Drive (storage) + Cosmos (SHA→FileId mapping).
/// </summary>
public class GitObjectStore(DaisiGitCosmo cosmo, IDriveAdapter drive)
{
    /// <summary>
    /// Stores a git object: zlib-compress, upload to Drive, record SHA→FileId in Cosmos.
    /// Returns the SHA.
    /// </summary>
    public async Task<string> StoreObjectAsync(string repositoryId, string driveRepositoryId, GitObject obj)
    {
        var sha = ObjectHasher.HashObject(obj);
        obj.Sha = sha;

        // Check if already exists
        var existing = await cosmo.GetObjectRecordAsync(sha, repositoryId);
        if (existing != null)
            return sha;

        // Serialize and compress
        var raw = obj.Serialize();
        var compressed = ObjectHasher.ZlibCompress(raw);

        // Upload to Drive
        var path = $"/objects/{sha[..2]}/{sha[2..]}";
        var driveFileId = await drive.UploadAsync(driveRepositoryId, path, compressed);

        // Record mapping
        await cosmo.UpsertObjectRecordAsync(new GitObjectRecord
        {
            id = sha,
            RepositoryId = repositoryId,
            DriveFileId = driveFileId,
            ObjectType = obj.TypeString,
            SizeBytes = compressed.Length
        });

        return sha;
    }

    /// <summary>
    /// Stores a raw git object (already serialized with header, not yet compressed).
    /// </summary>
    public async Task<string> StoreRawObjectAsync(string repositoryId, string driveRepositoryId, byte[] rawObject, string objectType)
    {
        var sha = ObjectHasher.HashRaw(rawObject);

        var existing = await cosmo.GetObjectRecordAsync(sha, repositoryId);
        if (existing != null)
            return sha;

        var compressed = ObjectHasher.ZlibCompress(rawObject);
        var path = $"/objects/{sha[..2]}/{sha[2..]}";
        var driveFileId = await drive.UploadAsync(driveRepositoryId, path, compressed);

        await cosmo.UpsertObjectRecordAsync(new GitObjectRecord
        {
            id = sha,
            RepositoryId = repositoryId,
            DriveFileId = driveFileId,
            ObjectType = objectType,
            SizeBytes = compressed.Length
        });

        return sha;
    }

    /// <summary>
    /// Retrieves a git object by SHA. Returns null if not found.
    /// </summary>
    public async Task<GitObject?> GetObjectAsync(string repositoryId, string sha)
    {
        var record = await cosmo.GetObjectRecordAsync(sha, repositoryId);
        if (record == null)
            return null;

        var compressed = await drive.DownloadAsync(record.DriveFileId);
        var raw = ObjectHasher.ZlibDecompress(compressed);
        return ObjectHasher.ParseObject(raw);
    }

    /// <summary>
    /// Retrieves the raw (decompressed, with header) bytes of a git object.
    /// </summary>
    public async Task<byte[]?> GetRawObjectAsync(string repositoryId, string sha)
    {
        var record = await cosmo.GetObjectRecordAsync(sha, repositoryId);
        if (record == null)
            return null;

        var compressed = await drive.DownloadAsync(record.DriveFileId);
        return ObjectHasher.ZlibDecompress(compressed);
    }

    /// <summary>
    /// Checks if an object exists in the repository.
    /// </summary>
    public async Task<bool> ObjectExistsAsync(string repositoryId, string sha)
    {
        var record = await cosmo.GetObjectRecordAsync(sha, repositoryId);
        return record != null;
    }

    /// <summary>
    /// Gets the set of SHAs that already exist (for push negotiation).
    /// </summary>
    public async Task<HashSet<string>> GetExistingShasAsync(string repositoryId, IEnumerable<string> shas)
    {
        return await cosmo.GetExistingShasAsync(repositoryId, shas);
    }
}
