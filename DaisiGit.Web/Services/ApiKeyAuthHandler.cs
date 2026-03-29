using System.Security.Claims;
using System.Text.Encodings.Web;
using DaisiGit.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DaisiGit.Web.Services;

/// <summary>
/// Authentication handler for API key (personal access token) authentication.
/// Accepts tokens via X-Api-Key header or Authorization: Bearer header.
/// </summary>
public class ApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceProvider serviceProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Try X-Api-Key header
        var token = Request.Headers["X-Api-Key"].FirstOrDefault();

        // Try Authorization: Bearer
        if (string.IsNullOrEmpty(token))
        {
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (authHeader?.StartsWith("Bearer ") == true)
                token = authHeader["Bearer ".Length..];
        }

        // Try HTTP Basic Auth (used by git clone/push)
        // Git sends the API key as the password in Basic auth
        if (string.IsNullOrEmpty(token))
        {
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (authHeader?.StartsWith("Basic ") == true)
            {
                try
                {
                    var decoded = System.Text.Encoding.UTF8.GetString(
                        Convert.FromBase64String(authHeader["Basic ".Length..]));
                    var colonIdx = decoded.IndexOf(':');
                    if (colonIdx >= 0)
                    {
                        var password = decoded[(colonIdx + 1)..];
                        if (password.StartsWith("dg_"))
                            token = password;
                    }
                }
                catch { }
            }
        }

        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        using var scope = serviceProvider.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var apiKey = await apiKeyService.ValidateTokenAsync(token);

        if (apiKey == null)
            return AuthenticateResult.Fail("Invalid or expired API key.");

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, apiKey.UserName),
            new Claim(ClaimTypes.Sid, apiKey.UserId),
            new Claim("accountId", apiKey.AccountId)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        // Set HttpContext.Items for endpoint handlers
        Context.Items["userId"] = apiKey.UserId;
        Context.Items["userName"] = apiKey.UserName;
        Context.Items["accountId"] = apiKey.AccountId;

        return AuthenticateResult.Success(ticket);
    }
}
