using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Worker;

public sealed class DurableWorkerOptions
{
    public int CompressionConcurrency { get; init; } = 1;
    public int AcquisitionConcurrency { get; init; } = 2;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan PersistProgressInterval { get; init; } = TimeSpan.FromSeconds(1);
}

public sealed class JobProgressHub : IJobProgressHub
{
    private readonly ConcurrentDictionary<JobId, JobProgressSnapshot> latest = new();
    public event Action<JobId, JobProgressSnapshot>? Changed;
    public JobProgressSnapshot? GetLatest(JobId id) => latest.GetValueOrDefault(id);
    public void Report(JobId id, JobProgressSnapshot progress) { latest[id] = progress; Changed?.Invoke(id, progress); }
}

public sealed class DurableWorker(IServiceScopeFactory scopes, DurableWorkerOptions options,
    IJobProgressHub progressHub, TimeProvider time, ILogger<DurableWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, int, Exception?> LogStarted = LoggerMessage.Define<int, int>(LogLevel.Information,
        new EventId(1201, "DurableWorkerStarted"), "Durable worker started with acquisition concurrency {AcquisitionConcurrency} and compression concurrency {CompressionConcurrency}.");
    private static readonly Action<ILogger, Exception?> LogPassFailed = LoggerMessage.Define(LogLevel.Error,
        new EventId(1202, "DurableWorkerPassFailed"), "Durable worker pass failed.");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, options.AcquisitionConcurrency, options.CompressionConcurrency, null);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunPassAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { LogPassFailed(logger, exception); }
            await Task.Delay(options.PollInterval, stoppingToken);
        }
    }

    private async Task RunPassAsync(CancellationToken token)
    {
        IReadOnlyList<BatchId> batches;
        using (var scope = scopes.CreateScope()) batches = await scope.ServiceProvider.GetRequiredService<IWorkerStore>().ListActiveBatchesAsync(token);
        foreach (var batchId in batches)
        {
            IReadOnlyList<WorkerJob> jobs;
            using (var scope = scopes.CreateScope()) jobs = await scope.ServiceProvider.GetRequiredService<IWorkerStore>().ListJobsAsync(batchId, token);
            if (jobs.Any(x => x.State is JobState.Acquiring or JobState.Probing))
            {
                await Parallel.ForEachAsync(jobs.Where(x => x.State == JobState.Acquiring),
                    new ParallelOptions { MaxDegreeOfParallelism = options.AcquisitionConcurrency, CancellationToken = token }, AcquireAsync);
                using var scope = scopes.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IWorkerStore>();
                var refreshed = await queue.ListJobsAsync(batchId, token);
                if (!refreshed.Any(x => x.State is JobState.Acquiring or JobState.Probing))
                    await queue.SetBatchStatusAsync(batchId, BatchStatus.Acquiring, BatchStatus.Processing, time.GetUtcNow(), token);
                continue;
            }

            await Parallel.ForEachAsync(jobs.Where(x => x.State == JobState.Queued),
                new ParallelOptions { MaxDegreeOfParallelism = options.CompressionConcurrency, CancellationToken = token }, CompressAsync);
            using (var scope = scopes.CreateScope())
            {
                var queue = scope.ServiceProvider.GetRequiredService<IWorkerStore>();
                var refreshed = await queue.ListJobsAsync(batchId, token);
                if (refreshed.Count > 0 && refreshed.All(x => x.State is JobState.Ready or JobState.NotBeneficial or JobState.Failed or JobState.Cancelled or JobState.Interrupted))
                    await queue.SetBatchStatusAsync(batchId, BatchStatus.Processing, BatchStatus.Completed, time.GetUtcNow(), token);
            }
        }
    }

    private async ValueTask AcquireAsync(WorkerJob candidate, CancellationToken shutdown)
    {
        using var scope = scopes.CreateScope(); var services = scope.ServiceProvider;
        var queue = services.GetRequiredService<IWorkerStore>();
        var claimed = await queue.TryClaimAcquisitionAsync(candidate.Id, candidate.Version, time.GetUtcNow(), shutdown);
        if (claimed is null) return;
        var job = claimed.Value; var version = claimed.Version;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        var monitor = MonitorCancellationAsync(job.Id, cancellation, shutdown);
        try
        {
            await queue.AppendLogAsync(job.Id, new(time.GetUtcNow(), "Information", "acquisition.started", "Original acquisition started."), shutdown);
            var source = services.GetRequiredService<IVideoSource>();
            var detail = await source.GetDetailAsync(job.Source, cancellation.Token);
            await using var download = await source.OpenOriginalAsync(job.Source, cancellation.Token);
            if (download.ContentLength is long length && !services.GetRequiredService<IDiskCapacityService>().Evaluate(length).IsAdmitted)
                throw new IOException("Available work capacity is insufficient for this acquisition.");
            var storage = services.GetRequiredService<IWorkStorage>(); var allocation = storage.Allocate(job.BatchId, job.Id, ArtifactKind.Source);
            await using var measured = new ProgressReadStream(download.Content, bytesRead =>
                progressHub.Report(job.Id, new(new TransferProgress(bytesRead, download.ContentLength), null, time.GetUtcNow())));
            var copyTask = storage.CopyToNewAsync(measured, allocation.Partial, cancellation.Token);
            while (!copyTask.IsCompleted)
            {
                var snapshot = progressHub.GetLatest(job.Id);
                if (snapshot is not null) await PersistProgressAsync(job.Id, snapshot, shutdown);
                await Task.WhenAny(copyTask, Task.Delay(options.PersistProgressInterval, cancellation.Token));
            }
            var bytes = await copyTask; await storage.FinalizeAsync(allocation.Partial, allocation.Final, cancellation.Token);
            var probe = await services.GetRequiredService<IMediaProbe>().ProbeAsync(services.GetRequiredService<IArtifactPathResolver>().ResolveExisting(allocation.Final), cancellation.Token);
            var video = probe.PrimaryVideo;
            var metadata = new VideoMetadata(detail.FileName, detail.MimeType ?? download.MimeType, bytes, probe.Duration,
                video.Width ?? throw new InvalidDataException("Primary video width is missing."), video.Height ?? throw new InvalidDataException("Primary video height is missing."),
                video.CodecName, probe.Streams.Where(x => x.CodecType == "audio").Select(x => x.CodecName).ToArray(),
                probe.CaptureTime ?? detail.TakenAt, probe.EffectiveRotation, detail.Description, detail.Latitude, detail.Longitude, detail.AlbumIds);
            job.RecordProbe(metadata, allocation.Final); job.TransitionTo(JobState.Queued, time.GetUtcNow());
            await services.GetRequiredService<ICompressionJobRepository>().UpdateAsync(job, version, shutdown);
            var done = new JobProgressSnapshot(new TransferProgress(bytes, bytes), null, time.GetUtcNow()); progressHub.Report(job.Id, done); await PersistProgressAsync(job.Id, done, shutdown);
            await queue.AppendLogAsync(job.Id, new(time.GetUtcNow(), "Information", "acquisition.completed", $"Original acquisition completed ({bytes} bytes)."), shutdown);
        }
        catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
        { await CancelClaimAsync(job, version, "acquisition.cancelled", shutdown); }
        catch (Exception exception)
        { await FailClaimAsync(job, version, "acquisition.failed", exception, shutdown); }
        finally { cancellation.Cancel(); try { await monitor; } catch (OperationCanceledException) { } }
    }

    private async ValueTask CompressAsync(WorkerJob candidate, CancellationToken shutdown)
    {
        using var scope = scopes.CreateScope(); var services = scope.ServiceProvider; var queue = services.GetRequiredService<IWorkerStore>();
        var claimed = await queue.TryClaimCompressionAsync(candidate.Id, candidate.Version, time.GetUtcNow(), shutdown); if (claimed is null) return;
        var job = claimed.Value; var version = claimed.Version;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown); var monitor = MonitorCancellationAsync(job.Id, cancellation, shutdown);
        Task progressPersistence = Task.CompletedTask;
        try
        {
            if (job.SourceArtifact is null || job.OriginalMetadata is null) throw new InvalidOperationException("Queued job has no acquired source.");
            if (!services.GetRequiredService<IDiskCapacityService>().Evaluate(job.OriginalMetadata.SizeBytes).IsAdmitted) throw new IOException("Available work capacity is insufficient for compression.");
            await queue.AppendLogAsync(job.Id, new(time.GetUtcNow(), "Information", "compression.started", "Compression started."), shutdown);
            var paths = services.GetRequiredService<IArtifactPathResolver>(); var storage = services.GetRequiredService<IWorkStorage>();
            var inputPath = paths.ResolveExisting(job.SourceArtifact); var inputProbe = await services.GetRequiredService<IMediaProbe>().ProbeAsync(inputPath, cancellation.Token);
            await SaveProbeSnapshotAsync(storage, job, ArtifactKind.InputProbe, inputProbe.RawJson, cancellation.Token);
            var video = inputProbe.PrimaryVideo; var audio = inputProbe.PrimaryAudio; var allocation = storage.Allocate(job.BatchId, job.Id, ArtifactKind.Output);
            var request = new MediaCompressionRequest(inputPath, ResolvePartialPath(paths, allocation.Partial), inputProbe.Duration,
                video.Width!.Value, video.Height!.Value, inputProbe.EffectiveRotation, video.Index, audio?.Index, audio?.CodecName, job.EffectiveOptions, video.IsHdr);
            var reporter = new Progress<CompressionProgress>(p => progressHub.Report(job.Id, new(null, p, time.GetUtcNow())));
            progressPersistence = PersistWhileRunningAsync(job.Id, cancellation.Token);
            var result = await services.GetRequiredService<IMediaCompressor>().CompressAsync(request, reporter, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!result.Succeeded) throw new InvalidOperationException($"FFmpeg failed: {result.DiagnosticTail}");
            job.TransitionTo(JobState.Validating, time.GetUtcNow()); version = await services.GetRequiredService<ICompressionJobRepository>().UpdateAsync(job, version, shutdown);
            var outputProbe = await services.GetRequiredService<IMediaProbe>().ProbeAsync(request.PartialOutputPath, cancellation.Token);
            await SaveProbeSnapshotAsync(storage, job, ArtifactKind.OutputProbe, outputProbe.RawJson, cancellation.Token);
            var findings = OutputValidationPolicy.Validate(Snapshot(inputProbe, job.OriginalMetadata), Snapshot(outputProbe), job.EffectiveOptions);
            if (findings.Any(x => x.IsBlocking))
            {
                job.RejectValidation(findings, time.GetUtcNow());
                await services.GetRequiredService<ICompressionJobRepository>().UpdateAsync(job, version, shutdown);
                await storage.DeleteKnownAsync([new(job.BatchId, job.Id, allocation.Partial)], shutdown);
                await queue.AppendLogAsync(job.Id, new(time.GetUtcNow(), "Error", "validation.failed",
                    string.Join(" ", findings.Where(x => x.IsBlocking).Select(x => $"{x.Code}: {x.Message}"))), shutdown);
                return;
            }
            var outputBytes = new FileInfo(request.PartialOutputPath).Length;
            await storage.FinalizeAsync(allocation.Partial, allocation.Final, cancellation.Token);
            job.CompleteValidation(outputBytes, allocation.Final, findings, time.GetUtcNow()); await services.GetRequiredService<ICompressionJobRepository>().UpdateAsync(job, version, shutdown);
            var latest = progressHub.GetLatest(job.Id) ?? new(null, null, time.GetUtcNow()); await PersistProgressAsync(job.Id, latest, shutdown);
            await queue.AppendLogAsync(job.Id, new(time.GetUtcNow(), "Information", "compression.completed", $"Compression completed ({outputBytes} bytes, {job.State})."), shutdown);
        }
        catch (OperationCanceledException) when (!shutdown.IsCancellationRequested) { await CancelClaimAsync(job, version, "compression.cancelled", shutdown); }
        catch (Exception exception) { await FailClaimAsync(job, version, "compression.failed", exception, shutdown); }
        finally
        {
            cancellation.Cancel();
            try { await monitor; } catch (OperationCanceledException) { }
            try { await progressPersistence; } catch (OperationCanceledException) { }
        }
    }

    private static VideoValidationSnapshot Snapshot(MediaProbeResult probe, VideoMetadata? authoritative = null)
    {
        var video = probe.PrimaryVideo;
        return new(probe.Container, probe.Duration, video.Width ?? 0, video.Height ?? 0, video.CodecName,
            authoritative?.CaptureTime ?? probe.CaptureTime, authoritative?.EffectiveRotation ?? probe.EffectiveRotation,
            authoritative?.Latitude ?? probe.Latitude, authoritative?.Longitude ?? probe.Longitude,
            authoritative is null ? probe.PrimaryAudio is not null : authoritative.AudioCodecs.Count > 0);
    }

    private static async Task SaveProbeSnapshotAsync(IWorkStorage storage, CompressionJob job,
        ArtifactKind kind, string json, CancellationToken token)
    {
        var allocation = storage.Allocate(job.BatchId, job.Id, kind);
        await storage.DeleteKnownAsync([
            new(job.BatchId, job.Id, allocation.Partial),
            new(job.BatchId, job.Id, allocation.Final)], token);
        await using (var stream = await storage.OpenCreateNewAsync(allocation.Partial, token))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await stream.WriteAsync(bytes, token);
            await stream.FlushAsync(token);
        }
        await storage.FinalizeAsync(allocation.Partial, allocation.Final, token);
    }

    private string ResolvePartialPath(IArtifactPathResolver paths, ArtifactRef partial)
    {
        // The local resolver requires existence; create and close the server-owned partial, then let FFmpeg replace it.
        using var scope = scopes.CreateScope(); var storage = scope.ServiceProvider.GetRequiredService<IWorkStorage>();
        using (storage.OpenCreateNewAsync(partial).GetAwaiter().GetResult()) { }
        return paths.ResolveExisting(partial);
    }

    private async Task MonitorCancellationAsync(JobId id, CancellationTokenSource work, CancellationToken shutdown)
    {
        while (!work.IsCancellationRequested && !shutdown.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), shutdown);
            using var scope = scopes.CreateScope(); if (await scope.ServiceProvider.GetRequiredService<IWorkerStore>().IsCancellationRequestedAsync(id, shutdown)) work.Cancel();
        }
    }
    private async Task PersistWhileRunningAsync(JobId id, CancellationToken token)
    {
        while (!token.IsCancellationRequested) { await Task.Delay(options.PersistProgressInterval, token); var p = progressHub.GetLatest(id); if (p is not null) await PersistProgressAsync(id, p, token); }
    }
    private async Task PersistProgressAsync(JobId id, JobProgressSnapshot progress, CancellationToken token)
    { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<ICompressionJobRepository>().SaveProgressAsync(id, progress, token); }
    private async Task CancelClaimAsync(CompressionJob job, long version, string code, CancellationToken token)
    {
        job.Cancel(time.GetUtcNow()); using var scope = scopes.CreateScope(); var services = scope.ServiceProvider;
        try { await services.GetRequiredService<ICompressionJobRepository>().UpdateAsync(job, version, token); } catch (PersistenceConcurrencyException) { }
        await services.GetRequiredService<IWorkerStore>().AppendLogAsync(job.Id, new(time.GetUtcNow(), "Warning", code, "The job was cancelled."), token);
    }
    private async Task FailClaimAsync(CompressionJob job, long version, string code, Exception exception, CancellationToken token)
    {
        var message = exception.Message.Length <= 1000 ? exception.Message : exception.Message[..1000];
        if (job.State is JobState.Acquiring or JobState.Probing) job.Fail(code, message, time.GetUtcNow()); else job.FailProcessing(code, message, time.GetUtcNow());
        using var scope = scopes.CreateScope(); var services = scope.ServiceProvider;
        try { await services.GetRequiredService<ICompressionJobRepository>().UpdateAsync(job, version, token); } catch (PersistenceConcurrencyException) { }
        await services.GetRequiredService<IWorkerStore>().AppendLogAsync(job.Id, new(time.GetUtcNow(), "Error", code, message), token);
    }

    private sealed class ProgressReadStream(Stream inner, Action<long> report) : Stream
    {
        private long bytesRead;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => bytesRead; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { var count = await inner.ReadAsync(buffer, token); bytesRead += count; report(bytesRead); return count; }
        public override int Read(byte[] buffer, int offset, int count) { var read = inner.Read(buffer, offset, count); bytesRead += read; report(bytesRead); return read; }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { GC.SuppressFinalize(this); return base.DisposeAsync(); }
    }
}

public static class DurableWorkerServiceCollectionExtensions
{
    public static IServiceCollection AddDurableWorker(this IServiceCollection services, DurableWorkerOptions options)
    {
        if (options.AcquisitionConcurrency is < 1 or > 16 || options.CompressionConcurrency is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(options));
        services.AddSingleton(options); services.AddSingleton<IJobProgressHub, JobProgressHub>(); services.AddHostedService<DurableWorker>(); return services;
    }
}
