namespace DaisiGit.Core;

/// <summary>
/// Names reserved by the system that cannot be used as user handles, org slugs, or repo slugs.
/// Populated dynamically from application routes at startup, plus a small set of always-reserved names.
/// </summary>
public static class ReservedNames
{
    private static readonly HashSet<string> AlwaysReserved = new(StringComparer.OrdinalIgnoreCase)
    {
        // System / security
        "admin", "administrator", "system", "root", "superuser", "sudo",
        "login", "logout", "signup", "register", "signin", "signout",
        "auth", "oauth", "sso", "account", "password", "reset",

        // Platform
        "api", "git", "app", "www", "mail", "ftp", "cdn", "static",
        "webhook", "webhooks", "callback",

        // Reserved words
        "null", "undefined", "true", "false", "none", "nil",

        // Content / legal
        "help", "about", "pricing", "terms", "privacy", "status",
        "blog", "docs", "documentation", "support", "contact", "legal",
        "security", "abuse", "dmca", "copyright",

        // Common impersonation targets
        "daisi", "daisinet", "daisigit", "github", "gitlab", "bitbucket"
    };

    private static HashSet<string> _routeNames = new(AlwaysReserved, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers top-level route segments extracted from application endpoints.
    /// Call this at startup after mapping routes.
    /// </summary>
    public static void RegisterRouteSegments(IEnumerable<string> segments)
    {
        foreach (var segment in segments)
        {
            if (!string.IsNullOrEmpty(segment) && !segment.StartsWith('{'))
                _routeNames.Add(segment);
        }
    }

    // Common profanity/obscene words that shouldn't be used as handles
    private static readonly HashSet<string> Profanity = new(StringComparer.OrdinalIgnoreCase)
    {
        "fuck", "shit", "ass", "bitch", "dick", "cock", "pussy", "cunt",
        "damn", "bastard", "whore", "slut", "nigger", "nigga", "faggot", "fag",
        "retard", "retarded", "penis", "vagina", "anus", "dildo", "porn",
        "porno", "pornography", "sex", "sexy", "nude", "naked", "xxx",
        "asshole", "bullshit", "motherfucker", "fucker", "fucking",
        "shithead", "dickhead", "wanker", "twat", "piss",
        "rape", "rapist", "pedophile", "molest",
        "nazi", "hitler", "holocaust", "genocide", "terrorist", "terrorism",
        "killall", "murder", "suicide"
    };

    /// <summary>
    /// Checks if a name is reserved by the system.
    /// </summary>
    public static bool IsReserved(string name)
    {
        return _routeNames.Contains(name);
    }

    /// <summary>
    /// Checks if a name contains profanity or obscene content.
    /// Checks the exact name and whether any profane word appears as a substring.
    /// </summary>
    public static bool IsProfane(string name)
    {
        var lower = name.ToLowerInvariant();
        if (Profanity.Contains(lower))
            return true;

        // Also check if any profane word is contained within the name
        foreach (var word in Profanity)
        {
            if (lower.Contains(word))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a name is disallowed (reserved or profane).
    /// </summary>
    public static bool IsDisallowed(string name)
    {
        return IsReserved(name) || IsProfane(name);
    }
}
