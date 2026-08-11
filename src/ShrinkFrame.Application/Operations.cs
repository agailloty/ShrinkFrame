using ShrinkFrame.Domain;

namespace ShrinkFrame.Application;

public sealed record DashboardView(StorageSummaryView Storage, int ActiveJobs, int QueuedJobs,
    int HealthyConnections, int UnhealthyConnections, IReadOnlyList<BatchHistoryItem> RecentBatches);
public sealed record StorageSummaryView(long TotalBytes, long FreeBytes, long ApplicationBytes,
    long OrphanBytes, int OrphanCount);
public sealed record BatchHistoryFilter(string? Search = null, BatchStatus? Status = null,
    SourceKind? Source = null, DateTimeOffset? From = null, DateTimeOffset? To = null);
public sealed record BatchHistoryItem(BatchId Id, string Name, SourceKind Source, BatchStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int JobCount, long SourceBytes,
    long OutputBytes, decimal? ReductionPercent, string Preset, PublicationState Publication);
public sealed record ArtifactView(ArtifactRef Key, long SizeBytes, bool Exists, bool IsPartial);
public sealed record JobOperationsView(JobId Id, string FileName, JobState State,
    PublicationState Publication, DateTimeOffset UpdatedAt, PresetId Preset, long? SourceBytes,
    long? OutputBytes, IReadOnlyList<ValidationFinding> Findings, IReadOnlyList<JobLogEntry> Logs,
    IReadOnlyList<ArtifactView> Artifacts, bool CanRetry, bool CanDelete,
    bool NotBeneficialPublicationOverride, string? PublishedAssetId);
public sealed record BatchOperationsView(BatchHistoryItem Batch, ConnectionId? ConnectionId,
    IReadOnlyList<JobOperationsView> Jobs);
public sealed record StorageJobView(BatchId BatchId, string BatchName, JobId JobId, string FileName,
    JobState State, DateTimeOffset UpdatedAt, long ArtifactBytes, int ArtifactCount, bool CanDelete);
public sealed record OrphanArtifactView(ArtifactRef Key, long SizeBytes, DateTimeOffset LastModifiedAt);
public sealed record StoragePageView(StorageSummaryView Summary, IReadOnlyList<StorageJobView> Jobs,
    IReadOnlyList<OrphanArtifactView> Orphans);
public sealed record JobDeletionResult(bool Succeeded, string Code, string Message,
    IReadOnlyList<ArtifactDeletionResult> Artifacts);

public interface IOperationsService
{
    Task<DashboardView> GetDashboardAsync(CancellationToken token = default);
    Task<IReadOnlyList<BatchHistoryItem>> SearchBatchesAsync(BatchHistoryFilter filter, CancellationToken token = default);
    Task<BatchOperationsView?> GetBatchAsync(BatchId id, CancellationToken token = default);
    Task<StoragePageView> GetStorageAsync(CancellationToken token = default);
    Task<JobDeletionResult> DeleteJobAsync(JobId id, bool confirmed, CancellationToken token = default);
}
