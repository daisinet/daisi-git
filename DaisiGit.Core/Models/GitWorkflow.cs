using System.Text.Json.Serialization;
using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// A visual workflow definition stored in Cosmos DB.
/// Triggered by git events, executes a sequence of steps.
/// </summary>
public class GitWorkflow
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(GitWorkflow);
    public string AccountId { get; set; } = "";

    /// <summary>Scope to a specific repo, or null for account-wide.</summary>
    public string? RepositoryId { get; set; }

    public string Name { get; set; } = "";
    public string? Description { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GitTriggerType TriggerType { get; set; }

    /// <summary>
    /// Optional filters to narrow the trigger (e.g. {"branch": "main", "label": "bug"}).
    /// </summary>
    public Dictionary<string, string>? TriggerFilters { get; set; }

    /// <summary>Environment variables available to all steps as {{env.KEY}}.</summary>
    public Dictionary<string, string>? Env { get; set; }

    public List<WorkflowStep> Steps { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    public string Status { get; set; } = "Active";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
