using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using DaisiGit.Data;
using DaisiGit.Services;
using DaisiGit.Worker;
using Daisi.SDK.Clients.V1.Orc;
using Daisi.SDK.Models;

var builder = Host.CreateApplicationBuilder(args);

// Cosmos DB
builder.Services.AddSingleton<DaisiGitCosmo>(sp =>
    new DaisiGitCosmo(builder.Configuration));

// Load Daisi static settings (ORC connection info, secret key)
DaisiStaticSettings.LoadFromConfiguration(
    builder.Configuration.AsEnumerable().ToDictionary(
        keySelector: x => x.Key, elementSelector: x => x.Value!));

// Storage adapters
builder.Services.AddScoped<DriveClientFactory>();
builder.Services.AddScoped<IStorageAdapter, DaisiDriveAdapter>();
var azureBlobConnectionString = builder.Configuration["AzureBlob:ConnectionString"];
if (!string.IsNullOrEmpty(azureBlobConnectionString))
{
    builder.Services.AddSingleton(new BlobServiceClient(azureBlobConnectionString));
    builder.Services.AddScoped<IStorageAdapter, AzureBlobStorageAdapter>();
}
builder.Services.AddScoped<StorageAdapterFactory>();

// Git services needed by WorkflowEngine
builder.Services.AddHttpClient();
builder.Services.AddScoped<GitObjectStore>();
builder.Services.AddScoped<GitRefService>();
builder.Services.AddScoped<RepositoryService>();
builder.Services.AddScoped<BrowseService>();
builder.Services.AddScoped<PullRequestService>();
builder.Services.AddScoped<IssueService>();
builder.Services.AddScoped<CommentService>();
builder.Services.AddScoped<MergeService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped(sp => new SecretService(
    sp.GetRequiredService<DaisiGitCosmo>(),
    builder.Configuration["Daisi:SecretKey"] ?? "default-secret-key"));

builder.Services.AddSingleton<EmailService>();

// Workflow engine
builder.Services.AddScoped<WorkflowEngine>();

// Queue client. Each runtime-specific Container Apps Job sets WorkflowQueue:Name to its
// own queue (e.g. workflow-executions-dotnet); we fall back to the legacy single queue
// when that env var isn't set so old deployments keep working.
var queueConnectionString = builder.Configuration["WorkflowQueue:ConnectionString"]
    ?? builder.Configuration["AzureStorage:ConnectionString"]
    ?? "";
var queueName = builder.Configuration["WorkflowQueue:Name"] ?? "workflow-executions";
if (!string.IsNullOrEmpty(queueConnectionString))
{
    builder.Services.AddSingleton(new QueueClient(
        queueConnectionString,
        queueName,
        new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 }));
}

builder.Services.AddHostedService<WorkflowQueueProcessor>();

var host = builder.Build();
host.Run();
