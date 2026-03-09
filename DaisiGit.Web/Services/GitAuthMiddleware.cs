using System.Text;
using DaisiGit.Data;

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

            // For now, store credentials in HttpContext items for endpoint handlers.
            // A full implementation would validate the token against Daisi auth.
            // Username can be "token" with a personal access token, or a Daisi username with client key.
            context.Items["git-username"] = username;
            context.Items["git-token"] = token;

            // TODO: Validate token via Daisi auth service and resolve accountId/userId
            // For development, use the token as both accountId and userId
            context.Items["accountId"] = token;
            context.Items["userId"] = username;
            context.Items["userName"] = username;
        }
        catch
        {
            context.Response.StatusCode = 401;
            return;
        }

        await next(context);
    }
}
