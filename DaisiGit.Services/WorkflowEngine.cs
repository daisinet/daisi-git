using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Git;
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
    SecretService secretService,
    GitObjectStore objectStore,
    BrowseService browseService,
    RepositoryService repoService,
    EmailService emailService,
    ImportService importService)
{
    /// <summary>
    /// Processes an execution, injecting secrets and env into the context.
    /// </summary>
    public async Task ProcessExecutionAsync(WorkflowExecution execution, List<WorkflowStep> steps,
        Dictionary<string, string>? env = null)
    {
        // Publish a queued/in_progress check on the relevant commit if this run was
        // triggered by something we can attach to (a PR or a push). Best-effort —
        // failures don't affect execution.
        var checkRun = await StartCheckRunAsync(execution);

        try
        {
            // Inject secrets into context (org secrets inherited, repo overrides)
            var orgId = execution.Context.GetValueOrDefault("_orgId");
            var secrets = await secretService.ResolveSecretsAsync(execution.RepositoryId, orgId);
            foreach (var (key, value) in secrets)
                execution.Context[key] = value;

            // Inject vars (non-secret config) — same precedence as secrets.
            try
            {
                var repo = await repoService.GetRepositoryAsync(execution.RepositoryId, execution.AccountId);
                if (repo != null)
                {
                    var org = await cosmo.GetOrganizationBySlugAsync(repo.OwnerName);
                    if (org?.Vars is { Count: > 0 })
                        foreach (var (k, v) in org.Vars) execution.Context[$"vars.{k}"] = v;
                    if (repo.Vars is { Count: > 0 })
                        foreach (var (k, v) in repo.Vars) execution.Context[$"vars.{k}"] = v;
                }
            }
            catch { /* vars are best-effort — don't fail the workflow if lookup throws */ }

            // Inject env variables
            if (env != null)
            {
                foreach (var (key, value) in env)
                    execution.Context[$"env.{key}"] = value;
            }
            var flatSteps = FlattenSteps(steps, execution.Context);
            execution.TotalSteps = flatSteps.Count;
            execution.StartedUtc ??= DateTime.UtcNow;

            while (execution.CurrentStepIndex < flatSteps.Count)
            {
                // Honor concurrency-cancellation between steps: if a newer dispatch flipped
                // this execution to Cancelled, stop right here without running the next step.
                var liveStatus = await cosmo.GetWorkflowExecutionAsync(execution.id, execution.AccountId);
                if (liveStatus?.Status == "Cancelled")
                {
                    execution.Status = "Cancelled";
                    execution.Error = liveStatus.Error;
                    execution.FinishedUtc = DateTime.UtcNow;
                    await cosmo.UpdateWorkflowExecutionAsync(execution);
                    return;
                }

                var step = flatSteps[execution.CurrentStepIndex];
                var stepResult = new WorkflowStepResult
                {
                    StepIndex = execution.CurrentStepIndex,
                    StepName = step.Name,
                    StepType = step.StepType,
                    ExecutedUtc = DateTime.UtcNow
                };

                // Overlay matrix values for this cell (fanned-out cells were given concrete
                // MatrixValues in FlattenSteps). Tracked in matrixKeys so we can clean up
                // after the step finishes — matrix vars never leak to subsequent steps.
                var matrixKeys = new List<string>();
                if (step.MatrixValues is { Count: > 0 })
                {
                    foreach (var (mk, mv) in step.MatrixValues)
                    {
                        var contextKey = $"matrix.{mk}";
                        execution.Context[contextKey] = mv;
                        matrixKeys.Add(contextKey);
                    }
                }

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
                            InjectStepOutputs(execution.Context, step, stepResult);
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
                        case WorkflowStepType.Checkout:
                            await ExecuteCheckout(step, stepResult, execution);
                            break;
                        case WorkflowStepType.RunScript:
                            await ExecuteRunScript(step, stepResult, execution);
                            break;
                        case WorkflowStepType.DeployAzureWebApp:
                            await ExecuteDeployAzureWebApp(step, stepResult, execution);
                            break;
                        case WorkflowStepType.SendEmail:
                            await ExecuteSendEmail(step, stepResult, execution);
                            break;
                        case WorkflowStepType.ImportFromUrl:
                            await ExecuteImportFromUrl(step, stepResult, execution);
                            break;
                        case WorkflowStepType.RunMinion:
                            await ExecuteRunMinion(step, stepResult, execution);
                            break;
                        case WorkflowStepType.AcrBuild:
                            await ExecuteAcrBuild(step, stepResult, execution);
                            break;
                        case WorkflowStepType.NugetPush:
                            await ExecuteNugetPush(step, stepResult, execution);
                            break;
                        case WorkflowStepType.DispatchWorkflow:
                            await ExecuteDispatchWorkflow(step, stepResult, execution);
                            break;
                        case WorkflowStepType.Condition:
                            stepResult.Success = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    var label = string.IsNullOrWhiteSpace(step.Name) ? step.StepType.ToString() : step.Name;
                    stepResult.Success = false;
                    stepResult.Error = ex.Message;
                    stepResult.FinishedUtc = DateTime.UtcNow;
                    execution.StepResults.Add(stepResult);
                    execution.Status = "Failed";
                    execution.Error = $"Step {execution.CurrentStepIndex + 1} ({label}) failed: {ex.Message}";
                    execution.FinishedUtc = DateTime.UtcNow;
                    await cosmo.UpdateWorkflowExecutionAsync(execution);
                    return;
                }

                stepResult.FinishedUtc = DateTime.UtcNow;
                execution.StepResults.Add(stepResult);
                InjectStepOutputs(execution.Context, step, stepResult);
                execution.CurrentStepIndex++;
                foreach (var k in matrixKeys) execution.Context.Remove(k);

                // Persist after each step so the UI sees live progress instead of waiting
                // for the whole workflow to terminate.
                await cosmo.UpdateWorkflowExecutionAsync(execution);

                if (!stepResult.Success)
                {
                    var label = string.IsNullOrWhiteSpace(step.Name) ? step.StepType.ToString() : step.Name;
                    execution.Status = "Failed";
                    execution.Error = $"Step {stepResult.StepIndex + 1} ({label}) failed: {stepResult.Error}";
                    execution.FinishedUtc = DateTime.UtcNow;
                    await cosmo.UpdateWorkflowExecutionAsync(execution);
                    return;
                }
            }

            execution.Status = "Completed";
            execution.NextRunAt = null;
            execution.FinishedUtc = DateTime.UtcNow;
            await cosmo.UpdateWorkflowExecutionAsync(execution);
        }
        catch (Exception ex)
        {
            execution.Status = "Failed";
            execution.FinishedUtc = DateTime.UtcNow;
            execution.Error = ex.Message;
            await cosmo.UpdateWorkflowExecutionAsync(execution);
        }
        finally
        {
            await FinishCheckRunAsync(checkRun, execution);
            CleanupWorkspace(execution);
        }
    }

    private async Task<CheckRun?> StartCheckRunAsync(WorkflowExecution execution)
    {
        try
        {
            var prNumber = execution.Context.GetValueOrDefault("pr.number");
            var headSha = execution.Context.GetValueOrDefault("pr.headSha")
                ?? execution.Context.GetValueOrDefault("push.newSha")
                ?? execution.Context.GetValueOrDefault("push.commit", "");
            // Only publish a check when there's something to attach to.
            if (string.IsNullOrEmpty(headSha) && string.IsNullOrEmpty(prNumber)) return null;

            var check = new CheckRun
            {
                RepositoryId = execution.RepositoryId,
                HeadSha = headSha,
                PullRequestNumber = int.TryParse(prNumber, out var n) ? n : 0,
                Name = string.IsNullOrEmpty(execution.WorkflowName) ? "Workflow" : execution.WorkflowName,
                Status = "in_progress",
                ExecutionId = execution.id,
                WorkflowId = execution.WorkflowId,
                StartedUtc = DateTime.UtcNow
            };
            return await cosmo.UpsertCheckRunAsync(check);
        }
        catch { return null; }
    }

    private async Task FinishCheckRunAsync(CheckRun? check, WorkflowExecution execution)
    {
        if (check == null) return;
        try
        {
            check.Status = "completed";
            check.Conclusion = execution.Status switch
            {
                "Completed" => "success",
                "Cancelled" => "cancelled",
                _           => "failure"
            };
            check.Summary = execution.Error;
            check.CompletedUtc = DateTime.UtcNow;
            await cosmo.UpsertCheckRunAsync(check);
        }
        catch { }
    }

    /// <summary>
    /// Injects step outputs into the execution context so downstream steps can
    /// reference them via merge fields: {{steps.0.response}}, {{steps.MyStep.output}}, etc.
    /// </summary>
    private static void InjectStepOutputs(Dictionary<string, string> context,
        WorkflowStep step, WorkflowStepResult result)
    {
        var index = result.StepIndex.ToString();
        var keys = new List<string> { $"steps.{index}" };
        if (!string.IsNullOrEmpty(step.Name))
            keys.Add($"steps.{step.Name}");

        foreach (var prefix in keys)
        {
            context[$"{prefix}.success"] = result.Success.ToString().ToLowerInvariant();

            if (result.HttpStatusCode.HasValue)
                context[$"{prefix}.status"] = result.HttpStatusCode.Value.ToString();
            if (result.HttpResponseBody != null)
                context[$"{prefix}.response"] = result.HttpResponseBody;

            if (result.ScriptOutput != null)
                context[$"{prefix}.output"] = result.ScriptOutput;
            if (result.ExitCode.HasValue)
                context[$"{prefix}.exitCode"] = result.ExitCode.Value.ToString();

            if (result.DeployUrl != null)
                context[$"{prefix}.deployUrl"] = result.DeployUrl;

            if (result.RenderedBody != null)
                context[$"{prefix}.body"] = result.RenderedBody;

            if (result.Error != null)
                context[$"{prefix}.error"] = result.Error;
        }
    }

    private static void CleanupWorkspace(WorkflowExecution execution)
    {
        if (!string.IsNullOrEmpty(execution.WorkspacePath) && Directory.Exists(execution.WorkspacePath))
            try { Directory.Delete(execution.WorkspacePath, recursive: true); } catch { }
    }

    private static string EnsureWorkspace(WorkflowExecution execution)
    {
        if (string.IsNullOrEmpty(execution.WorkspacePath))
        {
            execution.WorkspacePath = Path.Combine(Path.GetTempPath(), "daisigit-builds", execution.id);
            Directory.CreateDirectory(execution.WorkspacePath);
        }
        return execution.WorkspacePath;
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

    // ── Checkout ──

    private async Task ExecuteCheckout(WorkflowStep step, WorkflowStepResult result,
        WorkflowExecution execution)
    {
        var workspace = EnsureWorkspace(execution);
        var repoSlug = WorkflowMergeService.Render(step.CheckoutRepo ?? "", execution.Context);

        // External git URL — clone via git
        if (IsExternalGitUrl(repoSlug))
        {
            await ExecuteExternalCheckout(repoSlug, step, result, execution, workspace);
            return;
        }

        // Internal repo — read from object store
        string repoId;
        if (string.IsNullOrWhiteSpace(repoSlug))
        {
            repoId = execution.RepositoryId;
        }
        else
        {
            var parts = repoSlug.Split('/', 2);
            if (parts.Length != 2)
            {
                result.Success = false;
                result.Error = $"Invalid repo format '{repoSlug}'. Expected 'owner/slug' or a .git URL.";
                return;
            }
            var repo = await repoService.GetRepositoryBySlugAsync(parts[0], parts[1]);
            if (repo == null)
            {
                result.Success = false;
                result.Error = $"Repository '{repoSlug}' not found.";
                return;
            }
            repoId = repo.id;
        }

        var branch = WorkflowMergeService.Render(step.CheckoutBranch ?? "", execution.Context);
        if (string.IsNullOrWhiteSpace(branch))
            branch = execution.Context.GetValueOrDefault("push.branch", "main");

        var commitSha = await browseService.ResolveRefAsync(repoId, branch);
        if (commitSha == null)
        {
            result.Success = false;
            result.Error = $"Branch '{branch}' not found.";
            return;
        }

        var commit = await objectStore.GetObjectAsync(repoId, commitSha) as GitCommit;
        if (commit == null)
        {
            result.Success = false;
            result.Error = $"Could not read commit {commitSha[..7]}.";
            return;
        }

        var checkoutDir = workspace;
        if (!string.IsNullOrWhiteSpace(step.CheckoutPath))
        {
            checkoutDir = Path.Combine(workspace, step.CheckoutPath.Trim('/').Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(checkoutDir);
        }

        var fileCount = await WriteTreeToDisk(repoId, commit.TreeSha, checkoutDir);

        result.Success = true;
        result.RenderedBody = $"Checked out {branch} ({commitSha[..7]}) — {fileCount} files to {(string.IsNullOrWhiteSpace(step.CheckoutPath) ? "/" : step.CheckoutPath)}";
    }

    private static bool IsExternalGitUrl(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("git://", StringComparison.OrdinalIgnoreCase));

    private async Task ExecuteExternalCheckout(string url, WorkflowStep step, WorkflowStepResult result,
        WorkflowExecution execution, string workspace)
    {
        var branch = WorkflowMergeService.Render(step.CheckoutBranch ?? "", execution.Context);

        var checkoutDir = workspace;
        if (!string.IsNullOrWhiteSpace(step.CheckoutPath))
        {
            checkoutDir = Path.Combine(workspace, step.CheckoutPath.Trim('/').Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(checkoutDir);
        }

        // Build git clone command
        var args = "clone --depth 1";
        if (!string.IsNullOrWhiteSpace(branch))
            args += $" --branch {branch}";
        args += $" {url} .";

        var timeout = TimeSpan.FromMinutes(10);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = checkoutDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Pass secrets as env vars (e.g. for private repo token auth via URL)
        foreach (var (key, value) in execution.Context)
        {
            if (key.StartsWith("secrets."))
                process.StartInfo.Environment[key.Replace("secrets.", "SECRET_")] = value;
        }

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            result.Success = false;
            result.Error = "Git clone timed out after 10 minutes";
            result.ScriptOutput = Truncate(output.ToString(), 8000);
            return;
        }

        if (process.ExitCode != 0)
        {
            result.Success = false;
            result.Error = $"Git clone failed with exit code {process.ExitCode}";
            result.ScriptOutput = Truncate(output.ToString(), 8000);
            return;
        }

        // Count files cloned (excluding .git directory)
        var fileCount = Directory.Exists(checkoutDir)
            ? Directory.GetFiles(checkoutDir, "*", SearchOption.AllDirectories)
                .Count(f => !f.Replace('\\', '/').Contains("/.git/"))
            : 0;

        var branchLabel = string.IsNullOrWhiteSpace(branch) ? "default branch" : branch;
        result.Success = true;
        result.RenderedBody = $"Cloned {url} ({branchLabel}) — {fileCount} files to {(string.IsNullOrWhiteSpace(step.CheckoutPath) ? "/" : step.CheckoutPath)}";
    }

    private async Task<int> WriteTreeToDisk(string repoId, string treeSha, string targetDir)
    {
        var tree = await objectStore.GetObjectAsync(repoId, treeSha) as GitTree;
        if (tree == null) return 0;

        var count = 0;
        foreach (var entry in tree.Entries)
        {
            var entryPath = Path.Combine(targetDir, entry.Name);
            if (entry.IsTree)
            {
                Directory.CreateDirectory(entryPath);
                count += await WriteTreeToDisk(repoId, entry.Sha, entryPath);
            }
            else
            {
                var blob = await objectStore.GetObjectAsync(repoId, entry.Sha) as GitBlob;
                if (blob != null)
                {
                    await File.WriteAllBytesAsync(entryPath, blob.Data);
                    count++;
                }
            }
        }
        return count;
    }

    // ── Run Script ──

    private async Task ExecuteRunScript(WorkflowStep step, WorkflowStepResult result,
        WorkflowExecution execution)
    {
        var workspace = EnsureWorkspace(execution);
        var command = WorkflowMergeService.Render(step.ScriptCommand ?? "", execution.Context);
        if (string.IsNullOrWhiteSpace(command))
        {
            result.Success = false;
            result.Error = "Script command is required.";
            return;
        }

        var workDir = workspace;
        if (!string.IsNullOrWhiteSpace(step.ScriptWorkDir))
        {
            workDir = Path.Combine(workspace, step.ScriptWorkDir.Trim('/').Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(workDir))
            {
                result.Success = false;
                result.Error = $"Working directory '{step.ScriptWorkDir}' does not exist in workspace.";
                return;
            }
        }

        var timeout = TimeSpan.FromSeconds(step.ScriptTimeoutSeconds ?? 300);
        if (timeout > TimeSpan.FromMinutes(30)) timeout = TimeSpan.FromMinutes(30);

        // Determine shell. Use ArgumentList so the entire command stays as a single argv
        // element; passing it via Arguments would word-split it, and bash -c only takes the
        // first positional as the script (the rest become $0, $1, ...).
        var isWindows = OperatingSystem.IsWindows();
        var shell = isWindows ? "cmd.exe" : "/bin/bash";
        var shellFlag = isWindows ? "/c" : "-c";

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = shell,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add(shellFlag);
        process.StartInfo.ArgumentList.Add(command);

        // Pass workflow secrets as environment variables
        foreach (var (key, value) in execution.Context)
        {
            if (key.StartsWith("secrets."))
                process.StartInfo.Environment[key.Replace("secrets.", "SECRET_")] = value;
            else if (key.StartsWith("env."))
                process.StartInfo.Environment[key.Replace("env.", "")] = value;
        }

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            result.Success = false;
            result.Error = $"Script timed out after {timeout.TotalSeconds}s";
            result.ExitCode = -1;
            result.ScriptOutput = Truncate(output.ToString(), 8000);
            return;
        }

        result.ExitCode = process.ExitCode;
        result.ScriptOutput = Truncate(output.ToString(), 8000);
        result.Success = process.ExitCode == 0;
        if (!result.Success)
            result.Error = $"Script exited with code {process.ExitCode}";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\n... (truncated)";

    // ── Run Minion ──
    //
    // Installs daisi-minion as a scoped .NET global tool into the workspace, then invokes it in
    // `--cli --backend daisinet --goal <prompt>` mode. The daisinet backend auth flow is the
    // caller's problem: we exchange the system-scope DAISIGIT_WORKERS_SECRET_KEY for a short-lived
    // client key via the SDK (actually, we pass SECRET-KEY via env; minion does the exchange).

    private const string MinionSystemSecretName = "DAISIGIT_WORKERS_SECRET_KEY";
    private const string MinionFeedUrlEnvVar = "DAISIGIT_MINION_FEED_URL";
    private const string MinionPackageId = "Daisi.Minion";

    private async Task ExecuteRunMinion(WorkflowStep step, WorkflowStepResult result,
        WorkflowExecution execution)
    {
        var workspace = EnsureWorkspace(execution);

        // 1. Resolve working directory (guarded against escape).
        var workDir = workspace;
        if (!string.IsNullOrWhiteSpace(step.MinionWorkingDirectory))
        {
            var rel = step.MinionWorkingDirectory.Trim('/').Replace('/', Path.DirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(workspace, rel));
            var workspaceFull = Path.GetFullPath(workspace);
            if (!candidate.StartsWith(workspaceFull, StringComparison.Ordinal))
            {
                result.Success = false;
                result.Error = $"Working directory '{step.MinionWorkingDirectory}' escapes the workspace.";
                return;
            }
            if (!Directory.Exists(candidate))
            {
                result.Success = false;
                result.Error = $"Working directory '{step.MinionWorkingDirectory}' does not exist in the workspace.";
                return;
            }
            workDir = candidate;
        }

        // 2. Resolve instructions (inline or file-in-workspace).
        string instructions;
        var hasInline = !string.IsNullOrEmpty(step.MinionInstructions);
        var hasFile = !string.IsNullOrEmpty(step.MinionInstructionsFile);
        if (hasInline == hasFile)
        {
            result.Success = false;
            result.Error = "run-minion requires exactly one of `instructions` or `instructions-file`.";
            return;
        }
        if (hasFile)
        {
            var rel = step.MinionInstructionsFile!.Trim('/').Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(workspace, rel));
            var workspaceFull = Path.GetFullPath(workspace);
            if (!full.StartsWith(workspaceFull, StringComparison.Ordinal))
            {
                result.Success = false;
                result.Error = $"Instructions file '{step.MinionInstructionsFile}' escapes the workspace.";
                return;
            }
            if (!File.Exists(full))
            {
                result.Success = false;
                result.Error = $"Instructions file not found in workspace: {step.MinionInstructionsFile}";
                return;
            }
            instructions = await File.ReadAllTextAsync(full);
        }
        else
        {
            instructions = WorkflowMergeService.Render(step.MinionInstructions ?? "", execution.Context);
        }
        if (string.IsNullOrWhiteSpace(instructions))
        {
            result.Success = false;
            result.Error = "Resolved instructions are empty.";
            return;
        }

        // 3. Runtime prerequisite: need `dotnet` to install & run the minion tool.
        var dotnetCheck = await RunCaptureAsync("dotnet", "--version", workDir, env: null, TimeSpan.FromSeconds(10));
        if (dotnetCheck.ExitCode != 0)
        {
            result.Success = false;
            result.Error = "run-minion requires a runtime with the .NET SDK. Use `runtime: dotnet` or `runtime: full` in your workflow.";
            return;
        }

        // 4. System SECRET-KEY for ORC auth.
        var secretKey = await secretService.ResolveSystemSecretAsync(MinionSystemSecretName);
        if (string.IsNullOrEmpty(secretKey))
        {
            result.Success = false;
            result.Error = $"System secret '{MinionSystemSecretName}' is not configured. A platform admin must register the DaisiGit Workers App on ORC and store its SECRET-KEY.";
            return;
        }

        // 5. Install minion as a workspace-scoped tool (idempotent).
        var toolPath = Path.Combine(workspace, ".tools");
        var minionExe = OperatingSystem.IsWindows()
            ? Path.Combine(toolPath, "daisi-minion.exe")
            : Path.Combine(toolPath, "daisi-minion");

        if (!File.Exists(minionExe))
        {
            var feedUrl = Environment.GetEnvironmentVariable(MinionFeedUrlEnvVar);
            if (string.IsNullOrWhiteSpace(feedUrl))
            {
                result.Success = false;
                result.Error = $"{MinionFeedUrlEnvVar} is not set. Configure the Daisi.Minion NuGet feed URL in the worker environment.";
                return;
            }

            Directory.CreateDirectory(toolPath);
            var installArgs = $"tool install --tool-path \"{toolPath}\" --add-source \"{feedUrl}\" {MinionPackageId}";
            var install = await RunCaptureAsync("dotnet", installArgs, workspace, env: null, TimeSpan.FromMinutes(3));
            if (install.ExitCode != 0)
            {
                result.Success = false;
                result.Error = $"Failed to install {MinionPackageId}: exit {install.ExitCode}\n{Truncate(install.Output, 2000)}";
                return;
            }
        }

        // 6. Build args and child-process env.
        var args = BuildMinionArgs(step, instructions);
        var childEnv = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in execution.Context)
        {
            if (key.StartsWith("secrets."))
                childEnv[key.Replace("secrets.", "SECRET_")] = value;
            else if (key.StartsWith("env."))
                childEnv[key.Replace("env.", "")] = value;
        }
        childEnv["DAISI_SECRET_KEY"] = secretKey;
        if (!string.IsNullOrWhiteSpace(step.MinionOrcAddress))
            childEnv["DAISI_ORC_ADDRESS"] = step.MinionOrcAddress;

        // 7. Spawn minion and capture output.
        var timeoutSeconds = step.MinionTimeoutSeconds ?? 1500;
        if (timeoutSeconds > 1800) timeoutSeconds = 1800;
        var run = await RunCaptureAsync(minionExe, args, workDir, childEnv, TimeSpan.FromSeconds(timeoutSeconds));

        result.ExitCode = run.ExitCode;
        result.ScriptOutput = Truncate(run.Output, 8000);
        result.Success = run.TimedOut ? false : (run.ExitCode == 0);
        if (run.TimedOut)
            result.Error = $"Minion timed out after {timeoutSeconds}s.";
        else if (!result.Success)
            result.Error = $"Minion exited with code {run.ExitCode}.";
    }

    private static string BuildMinionArgs(WorkflowStep step, string instructions)
    {
        var sb = new StringBuilder();
        sb.Append("--cli --backend daisinet");
        if (!string.IsNullOrWhiteSpace(step.MinionModel))
            sb.Append(' ').Append("--model ").Append(Quote(step.MinionModel));
        if (step.MinionContextSize.HasValue)
            sb.Append(' ').Append("--context ").Append(step.MinionContextSize.Value);
        if (step.MinionMaxTokens.HasValue)
            sb.Append(' ').Append("--max-tokens ").Append(step.MinionMaxTokens.Value);
        if (step.MinionMaxIterations.HasValue)
            sb.Append(' ').Append("--max-iterations ").Append(step.MinionMaxIterations.Value);
        if (!string.IsNullOrWhiteSpace(step.MinionRole))
            sb.Append(' ').Append("--role ").Append(Quote(step.MinionRole));
        if (!string.IsNullOrWhiteSpace(step.MinionKvQuant))
            sb.Append(' ').Append("--kv-quant ").Append(Quote(step.MinionKvQuant));
        if (step.MinionJsonOutput == true) sb.Append(' ').Append("--json");
        if (step.MinionGrammar == true) sb.Append(' ').Append("--grammar");
        sb.Append(' ').Append("--goal ").Append(Quote(instructions));
        return sb.ToString();
    }

    private static string Quote(string s)
    {
        // Escape embedded quotes; surround with quotes if the value contains whitespace or quote chars.
        var escaped = s.Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    private record ProcessResult(int ExitCode, string Output, bool TimedOut);

    private static async Task<ProcessResult> RunCaptureAsync(string fileName, string arguments,
        string workingDirectory, Dictionary<string, string>? env, TimeSpan timeout)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (env != null)
        {
            foreach (var (k, v) in env)
                process.StartInfo.Environment[k] = v;
        }

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return new ProcessResult(process.ExitCode, output.ToString(), TimedOut: false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(-1, output.ToString(), TimedOut: true);
        }
    }

    // ── Send Email ──

    private async Task ExecuteSendEmail(WorkflowStep step, WorkflowStepResult result,
        WorkflowExecution execution)
    {
        var to = WorkflowMergeService.Render(step.EmailTo ?? "", execution.Context);
        var subject = WorkflowMergeService.Render(step.EmailSubject ?? "", execution.Context);
        var body = WorkflowMergeService.Render(step.EmailBody ?? "", execution.Context);

        if (string.IsNullOrWhiteSpace(to))
        {
            result.Success = false;
            result.Error = "Recipient email address is required.";
            return;
        }

        if (!emailService.IsEnabled)
        {
            result.Success = false;
            result.Error = "Email is not configured on this DaisiGit instance.";
            return;
        }

        try
        {
            await emailService.SendAsync(to, subject, body);
            result.Success = true;
            result.RenderedBody = $"Email sent to {to}";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = $"Failed to send email: {ex.Message}";
        }
    }

    // ── Import From URL ──

    private async Task ExecuteImportFromUrl(WorkflowStep step, WorkflowStepResult result,
        WorkflowExecution execution)
    {
        var url = WorkflowMergeService.Render(step.ImportUrl ?? "", execution.Context);
        if (string.IsNullOrWhiteSpace(url))
        {
            result.Success = false;
            result.Error = "Import URL is required.";
            return;
        }

        var repoId = execution.RepositoryId;
        if (string.IsNullOrEmpty(repoId))
        {
            result.Success = false;
            result.Error = "No repository associated with this workflow execution.";
            return;
        }

        var repo = await repoService.GetRepositoryAsync(repoId, execution.AccountId);
        if (repo == null)
        {
            result.Success = false;
            result.Error = $"Repository {repoId} not found.";
            return;
        }

        try
        {
            // Set/update the import source URL on the repo, then reimport
            repo.ImportedFromUrl = url;
            repo = await repoService.UpdateRepositoryAsync(repo);
            await importService.ReimportAsync(repo, msg => result.RenderedBody = msg);

            result.Success = true;
            result.RenderedBody = $"Imported from {url}";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = $"Import failed: {ex.Message}";
        }
    }

    // ── Deploy Azure Web App ──

    private async Task ExecuteDeployAzureWebApp(WorkflowStep step, WorkflowStepResult result,
        WorkflowExecution execution)
    {
        var appName = WorkflowMergeService.Render(step.AzureAppName ?? "", execution.Context);
        if (string.IsNullOrWhiteSpace(appName))
        {
            result.Success = false;
            result.Error = "Azure App Name is required";
            return;
        }

        // Pick auth mode early so we know whether secrets or a managed-identity token
        // will authenticate the deploy. Defaults to "basic" for back-compat.
        var authMode = (step.AzureAuthMode ?? "basic").Trim().ToLowerInvariant();
        string? username = null;
        string? password = null;
        string? bearerToken = null;

        if (authMode == "oidc" || authMode == "identity")
        {
            try
            {
                // Acquires an ARM token via the worker's managed identity. Locally this
                // falls through to dev creds (Azure CLI / VS / env). The Kudu /api/zipdeploy
                // endpoint accepts ARM bearer tokens when the principal has Website
                // Contributor (or equivalent) on the target App Service.
                var credential = new Azure.Identity.DefaultAzureCredential();
                var tokenRequest = new Azure.Core.TokenRequestContext(
                    new[] { "https://management.azure.com/.default" });
                var token = await credential.GetTokenAsync(tokenRequest, CancellationToken.None);
                bearerToken = token.Token;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = $"Could not acquire Azure token via managed identity: {ex.Message}. " +
                               "Confirm the worker has a system-assigned identity and the Website " +
                               "Contributor role on the target App Service.";
                return;
            }
        }
        else
        {
            var usernameKey = step.AzureUsernameSecret;
            var passwordKey = step.AzurePasswordSecret;
            if (string.IsNullOrEmpty(usernameKey) || string.IsNullOrEmpty(passwordKey))
            {
                result.Success = false;
                result.Error = "Deploy username and password secrets must be configured (or set auth-mode: oidc to use managed identity).";
                return;
            }

            username = execution.Context.GetValueOrDefault($"secrets.{usernameKey}", "").Trim();
            password = execution.Context.GetValueOrDefault($"secrets.{passwordKey}", "").Trim();
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                result.Success = false;
                result.Error = $"Could not resolve secrets '{usernameKey}' and/or '{passwordKey}'. Add them in Settings > Secrets.";
                return;
            }

            // Strip Azure's "{sitename}\" FTPS prefix so users can paste the portal value verbatim.
            var backslashIdx = username.IndexOf('\\');
            if (backslashIdx >= 0) username = username[(backslashIdx + 1)..];
        }

        // Build ZIP — from workspace (if Checkout+Build ran) or from git objects
        using var zipStream = new MemoryStream();
        var workDir = step.AzureWorkDir?.Trim('/');
        var deployPath = step.AzureDeployPath?.Trim('/');

        if (!string.IsNullOrEmpty(execution.WorkspacePath) && Directory.Exists(execution.WorkspacePath))
        {
            // Zip from workspace on disk (built artifacts)
            var sourceDir = execution.WorkspacePath;

            // Resolve working directory first, then deploy path within it
            if (!string.IsNullOrEmpty(workDir))
                sourceDir = Path.Combine(sourceDir, workDir.Replace('/', Path.DirectorySeparatorChar));
            if (!string.IsNullOrEmpty(deployPath))
                sourceDir = Path.Combine(sourceDir, deployPath.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(sourceDir))
            {
                var displayPath = string.Join("/", new[] { workDir, deployPath }.Where(p => !string.IsNullOrEmpty(p)));
                result.Success = false;
                result.Error = $"Deploy path '{displayPath}' not found in workspace";
                return;
            }

            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    var entryName = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                    archive.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
                }
            }
            zipStream.Position = 0;
        }
        else
        {
            // Zip directly from git objects (no build step)
            var repoId = execution.RepositoryId;
            var commitSha = execution.Context.GetValueOrDefault("push.commit", "");
            if (string.IsNullOrEmpty(commitSha))
            {
                result.Success = false;
                result.Error = "No commit SHA in context (push.commit). Use a Checkout step for built deployments.";
                return;
            }

            var commit = await objectStore.GetObjectAsync(repoId, commitSha) as GitCommit;
            if (commit == null)
            {
                result.Success = false;
                result.Error = $"Could not read commit {commitSha[..7]}";
                return;
            }

            var treeSha = commit.TreeSha;
            if (!string.IsNullOrEmpty(deployPath))
            {
                var subTree = await WalkToSubtree(repoId, treeSha, deployPath);
                if (subTree == null)
                {
                    result.Success = false;
                    result.Error = $"Deploy path '{deployPath}' not found in commit";
                    return;
                }
                treeSha = subTree;
            }

            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                await AddTreeToZip(archive, repoId, treeSha, "");
            }
            zipStream.Position = 0;
        }

        // Deploy via Kudu ZipDeploy API. Azure now assigns region-hashed hostnames
        // (e.g. {app}-{hash}.scm.{region}.azurewebsites.net), so allow scm-host override.
        var scmHost = !string.IsNullOrWhiteSpace(step.AzureScmHost)
            ? WorkflowMergeService.Render(step.AzureScmHost, execution.Context).Trim()
            : $"{appName}.scm.azurewebsites.net";
        var kuduUrl = $"https://{scmHost}/api/zipdeploy";
        var client = httpClientFactory.CreateClient("WorkflowHttp");
        client.Timeout = TimeSpan.FromMinutes(5);

        using var request = new HttpRequestMessage(HttpMethod.Post, kuduUrl);
        if (bearerToken != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        else
        {
            var authBytes = Encoding.ASCII.GetBytes($"{username}:{password}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        }
        request.Content = new StreamContent(zipStream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        var response = await client.SendAsync(request);
        result.HttpStatusCode = (int)response.StatusCode;
        result.DeployUrl = $"https://{appName}.azurewebsites.net";

        if (response.IsSuccessStatusCode)
        {
            result.Success = true;
            result.RenderedBody = $"Deployed to {result.DeployUrl}";
        }
        else
        {
            var body = await response.Content.ReadAsStringAsync();
            result.Success = false;
            result.Error = $"Deploy failed: HTTP {(int)response.StatusCode} — {(body.Length > 500 ? body[..500] : body)}";
        }
    }

    private async Task<string?> WalkToSubtree(string repoId, string treeSha, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentSha = treeSha;

        foreach (var segment in segments)
        {
            var tree = await objectStore.GetObjectAsync(repoId, currentSha) as GitTree;
            if (tree == null) return null;

            var entry = tree.Entries.FirstOrDefault(e => e.Name == segment && e.IsTree);
            if (entry == null) return null;
            currentSha = entry.Sha;
        }

        return currentSha;
    }

    private async Task AddTreeToZip(ZipArchive archive, string repoId, string treeSha, string prefix)
    {
        var tree = await objectStore.GetObjectAsync(repoId, treeSha) as GitTree;
        if (tree == null) return;

        foreach (var entry in tree.Entries)
        {
            var entryPath = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";

            if (entry.IsTree)
            {
                await AddTreeToZip(archive, repoId, entry.Sha, entryPath);
            }
            else
            {
                var blob = await objectStore.GetObjectAsync(repoId, entry.Sha) as GitBlob;
                if (blob != null)
                {
                    var zipEntry = archive.CreateEntry(entryPath, CompressionLevel.Fastest);
                    using var stream = zipEntry.Open();
                    await stream.WriteAsync(blob.Data);
                }
            }
        }
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

    // ── AcrBuild: server-side Docker build via `az acr build`. No DinD required. ──

    private async Task ExecuteAcrBuild(WorkflowStep step, WorkflowStepResult result, WorkflowExecution execution)
    {
        var registry = WorkflowMergeService.Render(step.AcrRegistry ?? "", execution.Context).Trim();
        var image = WorkflowMergeService.Render(step.AcrImage ?? "", execution.Context).Trim();
        var dockerfile = WorkflowMergeService.Render(step.AcrDockerfile ?? "Dockerfile", execution.Context).Trim();
        var contextDir = WorkflowMergeService.Render(step.AcrContext ?? ".", execution.Context).Trim();
        var buildArgs = WorkflowMergeService.Render(step.AcrBuildArgs ?? "", execution.Context).Trim();

        if (string.IsNullOrEmpty(registry) || string.IsNullOrEmpty(image))
        {
            result.Success = false;
            result.Error = "acr-build requires both `registry` and `image`.";
            return;
        }

        // Build a comma-separated tag list as repeated `-t image:tag`. Inputs accept
        // either a single image:tag, or a comma-separated list (e.g. `myapp:latest,myapp:1.2`).
        var tags = image.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var tagArgs = string.Join(" ", tags.Select(t => $"-t {EscapeArg(t)}"));

        var argsBuilder = new StringBuilder();
        argsBuilder.Append($"acr build --registry {EscapeArg(registry)} {tagArgs} --file {EscapeArg(dockerfile)}");
        if (!string.IsNullOrEmpty(buildArgs))
        {
            foreach (var pair in buildArgs.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                argsBuilder.Append($" --build-arg {EscapeArg(pair)}");
        }
        argsBuilder.Append($" {EscapeArg(contextDir)}");

        await RunShellCommandAsync(
            command: "az " + argsBuilder.ToString(),
            workspaceSubdir: null,
            timeoutSeconds: 1800,
            execution: execution,
            stepResult: result,
            successMessage: $"Built and pushed {image} to {registry}");
        result.RenderedBody ??= $"acr build {registry}/{image}";
    }

    // ── NugetPush ──

    private async Task ExecuteNugetPush(WorkflowStep step, WorkflowStepResult result, WorkflowExecution execution)
    {
        var packagePath = WorkflowMergeService.Render(step.NugetPackagePath ?? "", execution.Context).Trim();
        var source = WorkflowMergeService.Render(step.NugetSource ?? "https://api.nuget.org/v3/index.json", execution.Context).Trim();
        var apiKeyName = step.NugetApiKeySecret;
        if (string.IsNullOrEmpty(packagePath))
        {
            result.Success = false; result.Error = "nuget-push requires `package`."; return;
        }
        if (string.IsNullOrEmpty(apiKeyName))
        {
            result.Success = false; result.Error = "nuget-push requires `api-key-secret`."; return;
        }
        var apiKey = execution.Context.GetValueOrDefault($"secrets.{apiKeyName}");
        if (string.IsNullOrEmpty(apiKey))
        {
            result.Success = false; result.Error = $"Could not resolve secret '{apiKeyName}' for nuget-push."; return;
        }

        var skipDup = step.NugetSkipDuplicate == true ? " --skip-duplicate" : "";
        var cmd = $"dotnet nuget push {EscapeArg(packagePath)} --source {EscapeArg(source)} --api-key {EscapeArg(apiKey)}{skipDup}";

        await RunShellCommandAsync(cmd, workspaceSubdir: null, timeoutSeconds: 600,
            execution: execution, stepResult: result,
            successMessage: $"Pushed {packagePath} -> {source}");
        result.RenderedBody ??= $"nuget push {packagePath}";
    }

    // ── DispatchWorkflow: trigger another daisi-git workflow synchronously (no wait). ──

    private async Task ExecuteDispatchWorkflow(WorkflowStep step, WorkflowStepResult result, WorkflowExecution execution)
    {
        var repoStr = WorkflowMergeService.Render(step.DispatchRepo ?? "", execution.Context).Trim();
        var workflowRef = WorkflowMergeService.Render(step.DispatchWorkflow ?? "", execution.Context).Trim();
        if (string.IsNullOrEmpty(workflowRef))
        {
            result.Success = false; result.Error = "dispatch-workflow requires `workflow`."; return;
        }

        // Default target repo to the executing one.
        var ownerSlug = repoStr;
        if (string.IsNullOrEmpty(ownerSlug))
        {
            var owner = execution.Context.GetValueOrDefault("repo.owner");
            var slug = execution.Context.GetValueOrDefault("repo.slug");
            if (!string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(slug))
                ownerSlug = $"{owner}/{slug}";
        }
        if (string.IsNullOrEmpty(ownerSlug) || !ownerSlug.Contains('/'))
        {
            result.Success = false; result.Error = "dispatch-workflow could not determine target repo (`repo: owner/slug`)."; return;
        }
        var parts = ownerSlug.Split('/', 2);
        var targetRepo = await repoService.GetRepositoryBySlugAsync(parts[0], parts[1]);
        if (targetRepo == null)
        {
            result.Success = false; result.Error = $"Target repo '{ownerSlug}' not found."; return;
        }

        // Resolve the target workflow by id first, then by name.
        var targetWorkflow = await cosmo.GetWorkflowAsync(workflowRef, targetRepo.AccountId);
        if (targetWorkflow == null)
        {
            var all = await cosmo.GetWorkflowsAsync(targetRepo.AccountId);
            targetWorkflow = all.FirstOrDefault(w =>
                (w.RepositoryId == null || w.RepositoryId == targetRepo.id) &&
                string.Equals(w.Name, workflowRef, StringComparison.OrdinalIgnoreCase));
        }
        if (targetWorkflow == null)
        {
            result.Success = false; result.Error = $"Workflow '{workflowRef}' not found in {ownerSlug}."; return;
        }

        // Parse `inputs:` -> dictionary, with merge-field expansion against the current context.
        Dictionary<string, string>? inputs = null;
        var rawInputs = WorkflowMergeService.Render(step.DispatchInputs ?? "", execution.Context);
        if (!string.IsNullOrWhiteSpace(rawInputs))
        {
            inputs = new();
            foreach (var pair in rawInputs.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                inputs[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
            }
        }

        var actorId = execution.Context.GetValueOrDefault("actor.id", "system");
        var actorName = execution.Context.GetValueOrDefault("actor.name", "Workflow Dispatcher");
        var newExec = await new WorkflowService(cosmo).RunNowAsync(
            targetWorkflow, targetRepo, actorId, actorName, inputs: inputs);

        result.Success = true;
        result.RenderedBody = $"Dispatched {ownerSlug}/{targetWorkflow.Name} (execution {newExec.id})";
    }

    private static string EscapeArg(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.IndexOfAny(new[] { ' ', '"', '\\' }) < 0) return value;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>
    /// Common subprocess runner used by AcrBuild and NugetPush. Reuses the same shell-arg
    /// strategy as ExecuteRunScript so the command stays a single argv element on Linux.
    /// </summary>
    private async Task RunShellCommandAsync(string command, string? workspaceSubdir, int timeoutSeconds,
        WorkflowExecution execution, WorkflowStepResult stepResult, string successMessage)
    {
        var workspace = EnsureWorkspace(execution);
        var workDir = workspace;
        if (!string.IsNullOrWhiteSpace(workspaceSubdir))
        {
            workDir = Path.Combine(workspace, workspaceSubdir.Trim('/').Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(workDir))
            {
                stepResult.Success = false;
                stepResult.Error = $"Working directory '{workspaceSubdir}' does not exist.";
                return;
            }
        }

        var isWindows = OperatingSystem.IsWindows();
        var shell = isWindows ? "cmd.exe" : "/bin/bash";
        var shellFlag = isWindows ? "/c" : "-c";

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = shell,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add(shellFlag);
        process.StartInfo.ArgumentList.Add(command);

        foreach (var (key, value) in execution.Context)
        {
            if (key.StartsWith("secrets."))
                process.StartInfo.Environment[key.Replace("secrets.", "SECRET_")] = value;
            else if (key.StartsWith("env."))
                process.StartInfo.Environment[key.Replace("env.", "")] = value;
        }

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Min(timeoutSeconds, 1800)));
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            stepResult.Success = false;
            stepResult.Error = $"Command timed out after {timeoutSeconds}s";
            stepResult.ExitCode = -1;
            stepResult.ScriptOutput = Truncate(output.ToString(), 8000);
            return;
        }

        stepResult.ExitCode = process.ExitCode;
        stepResult.ScriptOutput = Truncate(output.ToString(), 8000);
        stepResult.Success = process.ExitCode == 0;
        if (!stepResult.Success)
            stepResult.Error = $"Command exited with code {process.ExitCode}";
        else
            stepResult.RenderedBody = successMessage;
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
            else if (step.Matrix is { Count: > 0 })
            {
                // Fan out the cartesian product of every dimension. Each cell becomes its
                // own copy of the step with MatrixValues populated; the engine main loop
                // overlays those into the context as matrix.<key> for the duration of the
                // cell's run.
                var dims = step.Matrix.Where(kv => kv.Value is { Count: > 0 }).ToList();
                if (dims.Count == 0) { flat.Add(step); continue; }

                IEnumerable<Dictionary<string, string>> seed = new[] { new Dictionary<string, string>() };
                foreach (var (dimName, dimValues) in dims)
                {
                    seed = seed.SelectMany(prefix =>
                        dimValues.Select(v =>
                        {
                            var next = new Dictionary<string, string>(prefix) { [dimName] = v };
                            return next;
                        }));
                }

                foreach (var values in seed)
                {
                    var clone = ClonePlainStep(step);
                    clone.Matrix = null; // already expanded
                    clone.MatrixValues = values;
                    var suffix = string.Join(", ", values.Select(kv => $"{kv.Key}={kv.Value}"));
                    if (!string.IsNullOrEmpty(step.Name))
                        clone.Name = $"{step.Name} ({suffix})";
                    flat.Add(clone);
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
    /// Shallow copy of a step intentionally leaving Matrix/Branches behind. Used for matrix
    /// fanout where each cell becomes its own concrete step.
    /// </summary>
    private static WorkflowStep ClonePlainStep(WorkflowStep src)
    {
        return new WorkflowStep
        {
            Order = src.Order,
            Name = src.Name,
            StepType = src.StepType,
            HttpUrl = src.HttpUrl, HttpMethod = src.HttpMethod, HttpBody = src.HttpBody,
            HttpContentType = src.HttpContentType, HttpHeaders = src.HttpHeaders,
            LabelName = src.LabelName,
            CommentBody = src.CommentBody,
            RequiredApprovals = src.RequiredApprovals,
            WaitDays = src.WaitDays, WaitHours = src.WaitHours, WaitMinutes = src.WaitMinutes,
            ConditionExpression = src.ConditionExpression,
            AzureAppName = src.AzureAppName, AzureWorkDir = src.AzureWorkDir,
            AzureDeployPath = src.AzureDeployPath, AzureUsernameSecret = src.AzureUsernameSecret,
            AzurePasswordSecret = src.AzurePasswordSecret, AzureScmHost = src.AzureScmHost,
            CheckoutRepo = src.CheckoutRepo, CheckoutBranch = src.CheckoutBranch, CheckoutPath = src.CheckoutPath,
            ScriptCommand = src.ScriptCommand, ScriptWorkDir = src.ScriptWorkDir, ScriptTimeoutSeconds = src.ScriptTimeoutSeconds,
            EmailTo = src.EmailTo, EmailSubject = src.EmailSubject, EmailBody = src.EmailBody,
            ImportUrl = src.ImportUrl,
            MinionInstructions = src.MinionInstructions, MinionInstructionsFile = src.MinionInstructionsFile,
            MinionWorkingDirectory = src.MinionWorkingDirectory, MinionModel = src.MinionModel,
            MinionContextSize = src.MinionContextSize, MinionMaxTokens = src.MinionMaxTokens,
            MinionMaxIterations = src.MinionMaxIterations, MinionRole = src.MinionRole,
            MinionKvQuant = src.MinionKvQuant, MinionJsonOutput = src.MinionJsonOutput,
            MinionGrammar = src.MinionGrammar, MinionTimeoutSeconds = src.MinionTimeoutSeconds,
            MinionOrcAddress = src.MinionOrcAddress,
            AcrRegistry = src.AcrRegistry, AcrImage = src.AcrImage, AcrDockerfile = src.AcrDockerfile,
            AcrContext = src.AcrContext, AcrBuildArgs = src.AcrBuildArgs,
            NugetPackagePath = src.NugetPackagePath, NugetSource = src.NugetSource,
            NugetApiKeySecret = src.NugetApiKeySecret, NugetSkipDuplicate = src.NugetSkipDuplicate,
            DispatchRepo = src.DispatchRepo, DispatchWorkflow = src.DispatchWorkflow, DispatchInputs = src.DispatchInputs
        };
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
