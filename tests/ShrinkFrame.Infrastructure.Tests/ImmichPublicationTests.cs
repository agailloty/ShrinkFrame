using Microsoft.EntityFrameworkCore;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;
using ShrinkFrame.Infrastructure.Persistence;
using ShrinkFrame.Infrastructure.Storage;

namespace ShrinkFrame.Infrastructure.Tests;

[TestClass]
public sealed class ImmichPublicationTests
{
    private string databasePath = null!;
    private string storageRoot = null!;
    private ShrinkFrameDbContext db = null!;
    private LocalWorkStorage storage = null!;
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [TestInitialize]
    public async Task InitializeAsync()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"shrinkframe-publication-{Guid.NewGuid():N}.db");
        storageRoot = Path.Combine(Path.GetTempPath(), "shrinkframe-publication-tests", Guid.NewGuid().ToString("N"));
        var options = new DbContextOptionsBuilder<ShrinkFrameDbContext>().UseSqlite($"Data Source={databasePath};Pooling=False;Foreign Keys=True").Options;
        var factory = new TestContextFactory(options); await new DatabaseInitializer(factory).InitializeAsync();
        db = await factory.CreateDbContextAsync();
        storage = new(new WorkStorageOptions { WorkRoot = storageRoot, BufferSizeBytes = 4096 });
        await storage.ValidateAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await db.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix);
        if (Directory.Exists(storageRoot)) Directory.Delete(storageRoot, true);
    }

    [TestMethod]
    public async Task Album_failure_retries_only_pending_album_without_reupload_and_removes_only_local_source()
    {
        var sourceConnection = ConnectionId.New(); var otherConnection = ConnectionId.New();
        var albumA = Guid.NewGuid().ToString("D"); var albumB = Guid.NewGuid().ToString("D");
        var prepared = await PrepareAsync(sourceConnection, [albumA, albumB]);
        var transport = new FakeTransport { FailAlbumOnce = albumB };
        var service = Service(sourceConnection, otherConnection, transport);

        var first = (await service.PublishAsync(prepared.BatchId, sourceConnection,
            [new(prepared.JobId, false)])).Single();
        Assert.AreEqual(PublicationState.PartiallyPublished, first.State);
        CollectionAssert.AreEqual(new[] { albumB }, first.PendingAlbumIds.ToArray());
        CollectionAssert.Contains(first.Warnings.ToArray(), "publication.metadata.not_guaranteed");
        Assert.AreEqual(1, transport.UploadCalls);
        Assert.AreEqual(Now.AddDays(-1), transport.UploadModifiedAt);
        Assert.IsTrue((await storage.InventoryAsync([new(prepared.BatchId, prepared.JobId, prepared.Source)])).ArtifactBytes > 0);

        var second = (await service.PublishAsync(prepared.BatchId, sourceConnection,
            [new(prepared.JobId, false)])).Single();
        Assert.AreEqual(PublicationState.Published, second.State);
        Assert.AreEqual(1, transport.UploadCalls, "A partial retry must not upload again.");
        CollectionAssert.AreEqual(new[] { albumA, albumB, albumB }, transport.AlbumCalls);
        Assert.AreEqual(0, (await storage.InventoryAsync([new(prepared.BatchId, prepared.JobId, prepared.Source)])).ArtifactBytes);
        Assert.IsTrue((await storage.InventoryAsync([new(prepared.BatchId, prepared.JobId, prepared.Output)])).ArtifactBytes > 0);
        var persisted = await new CompressionJobRepository(db).GetAsync(prepared.JobId);
        Assert.IsNull(persisted!.Value.SourceArtifact);
        Assert.IsNotNull(persisted.Value.OutputArtifact);
    }

    [TestMethod]
    public async Task Ambiguous_upload_retry_adopts_checksum_match_and_does_not_replay_body()
    {
        var connection = ConnectionId.New(); var prepared = await PrepareAsync(connection, []);
        var transport = new FakeTransport { AmbiguousFirstUpload = true };
        var service = Service(connection, ConnectionId.New(), transport);

        var first = (await service.PublishAsync(prepared.BatchId, connection, [new(prepared.JobId, false)])).Single();
        Assert.AreEqual(PublicationState.Failed, first.State);
        Assert.AreEqual("publication.upload.ambiguous", first.ErrorCode);

        transport.ExistingAssetId = transport.AssetId;
        var second = (await service.PublishAsync(prepared.BatchId, connection, [new(prepared.JobId, false)])).Single();
        Assert.AreEqual(PublicationState.Published, second.State);
        Assert.AreEqual(1, transport.UploadCalls, "An ambiguous multipart body must never be blindly replayed.");
        Assert.AreEqual(2, transport.CheckCalls);
    }

    [TestMethod]
    public async Task Immich_batch_rejects_cross_instance_destination_before_network_call()
    {
        var source = ConnectionId.New(); var other = ConnectionId.New(); var prepared = await PrepareAsync(source, []);
        var transport = new FakeTransport(); var service = Service(source, other, transport);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.PublishAsync(prepared.BatchId, other, [new(prepared.JobId, false)]));
        Assert.AreEqual(0, transport.CheckCalls + transport.UploadCalls);
    }

    [TestMethod]
    public async Task NotBeneficial_requires_per_result_force_and_persists_override()
    {
        var connection = ConnectionId.New(); var prepared = await PrepareAsync(connection, [], notBeneficial: true);
        var transport = new FakeTransport(); var service = Service(connection, ConnectionId.New(), transport);
        var refused = (await service.PublishAsync(prepared.BatchId, connection, [new(prepared.JobId, false)])).Single();
        Assert.AreEqual("publication.force.required", refused.ErrorCode);
        Assert.AreEqual(0, transport.UploadCalls);
        var accepted = (await service.PublishAsync(prepared.BatchId, connection, [new(prepared.JobId, true)])).Single();
        Assert.AreEqual(PublicationState.Published, accepted.State);
        Assert.IsTrue((await new CompressionJobRepository(db).GetAsync(prepared.JobId))!.Value.NotBeneficialPublicationOverride);
    }

    private ImmichPublicationService Service(ConnectionId source, ConnectionId other, FakeTransport transport) => new(
        new BatchRepository(db), new CompressionJobRepository(db), new PublicationCheckpointRepository(db),
        new FakeConnections([View(source), View(other)]), transport, storage, new FixedTimeProvider(Now));

    private async Task<Prepared> PrepareAsync(ConnectionId connection, string[] albums, bool notBeneficial = false)
    {
        var savedConnection = new ImmichConnection(connection, "Immich", new Uri("https://immich.example/"), false, true, false);
        savedConnection.RecordTest(Now, "3.1.0", CompatibilityResult.Compatible, null, "key", "test", "asset.upload,albumAsset.create");
        await new ImmichConnectionRepository(db).AddAsync(new(savedConnection, new EncryptedSecretEnvelope([1, 2, 3])));
        var batch = new CompressionBatch(BatchId.New(), "Publication", SourceKind.Immich, connection,
            BuiltInPresets.Snapshot(new("balanced")), Now);
        var jobId = JobId.New(); var sourceRef = VideoSourceRef.Immich(Guid.NewGuid().ToString("D"), connection);
        var source = await ArtifactAsync(batch.Id, jobId, ArtifactKind.Source, new byte[100]);
        var output = await ArtifactAsync(batch.Id, jobId, ArtifactKind.Output, new byte[notBeneficial ? 100 : 50]);
        var job = new CompressionJob(jobId, batch.Id, sourceRef, new("balanced"), BuiltInPresets.Snapshot(new("balanced")), Now);
        job.TransitionTo(JobState.Acquiring, Now); job.TransitionTo(JobState.Probing, Now);
        job.RecordProbe(new VideoMetadata("original.mov", "video/quicktime", 100, TimeSpan.FromSeconds(5), 640, 360,
            "h264", ["aac"], Now.AddYears(-1), 0, "description", 48.8, 2.3, albums, Now.AddDays(-1)), source);
        job.TransitionTo(JobState.Queued, Now); job.TransitionTo(JobState.Compressing, Now); job.TransitionTo(JobState.Validating, Now);
        job.CompleteValidation(notBeneficial ? 100 : 50, output, [], Now);
        batch.AddJob(job.Id, sourceRef, Now);
        await new BatchRepository(db).AddAsync(batch); await new CompressionJobRepository(db).AddAsync(job);
        return new(batch.Id, job.Id, source, output);
    }

    private async Task<ArtifactRef> ArtifactAsync(BatchId batch, JobId job, ArtifactKind kind, byte[] bytes)
    {
        var allocation = storage.Allocate(batch, job, kind);
        await storage.CopyToNewAsync(new MemoryStream(bytes), allocation.Partial);
        await storage.FinalizeAsync(allocation.Partial, allocation.Final);
        return allocation.Final;
    }

    private static ImmichConnectionView View(ConnectionId id) => new(id, "Immich", "https://immich.example/", false,
        true, false, true, Now, "3.1.0", CompatibilityResult.Compatible, "key", "test",
        ["asset.upload", "albumAsset.create"], new(false, true, [], []), null, null);

    private sealed record Prepared(BatchId BatchId, JobId JobId, ArtifactRef Source, ArtifactRef Output);
    private sealed class TestContextFactory(DbContextOptions<ShrinkFrameDbContext> options) : IDbContextFactory<ShrinkFrameDbContext>
    { public ShrinkFrameDbContext CreateDbContext() => new(options); public Task<ShrinkFrameDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext()); }
    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
    private sealed class FakeConnections(IReadOnlyList<ImmichConnectionView> values) : IImmichConnectionManager
    {
        public Task<IReadOnlyList<ImmichConnectionView>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(values);
        public Task<ImmichConnectionView> AddAsync(ImmichConnectionInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImmichConnectionView> UpdateAsync(ConnectionId id, ImmichConnectionInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImmichConnectionView> TestAsync(ConnectionId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetDefaultAsync(ConnectionId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(ConnectionId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FakeTransport : IImmichPublicationTransport
    {
        public string AssetId { get; } = Guid.NewGuid().ToString("D");
        public string? ExistingAssetId { get; set; }
        public string? FailAlbumOnce { get; set; }
        public bool AmbiguousFirstUpload { get; set; }
        public int CheckCalls { get; private set; }
        public int UploadCalls { get; private set; }
        public DateTimeOffset? UploadModifiedAt { get; private set; }
        public List<string> AlbumCalls { get; } = [];
        public Task<ImmichUploadCheck> CheckExistingAsync(ConnectionId connectionId, string clientAttemptId, string sha1Checksum, CancellationToken cancellationToken = default)
        { CheckCalls++; Assert.IsFalse(string.IsNullOrWhiteSpace(sha1Checksum)); return Task.FromResult(new ImmichUploadCheck(ExistingAssetId, false)); }
        public async Task<ImmichUploadResult> UploadAsync(ConnectionId connectionId, ImmichUploadRequest request, CancellationToken cancellationToken = default)
        {
            UploadCalls++; UploadModifiedAt = request.FileModifiedAt; await using var content = await request.OpenContent(cancellationToken); Assert.IsTrue(content.CanRead);
            if (AmbiguousFirstUpload && UploadCalls == 1) throw new ImmichPublicationTransportException("publication.upload.ambiguous", "ambiguous", true);
            return new(AssetId, "created");
        }
        public Task AddToAlbumAsync(ConnectionId connectionId, string albumId, string assetId, CancellationToken cancellationToken = default)
        {
            AlbumCalls.Add(albumId);
            if (FailAlbumOnce == albumId) { FailAlbumOnce = null; throw new ImmichPublicationTransportException("publication.album.failed", "failed"); }
            return Task.CompletedTask;
        }
    }
}
