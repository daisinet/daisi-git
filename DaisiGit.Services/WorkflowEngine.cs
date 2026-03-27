using System.Net.Http.Headers;
using System.Text;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Processes workflow executions step-by-step.
/// Adapted from CRM AutomationEngine with git-specific step types.
/// </summary>
public class WorkflowEngine(
    DaisiGitCosmo cosmo,
    IHttpClientFactory httpClientFactory,
    IssueService issueService,
    PullRequestService prService,
    CommentService commentService,
    SecretService secretService)
{
    /// <summary>
    /// Processes an execution, injecting secrets and env into the context.
    /// </summary>
    public async Task ProcessExecutionAsync(WorkflowExecution execution, List<WorkflowStep> steps,
        Dictionary<string, string>? env = null)
    {
        try
        {
            // Inject secrets into context (org secrets inherited, repo overrides)
            var orgId = execution.Context.GetValueOrDefault("_orgId");
            var secrets = await secretService.ResolveSecretsAsync(execution.RepositoryId, orgId);
            foreach (var (key, value) in secrets)
                execution.Context[key] = value;

            // Inject env variables
            if (env != null)
            {
                foreach (var (key, value) in env)
                    execution.Context[$"env.{key}"] = value;
            }
            var flatSteps = FlattenSteps(steps, execution.Context);
            execution.TotalSteps = flatSteps.Count;

            while (execution.CurrentStepIndex < flatSteps.Count)
            {
                var step = flatSteps[execution.CurrentStepIndex];
                var stepResult = new WorkflowStepResult
                {
                    StepIndex = execution.CurrentStepIndex,
                    StepType = step.StepType,
                    ExecutedUtc = DateTime.UtcNow
                };

                try
                {
                    switch (step.StepType)
                    {
                        case WorkflowStepType.HttpRequest:
                            await ExecuteHttpRequest(step, stepResult, execution.Context);
                            break;
                        case WorkflowStepType.Wait:
                            ExecuteWait(step, execution);
                            stepResult.Success = true;
                            execution.StepResults.Add(stepResult);
                            execution.CurrentStepIndex++;
                            await cosmo.UpdateWorkflowExecutionAsync(execution);
                            return; // pause — background worker resumes later
                        case WorkflowStepType.AddComment:
                            await ExecuteAddComment(step, stepResult, execution);
                            break;
                        case WorkflowStepType.CloseIssue:
                            await ExecuteCloseIssue(stepResult, execution);
                            break;
                        case WorkflowStepType.ClosePullRequest:
                            await ExecuteClosePullRequest(stepResult, execution);
                            break;
                        case WorkflowStepType.SetLabel:
                        case WorkflowStepType.RemoveLabel:
                            // Label operations — placeholder for when label service exists
                            stepResult.Success = true;
                            stepResult.RenderedBody = $"{step.StepType}: {step.LabelName}";
                            break;
                        case WorkflowStepType.RequireReview:
                            await ExecuteRequireReview(step, stepResult, execution);
                            break;
                        case WorkflowStepType.Condition:
                            stepResult.Success = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    stepResult.Success = false;
                    stepResult.Error = ex.Message;
                    execution.StepResults.Add(stepResult);
                    execution.Status = "Failed";
                    execution.Error = $"Step {execution.CurrentStepIndex} ({step.StepType}) failed: {ex.Message}";
                    await cosmo.UpdateWorkflowExecutionAsync(execution);
                    return;
                }

                execution.StepResults.Add(stepResult);
                execution.CurrentStepIndex++;
            }

            execution.Status = "Completed";
            execution.NextRunAt = null;
            await cosmo.UpdateWorkflowExecutionAsync(execution);
        }
        catch (Exception ex)
        {
            execution.Status = "Failed";
            execution.Error = ex.Message;
            await cosmo.UpdateWorkflowExecutionAsync(execution);
        }
    }

    // ── Step executors ──

    private async Task ExecuteHttpRequest(WorkflowStep step, WorkflowStepResult result,
        Dictionary<string, string> context)
    {
        var url = WorkflowMergeService.Render(step.HttpUrl ?? "", context);
        if (string.IsNullOrWhiteSpace(url))
        {
            result.Success = false;
            result.Error = "URL is required";
            return;
        }

        var method = (step.HttpMethod ?? "GET").ToUpperInvariant();
        var client = httpClientFactory.CreateClient("WorkflowHttp");
        client.Timeout = TimeSpan.FromSeconds(30);

        using var request = new HttpRequestMessage(
            method switch
            {
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                "PATCH" => HttpMethod.Patch,
                _ => HttpMethod.Get
            }, url);

        if (step.HttpHeaders is { Count: > 0 })
        {
            foreach (var (key, value) in step.HttpHeaders)
                request.Headers.TryAddWithoutValidation(key, WorkflowMergeService.Render(value, context));
        }

        if (method is "POST" or "PUT" or "PATCH" && !string.IsNullOrEmpty(step.HttpBody))
        {
            var body = WorkflowMergeService.Render(step.HttpBody, context);
            request.Content = new StringContent(body, Encoding.UTF8, step.HttpContentType ?? "application/json");
            result.RenderedBody = body;
        }

        var response = await client.SendAsync(request);
        result.HttpStatusCode = (int)response.StatusCode;

        var responseBody = await response.Content.ReadAsStringAsync();
        result.HttpResponseBody = responseBody.Length > 4000 ? responseBody[..4000] + "..." : responseBody;
        result.Success = response.IsSuccessStatusCode;
        if (!result.Success)
            result.Error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
    }

    private async Task ExecuteAddComment(WorkflowStep step, WorkflowStepResult result,
        WorkflowExecution execution)
    {
        var body = WorkflowMergeService.Render(step.CommentBody ?? "", execution.Context);
        result.RenderedBody = body;

        // Determine what to comment on based on trigger context
        var prNumber = execution.Context.GetValueOrDefault("pr.number");
        var issueNumber = execution.Context.GetValueOrDefault("issue.number");

        if (!string.IsNullOrEmpty(prNumber))
        {
            var pr = await prService.GetByNumberAsync(execution.RepositoryId, int.Parse(prNumber));
            if (pr != null)
            {
                await commentService.CreateAsync(execution.RepositoryId, pr.id, nameof(PullRequest),
                    body, "workflow", "Workflow");
                result.Success = true;
                return;
            }
        }

        if (!string.IsNullOrEmpty(issueNumber))
        {
            var issue = await issueService.GetByNumberAsync(execution.RepositoryId, int.Parse(issueNumber));
            if (issue != null)
            {
                await commentService.CreateAsync(execution.RepositoryId, issue.id, nameof(Issue),
                    body, "workflow", "Workflow");
                result.Success = true;
                return;
            }
        }

        result.Success = false;
        result.Error = "No PR or issue in context to comment on";
    }

    private async Task ExecuteCloseIssue(WorkflowStepResult result, WorkflowExecution execution)
    {
        var issueNumber = execution.Context.GetValueOrDefault("issue.number");
        if (string.IsNullOrEmpty(issueNumber))
        {
            result.Success = false;
            result.Error = "No issue number in context";
            return;
        }

        var issue = await issueService.GetByNumberAsync(execution.RepositoryId, int.Parse(issueNumber));
        if (issue == null)
        {
            result.Success = false;
            result.Error = $"Issue #{issueNumber} not found";
            return;
        }

        await issueService.CloseAsync(issue);
        result.Success = true;
    }

    private async Task ExecuteClosePullRequest(WorkflowStepResult result, WorkflowExecution execution)
    {
        var prNumber = execution.Context.GetValueOrDefault("pr.number");
        if (string.IsNullOrEmpty(prNumber))
        {
            result.Success = false;
            result.Error = "No PR number in context";
            return;
        }

        var pr = await prService.GetByNumberAsync(execution.RepositoryId, int.Parse(prNumber));
        if (pr == null)
        {
            result.Success = false;
            result.Error = $"PR #{prNumber} not found";
            return;
        }

        await prService.CloseAsync(pr);
        result.Success = true;
    }

    private async Task ExecuteRequireReview(WorkflowStep step, WorkflowStepResult result,
        WorkflowExecution execution)
    {
        var prNumber = execution.Context.GetValueOrDefault("pr.number");
        if (string.IsNullOrEmpty(prNumber))
        {
            result.Success = false;
            result.Error = "No PR number in context";
            return;
        }

        var reviews = await cosmo.ListReviewsForPrAsync(execution.RepositoryId, int.Parse(prNumber));
        var approvals = reviews.Count(r => r.State == Core.Enums.ReviewState.Approved);
        var required = step.RequiredApprovals ?? 1;

        result.RenderedBody = $"{approvals}/{required} approvals";
        result.Success = approvals >= required;
        if (!result.Success)
            result.Error = $"Requires {required} approvals, only {approvals} found";

        await Task.CompletedTask;
    }

    // ── Wait ──

    private static void ExecuteWait(WorkflowStep step, WorkflowExecution execution)
    {
        var waitTime = TimeSpan.Zero;
        if (step.WaitDays.HasValue) waitTime += TimeSpan.FromDays(step.WaitDays.Value);
        if (step.WaitHours.HasValue) waitTime += TimeSpan.FromHours(step.WaitHours.Value);
        if (step.WaitMinutes.HasValue) waitTime += TimeSpan.FromMinutes(step.WaitMinutes.Value);

        if (waitTime == TimeSpan.Zero)
            waitTime = TimeSpan.FromMinutes(1);

        execution.NextRunAt = DateTime.UtcNow + waitTime;
    }

    // ── Condition flattening ──

    private static List<WorkflowStep> FlattenSteps(List<WorkflowStep> steps, Dictionary<string, string> context)
    {
        var flat = new List<WorkflowStep>();
        foreach (var step in steps.OrderBy(s => s.Order))
        {
            if (step.StepType == WorkflowStepType.Condition && step.Branches is { Count: > 0 })
            {
                foreach (var branch in step.Branches)
                {
                    if (EvaluateSimpleCondition(branch.Expression, context))
                    {
                        flat.Add(new WorkflowStep
                        {
                            StepType = WorkflowStepType.Condition,
                            ConditionExpression = branch.Expression
                        });
                        flat.AddRange(FlattenSteps(branch.Steps, context));
                        break;
                    }
                }
            }
            else
            {
                flat.Add(step);
            }
        }
        return flat;
    }

    /// <summary>
    /// Simple condition evaluator — checks context values without Roslyn.
    /// Supports: "key == value", "key != value", "key contains value", null/empty = always true (else branch).
    /// </summary>
    internal static bool EvaluateSimpleCondition(string? expression, Dictionary<string, string> context)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return true; // else branch

        var parts = expression.Split(' ', 3, StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return context.ContainsKey(parts[0]); // just a key name = "is truthy"

        var key = parts[0];
        var op = parts[1];
        var value = parts[2].Trim('"', '\'');

        var actual = context.GetValueOrDefault(key, "");

        return op switch
        {
            "==" => string.Equals(actual, value, StringComparison.OrdinalIgnoreCase),
            "!=" => !string.Equals(actual, value, StringComparison.OrdinalIgnoreCase),
            "contains" => actual.Contains(value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
