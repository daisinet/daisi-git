namespace DaisiGit.Core.Models;

/// <summary>
/// An encrypted secret for use in workflows.
/// OwnerId is the partition key — either a RepositoryId or OrganizationId.
/// Scope distinguishes: "repo" or "org".
/// Repo-level secrets override org-level secrets with the same name.
/// </summary>
public class RepoSecret
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(RepoSecret);

    /// <summary>Partition key: repository ID or organization ID.</summary>
    public string OwnerId { get; set; } = "";

    /// <summary>"repo" or "org".</summary>
    public string Scope { get; set; } = "repo";

    public string Name { get; set; } = "";
    public string EncryptedValue { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
