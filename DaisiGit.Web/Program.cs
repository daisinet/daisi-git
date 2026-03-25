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

app.UseDaisiMiddleware();

// Redirect unauthenticated users to SSO (skip static files, API, git endpoints, and SSO callbacks)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLower() ?? "";
    if (!path.StartsWith("/_") && !path.StartsWith("/api/") && !path.Contains(".git/")
        && !path.StartsWith("/sso/") && !path.StartsWith("/account/")
        && !Path.HasExtension(path))
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
