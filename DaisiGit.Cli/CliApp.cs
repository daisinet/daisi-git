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
                    Console.Write("Session token: ");
                    token = Console.ReadLine()?.Trim();
                }

                if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(token))
                {
                    Console.Error.WriteLine("Server URL and session token are required.");
                    Environment.ExitCode = 1;
                    return;
                }

                // Validate by trying to list repos
                try
                {
                    using var client = new DaisiGitClient(server, token);
                    await client.ListRepositoriesAsync();
                }
                catch
                {
                    Console.Error.WriteLine("Failed to authenticate. Check your server URL and token.");
                    Environment.ExitCode = 1;
                    return;
                }

                var config = new CliConfig { ServerUrl = server, SessionToken = token };
                config.Save();
                Console.WriteLine($"Authenticated to {server}");
                break;

            case "logout":
                var cfg = CliConfig.Load();
                cfg.SessionToken = null;
                cfg.Save();
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

            default:
                Console.WriteLine("Usage: dg repo <list|create|view|fork>");
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
        var serverUrl = config.ServerUrl;
        if (string.IsNullOrEmpty(serverUrl))
        {
            Console.Error.WriteLine("Not authenticated. Run: dg auth login");
            Environment.ExitCode = 1;
            return;
        }

        var cloneUrl = $"{serverUrl.TrimEnd('/')}/{target}.git";
        var dir = GetArg(2);

        var gitArgs = string.IsNullOrEmpty(dir)
            ? $"clone {cloneUrl}"
            : $"clone {cloneUrl} {dir}";

        Console.WriteLine($"Cloning {target}...");
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = gitArgs,
            UseShellExecute = false
        });
        if (process != null)
        {
            await process.WaitForExitAsync();
            Environment.ExitCode = process.ExitCode;
        }
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

    private static void PrintUsage()
    {
        Console.WriteLine("""
            dg - DaisiGit CLI

            Usage: dg <command> [options]

            Commands:
              auth login       Authenticate with a DaisiGit server
              auth logout      Clear saved credentials
              auth status      Show authentication status

              repo list        List your repositories
              repo create      Create a new repository
              repo view        View repository details
              repo fork        Fork a repository

              clone            Clone a repository (wraps git clone)
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
              --token, -t      Session token (for auth login)

            Examples:
              dg auth login --server https://git.daisi.ai --token <token>
              dg repo list
              dg clone myorg/myrepo
              dg issue create "Fix login bug" --desc "Steps to reproduce..."
              dg pr create "Add feature" --source feature-branch
              dg pr merge 42 --strategy squash
            """);
    }
}
