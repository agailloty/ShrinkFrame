using ShrinkFrame.Application;
using ShrinkFrame.Domain;
using ShrinkFrame.Infrastructure.Storage;

namespace ShrinkFrame.Infrastructure.Tests;

[TestClass]
public sealed class WorkStorageTests
{
    [TestMethod]
    public async Task Write_finalize_inventory_and_delete_are_job_scoped()
    {
        using var fixture = new StorageFixture(bufferSize: 4096);
        var batch = BatchId.New();
        var job = JobId.New();
        var allocation = fixture.Storage.Allocate(batch, job, ArtifactKind.Output);

        var bytes = await fixture.Storage.CopyToNewAsync(new MemoryStream(new byte[10_000]), allocation.Partial);
        Assert.AreEqual(10_000, bytes);
        Assert.IsTrue(allocation.Partial.Key.Contains(".partial", StringComparison.Ordinal));
        Assert.IsFalse(allocation.Final.Key.Contains(".partial", StringComparison.Ordinal));

        var finalizedBytes = await fixture.Storage.FinalizeAsync(allocation.Partial, allocation.Final);
        Assert.AreEqual(10_000, finalizedBytes);
        var owned = new OwnedArtifact(batch, job, allocation.Final);
        var inventory = await fixture.Storage.InventoryAsync([owned]);
        Assert.AreEqual(10_000, inventory.ArtifactBytes);
        Assert.AreEqual(allocation.Final.Key, inventory.Artifacts.Single().Artifact.Key);

        var deletion = await fixture.Storage.DeleteKnownAsync([owned]);
        Assert.IsTrue(deletion.Succeeded);
        Assert.AreEqual(0, (await fixture.Storage.InventoryAsync([owned])).ArtifactBytes);
    }

    [TestMethod]
    public async Task Create_new_does_not_replace_an_existing_partial()
    {
        using var fixture = new StorageFixture();
        var allocation = fixture.Storage.Allocate(BatchId.New(), JobId.New(), ArtifactKind.Source);
        await using var first = await fixture.Storage.OpenCreateNewAsync(allocation.Partial);
        await Assert.ThrowsExactlyAsync<IOException>(async () =>
        {
            await using var ignored = await fixture.Storage.OpenCreateNewAsync(allocation.Partial);
        });
    }

    [TestMethod]
    public async Task Cancellation_removes_the_partial_artifact()
    {
        using var fixture = new StorageFixture(bufferSize: 4096);
        var batch = BatchId.New();
        var job = JobId.New();
        var allocation = fixture.Storage.Allocate(batch, job, ArtifactKind.Source);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Storage.CopyToNewAsync(new CancellingStream(cancellation), allocation.Partial, cancellation.Token));
        Assert.AreEqual(0, (await fixture.Storage.InventoryAsync([new(batch, job, allocation.Partial)])).ArtifactBytes);
    }

    [TestMethod]
    public async Task Mismatched_job_ownership_is_rejected()
    {
        using var fixture = new StorageFixture();
        var allocation = fixture.Storage.Allocate(BatchId.New(), JobId.New(), ArtifactKind.Output);
        var report = await fixture.Storage.DeleteKnownAsync(
            [new OwnedArtifact(BatchId.New(), JobId.New(), allocation.Partial)]);
        Assert.IsFalse(report.Succeeded);
        Assert.AreEqual("storage.delete.failed", report.Results.Single().ErrorCode);
    }

    [TestMethod]
    public async Task Unknown_file_below_a_job_is_not_a_deletion_target()
    {
        using var fixture = new StorageFixture();
        var batch = BatchId.New();
        var job = JobId.New();
        var unknown = new ArtifactRef($"batches/{batch.Value:N}/jobs/{job.Value:N}/other/file.bin");
        var report = await fixture.Storage.DeleteKnownAsync([new(batch, job, unknown)]);
        Assert.IsFalse(report.Succeeded);
    }

    [TestMethod]
    public void Domain_rejects_traversal_absolute_and_alternate_stream_keys()
    {
        Assert.ThrowsExactly<DomainException>(() => new ArtifactRef("../outside"));
        Assert.ThrowsExactly<DomainException>(() => new ArtifactRef("C:/outside"));
        Assert.ThrowsExactly<DomainException>(() => new ArtifactRef("safe\\outside"));
        Assert.ThrowsExactly<DomainException>(() => new ArtifactRef("safe/file:stream"));
    }

    [TestMethod]
    public async Task Writable_path_validation_succeeds_without_leaving_probe_data()
    {
        using var fixture = new StorageFixture();
        await fixture.Storage.ValidateAsync();
        Assert.AreEqual(0, Directory.EnumerateFiles(fixture.Root, ".writable-*", SearchOption.TopDirectoryOnly).Count());
    }

    [TestMethod]
    public void Capacity_uses_configured_reporter_and_requires_explicit_force()
    {
        var service = new DiskCapacityService(new FakeReporter(2_199), new WorkStorageOptions
        {
            WorkRoot = ".",
            ReserveBytes = 0,
        });
        var warning = service.Evaluate(1_000);
        Assert.AreEqual(2_200, warning.RequiredBytes);
        Assert.AreEqual(CapacityReason.InsufficientSpace, warning.Reason);
        Assert.IsTrue(warning.RequiresOverride);
        Assert.IsFalse(warning.IsAdmitted);
        Assert.IsTrue(service.Evaluate(1_000, forceRequested: true).IsAdmitted);
    }

    [TestMethod]
    public async Task Existing_artifact_path_is_server_resolved_and_never_the_opaque_key()
    {
        using var fixture = new StorageFixture();
        var allocation = fixture.Storage.Allocate(BatchId.New(), JobId.New(), ArtifactKind.Source);
        await fixture.Storage.CopyToNewAsync(new MemoryStream([1, 2, 3]), allocation.Partial);
        await fixture.Storage.FinalizeAsync(allocation.Partial, allocation.Final);
        var path = fixture.Storage.ResolveExisting(allocation.Final);
        Assert.IsTrue(Path.IsPathFullyQualified(path));
        Assert.AreNotEqual(allocation.Final.Key, path);
        Assert.AreEqual(3, new FileInfo(path).Length);
    }

    [TestMethod]
    public void Capacity_overflow_cannot_be_forced()
    {
        var service = new DiskCapacityService(new FakeReporter(long.MaxValue), new WorkStorageOptions
        {
            WorkRoot = ".",
            ReserveBytes = long.MaxValue,
        });
        var decision = service.Evaluate(long.MaxValue, forceRequested: true);
        Assert.AreEqual(CapacityReason.ArithmeticOverflow, decision.Reason);
        Assert.IsFalse(decision.IsAdmitted);
    }

    private sealed class FakeReporter(long available) : IStorageCapacityReporter
    {
        public StorageCapacity GetCapacity() => new(long.MaxValue, available);
    }

    private sealed class CancellingStream(CancellationTokenSource cancellation) : Stream
    {
        private bool read;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (read) return 0;
            read = true;
            buffer.Span[0] = 1;
            cancellation.Cancel();
            return 1;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StorageFixture : IDisposable
    {
        public StorageFixture(int bufferSize = 128 * 1024)
        {
            Root = Path.Combine(Path.GetTempPath(), "shrinkframe-storage-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Storage = new LocalWorkStorage(new WorkStorageOptions { WorkRoot = Root, BufferSizeBytes = bufferSize });
        }
        public string Root { get; }
        public LocalWorkStorage Storage { get; }
        public void Dispose()
        {
            var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "shrinkframe-storage-tests")) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(Root);
            if (resolved.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
                Directory.Delete(resolved, recursive: true);
        }
    }
}
