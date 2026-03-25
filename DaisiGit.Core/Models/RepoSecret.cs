namespace DaisiGit.Core.Models;

/// <summary>
/// An encrypted secret stored per-repository for use in workflows.
/// The value is encrypted at rest — only the name is visible.
/// </summary>
public class RepoSecret
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(RepoSecret);
    public string RepositoryId { get; set; } = "";
    public string Name { get; set; } = "";
    public string EncryptedValue { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
