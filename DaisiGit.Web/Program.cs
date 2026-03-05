using System.Text.Json.Serialization;
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

// Drive adapter
builder.Services.AddScoped<IDriveAdapter, DaisiDriveAdapter>();

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
builder.Services.AddScoped<DaisiUserService>();

// JSON enum serialization for API endpoints
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.UseDaisiMiddleware();

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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

DaisiStaticSettings.LoadFromConfiguration(
    builder.Configuration.AsEnumerable().ToDictionary(
        keySelector: x => x.Key, elementSelector: x => x.Value));

app.Run();
