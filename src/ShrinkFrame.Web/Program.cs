using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using ShrinkFrame.Web.Components;
using ShrinkFrame.Web.Configuration;

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
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
