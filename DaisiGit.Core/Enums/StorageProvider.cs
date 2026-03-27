namespace DaisiGit.Core.Enums;

/// <summary>
/// The blob storage backend used for git object storage.
/// </summary>
public enum StorageProvider
{
    /// <summary>Daisi Drive (default, gRPC-based).</summary>
    DaisiDrive = 0,

    /// <summary>Azure Blob Storage containers.</summary>
    AzureBlob = 1
}
