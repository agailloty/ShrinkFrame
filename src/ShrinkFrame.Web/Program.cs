using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using ShrinkFrame.Web.Components;
using ShrinkFrame.Web.Configuration;
using ShrinkFrame.Application;
using ShrinkFrame.Infrastructure.Persistence;
using ShrinkFrame.Infrastructure.Storage;
using ShrinkFrame.Infrastructure.Media;
using ShrinkFrame.Infrastructure.Immich;
using ShrinkFrame.Web.BrowserUploads;
using ShrinkFrame.Web.Immich;
using ShrinkFrame.Infrastructure.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration.GetValue<string>("DataProtection:KeyRingPath")
            ?? throw new InvalidOperationException("DataProtection:KeyRingPath is required.")))
    .SetApplicationName("ShrinkFrame");
builder.Services.AddOptions<StorageOptions>()
    .BindConfiguration(StorageOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<WorkerOptions>()
    .BindConfiguration(WorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<BrowserUploadOptions>()
    .BindConfiguration(BrowserUploadOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => options.AllowedOrigins.All(value => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.PathAndQuery.Trim('/'))),
        "BrowserUploads:AllowedOrigins must contain only absolute HTTP(S) origins without paths.")
    .ValidateOnStart();
builder.Services.AddScoped<BrowserUploadService>();
builder.Services.AddScoped<IBatchWizard, BatchWizard>();
builder.Services.AddScoped<SameOriginFilter>();
builder.Services.AddShrinkFrameSqlite(
    builder.Configuration.GetConnectionString("ShrinkFrame")
        ?? throw new InvalidOperationException("ConnectionStrings:ShrinkFrame is required."));
var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
    ?? throw new InvalidOperationException("Storage configuration is required.");
builder.Services.AddLocalWorkStorage(new WorkStorageOptions
{
    WorkRoot = storage.WorkRoot,
    ReserveBytes = storage.ReserveBytes,
    BufferSizeBytes = storage.BufferSizeBytes,
});
var mediaTools = builder.Configuration.GetSection(MediaToolOptions.SectionName).Get<MediaToolOptions>()
    ?? throw new InvalidOperationException("MediaTools configuration is required.");
builder.Services.AddMediaTools(mediaTools);
var immichConnections = builder.Configuration.GetSection(ImmichConnectionOptions.SectionName).Get<ImmichConnectionOptions>()
    ?? new ImmichConnectionOptions();
builder.Services.AddImmichConnections(immichConnections);
var worker = builder.Configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>() ?? new WorkerOptions();
builder.Services.AddDurableWorker(new DurableWorkerOptions
{
    AcquisitionConcurrency = worker.AcquisitionConcurrency,
    CompressionConcurrency = worker.CompressionConcurrency,
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

var supportedCultures = new[] { new CultureInfo("en") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
});

app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/health", (IMediaToolStatus media) => media.Current.Available
    ? Results.Ok(new { status = "Healthy", media = media.Current })
    : Results.Json(new { status = "Unhealthy", media = media.Current }, statusCode: 503));
app.MapBrowserUploads();
app.MapImmichBrowser();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
