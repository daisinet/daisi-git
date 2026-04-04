namespace DaisiGit.Core.Enums;

/// <summary>
/// Container runtime environment for workflow execution.
/// Maps to a pre-built Docker image variant.
/// </summary>
public enum WorkflowRuntime
{
    /// <summary>Base tools only (git, curl, wget, jq, unzip). Smallest image, fastest cold start.</summary>
    Minimal,

    /// <summary>.NET 10 SDK for building and publishing .NET projects.</summary>
    DotNet,

    /// <summary>Node.js 22 LTS with npm for JavaScript/TypeScript projects.</summary>
    Node,

    /// <summary>Python 3 with pip for Python projects.</summary>
    Python,

    /// <summary>All runtimes pre-installed (.NET, Node, Python). Largest image, slowest cold start.</summary>
    Full
}
