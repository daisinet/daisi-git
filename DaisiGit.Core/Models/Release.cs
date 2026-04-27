namespace DaisiGit.Core.Models;

/// <summary>
/// A versioned set of artifacts attached to a tag in a repository. Modeled after
/// GitHub's Releases — surfaces a downloadable bundle of build outputs alongside the
/// commit/tag that produced them.
/// </summary>
public class Release
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(Release);

    /// <summary>Cosmos partition.</summary>
    public string RepositoryId { get; set; } = "";

    /// <summary>Tag name this release attaches to (e.g. "v1.2.3").</summary>
    public string Tag { get; set; } = "";

    /// <summary>Display title; falls back to Tag when empty.</summary>
    public string Name { get; set; } = "";

    /// <summary>Markdown release notes.</summary>
    public string? Body { get; set; }

    public bool Prerelease { get; set; }

    /// <summary>Files attached to this release.</summary>
    public List<ReleaseAsset> Assets { get; set; } = [];

    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}

/// <summary>A single file attached to a <see cref="Release"/>.</summary>
public class ReleaseAsset
{
    public string Name { get; set; } = "";

    /// <summary>Storage adapter file id (returned by IStorageAdapter.UploadAsync).</summary>
    public string DriveFileId { get; set; } = "";

    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";

    public DateTime UploadedUtc { get; set; } = DateTime.UtcNow;
}
