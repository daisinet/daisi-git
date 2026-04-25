using System.Text.Json.Serialization;
using DaisiGit.Core.Enums;

namespace DaisiGit.Core.Models;

/// <summary>
/// Result of executing a single workflow step.
/// </summary>
public class WorkflowStepResult
{
    public int StepIndex { get; set; }

    public string StepName { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowStepType StepType { get; set; }

    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime ExecutedUtc { get; set; } = DateTime.UtcNow;

    // ── HttpRequest results ──
    public int? HttpStatusCode { get; set; }
    public string? HttpResponseBody { get; set; }

    // ── AddComment results ──
    public string? RenderedBody { get; set; }

    // ── Condition results ──
    public string? BranchTaken { get; set; }

    // ── DeployAzureWebApp results ──
    public string? DeployUrl { get; set; }

    // ── RunScript results ──
    public int? ExitCode { get; set; }
    public string? ScriptOutput { get; set; }
}
