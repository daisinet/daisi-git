using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Services;

namespace DaisiGit.Web.Endpoints;

/// <summary>
/// REST API endpoints for workflow management.
/// All endpoints verify the authenticated user has appropriate repo permissions.
/// </summary>
public static class WorkflowApiEndpoints
{
    public static void MapWorkflowApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/git").RequireAuthorization();

        api.MapGet("/repos/{owner}/{slug}/workflows", ListWorkflows);
        api.MapPost("/repos/{owner}/{slug}/workflows", CreateWorkflow);
        api.MapPut("/repos/{owner}/{slug}/workflows/{id}", UpdateWorkflow);
        api.MapDelete("/repos/{owner}/{slug}/workflows/{id}", DeleteWorkflow);
        api.MapGet("/repos/{owner}/{slug}/workflows/{id}/runs", ListWorkflowRuns);
        api.MapGet("/repos/{owner}/{slug}/runs", ListRepoRuns);
        api.MapGet("/repos/{owner}/{slug}/events", ListEvents);
    }

    private static string GetUserId(HttpContext ctx) => ctx.Items["userId"] as string ?? "";

    private static async Task<IResult> ListWorkflows(
        HttpContext ctx, string owner, string slug,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanReadAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        var workflows = await workflowService.ListAsync(repo.AccountId);
        var filtered = workflows.Where(w => w.RepositoryId == null || w.RepositoryId == repo.id).ToList();
        return Results.Ok(filtered);
    }

    private static async Task<IResult> CreateWorkflow(
        HttpContext ctx, string owner, string slug, CreateWorkflowRequest req,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanWriteAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        var workflow = new GitWorkflow
        {
            AccountId = repo.AccountId,
            RepositoryId = req.AccountWide ? null : repo.id,
            Name = req.Name,
            Description = req.Description,
            TriggerType = req.TriggerType,
            TriggerFilters = req.TriggerFilters,
            Steps = req.Steps ?? [],
            IsEnabled = req.IsEnabled ?? true
        };

        var created = await workflowService.CreateAsync(workflow);
        return Results.Created($"/api/git/repos/{owner}/{slug}/workflows/{created.id}", created);
    }

    private static async Task<IResult> UpdateWorkflow(
        HttpContext ctx, string owner, string slug, string id, UpdateWorkflowRequest req,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanWriteAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        var workflow = await workflowService.GetAsync(id, repo.AccountId);
        if (workflow == null) return Results.NotFound();

        if (req.Name != null) workflow.Name = req.Name;
        if (req.Description != null) workflow.Description = req.Description;
        if (req.TriggerType.HasValue) workflow.TriggerType = req.TriggerType.Value;
        if (req.TriggerFilters != null) workflow.TriggerFilters = req.TriggerFilters;
        if (req.Steps != null) workflow.Steps = req.Steps;
        if (req.IsEnabled.HasValue) workflow.IsEnabled = req.IsEnabled.Value;

        var updated = await workflowService.UpdateAsync(workflow);
        return Results.Ok(updated);
    }

    private static async Task<IResult> DeleteWorkflow(
        HttpContext ctx, string owner, string slug, string id,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanWriteAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        await workflowService.DeleteAsync(id, repo.AccountId);
        return Results.NoContent();
    }

    private static async Task<IResult> ListWorkflowRuns(
        HttpContext ctx, string owner, string slug, string id,
        int? skip, int? take,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanReadAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        var runs = await workflowService.ListExecutionsAsync(
            repo.AccountId, workflowId: id, take: take ?? 50, skip: skip ?? 0);
        return Results.Ok(runs);
    }

    private static async Task<IResult> ListRepoRuns(
        HttpContext ctx, string owner, string slug,
        int? skip, int? take,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanReadAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        var runs = await workflowService.ListExecutionsAsync(
            repo.AccountId, repositoryId: repo.id, take: take ?? 50, skip: skip ?? 0);
        return Results.Ok(runs);
    }

    private static async Task<IResult> ListEvents(
        HttpContext ctx, string owner, string slug,
        int? take,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanReadAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        var events = await workflowService.ListEventsAsync(repo.id, take ?? 50);
        return Results.Ok(events);
    }
}

// ── Request DTOs ──

public record CreateWorkflowRequest(
    string Name,
    string? Description,
    GitTriggerType TriggerType,
    Dictionary<string, string>? TriggerFilters = null,
    List<WorkflowStep>? Steps = null,
    bool? IsEnabled = true,
    bool AccountWide = false);

public record UpdateWorkflowRequest(
    string? Name = null,
    string? Description = null,
    GitTriggerType? TriggerType = null,
    Dictionary<string, string>? TriggerFilters = null,
    List<WorkflowStep>? Steps = null,
    bool? IsEnabled = null);
