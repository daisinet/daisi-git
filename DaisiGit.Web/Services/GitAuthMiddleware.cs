using System.Text;
using DaisiGit.Data;
using DaisiGit.Services;

namespace DaisiGit.Web.Services;

/// <summary>
/// Middleware that handles HTTP Basic auth for git smart protocol endpoints.
/// Maps credentials to a Daisi identity (accountId, userId, userName).
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
            // Request credentials
            context.Response.StatusCode = 401;
            context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Daisi Git\"";
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
                    context.Response.StatusCode = 401;
                    context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Daisi Git\"";
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
}
