using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// CRUD wrapper for workflow management.
/// </summary>
public class WorkflowService(DaisiGitCosmo cosmo)
{
    public async Task<GitWorkflow> CreateAsync(GitWorkflow workflow)
        => await cosmo.CreateWorkflowAsync(workflow);

    public async Task<GitWorkflow?> GetAsync(string id, string accountId)
        => await cosmo.GetWorkflowAsync(id, accountId);

    public async Task<GitWorkflow> UpdateAsync(GitWorkflow workflow)
        => await cosmo.UpdateWorkflowAsync(workflow);

    public async Task DeleteAsync(string id, string accountId)
        => await cosmo.DeleteWorkflowAsync(id, accountId);

    public async Task<List<GitWorkflow>> ListAsync(string accountId)
        => await cosmo.GetWorkflowsAsync(accountId);

    public async Task<List<WorkflowExecution>> ListExecutionsAsync(
        string accountId, string? workflowId = null, string? repositoryId = null,
        int take = 50, int skip = 0)
        => await cosmo.GetWorkflowExecutionsAsync(accountId, workflowId, repositoryId, take, skip);

    public async Task<List<GitEvent>> ListEventsAsync(string repositoryId, int take = 50)
        => await cosmo.GetRecentEventsAsync(repositoryId, take);
}
