using System.Text.Json.Serialization;
using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// Audit record of a git event that may trigger workflows.
/// </summary>
public class GitEvent
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(GitEvent);
    public string AccountId { get; set; } = "";
    public string RepositoryId { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GitTriggerType EventType { get; set; }

    public string ActorId { get; set; } = "";
    public string ActorName { get; set; } = "";

    /// <summary>Flattened event data (e.g. pr.title, push.branch, issue.number).</summary>
    public Dictionary<string, string> Payload { get; set; } = new();

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
