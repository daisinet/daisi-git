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
                InjectStepOutputs(execution.Context, step, stepResult);
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
        finally
        {
            CleanupWorkspace(execution);
        }
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

        // Determine shell
        var isWindows = OperatingSystem.IsWindows();
        var shell = isWindows ? "cmd.exe" : "/bin/bash";
        var shellArg = isWindows ? $"/c {command}" : $"-c {command}";

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = shellArg,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

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

        var usernameKey = step.AzureUsernameSecret;
        var passwordKey = step.AzurePasswordSecret;
        if (string.IsNullOrEmpty(usernameKey) || string.IsNullOrEmpty(passwordKey))
        {
            result.Success = false;
            result.Error = "Deploy username and password secrets must be configured";
            return;
        }

        var username = execution.Context.GetValueOrDefault($"secrets.{usernameKey}", "");
        var password = execution.Context.GetValueOrDefault($"secrets.{passwordKey}", "");
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            result.Success = false;
            result.Error = $"Could not resolve secrets '{usernameKey}' and/or '{passwordKey}'. Add them in Settings > Secrets.";
            return;
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

        // Deploy via Kudu ZipDeploy API
        var kuduUrl = $"https://{appName}.scm.azurewebsites.net/api/zipdeploy";
        var client = httpClientFactory.CreateClient("WorkflowHttp");
        client.Timeout = TimeSpan.FromMinutes(5);

        using var request = new HttpRequestMessage(HttpMethod.Post, kuduUrl);
        var authBytes = Encoding.ASCII.GetBytes($"{username}:{password}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
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
