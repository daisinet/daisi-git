using System.Net.Http.Json;
using System.Text.Json;
using DaisiGit.SDK;

namespace DaisiGit.Cli;

/// <summary>
/// Main CLI application dispatcher.
/// Usage: dg <command> [subcommand] [args] [--flags]
/// </summary>
public class CliApp(string[] args)
{
    public async Task RunAsync()
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        var command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "auth":
                await HandleAuth();
                break;
            case "repo":
                await HandleRepo();
                break;
            case "clone":
                await HandleClone();
                break;
            case "push":
                await HandlePush();
                break;
            case "pull":
                await HandlePull();
                break;
            case "credential-fill":
                HandleCredentialFill();
                break;
            case "issue":
                await HandleIssue();
                break;
            case "pr":
                await HandlePr();
                break;
            case "browse":
                HandleBrowse();
                break;
            case "version":
            case "--version":
                Console.WriteLine("dg 0.1.0");
                break;
            case "help":
            case "--help":
            case "-h":
                PrintUsage();
                break;
            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                PrintUsage();
                Environment.ExitCode = 1;
                break;
        }
    }

    // ── Auth ──

    private async Task HandleAuth()
    {
        var sub = GetSubcommand();
        switch (sub)
        {
            case "login":
                var server = GetFlag("--server") ?? GetFlag("-s");
                var token = GetFlag("--token") ?? GetFlag("-t");

                if (string.IsNullOrEmpty(server))
                {
                    Console.Write("Server URL: ");
                    server = Console.ReadLine()?.Trim();
                }
                if (string.IsNullOrEmpty(token))
                {
                    Console.Write("Personal access token: ");
                    token = Console.ReadLine()?.Trim();
                }

                if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(token))
                {
                    Console.Error.WriteLine("Server URL and personal access token are required.");
                    Environment.ExitCode = 1;
                    return;
                }

                // Validate by trying to list repos
                try
                {
                    using var client = new DaisiGitClient(server, token);
                    await client.ListRepositoriesAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to authenticate: {ex.Message}");
                    Environment.ExitCode = 1;
                    return;
                }

                var config = new CliConfig { ServerUrl = server, SessionToken = token };
                config.Save();
                ConfigureGitCredentialHelper(server);
                Console.WriteLine($"Authenticated to {server}");
                Console.WriteLine("Git credential helper configured — git push/pull/clone will use your token automatically.");
                break;

            case "logout":
                var cfg = CliConfig.Load();
                var logoutServer = cfg.ServerUrl;
                cfg.SessionToken = null;
                cfg.Save();
                if (!string.IsNullOrEmpty(logoutServer))
                    RemoveGitCredentialHelper(logoutServer);
                Console.WriteLine("Logged out.");
                break;

            case "status":
                var status = CliConfig.Load();
                if (status.IsAuthenticated)
                    Console.WriteLine($"Authenticated to {status.ServerUrl}");
                else
                    Console.WriteLine("Not authenticated. Run: dg auth login");
                break;

            default:
                Console.WriteLine("Usage: dg auth <login|logout|status>");
                break;
        }
    }

    /// <summary>
    /// Configures git to use the dg credential helper for the given server URL.
    /// This makes native git push/pull/clone work with the stored PAT.
    /// </summary>
    private static void ConfigureGitCredentialHelper(string serverUrl)
    {
        var uri = new Uri(serverUrl);
        var host = uri.Host;

        // Write the credential helper script
        var helperDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisigit");
        Directory.CreateDirectory(helperDir);

        // Write a helper script that reads the token from config and returns it in git credential format
        var helperPath = Path.Combine(helperDir, "git-credential-daisigit");

        if (OperatingSystem.IsWindows())
        {
            helperPath += ".bat";
            // Batch script that invokes dg credential-fill
            var exePath = Environment.ProcessPath ?? "dg";
            File.WriteAllText(helperPath, $"""
                @echo off
                "{exePath}" credential-fill
                """);
        }
        else
        {
            File.WriteAllText(helperPath, $"""
                #!/bin/sh
                exec "{Environment.ProcessPath ?? "dg"}" credential-fill
                """);
            // Make executable
            RunGitProcess("", $"chmod +x \"{helperPath}\"", useShell: true);
        }

        // Configure git to use this helper for the server's host.
        // Git requires forward slashes in paths, even on Windows.
        var gitHelperPath = helperPath.Replace('\\', '/');
        var credentialKey = $"credential.https://{host}.helper";

        // Remove any previous daisigit helper for this host, then add the new one
        RunGitProcess("", $"config --global --unset-all {credentialKey}");
        RunGitProcess("", $"config --global {credentialKey} \"{gitHelperPath}\"");
    }

    /// <summary>
    /// Removes the git credential helper for the given server URL.
    /// </summary>
    private static void RemoveGitCredentialHelper(string serverUrl)
    {
        try
        {
            var uri = new Uri(serverUrl);
            var credentialKey = $"credential.https://{uri.Host}.helper";
            RunGitProcess("", $"config --global --unset-all {credentialKey}");
        }
        catch { }
    }

    // ── Repo ──

    private async Task HandleRepo()
    {
        var sub = GetSubcommand();
        using var client = RequireAuth();
        if (client == null) return;

        switch (sub)
        {
            case "list" or "ls":
                var repos = await client.ListRepositoriesAsync();
                if (repos.Count == 0)
                {
                    Console.WriteLine("No repositories found.");
                    return;
                }
                Console.WriteLine($"{"NAME",-40} {"VISIBILITY",-12} {"STARS",-6} {"FORKS"}");
                foreach (var r in repos)
                {
                    Console.WriteLine($"{r.OwnerName + "/" + r.Slug,-40} {r.Visibility,-12} {r.StarCount,-6} {r.ForkCount}");
                }
                break;

            case "create":
                var name = GetArg(2);
                if (string.IsNullOrEmpty(name))
                {
                    Console.Error.WriteLine("Usage: dg repo create <name> [--desc \"description\"] [--public|--private]");
                    Environment.ExitCode = 1;
                    return;
                }
                var desc = GetFlag("--desc") ?? GetFlag("-d");
                var visibility = HasFlag("--public") ? DaisiGit.Core.Enums.GitRepoVisibility.Public
                    : HasFlag("--internal") ? DaisiGit.Core.Enums.GitRepoVisibility.Internal
                    : DaisiGit.Core.Enums.GitRepoVisibility.Private;

                var created = await client.CreateRepositoryAsync(name, desc, visibility);
                Console.WriteLine($"Created {created.OwnerName}/{created.Slug}");
                break;

            case "view":
                var (owner, slug) = ParseOwnerSlug(GetArg(2));
                if (owner == null)
                {
                    Console.Error.WriteLine("Usage: dg repo view <owner/repo>");
                    Environment.ExitCode = 1;
                    return;
                }
                var repo = await client.GetRepositoryAsync(owner, slug!);
                if (repo == null)
                {
                    Console.Error.WriteLine("Repository not found.");
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine($"Name:        {repo.OwnerName}/{repo.Slug}");
                Console.WriteLine($"Description: {repo.Description ?? "(none)"}");
                Console.WriteLine($"Visibility:  {repo.Visibility}");
                Console.WriteLine($"Branch:      {repo.DefaultBranch}");
                Console.WriteLine($"Stars:       {repo.StarCount}");
                Console.WriteLine($"Forks:       {repo.ForkCount}");
                Console.WriteLine($"Created:     {repo.CreatedUtc:yyyy-MM-dd}");
                break;

            case "fork":
                var (fOwner, fSlug) = ParseOwnerSlug(GetArg(2));
                if (fOwner == null)
                {
                    Console.Error.WriteLine("Usage: dg repo fork <owner/repo>");
                    Environment.ExitCode = 1;
                    return;
                }
                var forked = await client.ForkRepositoryAsync(fOwner, fSlug!);
                Console.WriteLine($"Forked to {forked.OwnerName}/{forked.Slug}");
                break;

            case "import":
                var importUrl = GetArg(2);
                if (string.IsNullOrEmpty(importUrl))
                {
                    Console.Error.WriteLine("Usage: dg repo import <url> [--name name] [--public|--private]");
                    Environment.ExitCode = 1;
                    return;
                }
                var importName = GetFlag("--name") ?? GetFlag("-n");
                var importVisibility = HasFlag("--public") ? DaisiGit.Core.Enums.GitRepoVisibility.Public
                    : DaisiGit.Core.Enums.GitRepoVisibility.Private;

                Console.WriteLine($"Importing from {importUrl}...");
                try
                {
                    var config = CliConfig.Load();
                    var importHttp = new HttpClient(new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                    })
                    { BaseAddress = new Uri(config.ServerUrl!.TrimEnd('/') + "/") };
                    importHttp.DefaultRequestHeaders.Add("X-Api-Key", config.SessionToken);

                    var importResp = await importHttp.PostAsJsonAsync("api/git/repos/import",
                        new { url = importUrl, name = importName, visibility = importVisibility });
                    importResp.EnsureSuccessStatusCode();
                    var importResult = await importResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    Console.WriteLine($"Imported to {importResult.GetProperty("ownerName")}/{importResult.GetProperty("slug")}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Import failed: {ex.Message}");
                    Environment.ExitCode = 1;
                }
                break;

            default:
                Console.WriteLine("Usage: dg repo <list|create|view|fork|import>");
                break;
        }
    }

    // ── Clone ──

    private async Task HandleClone()
    {
        var target = GetArg(1);
        if (string.IsNullOrEmpty(target))
        {
            Console.Error.WriteLine("Usage: dg clone <owner/repo> [directory]");
            Environment.ExitCode = 1;
            return;
        }

        var config = CliConfig.Load();
        if (!config.IsAuthenticated)
        {
            Console.Error.WriteLine("Not authenticated. Run: dg auth login");
            Environment.ExitCode = 1;
            return;
        }

        var cloneUrl = BuildAuthUrl(config, target);
        var dir = GetArg(2);

        var gitArgs = string.IsNullOrEmpty(dir)
            ? $"clone {cloneUrl}"
            : $"clone {cloneUrl} {dir}";

        Console.WriteLine($"Cloning {target}...");
        var exitCode = await RunGitAsync(gitArgs);
        Environment.ExitCode = exitCode;
    }

    // ── Push ──

    private async Task HandlePush()
    {
        var config = CliConfig.Load();
        if (!config.IsAuthenticated)
        {
            Console.Error.WriteLine("Not authenticated. Run: dg auth login");
            Environment.ExitCode = 1;
            return;
        }

        // Pass through any extra args (e.g. --force, origin main, etc.)
        var extraArgs = args.Length > 1 ? string.Join(" ", args[1..]) : "";
        var gitArgs = $"push {extraArgs}".Trim();

        var exitCode = await RunGitAsync(gitArgs);
        Environment.ExitCode = exitCode;
    }

    // ── Pull ──

    private async Task HandlePull()
    {
        var config = CliConfig.Load();
        if (!config.IsAuthenticated)
        {
            Console.Error.WriteLine("Not authenticated. Run: dg auth login");
            Environment.ExitCode = 1;
            return;
        }

        var extraArgs = args.Length > 1 ? string.Join(" ", args[1..]) : "";
        var gitArgs = $"pull {extraArgs}".Trim();

        var exitCode = await RunGitAsync(gitArgs);
        Environment.ExitCode = exitCode;
    }

    // ── Credential Fill (internal, called by git credential helper) ──

    private static void HandleCredentialFill()
    {
        // Git credential helper protocol: read key=value pairs from stdin,
        // write back protocol/host/username/password to stdout.
        var input = new Dictionary<string, string>();
        string? line;
        while ((line = Console.ReadLine()) != null && line.Length > 0)
        {
            var eq = line.IndexOf('=');
            if (eq > 0)
                input[line[..eq]] = line[(eq + 1)..];
        }

        var config = CliConfig.Load();
        if (!config.IsAuthenticated || string.IsNullOrEmpty(config.ServerUrl))
            return;

        var serverUri = new Uri(config.ServerUrl);

        // Only respond if the request matches our server
        if (input.TryGetValue("host", out var host) && !host.Equals(serverUri.Host, StringComparison.OrdinalIgnoreCase))
            return;

        Console.WriteLine($"protocol={serverUri.Scheme}");
        Console.WriteLine($"host={serverUri.Host}");
        Console.WriteLine("username=token");
        Console.WriteLine($"password={config.SessionToken}");
        Console.WriteLine();
    }

    // ── Issue ──

    private async Task HandleIssue()
    {
        var sub = GetSubcommand();
        using var client = RequireAuth();
        if (client == null) return;

        var (owner, slug) = ResolveRepo();
        if (owner == null)
        {
            Console.Error.WriteLine("Not in a repository. Specify with --repo owner/repo or run from a cloned repo.");
            Environment.ExitCode = 1;
            return;
        }

        switch (sub)
        {
            case "list" or "ls":
                var statusFilter = GetFlag("--status") ?? "open";
                var issues = await client.ListIssuesAsync(owner, slug!, statusFilter);
                if (issues.Count == 0)
                {
                    Console.WriteLine("No issues found.");
                    return;
                }
                Console.WriteLine($"{"#",-6} {"STATUS",-8} {"TITLE",-50} {"AUTHOR"}");
                foreach (var i in issues)
                {
                    Console.WriteLine($"#{i.Number,-5} {i.Status,-8} {Truncate(i.Title, 50),-50} {i.AuthorName}");
                }
                break;

            case "create":
                var title = GetArg(2);
                if (string.IsNullOrEmpty(title))
                {
                    Console.Error.WriteLine("Usage: dg issue create \"title\" [--desc \"description\"]");
                    Environment.ExitCode = 1;
                    return;
                }
                var issueDesc = GetFlag("--desc") ?? GetFlag("-d");
                var issue = await client.CreateIssueAsync(owner, slug!, title, issueDesc);
                Console.WriteLine($"Created issue #{issue.Number}: {issue.Title}");
                break;

            case "close":
                var closeNum = int.TryParse(GetArg(2), out var cn) ? cn : 0;
                if (closeNum == 0)
                {
                    Console.Error.WriteLine("Usage: dg issue close <number>");
                    Environment.ExitCode = 1;
                    return;
                }
                await client.CloseIssueAsync(owner, slug!, closeNum);
                Console.WriteLine($"Closed issue #{closeNum}");
                break;

            case "view":
                var viewNum = int.TryParse(GetArg(2), out var vn) ? vn : 0;
                if (viewNum == 0)
                {
                    Console.Error.WriteLine("Usage: dg issue view <number>");
                    Environment.ExitCode = 1;
                    return;
                }
                var viewIssue = await client.GetIssueAsync(owner, slug!, viewNum);
                if (viewIssue == null)
                {
                    Console.Error.WriteLine("Issue not found.");
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine($"#{viewIssue.Number} {viewIssue.Title}");
                Console.WriteLine($"Status: {viewIssue.Status}  Author: {viewIssue.AuthorName}  Created: {viewIssue.CreatedUtc:yyyy-MM-dd}");
                if (!string.IsNullOrEmpty(viewIssue.Description))
                    Console.WriteLine($"\n{viewIssue.Description}");
                break;

            default:
                Console.WriteLine("Usage: dg issue <list|create|close|view>");
                break;
        }
    }

    // ── PR ──

    private async Task HandlePr()
    {
        var sub = GetSubcommand();
        using var client = RequireAuth();
        if (client == null) return;

        var (owner, slug) = ResolveRepo();
        if (owner == null)
        {
            Console.Error.WriteLine("Not in a repository. Specify with --repo owner/repo or run from a cloned repo.");
            Environment.ExitCode = 1;
            return;
        }

        switch (sub)
        {
            case "list" or "ls":
                var statusFilter = GetFlag("--status") ?? "open";
                var prs = await client.ListPullRequestsAsync(owner, slug!, statusFilter);
                if (prs.Count == 0)
                {
                    Console.WriteLine("No pull requests found.");
                    return;
                }
                Console.WriteLine($"{"#",-6} {"STATUS",-8} {"TITLE",-45} {"SOURCE",-15} {"TARGET"}");
                foreach (var p in prs)
                {
                    Console.WriteLine($"#{p.Number,-5} {p.Status,-8} {Truncate(p.Title, 45),-45} {p.SourceBranch,-15} {p.TargetBranch}");
                }
                break;

            case "create":
                var title = GetArg(2);
                var source = GetFlag("--source") ?? GetFlag("-s");
                var target = GetFlag("--target") ?? GetFlag("-t") ?? "main";
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(source))
                {
                    Console.Error.WriteLine("Usage: dg pr create \"title\" --source <branch> [--target main]");
                    Environment.ExitCode = 1;
                    return;
                }
                var prDesc = GetFlag("--desc") ?? GetFlag("-d");
                var pr = await client.CreatePullRequestAsync(owner, slug!, title, source, target, prDesc);
                Console.WriteLine($"Created PR #{pr.Number}: {pr.Title} ({pr.SourceBranch} -> {pr.TargetBranch})");
                break;

            case "merge":
                var mergeNum = int.TryParse(GetArg(2), out var mn) ? mn : 0;
                if (mergeNum == 0)
                {
                    Console.Error.WriteLine("Usage: dg pr merge <number> [--strategy merge|squash]");
                    Environment.ExitCode = 1;
                    return;
                }
                var strategy = GetFlag("--strategy") ?? "merge";
                var result = await client.MergePullRequestAsync(owner, slug!, mergeNum, strategy);
                if (result.Success)
                    Console.WriteLine($"Merged PR #{mergeNum} (commit: {result.MergeCommitSha?[..7]})");
                else
                    Console.Error.WriteLine($"Merge failed: {result.Error}");
                break;

            case "view":
                var viewNum = int.TryParse(GetArg(2), out var pvn) ? pvn : 0;
                if (viewNum == 0)
                {
                    Console.Error.WriteLine("Usage: dg pr view <number>");
                    Environment.ExitCode = 1;
                    return;
                }
                var viewPr = await client.GetPullRequestAsync(owner, slug!, viewNum);
                if (viewPr == null)
                {
                    Console.Error.WriteLine("Pull request not found.");
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine($"#{viewPr.Number} {viewPr.Title}");
                Console.WriteLine($"Status: {viewPr.Status}  {viewPr.SourceBranch} -> {viewPr.TargetBranch}");
                Console.WriteLine($"Author: {viewPr.AuthorName}  Created: {viewPr.CreatedUtc:yyyy-MM-dd}");
                if (!string.IsNullOrEmpty(viewPr.Description))
                    Console.WriteLine($"\n{viewPr.Description}");
                break;

            default:
                Console.WriteLine("Usage: dg pr <list|create|merge|view>");
                break;
        }
    }

    // ── Browse ──

    private void HandleBrowse()
    {
        var config = CliConfig.Load();
        var (owner, slug) = ResolveRepo();
        if (config.ServerUrl != null && owner != null)
        {
            var url = $"{config.ServerUrl.TrimEnd('/')}/{owner}/{slug}";
            Console.WriteLine($"Opening {url}");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                Console.WriteLine(url);
            }
        }
        else
        {
            Console.Error.WriteLine("Not in a repository or not authenticated.");
            Environment.ExitCode = 1;
        }
    }

    // ── Helpers ──

    private DaisiGitClient? RequireAuth()
    {
        var config = CliConfig.Load();
        if (!config.IsAuthenticated)
        {
            Console.Error.WriteLine("Not authenticated. Run: dg auth login");
            Environment.ExitCode = 1;
            return null;
        }
        return new DaisiGitClient(config.ServerUrl!, config.SessionToken!);
    }

    private (string? owner, string? slug) ResolveRepo()
    {
        // Check --repo flag first
        var repoFlag = GetFlag("--repo") ?? GetFlag("-r");
        if (!string.IsNullOrEmpty(repoFlag))
            return ParseOwnerSlug(repoFlag);

        // Try to detect from git remote
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "remote get-url origin",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (process != null)
            {
                var url = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode == 0 && !string.IsNullOrEmpty(url))
                {
                    // Parse owner/repo from URL like https://server/owner/repo.git
                    var uri = new Uri(url.Replace(".git", ""));
                    var pathParts = uri.AbsolutePath.Trim('/').Split('/');
                    if (pathParts.Length >= 2)
                        return (pathParts[^2], pathParts[^1]);
                }
            }
        }
        catch { }

        return (null, null);
    }

    private static (string? owner, string? slug) ParseOwnerSlug(string? input)
    {
        if (string.IsNullOrEmpty(input)) return (null, null);
        var parts = input.Split('/');
        return parts.Length >= 2 ? (parts[0], parts[1]) : (null, null);
    }

    private string? GetSubcommand() => args.Length > 1 ? args[1].ToLowerInvariant() : null;
    private string? GetArg(int index) => args.Length > index ? args[index] : null;

    private string? GetFlag(string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) return args[i + 1];
        return null;
    }

    private bool HasFlag(string flag) => args.Contains(flag);

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    /// <summary>
    /// Builds a clone URL with the PAT embedded for authenticated git operations.
    /// </summary>
    private static string BuildAuthUrl(CliConfig config, string ownerSlashRepo)
    {
        var uri = new Uri(config.ServerUrl!);
        return $"{uri.Scheme}://token:{config.SessionToken}@{uri.Host}{uri.AbsolutePath.TrimEnd('/')}/{ownerSlashRepo}.git";
    }

    /// <summary>
    /// Runs a git command, inheriting stdin/stdout/stderr so the user sees output.
    /// </summary>
    private static async Task<int> RunGitAsync(string arguments)
    {
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            UseShellExecute = false
        });
        if (process == null) return 1;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    /// <summary>
    /// Runs a git command synchronously (for config operations during setup).
    /// </summary>
    private static void RunGitProcess(string workingDir, string arguments, bool useShell = false)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = useShell ? (OperatingSystem.IsWindows() ? "cmd" : "sh") : "git",
                Arguments = useShell
                    ? (OperatingSystem.IsWindows() ? $"/c {arguments}" : $"-c \"{arguments}\"")
                    : arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (!string.IsNullOrEmpty(workingDir))
                psi.WorkingDirectory = workingDir;

            var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit();
        }
        catch { }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            dg - DaisiGit CLI

            Usage: dg <command> [options]

            Commands:
              auth login       Authenticate and configure git credentials
              auth logout      Clear saved credentials and git config
              auth status      Show authentication status

              repo list        List your repositories
              repo create      Create a new repository
              repo view        View repository details
              repo fork        Fork a repository
              repo import      Import a repository from a URL

              clone            Clone a repository
              push             Push commits to remote
              pull             Pull latest changes from remote
              browse           Open repository in browser

              issue list       List issues
              issue create     Create an issue
              issue close      Close an issue
              issue view       View issue details

              pr list          List pull requests
              pr create        Create a pull request
              pr merge         Merge a pull request
              pr view          View pull request details

              version          Show version
              help             Show this help

            Flags:
              --repo, -r       Specify repo as owner/slug (auto-detected from git remote)
              --server, -s     Server URL (for auth login)
              --token, -t      Personal access token (for auth login)

            Examples:
              dg auth login --server https://git.daisi.ai --token dg_YOUR_TOKEN
              dg repo list
              dg clone myorg/myrepo
              dg push
              dg pull
              dg issue create "Fix login bug" --desc "Steps to reproduce..."
              dg pr create "Add feature" --source feature-branch
              dg pr merge 42 --strategy squash
            """);
    }
}
