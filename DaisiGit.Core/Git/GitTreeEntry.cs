namespace DaisiGit.Core.Git;

/// <summary>
/// A single entry in a git tree object.
/// </summary>
public class GitTreeEntry
{
    /// <summary>File mode (e.g., "100644" for regular file, "040000" for directory, "100755" for executable).</summary>
    public string Mode { get; set; } = "100644";

    /// <summary>Entry name (file or directory name).</summary>
    public string Name { get; set; } = "";

    /// <summary>SHA-1 hash of the referenced object.</summary>
    public string Sha { get; set; } = "";

    public bool IsTree => Mode == "040000" || Mode == "40000";
    public bool IsBlob => !IsTree;
}
