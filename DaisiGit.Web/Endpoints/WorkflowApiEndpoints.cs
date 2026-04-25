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
        api.MapGet("/repos/{owner}/{slug}/workflows/{id}", GetWorkflow);
        api.MapPut("/repos/{owner}/{slug}/workflows/{id}", UpdateWorkflow);
        api.MapDelete("/repos/{owner}/{slug}/workflows/{id}", DeleteWorkflow);
        api.MapGet("/repos/{owner}/{slug}/workflows/{id}/yaml", GetWorkflowYaml);
        api.MapPut("/repos/{owner}/{slug}/workflows/{id}/yaml", UpdateWorkflowYaml);
        api.MapPost("/repos/{owner}/{slug}/workflows/yaml", CreateWorkflowYaml);
        api.MapPost("/repos/{owner}/{slug}/workflows/{id}/run", RunWorkflowNow);
        api.MapGet("/repos/{owner}/{slug}/workflows/{id}/runs", ListWorkflowRuns);
        api.MapGet("/repos/{owner}/{slug}/runs", ListRepoRuns);
        api.MapGet("/repos/{owner}/{slug}/runs/{execId}", GetRun);
        api.MapGet("/repos/{owner}/{slug}/events", ListEvents);
    }

    private static string GetUserName(HttpContext ctx) => ctx.Items["userName"] as string ?? "";

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

    private static async Task<IResult> GetWorkflow(
        HttpContext ctx, string owner, string slug, string id,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanReadAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        var workflow = await workflowService.GetAsync(id, repo.AccountId);
        if (workflow == null) return Results.NotFound();
        if (workflow.RepositoryId != null && workflow.RepositoryId != repo.id) return Results.NotFound();
        return Results.Ok(workflow);
    }

    private static async Task<IResult> GetWorkflowYaml(
        HttpContext ctx, string owner, string slug, string id,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanReadAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        var workflow = await workflowService.GetAsync(id, repo.AccountId);
        if (workflow == null) return Results.NotFound();
        if (workflow.RepositoryId != null && workflow.RepositoryId != repo.id) return Results.NotFound();

        var yaml = WorkflowYamlParser.ToYaml(workflow);
        return Results.Text(yaml, "application/x-yaml");
    }

    private static async Task<IResult> UpdateWorkflowYaml(
        HttpContext ctx, string owner, string slug, string id, YamlBody req,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanWriteAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        if (!WorkflowYamlParser.TryParse(req.Yaml ?? "", out var parseError, out var parsed) || parsed == null)
            return Results.BadRequest(new { error = parseError ?? "Invalid YAML" });

        var workflow = await workflowService.GetAsync(id, repo.AccountId);
        if (workflow == null) return Results.NotFound();
        if (workflow.RepositoryId != null && workflow.RepositoryId != repo.id) return Results.NotFound();

        ApplyParsedToWorkflow(workflow, parsed);
        var updated = await workflowService.UpdateAsync(workflow);
        return Results.Ok(updated);
    }

    private static async Task<IResult> CreateWorkflowYaml(
        HttpContext ctx, string owner, string slug, YamlBody req,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanWriteAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        if (!WorkflowYamlParser.TryParse(req.Yaml ?? "", out var parseError, out var parsed) || parsed == null)
            return Results.BadRequest(new { error = parseError ?? "Invalid YAML" });

        var workflow = new GitWorkflow
        {
            AccountId = repo.AccountId,
            RepositoryId = repo.id,
            IsEnabled = true
        };
        ApplyParsedToWorkflow(workflow, parsed);
        var created = await workflowService.CreateAsync(workflow);
        return Results.Created($"/api/git/repos/{owner}/{slug}/workflows/{created.id}", created);
    }

    private static async Task<IResult> RunWorkflowNow(
        HttpContext ctx, string owner, string slug, string id, RunWorkflowRequest? req,
        RepositoryService repoService, WorkflowService workflowService,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanWriteAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        var workflow = await workflowService.GetAsync(id, repo.AccountId);
        if (workflow == null) return Results.NotFound();
        if (workflow.RepositoryId != null && workflow.RepositoryId != repo.id) return Results.NotFound();

        try
        {
            var execution = await workflowService.RunNowAsync(
                workflow, repo, GetUserId(ctx), GetUserName(ctx), inputs: req?.Inputs);
            return Results.Accepted($"/api/git/repos/{owner}/{slug}/runs/{execution.id}", execution);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetRun(
        HttpContext ctx, string owner, string slug, string execId,
        RepositoryService repoService, WorkflowService workflowService,
        DaisiGit.Data.DaisiGitCosmo cosmo,
        PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        if (!await permissionService.CanReadAsync(GetUserId(ctx), repo))
            return Results.Forbid();

        var execution = await cosmo.GetWorkflowExecutionAsync(execId, repo.AccountId);
        if (execution == null) return Results.NotFound();
        if (execution.RepositoryId != repo.id) return Results.NotFound();
        return Results.Ok(execution);
    }

    private static void ApplyParsedToWorkflow(GitWorkflow workflow, ParsedFileWorkflow parsed)
    {
        workflow.Name = parsed.Name;
        workflow.Steps = parsed.Steps;
        workflow.Env = parsed.Env;
        workflow.Inputs = parsed.Inputs ?? [];

        var firstTrigger = parsed.Triggers.FirstOrDefault();
        if (firstTrigger != null)
        {
            workflow.TriggerType = firstTrigger.EventType;
            workflow.TriggerFilters = firstTrigger.Branches is { Count: > 0 }
                ? new Dictionary<string, string> { ["branch"] = string.Join(",", firstTrigger.Branches) }
                : null;
        }
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

public record YamlBody(string? Yaml);

public record RunWorkflowRequest(Dictionary<string, string>? Inputs);

public record UpdateWorkflowRequest(
    string? Name = null,
    string? Description = null,
    GitTriggerType? TriggerType = null,
    Dictionary<string, string>? TriggerFilters = null,
    List<WorkflowStep>? Steps = null,
    bool? IsEnabled = null);
