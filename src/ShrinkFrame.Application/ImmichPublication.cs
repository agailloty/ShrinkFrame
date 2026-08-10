using System.Security.Cryptography;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Application;

public sealed record ImmichUploadRequest(string ClientAttemptId, string Sha1Checksum, string FileName,
    DateTimeOffset FileCreatedAt, DateTimeOffset FileModifiedAt, Func<CancellationToken, Task<Stream>> OpenContent);
public sealed record ImmichUploadCheck(string? ExistingAssetId, bool IsTrashed);
public sealed record ImmichUploadResult(string AssetId, string Status);

public interface IImmichPublicationTransport
{
    Task<ImmichUploadCheck> CheckExistingAsync(ConnectionId connectionId, string clientAttemptId,
        string sha1Checksum, CancellationToken cancellationToken = default);
    Task<ImmichUploadResult> UploadAsync(ConnectionId connectionId, ImmichUploadRequest request,
        CancellationToken cancellationToken = default);
    Task AddToAlbumAsync(ConnectionId connectionId, string albumId, string assetId,
        CancellationToken cancellationToken = default);
}

public sealed class ImmichPublicationTransportException(string code, string message, bool ambiguousUpload = false,
    Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
    public bool AmbiguousUpload { get; } = ambiguousUpload;
}

public sealed record PublicationSelection(JobId JobId, bool ForceNotBeneficial);
public sealed record PublicationResult(JobId JobId, PublicationState State, string? AssetId,
    IReadOnlyList<string> PendingAlbumIds, IReadOnlyList<string> Warnings, string? ErrorCode);

