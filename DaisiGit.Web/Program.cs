using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
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
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped(sp => new SecretService(
    sp.GetRequiredService<DaisiGitCosmo>(),
    builder.Configuration["Daisi:SecretKey"] ?? "default-secret-key"));
builder.Services.AddSingleton<AvatarService>();

builder.Services.AddSingleton<EmailService>();

// Workflow services
builder.Services.AddScoped<GitEventService>();
builder.Services.AddScoped<WorkflowTriggerService>();
builder.Services.AddScoped<WorkflowEngine>();
builder.Services.AddScoped<WorkflowService>();

// Workflow dispatch (queue-based isolation)
var workflowQueueConnectionString = builder.Configuration["WorkflowQueue:ConnectionString"];
if (!string.IsNullOrEmpty(workflowQueueConnectionString))
{
    var queueClient = new QueueClient(
        workflowQueueConnectionString,
        "workflow-executions",
        new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
    builder.Services.AddSingleton(queueClient);
    builder.Services.AddScoped<WorkflowDispatchService>(sp =>
        new WorkflowDispatchService(
            sp.GetRequiredService<QueueClient>(),
            sp.GetRequiredService<ILogger<WorkflowDispatchService>>()));
}
else
{
    // No queue configured — dispatch service will fall back to in-process execution
    builder.Services.AddScoped<WorkflowDispatchService>(sp =>
        new WorkflowDispatchService(
            null,
            sp.GetRequiredService<ILogger<WorkflowDispatchService>>()));
}
builder.Services.AddHostedService<GitWorkflowBackgroundWorker>();

builder.Services.AddScoped<DaisiUserService>();
builder.Services.AddScoped<FileWriteService>();

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

        // HTTP Basic Auth (used by git clone/push) — API key in the password field
        if (string.IsNullOrEmpty(apiKey) && authHeader?.StartsWith("Basic ") == true)
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
                        apiKey = password;
                }
            }
            catch { }
        }
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

        if (!isPotentialPublicPage && !path.StartsWith("/welcome"))
        {
            var authService = context.RequestServices.GetRequiredService<Daisi.SDK.Web.Services.AuthService>();
            var isAuthenticated = false;
            try { isAuthenticated = await authService.IsAuthenticatedAsync(); } catch { }

            if (!isAuthenticated)
            {
                // Redirect to welcome page instead of SSO
                context.Response.Redirect("/welcome");
                return;
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

// CLI install and download endpoints (public, no auth required)
app.MapCliEndpoints();

// Avatar proxy (public, no auth required, cached 10 min)
app.MapGet("/api/git/avatars/{type}/{id}", async (HttpContext ctx, string type, string id, AvatarService avatarService) =>
{
    var result = await avatarService.DownloadAvatarAsync(type, id);
    if (result == null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=600";
    return Results.Stream(result.Value.Stream, result.Value.ContentType);
});

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
