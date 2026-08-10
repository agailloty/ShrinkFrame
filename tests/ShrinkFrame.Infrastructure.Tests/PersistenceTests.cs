using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;
using ShrinkFrame.Infrastructure.Persistence;
using ShrinkFrame.Infrastructure.Storage;

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
        CollectionAssert.IsSubsetOf(new[] { "Batches", "ImmichConnections", "Jobs", "JobAudioCodecs", "JobAlbums", "JobProgress", "JobLogs", "PublicationAttempts", "ValidationFindings" }, tables);

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
        batch.AuthorizeCapacityAdmissionOverride(now.AddMinutes(4));

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
            Assert.IsTrue(storedBatch.CapacityAdmissionOverride);
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

    [TestMethod]
    public async Task WorkerStoreGuardsAcquisitionClaimAndPersistsReconnectState()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var connectionId = ConnectionId.New();
        var batch = new CompressionBatch(BatchId.New(), "Immich", SourceKind.Immich, connectionId, BuiltInPresets.Snapshot(new("balanced")), now);
        var source = VideoSourceRef.Immich("asset-1", connectionId);
        var job = new CompressionJob(JobId.New(), batch.Id, source, new("balanced"), batch.DefaultOptions, now);
        batch.AddJob(job.Id, source, now); job.TransitionTo(JobState.Acquiring, now); batch.Confirm(now);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await new BatchRepository(db).AddAsync(batch);
            await new CompressionJobRepository(db).AddAsync(job);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var store = new WorkerStore(db);
            var candidate = (await store.ListJobsAsync(batch.Id)).Single();
            Assert.IsNotNull(await store.TryClaimAcquisitionAsync(job.Id, candidate.Version, now));
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var store = new WorkerStore(db);
            Assert.IsNull(await store.TryClaimAcquisitionAsync(job.Id, 1, now));
            await new CompressionJobRepository(db).SaveProgressAsync(job.Id, new(new TransferProgress(50, 100), null, now));
            await store.AppendLogAsync(job.Id, new(now, "Information", "acquisition.started", "Started."));
            var runtime = await store.GetRuntimeAsync(job.Id);
            Assert.AreEqual(50, runtime!.Progress!.Transfer!.BytesTransferred);
            Assert.AreEqual("acquisition.started", runtime.Logs.Single().Code);
            var aggregate = await store.GetBatchProgressAsync(batch.Id);
            Assert.AreEqual(0m, aggregate!.Percentage);
        }
    }

    [TestMethod]
    public async Task CancellationAndExplicitRetryAreDurable()
    {
        var (jobId, _) = await AddQueuedBrowserJobAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var store = new WorkerStore(db); await store.RequestJobCancellationAsync(jobId, DateTimeOffset.UtcNow);
            Assert.IsTrue(await store.IsCancellationRequestedAsync(jobId));
            var candidate = (await store.ListJobsAsync((await new CompressionJobRepository(db).GetAsync(jobId))!.Value.BatchId)).Single();
            Assert.IsNull(await store.TryClaimCompressionAsync(jobId, candidate.Version, DateTimeOffset.UtcNow));
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var repository = new CompressionJobRepository(db); var stored = (await repository.GetAsync(jobId))!;
            stored.Value.Cancel(DateTimeOffset.UtcNow); await repository.UpdateAsync(stored.Value, stored.Version);
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var store = new WorkerStore(db); await store.RetryAsync(jobId, DateTimeOffset.UtcNow);
            Assert.IsFalse(await store.IsCancellationRequestedAsync(jobId));
            Assert.AreEqual(JobState.Queued, (await new CompressionJobRepository(db).GetAsync(jobId))!.Value.State);
        }
    }

    [TestMethod]
    public async Task BatchWizardPersistsEffectiveSnapshotsAndRequiresCapacityOverride()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        BatchId batchId;
        JobId jobId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var batchRepository = new BatchRepository(db);
            var jobRepository = new CompressionJobRepository(db);
            var wizard = new BatchWizard(batchRepository, jobRepository, new FixedCapacity(false), new FixedTime(now));
            var created = await wizard.CreateAsync(SourceKind.BrowserUpload, null, "Editable");
            batchId = created.Id;
            var source = VideoSourceRef.Browser("upload-1");
            var job = new CompressionJob(JobId.New(), batchId, source, new("balanced"), created.DefaultOptions, now);
            jobId = job.Id;
            job.TransitionTo(JobState.Acquiring, now); job.TransitionTo(JobState.Probing, now);
            job.RecordProbe(new VideoMetadata("clip.mp4", "video/mp4", 1000, TimeSpan.FromSeconds(1), 16, 16,
                "h264", ["aac"], now, 0), new ArtifactRef("source/input.bin"));
            await jobRepository.AddAsync(job);
            var aggregate = (await batchRepository.GetAsync(batchId))!; aggregate.AddJob(jobId, source, now); await batchRepository.UpdateAsync(aggregate);
            var settings = new BatchSettings("Renamed", new("balanced"), new(31, EncoderPreset.Fast,
                MaximumResolution.P1080, AudioMode.Aac, "_small"), new Dictionary<JobId, PresetId> { [jobId] = new("hd") });
            var summary = await wizard.SaveSettingsAsync(batchId, settings);
            Assert.AreEqual(BuiltInPresets.Snapshot(new("hd")), summary.Jobs.Single().EffectiveOptions);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => wizard.ConfirmAsync(batchId, false));
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var wizard = new BatchWizard(new BatchRepository(db), new CompressionJobRepository(db), new FixedCapacity(false), new FixedTime(now));
            var confirmed = await wizard.ConfirmAsync(batchId, true);
            Assert.AreEqual(BatchStatus.Processing, confirmed.Status);
            Assert.IsTrue(confirmed.CapacityAdmissionOverride);
            Assert.AreEqual(JobState.Queued, confirmed.Jobs.Single().State);
            Assert.AreEqual(BuiltInPresets.Snapshot(new("hd")), confirmed.Jobs.Single().EffectiveOptions);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => wizard.SaveSettingsAsync(batchId,
                new("Too late", new("balanced"), BuiltInPresets.Snapshot(new("balanced")), new Dictionary<JobId, PresetId>())));
        }
    }

    [TestMethod]
    public async Task RecompressionCreatesDistinctJobAndPreservesPriorResult()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var options = BuiltInPresets.Snapshot(new("balanced"));
        var batch = new CompressionBatch(BatchId.New(), "Completed", SourceKind.BrowserUpload, null, options, now);
        var source = VideoSourceRef.Browser("upload-1");
        var original = new CompressionJob(JobId.New(), batch.Id, source, new("balanced"), options, now);
        original.TransitionTo(JobState.Acquiring, now); original.TransitionTo(JobState.Probing, now);
        var sourceArtifact = new ArtifactRef($"batches/{batch.Id.Value:N}/jobs/{original.Id.Value:N}/source/input.bin");
        original.RecordProbe(new VideoMetadata("portrait.mp4", "video/mp4", 1000, TimeSpan.FromSeconds(3),
            720, 1280, "h264", ["aac"], now, 90), sourceArtifact);
        original.TransitionTo(JobState.Queued, now); original.TransitionTo(JobState.Compressing, now);
        original.TransitionTo(JobState.Validating, now);
        var output = new ArtifactRef($"batches/{batch.Id.Value:N}/jobs/{original.Id.Value:N}/output/result.mp4");
        original.CompleteValidation(500, output, [], now);
        batch.AddJob(original.Id, source, now); batch.Confirm(now); batch.Complete(now);

        await using var db = await factory.CreateDbContextAsync();
        var batches = new BatchRepository(db); var jobs = new CompressionJobRepository(db);
        await batches.AddAsync(batch); await jobs.AddAsync(original);
        var storage = new LocalWorkStorage(new WorkStorageOptions { WorkRoot = Path.Combine(Path.GetTempPath(), $"sf-{Guid.NewGuid():N}") });
        var service = new ResultDelivery(jobs, storage, new WorkerStore(db), new FixedTime(now.AddMinutes(1)));
        var smaller = new CompressionOptions(30, EncoderPreset.Slow, MaximumResolution.P720, AudioMode.Aac, "_small");
        var newId = await service.RecompressAsync(original.Id, new(new("smallest-practical"), smaller));

        var stored = await jobs.ListByBatchAsync(batch.Id);
        Assert.HasCount(2, stored);
        Assert.AreEqual(output, stored.Single(x => x.Value.Id == original.Id).Value.OutputArtifact);
        var recompression = stored.Single(x => x.Value.Id == newId).Value;
        Assert.AreEqual(JobState.Queued, recompression.State);
        Assert.AreEqual(smaller, recompression.EffectiveOptions);
        Assert.AreEqual(sourceArtifact, recompression.SourceArtifact);
        Assert.IsNull(recompression.OutputArtifact);
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

    private sealed class FixedCapacity(bool sufficient) : IDiskCapacityService
    {
        public CapacityAdmission Evaluate(long sourceBytes, bool forceRequested = false) => new(sourceBytes, 10_000, 1, 0,
            sufficient ? CapacityReason.Sufficient : CapacityReason.InsufficientSpace, forceRequested);
    }

    private sealed class FixedTime(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
