using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Central event dispatch hub. Persists events for audit, then triggers workflow evaluation.
/// </summary>
public class GitEventService(DaisiGitCosmo cosmo, WorkflowTriggerService triggerService)
{
    /// <summary>
    /// Emits a git event: persists to Cosmos for audit, then fires workflow triggers.
    /// </summary>
    public async Task EmitAsync(
        string accountId, string repositoryId,
        GitTriggerType eventType,
        string actorId, string actorName,
        Dictionary<string, string> payload)
    {
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
