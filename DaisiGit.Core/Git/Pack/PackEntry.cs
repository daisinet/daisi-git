namespace DaisiGit.Core.Git.Pack;

/// <summary>
/// A single entry within a pack file.
/// </summary>
public class PackEntry
{
    public GitObjectType ObjectType { get; set; }
    public byte[] Data { get; set; } = [];
    public string? Sha { get; set; }

    /// <summary>Offset of this entry within the pack file (for OFS_DELTA base lookup).</summary>
    public int PackOffset { get; set; }

    // For OFS_DELTA / REF_DELTA types
    public string? BaseSha { get; set; }
    public long BaseOffset { get; set; }
    public byte[]? DeltaData { get; set; }
}
