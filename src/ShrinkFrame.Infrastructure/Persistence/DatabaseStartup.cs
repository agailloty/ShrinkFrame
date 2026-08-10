using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Persistence;

public sealed class DatabaseInitializer(IDbContextFactory<ShrinkFrameDbContext> contextFactory) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

public sealed class StartupRecovery(IDbContextFactory<ShrinkFrameDbContext> contextFactory) : IStartupRecovery
{
    public async Task<int> RecoverInterruptedJobsAsync(DateTimeOffset recoveredAt, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var activeStates = new[]
        {
            nameof(JobState.Acquiring), nameof(JobState.Probing), nameof(JobState.Compressing), nameof(JobState.Validating),
        };
        return await db.Jobs
            .Where(x => activeStates.Contains(x.State) || x.PublicationState == nameof(PublicationState.Publishing))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.State, nameof(JobState.Interrupted))
                .SetProperty(x => x.PublicationState, x => x.PublicationState == nameof(PublicationState.Publishing)
                    ? nameof(PublicationState.Failed) : x.PublicationState)
                .SetProperty(x => x.UpdatedAt, recoveredAt)
                .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
    }
}

public sealed class DatabaseStartupService(
    IDatabaseInitializer initializer,
    IStartupRecovery recovery,
    TimeProvider timeProvider,
    ILogger<DatabaseStartupService> logger) : IHostedService
{
    private static readonly Action<ILogger, int, Exception?> LogRecovery = LoggerMessage.Define<int>(
        LogLevel.Information, new EventId(1001, "DatabaseStartupCompleted"),
        "Database migration completed and {RecoveredJobCount} active jobs were marked interrupted.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken);
        var recovered = await recovery.RecoverInterruptedJobsAsync(timeProvider.GetUtcNow(), cancellationToken);
        LogRecovery(logger, recovered, null);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddShrinkFrameSqlite(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var connectionBuilder = new SqliteConnectionStringBuilder(connectionString) { ForeignKeys = true };
        var dataSource = connectionBuilder.DataSource;
        if (!string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        }
        services.AddPooledDbContextFactory<ShrinkFrameDbContext>(options => options.UseSqlite(connectionBuilder.ConnectionString));
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<ShrinkFrameDbContext>>().CreateDbContext());
        services.AddScoped<IImmichConnectionRepository, ImmichConnectionRepository>();
        services.AddScoped<IBatchRepository, BatchRepository>();
        services.AddScoped<ICompressionJobRepository, CompressionJobRepository>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IStartupRecovery, StartupRecovery>();
        services.AddSingleton(TimeProvider.System);
        services.AddHostedService<DatabaseStartupService>();
        return services;
    }
}
