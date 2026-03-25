namespace DaisiGit.Core.Models;

/// <summary>
/// Git-specific user profile stored in Cosmos DB.
/// Contains the user's unique handle and display preferences.
/// </summary>
public class UserProfile
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(UserProfile);
    public string AccountId { get; set; } = "";
    public string UserId { get; set; } = "";

    /// <summary>Unique URL handle (e.g. "alice"). Must be unique across users and orgs.</summary>
    public string Handle { get; set; } = "";

    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
