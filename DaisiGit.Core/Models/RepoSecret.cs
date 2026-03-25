namespace DaisiGit.Core.Models;

/// <summary>
/// An encrypted secret for use in workflows.
/// Can be scoped to a repository (RepositoryId set) or an organization (OrganizationId set).
/// Repo-level secrets override org-level secrets with the same name.
/// </summary>
public class RepoSecret
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(RepoSecret);
    public string RepositoryId { get; set; } = "";
    public string? OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string EncryptedValue { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
