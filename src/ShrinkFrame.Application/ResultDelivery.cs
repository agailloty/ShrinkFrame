using ShrinkFrame.Domain;

namespace ShrinkFrame.Application;

public sealed record ResultDownload(ArtifactRef Artifact, string FileName, string ContentType, long Length);
public sealed record RecompressionRequest(PresetId PresetId, CompressionOptions Options);

public interface IResultDelivery
{
    Task<ResultDownload?> GetDownloadAsync(JobId id, CancellationToken token = default);
    Task<JobId> RecompressAsync(JobId id, RecompressionRequest request, CancellationToken token = default);
    Task AuthorizeNotBeneficialPublicationAsync(JobId id, CancellationToken token = default);
}

public sealed class ResultDelivery(ICompressionJobRepository jobs, IWorkStorage storage,
    IWorkerStore worker, TimeProvider time) : IResultDelivery
{
    public async Task<ResultDownload?> GetDownloadAsync(JobId id, CancellationToken token = default)
    {
        var stored = await jobs.GetAsync(id, token);
        if (stored is null || stored.Value.State is not (JobState.Ready or JobState.NotBeneficial) ||
            stored.Value.OutputArtifact is null || stored.Value.OriginalMetadata is null) return null;
        var inventory = await storage.InventoryAsync([new(stored.Value.BatchId, id, stored.Value.OutputArtifact)], token);
        var item = inventory.Artifacts.SingleOrDefault();
        return item is null ? null : new(stored.Value.OutputArtifact,
            MediaPolicies.BuildOutputFileName(stored.Value.OriginalMetadata.FileName, stored.Value.EffectiveOptions.Suffix),
            "video/mp4", item.SizeBytes);
    }

    public async Task<JobId> RecompressAsync(JobId id, RecompressionRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = BuiltInPresets.Get(request.PresetId);
        var stored = await jobs.GetAsync(id, token) ?? throw new KeyNotFoundException("Compression result was not found.");
        var prior = stored.Value;
        if (prior.State is not (JobState.Ready or JobState.NotBeneficial) || prior.SourceArtifact is null || prior.OriginalMetadata is null)
            throw new InvalidOperationException("Only a completed result with a retained source can be recompressed.");
        var now = time.GetUtcNow();
        var replacement = new CompressionJob(JobId.New(), prior.BatchId, prior.Source, request.PresetId, request.Options, now);
        replacement.TransitionTo(JobState.Acquiring, now);
        replacement.TransitionTo(JobState.Probing, now);
        replacement.RecordProbe(prior.OriginalMetadata, prior.SourceArtifact);
        replacement.TransitionTo(JobState.Queued, now);
        await jobs.AddAsync(replacement, token);
        await worker.SetBatchStatusAsync(prior.BatchId, BatchStatus.Completed, BatchStatus.Processing, now, token);
        await worker.SetBatchStatusAsync(prior.BatchId, BatchStatus.Cancelled, BatchStatus.Processing, now, token);
        return replacement.Id;
    }

    public async Task AuthorizeNotBeneficialPublicationAsync(JobId id, CancellationToken token = default)
    {
        var stored = await jobs.GetAsync(id, token) ?? throw new KeyNotFoundException("Compression result was not found.");
        stored.Value.AuthorizeNotBeneficialPublication();
        await jobs.UpdateAsync(stored.Value, stored.Version, token);
    }
}
