namespace DaisiGit.Core.Models;

/// <summary>
/// A named, ordered collection of repositories shown as a section on an org's profile page.
/// Purely organizational — does not affect permissions.
/// </summary>
public class RepoGroup
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(RepoGroup);

    /// <summary>Cosmos partition key. Mirrors the org id.</summary>
    public string OrganizationId { get; set; } = "";

    public string AccountId { get; set; } = "";

    /// <summary>Human-readable group name shown as the section header.</summary>
    public string Name { get; set; } = "";

    /// <summary>URL-safe identifier; unique within the org.</summary>
    public string Slug { get; set; } = "";

    /// <summary>Optional description rendered under the group header.</summary>
    public string? Description { get; set; }

    /// <summary>Display order on the profile page (lower = first). Ties broken by Name.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