public interface IImmichPublicationService
{
    Task<PublicationResult?> GetAsync(JobId jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublicationResult>> PublishAsync(BatchId batchId, ConnectionId destination,
        IReadOnlyCollection<PublicationSelection> selection, CancellationToken cancellationToken = default);
}

public sealed class ImmichPublicationService(IBatchRepository batches, ICompressionJobRepository jobs,
    IPublicationCheckpointRepository checkpoints, IImmichConnectionManager connectionManager,
    IImmichPublicationTransport transport, IWorkStorage storage, TimeProvider time) : IImmichPublicationService
{
    public async Task<PublicationResult?> GetAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken);
        if (job is null) return null;
        var checkpoint = await checkpoints.GetAsync(jobId, cancellationToken);
        var error = checkpoint?.UploadAmbiguous == true ? "publication.upload.ambiguous"
            : job.Value.PublicationState == PublicationState.PartiallyPublished ? "publication.album.sync_failed" : null;
        return new(jobId, job.Value.PublicationState, job.Value.PublishedAssetId,
            checkpoint?.PendingAlbumIds ?? [], checkpoint?.Warnings ?? [], error);
    }

    public async Task<IReadOnlyList<PublicationResult>> PublishAsync(BatchId batchId, ConnectionId destination,
        IReadOnlyCollection<PublicationSelection> selection, CancellationToken cancellationToken = default)
    {
        var batch = await batches.GetAsync(batchId, cancellationToken) ?? throw new KeyNotFoundException("Batch was not found.");
        if (batch.SourceKind == SourceKind.Immich && batch.ConnectionId != destination)
            throw new InvalidOperationException("Immich results can only publish to their source connection.");
        var available = (await connectionManager.ListAsync(cancellationToken)).SingleOrDefault(x => x.Id == destination);
        if (available is null || !available.Enabled || available.Compatibility != CompatibilityResult.Compatible || !available.Capabilities.CanPublish)
            throw new InvalidOperationException("Choose an enabled, compatible, publish-capable Immich connection.");
        var selected = selection.GroupBy(x => x.JobId).Select(x => x.Last()).ToArray();
        if (selected.Length == 0) throw new InvalidOperationException("Select at least one result to publish.");
        var batchJobs = (await jobs.ListByBatchAsync(batchId, cancellationToken)).ToDictionary(x => x.Value.Id);
        var results = new List<PublicationResult>(selected.Length);
        foreach (var item in selected)
        {
            if (!batchJobs.TryGetValue(item.JobId, out var stored)) throw new InvalidOperationException("A selected result does not belong to this batch.");
            results.Add(await PublishOneAsync(stored, destination, item.ForceNotBeneficial, cancellationToken));
        }
        return results;
    }

    private async Task<PublicationResult> PublishOneAsync(Versioned<CompressionJob> stored, ConnectionId destination,
        bool force, CancellationToken token)
    {
        var job = stored.Value;
        if (job.State is not (JobState.Ready or JobState.NotBeneficial) || job.OutputArtifact is null || job.OriginalMetadata?.CaptureTime is null)
            return new(job.Id, job.PublicationState, job.PublishedAssetId, [], [], "publication.result.invalid");
        if (job.State == JobState.NotBeneficial && force && !job.NotBeneficialPublicationOverride)
        {
            job.AuthorizeNotBeneficialPublication();
            stored = new(job, await jobs.UpdateAsync(job, stored.Version, token));
        }
        if (job.State == JobState.NotBeneficial && !job.NotBeneficialPublicationOverride)
            return new(job.Id, job.PublicationState, job.PublishedAssetId, [], [], "publication.force.required");
        if (job.PublicationState == PublicationState.Published)
            return new(job.Id, job.PublicationState, job.PublishedAssetId, [], [], null);

        var checkpoint = await checkpoints.GetAsync(job.Id, token);
        var checksum = checkpoint?.Sha1Checksum ?? await ComputeSha1Async(job.OutputArtifact, token);
        var warnings = checkpoint?.Warnings.ToArray() ?? MetadataWarnings(job.OriginalMetadata);
        var pending = checkpoint?.PendingAlbumIds.ToArray() ?? job.OriginalMetadata.AlbumIds.Distinct(StringComparer.Ordinal).ToArray();
        checkpoint ??= new(job.Id, destination, Guid.NewGuid().ToString("D"), checksum, false, pending, warnings);
        if (checkpoint.DestinationConnectionId != destination)
            return new(job.Id, job.PublicationState, job.PublishedAssetId, pending, warnings, "publication.destination.changed");
        await checkpoints.UpsertAsync(checkpoint, token);

        var started = time.GetUtcNow();
        job.BeginPublication(started);
        var version = await jobs.UpdateAsync(job, stored.Version, token);
        try
        {
            if (job.PublishedAssetId is null)
            {
                var check = await transport.CheckExistingAsync(destination, checkpoint.ClientAttemptId, checksum, token);
                if (check.IsTrashed) throw new ImmichPublicationTransportException("publication.duplicate.trashed", "The matching Immich asset is trashed; restore it before retrying.");
                var assetId = check.ExistingAssetId;
                if (assetId is null)
                {
                    var name = MediaPolicies.BuildOutputFileName(job.OriginalMetadata.FileName, job.EffectiveOptions.Suffix);
                    var upload = new ImmichUploadRequest(checkpoint.ClientAttemptId, checksum, name,
                        job.OriginalMetadata.CaptureTime.Value, job.OriginalMetadata.FileModifiedTime ?? job.OriginalMetadata.CaptureTime.Value,
                        ct => storage.OpenReadAsync(job.OutputArtifact, ct));
                    assetId = (await transport.UploadAsync(destination, upload, token)).AssetId;
                }
                job.RecordPublishedAsset(assetId);
                version = await jobs.UpdateAsync(job, version, token);
                checkpoint = checkpoint with { UploadAmbiguous = false };
                await checkpoints.UpsertAsync(checkpoint, token);
            }

            foreach (var albumId in pending.ToArray())
            {
                try
                {
                    await transport.AddToAlbumAsync(destination, albumId, job.PublishedAssetId!, token);
                    pending = pending.Where(x => !string.Equals(x, albumId, StringComparison.Ordinal)).ToArray();
                    checkpoint = checkpoint with { PendingAlbumIds = pending };
                    await checkpoints.UpsertAsync(checkpoint, token);
                }
                catch (ImmichPublicationTransportException) { break; }
            }
            job.CompletePublication(pending.Length == 0, time.GetUtcNow());
            version = await jobs.UpdateAsync(job, version, token);
            if (pending.Length == 0 && job.Source.Kind == SourceKind.Immich && job.SourceArtifact is not null)
            {
                var report = await storage.DeleteKnownAsync([new(job.BatchId, job.Id, job.SourceArtifact)], token);
                if (report.Succeeded)
                {
                    job.ReleasePublishedImmichSource(time.GetUtcNow());
                    await jobs.UpdateAsync(job, version, token);
                }
                else
                {
                    warnings = [.. warnings, "publication.source_cleanup.failed"];
                    checkpoint = checkpoint with { Warnings = warnings };
                    await checkpoints.UpsertAsync(checkpoint, token);
                }
            }
            await jobs.AddPublicationAttemptAsync(new(Guid.NewGuid(), job.Id, started, time.GetUtcNow(), job.PublicationState,
                pending.Length == 0 ? null : "publication.album.sync_failed"), token);
            return new(job.Id, job.PublicationState, job.PublishedAssetId, pending, warnings,
                pending.Length == 0 ? null : "publication.album.sync_failed");
        }
        catch (ImmichPublicationTransportException exception)
        {
            if (exception.AmbiguousUpload)
            {
                checkpoint = checkpoint with { UploadAmbiguous = true };
                await checkpoints.UpsertAsync(checkpoint, token);
            }
            job.FailPublication(time.GetUtcNow());
            await jobs.UpdateAsync(job, version, token);
            await jobs.AddPublicationAttemptAsync(new(Guid.NewGuid(), job.Id, started, time.GetUtcNow(), PublicationState.Failed, exception.Code), token);
            return new(job.Id, PublicationState.Failed, job.PublishedAssetId, pending, warnings, exception.Code);
        }
    }

    private async Task<string> ComputeSha1Async(ArtifactRef artifact, CancellationToken token)
    {
        await using var stream = await storage.OpenReadAsync(artifact, token);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[128 * 1024];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, token);
            if (count == 0) break;
            hash.AppendData(buffer, 0, count);
        }
        return Convert.ToBase64String(hash.GetHashAndReset());
    }

    private static string[] MetadataWarnings(VideoMetadata metadata) =>
        !string.IsNullOrWhiteSpace(metadata.Description) || metadata.Latitude is not null || metadata.Longitude is not null
            ? ["publication.metadata.not_guaranteed"] : [];
}
