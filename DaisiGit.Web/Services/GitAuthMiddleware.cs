using System.Text;
using DaisiGit.Core.Enums;
using DaisiGit.Data;
using DaisiGit.Services;

namespace DaisiGit.Web.Services;

/// <summary>
/// Middleware that handles HTTP Basic auth for git smart protocol endpoints.
/// Maps credentials to a Daisi identity (accountId, userId, userName).
/// Anonymous read access is allowed when the target repository is public; all
/// other paths (private/internal repos, push operations, unknown repos) require
/// Basic auth with a valid PAT and respond with 401 + WWW-Authenticate so git
/// clients prompt for credentials.
/// </summary>
public class GitAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Only intercept git smart protocol paths (*.git/)
        var path = context.Request.Path.Value ?? "";
        if (!path.Contains(".git/"))
        {
            await next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic "))
        {
            // No Basic auth — allow only anonymous read of a public repository.
            // Push/receive-pack, private/internal repos, and unknown repos all
            // get challenged so the client prompts for credentials.
            if (await IsAnonymousReadOfPublicRepoAsync(context, path))
            {
                SetAnonymousIdentity(context);
                await next(context);
                return;
            }

            ChallengeForCredentials(context);
            return;
        }

        try
        {
            var encoded = authHeader["Basic ".Length..];
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var parts = decoded.Split(':', 2);
            if (parts.Length != 2)
            {
                context.Response.StatusCode = 401;
                return;
            }

            var username = parts[0];
            var token = parts[1];

            context.Items["git-username"] = username;
            context.Items["git-token"] = token;

            // Check if either field is a PAT (dg_ prefix) and validate via ApiKeyService.
            // Git clients send Basic auth as username:password — the PAT can be in either field.
            var rawToken = token.StartsWith("dg_") ? token : username.StartsWith("dg_") ? username : null;

            if (rawToken != null)
            {
                var keyService = context.RequestServices.GetRequiredService<ApiKeyService>();
                var apiKey = await keyService.ValidateTokenAsync(rawToken);
                if (apiKey == null)
                {
                    ChallengeForCredentials(context);
                    return;
                }

                context.Items["accountId"] = apiKey.AccountId;
                context.Items["userId"] = apiKey.UserId;
                context.Items["userName"] = apiKey.UserName;
            }
            else
            {
                // Non-PAT credentials (e.g. Daisi username + client key) — fall through
                // with username-based identity for now.
                context.Items["accountId"] = "";
                context.Items["userId"] = username;
                context.Items["userName"] = username;
            }
        }
        catch
        {
            context.Response.StatusCode = 401;
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Returns true when the request is a read-only smart-protocol op against an
    /// existing public repository. Only this combination may proceed without auth.
    /// </summary>
    private static async Task<bool> IsAnonymousReadOfPublicRepoAsync(HttpContext context, string path)
    {
        if (!TryParseRepoSlug(path, out var owner, out var repoSlug))
            return false;

        if (!IsReadOperation(path, context.Request.Query))
            return false;

        var repoService = context.RequestServices.GetRequiredService<RepositoryService>();
        var repo = await repoService.GetRepositoryBySlugAsync(owner, repoSlug);
        return repo is { Visibility: GitRepoVisibility.Public };
    }

    /// <summary>
    /// Parses /{owner}/{repo}.git/... into owner and repo (without the .git suffix).
    /// Returns false if the path doesn't match this shape.
    /// </summary>
    private static bool TryParseRepoSlug(string path, out string owner, out string repo)
    {
        owner = "";
        repo = "";
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3) return false;
        if (!segments[1].EndsWith(".git", StringComparison.Ordinal)) return false;
        owner = segments[0];
        repo = segments[1][..^".git".Length];
        return owner.Length > 0 && repo.Length > 0;
    }

    /// <summary>
    /// A request is read-only if it targets git-upload-pack — either the
    /// info/refs advertisement (?service=git-upload-pack) or the upload-pack POST.
    /// receive-pack (push) is never anonymous.
    /// </summary>
    private static bool IsReadOperation(string path, IQueryCollection query)
    {
        if (path.EndsWith("/info/refs", StringComparison.Ordinal))
            return string.Equals(query["service"], "git-upload-pack", StringComparison.Ordinal);

        return path.EndsWith("/git-upload-pack", StringComparison.Ordinal);
    }

    private static void SetAnonymousIdentity(HttpContext context)
    {
        context.Items["accountId"] = "";
        context.Items["userId"] = "";
        context.Items["userName"] = "";
    }

    private static void ChallengeForCredentials(HttpContext context)
    {
        context.Response.StatusCode = 401;
        context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Daisi Git\"";
    }
}
