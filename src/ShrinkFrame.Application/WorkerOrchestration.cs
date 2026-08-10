using ShrinkFrame.Domain;

namespace ShrinkFrame.Application;

public sealed record SourceDownload(Stream Content, long? ContentLength, string FileName, string MimeType) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public interface IVideoSource
{
    Task<SourceDownload> OpenOriginalAsync(VideoSourceRef source, CancellationToken token = default);
    Task<ImmichVideoDetail> GetDetailAsync(VideoSourceRef source, CancellationToken token = default);
}

public sealed record WorkerJob(JobId Id, BatchId BatchId, SourceKind SourceKind, JobState State, long Version);
public sealed record JobLogEntry(DateTimeOffset At, string Level, string Code, string Message);
public sealed record JobRuntimeView(JobId JobId, JobState State, JobProgressSnapshot? Progress,
    IReadOnlyList<JobLogEntry> Logs);
public sealed record BatchProgressView(BatchId BatchId, BatchStatus Status, int TotalJobs, int FinishedJobs,
    int FailedJobs, int CancelledJobs, decimal Percentage);

public interface IWorkerStore
{
    Task<IReadOnlyList<BatchId>> ListActiveBatchesAsync(CancellationToken token = default);
    Task<IReadOnlyList<WorkerJob>> ListJobsAsync(BatchId batchId, CancellationToken token = default);
    Task<Versioned<CompressionJob>?> TryClaimAcquisitionAsync(JobId id, long version, DateTimeOffset now, CancellationToken token = default);
    Task<Versioned<CompressionJob>?> TryClaimCompressionAsync(JobId id, long version, DateTimeOffset now, CancellationToken token = default);
    Task<bool> IsCancellationRequestedAsync(JobId id, CancellationToken token = default);
    Task AppendLogAsync(JobId id, JobLogEntry entry, CancellationToken token = default);
    Task<JobRuntimeView?> GetRuntimeAsync(JobId id, CancellationToken token = default);
    Task<BatchProgressView?> GetBatchProgressAsync(BatchId id, CancellationToken token = default);
    Task SetBatchStatusAsync(BatchId id, BatchStatus expected, BatchStatus target, DateTimeOffset now, CancellationToken token = default);
    Task RequestJobCancellationAsync(JobId id, DateTimeOffset now, CancellationToken token = default);
    Task RequestBatchCancellationAsync(BatchId id, DateTimeOffset now, CancellationToken token = default);
    Task RetryAsync(JobId id, DateTimeOffset now, CancellationToken token = default);
}

public interface IJobProgressHub
{
    event Action<JobId, JobProgressSnapshot>? Changed;
    JobProgressSnapshot? GetLatest(JobId id);
    void Report(JobId id, JobProgressSnapshot progress);
}
