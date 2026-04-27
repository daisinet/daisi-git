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

    /// <summary>Container runtime for workflow execution (Minimal, DotNet, Node, Python, Full).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowRuntime Runtime { get; set; } = WorkflowRuntime.Minimal;

    /// <summary>Environment variables available to all steps as {{env.KEY}}.</summary>
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>
    /// Declared inputs collected at Run Now time. Available to steps as {{inputs.NAME}}.
    /// </summary>
    public List<WorkflowInput> Inputs { get; set; } = [];

    public List<WorkflowStep> Steps { get; set; } = [];

    /// <summary>
    /// Multi-job definition. When non-empty this takes precedence over <see cref="Steps"/>;
    /// when null/empty the engine falls back to running <see cref="Steps"/> as a single
    /// implicit job named "default". Allows incremental migration of legacy workflows.
    /// </summary>
    public List<WorkflowJob>? Jobs { get; set; }

    public bool IsEnabled { get; set; } = true;
    public string Status { get; set; } = "Active";

    /// <summary>
    /// For Scheduled triggers: when to next fire the workflow.
    /// Set when the workflow is created/updated/fires. Null for non-scheduled triggers.
    /// </summary>
    public DateTime? NextScheduledRunUtc { get; set; }

    /// <summary>
    /// Concurrency group key. When set, the engine ensures at most one execution per
    /// (group, trigger-context) is running at a time; if <see cref="ConcurrencyCancelInProgress"/>
    /// is true, a new dispatch cancels any earlier execution in the same group.
    /// </summary>
    public string? ConcurrencyGroup { get; set; }

    /// <summary>If true, dispatching cancels any earlier execution in the same group.</summary>
    public bool ConcurrencyCancelInProgress { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}
