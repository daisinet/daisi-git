using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using DaisiGit.Data;
using DaisiGit.Services;
using DaisiGit.Web.Components;
using DaisiGit.Web.Endpoints;
using DaisiGit.Web.Services;
using Daisi.SDK.Models;
using Daisi.SDK.Web.Extensions;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddHttpClient();

// Daisi SSO authentication
builder.Services.AddDaisiForWeb()
                .AddDaisiMiddleware()
                .AddDaisiCookieKeyProvider();

// API key authentication (for CLI and external integrations)
builder.Services.AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
        DaisiGit.Web.Services.ApiKeyAuthHandler>("ApiKey", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes("ApiKey")
        .RequireAuthenticatedUser()
        .Build();
});

// Cosmos DB
builder.Services.AddSingleton<DaisiGitCosmo>(sp =>
    new DaisiGitCosmo(builder.Configuration));

// Storage adapters
builder.Services.AddScoped<IStorageAdapter, DaisiDriveAdapter>();
var azureBlobConnectionString = builder.Configuration["AzureBlob:ConnectionString"];
if (!string.IsNullOrEmpty(azureBlobConnectionString))
{
    builder.Services.AddSingleton(new BlobServiceClient(azureBlobConnectionString));
    builder.Services.AddScoped<IStorageAdapter, AzureBlobStorageAdapter>();
}
builder.Services.AddScoped<StorageAdapterFactory>();

// Git services
builder.Services.AddScoped<GitObjectStore>();
builder.Services.AddScoped<GitRefService>();
builder.Services.AddScoped<RepositoryService>();
builder.Services.AddScoped<BrowseService>();
builder.Services.AddScoped<PullRequestService>();
builder.Services.AddScoped<IssueService>();
builder.Services.AddScoped<CommentService>();
builder.Services.AddScoped<MergeService>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<AccountSettingsService>();
builder.Services.AddScoped<UserProfileService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddSingleton<AvatarService>();

// Workflow services
builder.Services.AddScoped<GitEventService>();
builder.Services.AddScoped<WorkflowTriggerService>();
builder.Services.AddScoped<WorkflowEngine>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddHostedService<GitWorkflowBackgroundWorker>();

builder.Services.AddScoped<DaisiUserService>();

// JSON enum serialization for API endpoints
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

DaisiStaticSettings.LoadFromConfiguration(
    builder.Configuration.AsEnumerable().ToDictionary(
        keySelector: x => x.Key, elementSelector: x => x.Value));

// Authenticate API key on all requests (even anonymous endpoints)
app.Use(async (context, next) =>
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (string.IsNullOrEmpty(apiKey))
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader?.StartsWith("Bearer dg_") == true)
            apiKey = authHeader["Bearer ".Length..];
    }

    if (!string.IsNullOrEmpty(apiKey) && apiKey.StartsWith("dg_"))
    {
        using var scope = app.Services.CreateScope();
        var keyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var key = await keyService.ValidateTokenAsync(apiKey);
        if (key != null)
        {
            context.Items["userId"] = key.UserId;
            context.Items["userName"] = key.UserName;
            context.Items["accountId"] = key.AccountId;

            var claims = new[] {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, key.UserName),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Sid, key.UserId),
                new System.Security.Claims.Claim("accountId", key.AccountId)
            };
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(claims, "ApiKey"));
        }
    }

    await next();
});

app.UseDaisiMiddleware();

// Redirect unauthenticated users to SSO (skip static files, API, git endpoints, SSO callbacks,
// and potential public repo/org pages which handle auth in the UI)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLower() ?? "";
    if (!path.StartsWith("/_") && !path.StartsWith("/api/") && !path.Contains(".git/")
        && !path.StartsWith("/sso/") && !path.StartsWith("/account/")
        && !path.StartsWith("/explore")
        && !Path.HasExtension(path))
    {
        // Allow potential public pages through without auth:
        // /{owner}/{repo}/... paths (2+ segments that aren't system routes)
        var segments = path.Trim('/').Split('/');
        var firstSegment = segments.Length > 0 ? segments[0] : "";
        var isSystemRoute = firstSegment is "" or "settings" or "repositories" or "organizations" or "new";
        var isPotentialPublicPage = segments.Length >= 2 && !isSystemRoute;

        if (!isPotentialPublicPage)
        {
            var authService = context.RequestServices.GetRequiredService<Daisi.SDK.Web.Services.AuthService>();
            var isAuthenticated = false;
            try { isAuthenticated = await authService.IsAuthenticatedAsync(); } catch { }

            if (!isAuthenticated)
            {
                var appUrl = DaisiStaticSettings.SsoAppUrl;
                var authorityUrl = DaisiStaticSettings.SsoAuthorityUrl;
                if (!string.IsNullOrEmpty(appUrl) && !string.IsNullOrEmpty(authorityUrl))
                {
                    var callbackUrl = Uri.EscapeDataString($"{appUrl}/sso/callback");
                    var origin = Uri.EscapeDataString(appUrl);
                    context.Response.Redirect($"{authorityUrl}/sso/authorize?returnUrl={callbackUrl}&origin={origin}");
                    return;
                }
            }
        }
    }
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

// Git smart protocol endpoints (HTTP Basic auth)
app.UseMiddleware<GitAuthMiddleware>();
app.MapGitSmartProtocolEndpoints();

// REST API endpoints
app.MapDaisiGitApiEndpoints();
app.MapWorkflowApiEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Populate reserved names from mapped routes
var routeSegments = app.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>()
    .Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
    .Select(e => e.RoutePattern.RawText?.Split('/').FirstOrDefault(s => !string.IsNullOrEmpty(s) && !s.StartsWith('{')))
    .Where(s => s != null)
    .Cast<string>()
    .Distinct();
DaisiGit.Core.ReservedNames.RegisterRouteSegments(routeSegments);

app.Run();
