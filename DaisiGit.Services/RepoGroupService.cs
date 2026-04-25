using DaisiGit.Core.Models;
using DaisiGit.Data;
using System.Text.RegularExpressions;

namespace DaisiGit.Services;

/// <summary>
/// Manages org-level repository groups: named, ordered sections rendered on the org profile.
/// Slugs are derived from the name and unique within the org.
/// </summary>
public class RepoGroupService(DaisiGitCosmo cosmo)
{
    public Task<List<RepoGroup>> ListAsync(string organizationId)
        => cosmo.GetRepoGroupsAsync(organizationId);

    public Task<RepoGroup?> GetAsync(string id, string organizationId)
        => cosmo.GetRepoGroupAsync(id, organizationId);

    public async Task<RepoGroup> CreateAsync(string organizationId, string accountId, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.");

        var slug = await GenerateUniqueSlugAsync(organizationId, name);

        // Append at the bottom of the existing order.
        var existing = await cosmo.GetRepoGroupsAsync(organizationId);
        var nextOrder = existing.Count == 0 ? 0 : existing.Max(g => g.SortOrder) + 1;

        var group = new RepoGroup
        {
            OrganizationId = organizationId,
            AccountId = accountId,
            Name = name.Trim(),
            Slug = slug,
            Description = string.IsNullOrWhiteSpace(description) ? null : description!.Trim(),
            SortOrder = nextOrder
        };
        return await cosmo.CreateRepoGroupAsync(group);
    }

    public async Task<RepoGroup> UpdateAsync(string id, string organizationId, string? name, string? description, int? sortOrder)
    {
        var group = await cosmo.GetRepoGroupAsync(id, organizationId)
            ?? throw new InvalidOperationException("Group not found.");

        if (!string.IsNullOrWhiteSpace(name) && name!.Trim() != group.Name)
        {
            group.Name = name.Trim();
            group.Slug = await GenerateUniqueSlugAsync(organizationId, group.Name, excludeGroupId: id);
        }
        if (description != null) group.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (sortOrder.HasValue) group.SortOrder = sortOrder.Value;

        return await cosmo.UpdateRepoGroupAsync(group);
    }

    public Task DeleteAsync(string id, string organizationId)
        => cosmo.DeleteRepoGroupAsync(id, organizationId);

    private async Task<string> GenerateUniqueSlugAsync(string organizationId, string name, string? excludeGroupId = null)
    {
        var baseSlug = Slugify(name);
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "group";

        var slug = baseSlug;
        var n = 2;
        while (true)
        {
            var existing = await cosmo.GetRepoGroupBySlugAsync(organizationId, slug);
            if (existing == null || existing.id == excludeGroupId) return slug;
            slug = $"{baseSlug}-{n++}";
        }
    }

    private static string Slugify(string s)
    {
        var lower = s.Trim().ToLowerInvariant();
        var ascii = Regex.Replace(lower, @"[^a-z0-9\s-]", "");
        var collapsed = Regex.Replace(ascii, @"[\s-]+", "-");
        return collapsed.Trim('-');
    }
}
