namespace DaisiGit.Core.Models;

/// <summary>
/// A git ref (branch, tag, or HEAD) stored in Cosmos DB.
/// </summary>
public class GitRef
{
    /// <summary>Composite ID: "{RepositoryId}:{Name}" (e.g., "repo-xxx:refs/heads/main").</summary>
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(GitRef);
    public string RepositoryId { get; set; } = "";

    /// <summary>Full ref name (e.g., "refs/heads/main", "refs/tags/v1.0", "HEAD").</summary>
    public string Name { get; set; } = "";

    /// <summary>The SHA this ref points to, or a symbolic ref like "ref: refs/heads/main".</summary>
    public string Target { get; set; } = "";

    /// <summary>Whether this is a symbolic ref (like HEAD).</summary>
    public bool IsSymbolic { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
