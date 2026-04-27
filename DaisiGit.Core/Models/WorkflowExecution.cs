using System.Text.Json.Serialization;
using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// Tracks the execution of a workflow instance.
/// Created when a trigger fires, updated as steps execute.
/// </summary>
public class WorkflowExecution
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(WorkflowExecution);
    public string AccountId { get; set; } = "";
    public string RepositoryId { get; set; } = "";

    /// <summary>ID of the visual workflow, or null for file-based.</summary>
    public string? WorkflowId { get; set; }
    public string WorkflowName { get; set; } = "";

    /// <summary>"Visual" or "File".</summary>
    public string Source { get; set; } = "Visual";

    /// <summary>For file-based workflows, the path within the repo.</summary>
    public string? WorkflowFilePath { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GitTriggerType TriggerType { get; set; }

    public string? TriggerEventId { get; set; }

    /// <summary>Flattened context available to merge fields and conditions.</summary>
    public Dictionary<string, string> Context { get; set; } = new();

    public int CurrentStepIndex { get; set; }
    public int TotalSteps { get; set; }

    /// <summary>"Running", "Dispatched", "Completed", "Failed", "Cancelled".</summary>
    public string Status { get; set; } = "Running";

    public string? Error { get; set; }

    /// <summary>Concurrency group this execution participates in (copied from the workflow).</summary>
    public string? ConcurrencyGroup { get; set; }

    /// <summary>When to next process this execution (for Wait steps).</summary>
    public DateTime? NextRunAt { get; set; }

    public List<WorkflowStepResult> StepResults { get; set; } = [];

    /// <summary>When the execution record was created (queue start).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }

    /// <summary>When the worker first began running steps (after queue dispatch).</summary>
    public DateTime? StartedUtc { get; set; }

    /// <summary>When the execution reached a terminal status (Completed/Failed/Cancelled).</summary>
    public DateTime? FinishedUtc { get; set; }

    /// <summary>Temp directory on disk for checkout/build steps. Cleaned up after execution.</summary>
    [JsonIgnore]
    public string? WorkspacePath { get; set; }
}
