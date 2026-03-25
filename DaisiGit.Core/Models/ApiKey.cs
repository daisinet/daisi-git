namespace DaisiGit.Core.Models;

/// <summary>
/// Personal access token for API/CLI authentication.
/// The token value is hashed — the raw token is only shown once at creation.
/// </summary>
public class ApiKey
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(ApiKey);
    public string AccountId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public string TokenPrefix { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresUtc { get; set; }
    public DateTime? LastUsedUtc { get; set; }
    public bool IsRevoked { get; set; }
}
