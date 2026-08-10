using Microsoft.EntityFrameworkCore;
using ShrinkFrame.Application;
using ShrinkFrame.Infrastructure.Persistence;

namespace ShrinkFrame.Web.Operations;

public sealed record OperationalHealthReport(string Status, DateTimeOffset CheckedAt,
    HealthComponent Database, HealthComponent WorkPath, HealthComponent MediaTools,
    HealthComponent Disk, string Immich);

public sealed record HealthComponent(string Status, string? Detail = null, long? AvailableBytes = null,
    long? RequiredReserveBytes = null);

public sealed class OperationalHealthService(
    IDbContextFactory<ShrinkFrameDbContext> contexts,
    IWorkStorageStartupValidator storage,
    IMediaToolStatus media,
    IStorageCapacityReporter capacity,
    ShrinkFrame.Infrastructure.Storage.WorkStorageOptions storageOptions,
    TimeProvider time)
{
    public async Task<OperationalHealthReport> CheckAsync(CancellationToken cancellationToken)
    {
        var database = await CheckDatabaseAsync(cancellationToken);
        var workPath = await CheckWorkPathAsync(cancellationToken);
        var tools = media.Current.Available
            ? new HealthComponent("Healthy", $"{media.Current.FfmpegVersion}; {media.Current.FfprobeVersion}")
            : new HealthComponent("Unhealthy", "ffmpeg or ffprobe is unavailable.");
        var disk = CheckDisk();
        var status = database.Status == "Unhealthy" || workPath.Status == "Unhealthy" || tools.Status == "Unhealthy"
            ? "Unhealthy" : disk.Status == "Degraded" ? "Degraded" : "Healthy";
        return new(status, time.GetUtcNow(), database, workPath, tools, disk,
            "Immich connection outages are reported per connection and do not affect application readiness.");
    }

    private async Task<HealthComponent> CheckDatabaseAsync(CancellationToken token)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            return await db.Database.CanConnectAsync(token) ? new("Healthy") : new("Unhealthy", "Database is unreachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return new("Unhealthy", "Database check failed."); }
    }

    private async Task<HealthComponent> CheckWorkPathAsync(CancellationToken token)
    {
        try { await storage.ValidateAsync(token); return new("Healthy"); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return new("Unhealthy", "Work path is not writable."); }
    }

    private HealthComponent CheckDisk()
    {
        try
        {
            var value = capacity.GetCapacity();
            return value.AvailableBytes < storageOptions.ReserveBytes
                ? new("Degraded", "Available disk space is below the configured reserve.", value.AvailableBytes, storageOptions.ReserveBytes)
                : new("Healthy", null, value.AvailableBytes, storageOptions.ReserveBytes);
        }
        catch (Exception) { return new("Unhealthy", "Disk capacity could not be read."); }
    }
}

public static class OperationalHealthEndpoints
{
    public static IEndpointRouteBuilder MapOperationalHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
        endpoints.MapGet("/health/ready", async (OperationalHealthService health, CancellationToken token) =>
        {
            var report = await health.CheckAsync(token);
            return report.Status == "Unhealthy" ? Results.Json(report, statusCode: 503) : Results.Json(report);
        });
        endpoints.MapGet("/health/details", async (OperationalHealthService health, CancellationToken token) =>
        {
            var report = await health.CheckAsync(token);
            return report.Status == "Unhealthy" ? Results.Json(report, statusCode: 503) : Results.Json(report);
        });
        endpoints.MapGet("/health", () => Results.Redirect("/health/ready"));
        return endpoints;
    }
}
