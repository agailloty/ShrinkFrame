using ShrinkFrame.Domain;

namespace ShrinkFrame.Application;

public enum ArtifactKind { Source, Output, InputProbe, OutputProbe, FfmpegLog }

public sealed record ArtifactAllocation(ArtifactRef Partial, ArtifactRef Final);
public sealed record OwnedArtifact(BatchId BatchId, JobId JobId, ArtifactRef Artifact);
public sealed record ArtifactInventoryItem(JobId JobId, ArtifactRef Artifact, long SizeBytes, bool IsPartial);
public sealed record StorageInventory(long ArtifactBytes, IReadOnlyList<ArtifactInventoryItem> Artifacts);
public sealed record ArtifactDeletionResult(ArtifactRef Artifact, bool Deleted, string? ErrorCode);
public sealed record StorageDeletionReport(IReadOnlyList<ArtifactDeletionResult> Results)
{
    public bool Succeeded => Results.All(x => x.Deleted);
}

public sealed record StorageCapacity(long TotalBytes, long AvailableBytes);
public enum CapacityReason { Sufficient, InsufficientSpace, ArithmeticOverflow }
public sealed record CapacityAdmission(
    long SourceBytes,
    long RequiredBytes,
    long AvailableBytes,
    long ReserveBytes,
    CapacityReason Reason,
    bool ForceRequested)
{
    public bool HasWarning => Reason != CapacityReason.Sufficient;
    public bool RequiresOverride => Reason == CapacityReason.InsufficientSpace && !ForceRequested;
    public bool IsAdmitted => Reason == CapacityReason.Sufficient || Reason == CapacityReason.InsufficientSpace && ForceRequested;
}

public interface IWorkStorage
{
    ArtifactAllocation Allocate(BatchId batchId, JobId jobId, ArtifactKind kind);
    Task<Stream> OpenCreateNewAsync(ArtifactRef partialArtifact, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(ArtifactRef artifact, CancellationToken cancellationToken = default);
    Task<long> CopyToNewAsync(Stream source, ArtifactRef partialArtifact, CancellationToken cancellationToken = default);
    Task<long> FinalizeAsync(ArtifactRef partialArtifact, ArtifactRef finalArtifact, CancellationToken cancellationToken = default);
    Task<StorageDeletionReport> DeleteKnownAsync(IReadOnlyCollection<OwnedArtifact> artifacts, CancellationToken cancellationToken = default);
    Task<StorageInventory> InventoryAsync(IReadOnlyCollection<OwnedArtifact> artifacts, CancellationToken cancellationToken = default);
}

public interface IStorageCapacityReporter
{
    StorageCapacity GetCapacity();
}

public interface IDiskCapacityService
{
    CapacityAdmission Evaluate(long sourceBytes, bool forceRequested = false);
}

public interface IWorkStorageStartupValidator
{
    Task ValidateAsync(CancellationToken cancellationToken = default);
}
