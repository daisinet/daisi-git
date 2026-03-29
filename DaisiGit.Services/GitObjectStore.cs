using DaisiGit.Core.Enums;
using DaisiGit.Core.Git;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Stores and retrieves git objects via a storage backend + Cosmos (SHA→FileId mapping).
/// Writes resolve the storage backend per-repository; reads use the provider stored on each object record.
/// </summary>
public class GitObjectStore(DaisiGitCosmo cosmo, StorageAdapterFactory storageFactory)
{
    /// <summary>
    /// Stores a git object: zlib-compress, upload to storage, record SHA→FileId in Cosmos.
    /// Returns the SHA.
    /// </summary>
    public async Task<string> StoreObjectAsync(GitRepository repo, GitObject obj)
    {
        var sha = ObjectHasher.HashObject(obj);
        obj.Sha = sha;

        var existing = await cosmo.GetObjectRecordAsync(sha, repo.id);
        if (existing != null)
            return sha;

        var raw = obj.Serialize();
        var compressed = ObjectHasher.ZlibCompress(raw);

        var drive = await storageFactory.GetAdapterAsync(repo);
        var path = $"/objects/{sha[..2]}/{sha[2..]}";
        var fileId = await drive.UploadAsync(repo.DriveRepositoryId, path, compressed);

        await cosmo.UpsertObjectRecordAsync(new GitObjectRecord
        {
            id = sha,
            Sha = sha,
            RepositoryId = repo.id,
            DriveFileId = fileId,
            ObjectType = obj.TypeString,
            SizeBytes = compressed.Length,
            StorageProvider = drive.ProviderType
        });

        return sha;
    }

    /// <summary>
    /// Stores a raw git object (already serialized with header, not yet compressed).
    /// </summary>
    public async Task<string> StoreRawObjectAsync(GitRepository repo, byte[] rawObject, string objectType)
    {
        var sha = ObjectHasher.HashRaw(rawObject);

        // Always write — a re-push of the same SHA ensures corrupted objects get fixed.
        var compressed = ObjectHasher.ZlibCompress(rawObject);
        var drive = await storageFactory.GetAdapterAsync(repo);
        var path = $"/objects/{sha[..2]}/{sha[2..]}";
        var fileId = await drive.UploadAsync(repo.DriveRepositoryId, path, compressed);

        await cosmo.UpsertObjectRecordAsync(new GitObjectRecord
        {
            id = sha,
            Sha = sha,
            RepositoryId = repo.id,
            DriveFileId = fileId,
            ObjectType = objectType,
            SizeBytes = compressed.Length,
            StorageProvider = drive.ProviderType
        });

        return sha;
    }

    /// <summary>
    /// Retrieves a git object by SHA. Returns null if not found.
    /// Uses the StorageProvider recorded on the object to select the correct adapter.
    /// </summary>
    public async Task<GitObject?> GetObjectAsync(string repositoryId, string sha)
    {
        var record = await cosmo.GetObjectRecordAsync(sha, repositoryId);
        if (record == null)
            return null;

        var drive = storageFactory.GetAdapter(record.StorageProvider);
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

        var drive = storageFactory.GetAdapter(record.StorageProvider);
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
