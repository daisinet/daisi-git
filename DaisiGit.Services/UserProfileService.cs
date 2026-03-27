using System.Text.RegularExpressions;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages user profiles with globally unique handles (unique across users and orgs).
/// </summary>
public partial class UserProfileService(DaisiGitCosmo cosmo)
{
    public async Task<UserProfile?> GetProfileAsync(string userId, string accountId)
    {
        return await cosmo.GetUserProfileAsync(userId, accountId);
    }

    public async Task<UserProfile?> GetByHandleAsync(string handle)
    {
        return await cosmo.GetUserProfileByHandleAsync(handle);
    }

    /// <summary>
    /// Creates or updates a user profile. Validates handle uniqueness across users and orgs.
    /// </summary>
    public async Task<UserProfile> SaveProfileAsync(UserProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Handle))
            throw new InvalidOperationException("Handle is required.");

        profile.Handle = Slugify(profile.Handle);

        if (profile.Handle.Length < 2)
            throw new InvalidOperationException("Handle must be at least 2 characters.");

        if (DaisiGit.Core.ReservedNames.IsDisallowed(profile.Handle))
            throw new InvalidOperationException($"'{profile.Handle}' is not available.");

        // Check uniqueness against other user profiles
        var existingUser = await cosmo.GetUserProfileByHandleAsync(profile.Handle);
        if (existingUser != null && existingUser.UserId != profile.UserId)
            throw new InvalidOperationException($"Handle '{profile.Handle}' is already taken.");

        // Check uniqueness against organizations
        var existingOrg = await cosmo.GetOrganizationBySlugAsync(profile.Handle);
        if (existingOrg != null)
            throw new InvalidOperationException($"Handle '{profile.Handle}' is already taken by an organization.");

        return await cosmo.UpsertUserProfileAsync(profile);
    }

    /// <summary>
    /// Checks if a handle is available (not taken by any user or org).
    /// </summary>
    public async Task<bool> IsHandleAvailableAsync(string handle, string? excludeUserId = null)
    {
        var slug = Slugify(handle);

        var existingUser = await cosmo.GetUserProfileByHandleAsync(slug);
        if (existingUser != null && existingUser.UserId != excludeUserId)
            return false;

        var existingOrg = await cosmo.GetOrganizationBySlugAsync(slug);
        if (existingOrg != null)
            return false;

        return true;
    }

    internal static string Slugify(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = SlugRegex().Replace(slug, "-");
        slug = slug.Trim('-');
        return slug;
    }

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex SlugRegex();
}
