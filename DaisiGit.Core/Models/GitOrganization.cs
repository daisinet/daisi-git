namespace DaisiGit.Core.Models;

/// <summary>
/// Organization that owns repositories and contains teams/members.
/// Stored in Cosmos DB, partitioned by AccountId.
/// </summary>
public class GitOrganization
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(GitOrganization);
    public string AccountId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public string CreatedByUserName { get; set; } = "";
    public int MemberCount { get; set; }
    public int TeamCount { get; set; }

    /// <summary>
    /// Non-secret config exposed to workflows as {{vars.NAME}}. Repo-level vars
    /// override org-level vars with the same key.
    /// </summary>
    public Dictionary<string, string> Vars { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
