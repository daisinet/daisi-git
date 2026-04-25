namespace DaisiGit.Core.Models;

/// <summary>
/// Declares an input parameter for a workflow that can be supplied at Run Now time.
/// Mirrors GitHub Actions' <c>on: workflow_dispatch: inputs:</c> shape.
/// </summary>
public class WorkflowInput
{
    /// <summary>Identifier used in templates as <c>{{inputs.&lt;name&gt;}}</c>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Human-readable label shown in the Run Now form.</summary>
    public string? Description { get; set; }

    /// <summary>"string" (default), "number", "boolean", or "choice".</summary>
    public string Type { get; set; } = "string";

    /// <summary>Default value applied when none is supplied.</summary>
    public string? Default { get; set; }

    /// <summary>If true, the workflow refuses to run without an explicit value.</summary>
    public bool Required { get; set; }

    /// <summary>Allowed values for Type=="choice".</summary>
    public List<string>? Choices { get; set; }
}
