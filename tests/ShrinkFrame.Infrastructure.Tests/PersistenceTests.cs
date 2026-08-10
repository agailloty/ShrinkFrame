using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;
using ShrinkFrame.Infrastructure.Persistence;

[assembly: DoNotParallelize]

namespace ShrinkFrame.Infrastructure.Tests;

[TestClass]
public sealed class PersistenceTests
{
    private string databasePath = null!;
    private TestContextFactory factory = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"shrinkframe-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ShrinkFrameDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=5;Pooling=False;Foreign Keys=True")
            .Options;
        factory = new TestContextFactory(options);
        await new DatabaseInitializer(factory).InitializeAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public async Task MigrationCreatesSafeSchemaAndWalDatabase()
    {
        await using var db = await factory.CreateDbContextAsync();
        var tables = await ReadFirstColumnAsync(db, "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;");
        CollectionAssert.IsSubsetOf(new[] { "Batches", "ImmichConnections", "Jobs", "JobAudioCodecs", "JobAlbums", "JobProgress", "PublicationAttempts", "ValidationFindings" }, tables);

        var columns = await ReadFirstColumnAsync(db, "SELECT name FROM pragma_table_info('Jobs');");
        Assert.IsFalse(columns.Any(x => x.Contains("VideoBytes", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(columns.Any(x => x.Contains("AbsolutePath", StringComparison.OrdinalIgnoreCase)));
        var indexes = await ReadFirstColumnAsync(db, "SELECT name FROM sqlite_master WHERE type='index';");
        CollectionAssert.IsSubsetOf(new[] { "IX_Jobs_Queue", "IX_Jobs_BatchHistory", "IX_Jobs_SourceDuplicate", "IX_Batches_History" }, indexes);
        Assert.HasCount(0, await ReadFirstColumnAsync(db, "PRAGMA foreign_key_check;"));
        var journalMode = await ReadScalarAsync(db, "PRAGMA journal_mode;");
        Assert.AreEqual("wal", journalMode);
    }

    [TestMethod]
    public async Task RepositoriesRoundTripAggregatesAndEncryptedEnvelope()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var connection = new ImmichConnection(ConnectionId.New(), "Home", new Uri("https://immich.example/api/path"), false, true, true);
        connection.RecordTest(now, "3.1.0", CompatibilityResult.Compatible, null);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var repository = new ImmichConnectionRepository(db);
            await repository.AddAsync(new(connection, new EncryptedSecretEnvelope([1, 2, 3, 4])));
        }

        var batch = new CompressionBatch(BatchId.New(), "Vacation", SourceKind.Immich, connection.Id, BuiltInPresets.Snapshot(new("balanced")), now);
        var source = VideoSourceRef.Immich("asset-123", connection.Id);
        var job = new CompressionJob(JobId.New(), batch.Id, source, new("balanced"), BuiltInPresets.Snapshot(new("balanced")), now);
        job.TransitionTo(JobState.Acquiring, now.AddMinutes(1));
        job.TransitionTo(JobState.Probing, now.AddMinutes(2));
        job.RecordProbe(new VideoMetadata("clip.mov", "video/quicktime", 9000, TimeSpan.FromSeconds(12), 1920, 1080,
            "h264", ["aac", "ac3"], now.AddYears(-1), 90, "description", 48.8, 2.3, ["album-a", "album-b"]),
            new ArtifactRef("batches/a/jobs/b/source/input.bin"));
        job.TransitionTo(JobState.Queued, now.AddMinutes(3));
        batch.AddJob(job.Id, source, now.AddMinutes(3));

        await using (var db = await factory.CreateDbContextAsync())
        {
            await new BatchRepository(db).AddAsync(batch);
            await new CompressionJobRepository(db).AddAsync(job);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var storedConnection = await new ImmichConnectionRepository(db).GetAsync(connection.Id);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, storedConnection!.ApiKeyEnvelope!.Payload);
            var storedBatch = await new BatchRepository(db).GetAsync(batch.Id);
            CollectionAssert.AreEqual(new[] { job.Id }, storedBatch!.JobIds);
            var storedJob = await new CompressionJobRepository(db).GetAsync(job.Id);
            Assert.AreEqual(JobState.Queued, storedJob!.Value.State);
            Assert.AreEqual("clip.mov", storedJob.Value.OriginalMetadata!.FileName);
            CollectionAssert.AreEqual(new[] { "aac", "ac3" }, storedJob.Value.OriginalMetadata.AudioCodecs);
            CollectionAssert.AreEqual(new[] { "album-a", "album-b" }, storedJob.Value.OriginalMetadata.AlbumIds);
            Assert.AreEqual("batches/a/jobs/b/source/input.bin", storedJob.Value.SourceArtifact!.Key);
        }
    }

    [TestMethod]
    public async Task GuardedClaimSucceedsOnlyOnceAndRecoveryIsIdempotent()
    {
        var (jobId, version) = await AddQueuedBrowserJobAsync();
        Versioned<CompressionJob>? first;
        Versioned<CompressionJob>? second;
        await using (var firstDb = await factory.CreateDbContextAsync())
            first = await new CompressionJobRepository(firstDb).TryClaimAsync(jobId, JobState.Queued, version, DateTimeOffset.UtcNow);
        await using (var secondDb = await factory.CreateDbContextAsync())
            second = await new CompressionJobRepository(secondDb).TryClaimAsync(jobId, JobState.Queued, version, DateTimeOffset.UtcNow);

        Assert.IsNotNull(first);
        Assert.AreEqual(JobState.Compressing, first.Value.State);
        Assert.IsNull(second);

        var recovery = new StartupRecovery(factory);
        Assert.AreEqual(1, await recovery.RecoverInterruptedJobsAsync(DateTimeOffset.UtcNow));
        Assert.AreEqual(0, await recovery.RecoverInterruptedJobsAsync(DateTimeOffset.UtcNow.AddMinutes(1)));
        await using var verificationDb = await factory.CreateDbContextAsync();
        var recovered = await new CompressionJobRepository(verificationDb).GetAsync(jobId);
        Assert.AreEqual(JobState.Interrupted, recovered!.Value.State);
    }

    [TestMethod]
    public async Task OptimisticConcurrencyRejectsStaleVersion()
    {
        var (jobId, version) = await AddQueuedBrowserJobAsync();
        await using var db = await factory.CreateDbContextAsync();
        var repository = new CompressionJobRepository(db);
        var job = (await repository.GetAsync(jobId))!.Value;
        job.TransitionTo(JobState.Cancelled, DateTimeOffset.UtcNow);
        _ = await repository.UpdateAsync(job, version);
        await Assert.ThrowsExactlyAsync<PersistenceConcurrencyException>(() => repository.UpdateAsync(job, version));
    }

    private async Task<(JobId Id, long Version)> AddQueuedBrowserJobAsync()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var batch = new CompressionBatch(BatchId.New(), "Uploads", SourceKind.BrowserUpload, null, BuiltInPresets.Snapshot(new("balanced")), now);
        var source = VideoSourceRef.Browser("upload-1");
        var job = new CompressionJob(JobId.New(), batch.Id, source, new("balanced"), BuiltInPresets.Snapshot(new("balanced")), now);
        job.TransitionTo(JobState.Acquiring, now); job.TransitionTo(JobState.Probing, now);
        job.RecordProbe(new VideoMetadata("clip.mp4", "video/mp4", 100, TimeSpan.FromSeconds(1), 10, 10, "h264", ["aac"], now, 0), new ArtifactRef("source/input.bin"));
        job.TransitionTo(JobState.Queued, now); batch.AddJob(job.Id, source, now);
        await using var db = await factory.CreateDbContextAsync();
        await new BatchRepository(db).AddAsync(batch);
        var stored = await new CompressionJobRepository(db).AddAsync(job);
        return (job.Id, stored.Version);
    }

    private static async Task<List<string>> ReadFirstColumnAsync(ShrinkFrameDbContext db, string commandText)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>(); while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }

    private static async Task<string> ReadScalarAsync(ShrinkFrameDbContext db, string commandText)
    {
        await db.Database.OpenConnectionAsync();
        await using DbCommand command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = commandText;
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("No SQLite scalar result."));
    }

    private sealed class TestContextFactory(DbContextOptions<ShrinkFrameDbContext> options) : IDbContextFactory<ShrinkFrameDbContext>
    {
        public ShrinkFrameDbContext CreateDbContext() => new(options);
        public Task<ShrinkFrameDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ShrinkFrameDbContext(options));
    }
}
