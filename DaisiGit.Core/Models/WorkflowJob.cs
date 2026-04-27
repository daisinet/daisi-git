namespace DaisiGit.Core.Models;

/// <summary>
/// A single job within a workflow. Jobs run in dependency order (declared via
/// <see cref="Needs"/>), with each job's steps executing serially.
/// Workflows authored with the old flat-steps shape are treated as a single
/// implicit job named "default".
/// </summary>
public class WorkflowJob
{
    /// <summary>Stable key referenced by Needs and {{needs.&lt;id&gt;.outputs.&lt;name&gt;}}.</summary>
    public string Id { get; set; } = "";

    /// <summary>Human-readable label shown in history; falls back to Id when empty.</summary>
    public string Name { get; set; } = "";

    /// <summary>Ids of jobs that must finish (Completed) before this job runs.</summary>
    public List<string> Needs { get; set; } = [];

    /// <summary>
    /// Optional matrix dimensions for the job. Each cell becomes its own job execution
    /// with matrix.&lt;key&gt; in scope for every step. Mirrors GH Actions' strategy.matrix.
    /// </summary>
    public Dictionary<string, List<string>>? Matrix { get; set; }

    /// <summary>
    /// Output declarations: key is the output name, value is a template (e.g.
    /// "${{ steps.set-version.output }}") evaluated against the job's context after
    /// the steps complete. Downstream jobs reference these as
    /// {{needs.&lt;job-id&gt;.outputs.&lt;key&gt;}}.
    /// </summary>
    public Dictionary<string, string>? Outputs { get; set; }

    public List<WorkflowStep> Steps { get; set; } = [];
}
