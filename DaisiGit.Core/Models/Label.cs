namespace DaisiGit.Core.Models;

/// <summary>
/// Label for categorizing pull requests and issues, stored in Cosmos DB.
/// </summary>
public class Label
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(Label);
    public string RepositoryId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#6b7280";
    public string? Description { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
