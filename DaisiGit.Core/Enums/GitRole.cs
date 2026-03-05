namespace DaisiGit.Core.Enums;

/// <summary>
/// Role within an organization or team.
/// </summary>
public enum GitRole
{
    /// <summary>Read-only access to public and internal repos.</summary>
    Read = 0,

    /// <summary>Read + write (push) to assigned repos.</summary>
    Write = 1,

    /// <summary>Read + write + manage repo settings, PRs, issues.</summary>
    Maintain = 2,

    /// <summary>Full control including org/team management.</summary>
    Admin = 3,

    /// <summary>Organization owner — can delete org, manage billing, transfer ownership.</summary>
    Owner = 4
}
