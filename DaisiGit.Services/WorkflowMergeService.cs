using System.Text.RegularExpressions;
using DaisiGit.Core.Models;

namespace DaisiGit.Services;

/// <summary>
/// Renders merge fields in workflow templates.
/// Replaces {{key}} placeholders with values from the execution context.
/// </summary>
public static partial class WorkflowMergeService
{
    /// <summary>
    /// Replaces all {{key}} placeholders in a template with context values.
    /// </summary>
    public static string Render(string template, Dictionary<string, string> context)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        return MergeFieldRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();
            return context.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    /// <summary>
    /// Builds a flattened context dictionary from a repository, actor, and event payload.
    /// </summary>
    public static Dictionary<string, string> BuildContext(
        GitRepository repo, string actorId, string actorName,
        Dictionary<string, string> eventPayload)
    {
        var ctx = new Dictionary<string, string>
        {
            ["repo.id"] = repo.id,
            ["repo.name"] = repo.Name,
            ["repo.slug"] = repo.Slug,
            ["repo.ownerName"] = repo.OwnerName,
            ["repo.defaultBranch"] = repo.DefaultBranch,
            ["repo.visibility"] = repo.Visibility.ToString(),
            ["actor.id"] = actorId,
            ["actor.name"] = actorName
        };

        foreach (var (key, value) in eventPayload)
            ctx[key] = value;

        return ctx;
    }

    [GeneratedRegex(@"\{\{(.+?)\}\}")]
    private static partial Regex MergeFieldRegex();
}
