using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Central event dispatch hub. Persists events for audit, then triggers workflow evaluation.
/// </summary>
public class GitEventService(DaisiGitCosmo cosmo, WorkflowTriggerService triggerService, UserProfileService userProfileService)
{
    /// <summary>
    /// Emits a git event: persists to Cosmos for audit, then fires workflow triggers.
    /// </summary>
    public async Task EmitAsync(
        string accountId, string repositoryId,
        GitTriggerType eventType,
        string actorId, string actorName,
        Dictionary<string, string> payload,
        string? actorEmail = null)
    {
        // Inject actor fields into the workflow context
        payload["actor.id"] = actorId;
        payload["actor.name"] = actorName;

        // Resolve actor email: explicit param > user profile > default
        if (string.IsNullOrEmpty(actorEmail))
        {
            try
            {
                var profile = await userProfileService.GetProfileAsync(actorId, accountId);
                actorEmail = profile?.Email;
            }
            catch { }
        }
        payload["actor.email"] = actorEmail ?? $"{actorName}@daisi.ai";

        var nameParts = actorName.Split(' ', 2, StringSplitOptions.TrimEntries);
        payload["actor.firstName"] = nameParts[0];
        payload["actor.lastName"] = nameParts.Length > 1 ? nameParts[1] : "";

        // Persist the event
        var gitEvent = new GitEvent
        {
            AccountId = accountId,
            RepositoryId = repositoryId,
            EventType = eventType,
            ActorId = actorId,
            ActorName = actorName,
            Payload = payload
        };

        try
        {
            await cosmo.CreateEventAsync(gitEvent);
        }
        catch
        {
            // Event persistence failure should not block the operation
        }

        // Fire-and-forget trigger evaluation
        _ = Task.Run(async () =>
        {
            try
            {
                await triggerService.FireTriggerAsync(accountId, repositoryId, eventType, payload);
            }
            catch
            {
                // Trigger failure should not propagate
            }
        });
    }
}
