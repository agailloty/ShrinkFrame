using Microsoft.EntityFrameworkCore;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Persistence;

public sealed class OperationsService(
    IDbContextFactory<ShrinkFrameDbContext> contexts,
    IWorkStorage storage,
    IStorageCapacityReporter capacity) : IOperationsService
{
    private static readonly string[] ActiveStates =
    [
        nameof(JobState.Acquiring), nameof(JobState.Probing), nameof(JobState.Queued),
        nameof(JobState.Compressing), nameof(JobState.Validating)
    ];

    public async Task<DashboardView> GetDashboardAsync(CancellationToken token = default)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        var active = await db.Jobs.CountAsync(x => ActiveStates.Contains(x.State)
            || x.PublicationState == nameof(PublicationState.Publishing), token);
        var queued = await db.Jobs.CountAsync(x => x.State == nameof(JobState.Queued), token);
        var connections = await db.Connections.AsNoTracking().Where(x => x.Enabled)
            .Select(x => x.Compatibility).ToListAsync(token);
        var recent = (await SearchCoreAsync(db, new(), token)).Take(5).ToArray();
        var storageView = await BuildStorageAsync(db, token);
        return new(storageView.Summary, active, queued,
            connections.Count(x => x == nameof(CompatibilityResult.Compatible)),
            connections.Count(x => x != nameof(CompatibilityResult.Compatible)), recent);
    }

    public async Task<IReadOnlyList<BatchHistoryItem>> SearchBatchesAsync(BatchHistoryFilter filter, CancellationToken token = default)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        return await SearchCoreAsync(db, filter, token);
    }

    public async Task<BatchOperationsView?> GetBatchAsync(BatchId id, CancellationToken token = default)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        var entity = await db.Batches.AsNoTracking().Include(x => x.Jobs).ThenInclude(x => x.Findings)
            .Include(x => x.Jobs).ThenInclude(x => x.Logs)
            .SingleOrDefaultAsync(x => x.Id == id.Value, token);
        if (entity is null) return null;
        var history = ToHistory(entity, new Dictionary<string, long>());
        var allReferences = await db.Jobs.AsNoTracking().Select(x => new { x.Id, x.SourceArtifactKey, x.OutputArtifactKey }).ToListAsync(token);
        var owned = entity.Jobs.SelectMany(KnownArtifacts).ToArray();
        var inventory = await storage.InventoryAsync(owned, token);
        var sizes = inventory.Artifacts.ToDictionary(x => x.Artifact.Key, StringComparer.Ordinal);
        var jobs = entity.Jobs.OrderBy(x => x.CreatedAt).Select(job =>
        {
            var artifacts = KnownArtifacts(job).Select(x => sizes.TryGetValue(x.Artifact.Key, out var item)
                ? new ArtifactView(x.Artifact, item.SizeBytes, true, item.IsPartial)
                : new ArtifactView(x.Artifact, 0, false, IsPartial(x.Artifact))).ToArray();
            var output = job.OutputArtifactKey is not null && sizes.TryGetValue(job.OutputArtifactKey, out var outputItem)
                ? outputItem.SizeBytes : (long?)null;
            return new JobOperationsView(new(job.Id), job.MetadataFileName ?? job.SourceId,
                Enum.Parse<JobState>(job.State), Enum.Parse<PublicationState>(job.PublicationState), job.UpdatedAt,
                new(job.PresetId), job.MetadataSizeBytes, output,
                job.Findings.Select(x => new ValidationFinding(x.Code, Enum.Parse<FindingSeverity>(x.Severity), x.Message)).ToArray(),
                job.Logs.OrderByDescending(x => x.At).Take(50).OrderBy(x => x.At)
                    .Select(x => new JobLogEntry(x.At, x.Level, x.Code, x.Message)).ToArray(),
                artifacts, CanRetry(job.State), CanDelete(job.State, job.PublicationState)
                    && !IsReferencedByAnotherJob(job, allReferences.Select(x => (x.Id, x.SourceArtifactKey, x.OutputArtifactKey))));
        }).ToArray();
        return new(history with { OutputBytes = jobs.Sum(x => x.OutputBytes ?? 0) }, jobs);
    }

    public async Task<StoragePageView> GetStorageAsync(CancellationToken token = default)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        return await BuildStorageAsync(db, token);
    }

    public async Task<JobDeletionResult> DeleteJobAsync(JobId id, bool confirmed, CancellationToken token = default)
    {
        if (!confirmed) return new(false, "storage.confirmation.required", "Explicit confirmation is required.", []);
        await using var db = await contexts.CreateDbContextAsync(token);
        var job = await db.Jobs.Include(x => x.Batch).SingleOrDefaultAsync(x => x.Id == id.Value, token);
        if (job is null) return new(false, "storage.job.not_found", "The job no longer exists.", []);
        if (!CanDelete(job.State, job.PublicationState))
            return new(false, "storage.job.active", "Active or publishing jobs cannot be deleted.", []);

        var keys = KnownArtifacts(job).Select(x => x.Artifact.Key).ToArray();
        var referenced = await db.Jobs.AsNoTracking().AnyAsync(x => x.Id != job.Id
            && ((x.SourceArtifactKey != null && keys.Contains(x.SourceArtifactKey))
                || (x.OutputArtifactKey != null && keys.Contains(x.OutputArtifactKey))), token);
        if (referenced)
            return new(false, "storage.job.referenced", "Another job still references an artifact owned by this job.", []);

        var report = await storage.DeleteKnownAsync(KnownArtifacts(job), token);
        if (!report.Succeeded)
            return new(false, "storage.delete.failed", "A filesystem artifact could not be deleted. The history was retained; resolve the filesystem condition and retry.", report.Results);

        var batchId = job.BatchId;
        db.Jobs.Remove(job);
        await db.SaveChangesAsync(token);
        if (!await db.Jobs.AnyAsync(x => x.BatchId == batchId, token))
        {
            var emptyBatch = await db.Batches.SingleOrDefaultAsync(x => x.Id == batchId, token);
            if (emptyBatch is not null) { db.Batches.Remove(emptyBatch); await db.SaveChangesAsync(token); }
        }
        return new(true, "storage.delete.succeeded", "The selected job artifacts and history were deleted.", report.Results);
    }

    private async Task<StoragePageView> BuildStorageAsync(ShrinkFrameDbContext db, CancellationToken token)
    {
        var jobs = await db.Jobs.AsNoTracking().Include(x => x.Batch).OrderByDescending(x => x.UpdatedAt).ToListAsync(token);
        var owned = jobs.SelectMany(KnownArtifacts).ToArray();
        var known = await storage.InventoryAsync(owned, token);
        var all = await storage.InventoryAllAsync(token);
        var knownKeys = owned.Select(x => x.Artifact.Key).ToHashSet(StringComparer.Ordinal);
        var orphans = all.Where(x => !knownKeys.Contains(x.Artifact.Key))
            .Select(x => new OrphanArtifactView(x.Artifact, x.SizeBytes, x.LastModifiedAt)).ToArray();
        var byJob = known.Artifacts.GroupBy(x => x.JobId).ToDictionary(x => x.Key,
            x => (Bytes: x.Sum(y => y.SizeBytes), Count: x.Count()));
        var references = jobs.Select(x => (x.Id, x.SourceArtifactKey, x.OutputArtifactKey)).ToArray();
        var rows = jobs.Select(x =>
        {
            var usage = byJob.GetValueOrDefault(new JobId(x.Id));
            return new StorageJobView(new(x.BatchId), x.Batch?.Name ?? "Unknown batch", new(x.Id),
                x.MetadataFileName ?? x.SourceId, Enum.Parse<JobState>(x.State), x.UpdatedAt,
                usage.Bytes, usage.Count, CanDelete(x.State, x.PublicationState)
                    && !IsReferencedByAnotherJob(x, references));
        }).ToArray();
        var disk = capacity.GetCapacity();
        var orphanBytes = orphans.Sum(x => x.SizeBytes);
        return new(new(disk.TotalBytes, disk.AvailableBytes, known.ArtifactBytes + orphanBytes,
            orphanBytes, orphans.Length), rows, orphans);
    }

    private async Task<IReadOnlyList<BatchHistoryItem>> SearchCoreAsync(
        ShrinkFrameDbContext db, BatchHistoryFilter filter, CancellationToken token)
    {
        var query = db.Batches.AsNoTracking().Include(x => x.Jobs).AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(x => EF.Functions.Like(x.Name, $"%{search}%")
                || x.Jobs.Any(j => j.MetadataFileName != null && EF.Functions.Like(j.MetadataFileName, $"%{search}%")));
        }
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value.ToString());
        if (filter.Source.HasValue) query = query.Where(x => x.SourceKind == filter.Source.Value.ToString());
        if (filter.From.HasValue) query = query.Where(x => x.CreatedAt >= filter.From.Value);
        if (filter.To.HasValue) query = query.Where(x => x.CreatedAt < filter.To.Value);
        var values = await query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id).ToListAsync(token);
        var owned = values.SelectMany(x => x.Jobs).SelectMany(KnownArtifacts).ToArray();
        var inventory = await storage.InventoryAsync(owned, token);
        var sizes = inventory.Artifacts.ToDictionary(x => x.Artifact.Key, x => x.SizeBytes, StringComparer.Ordinal);
        return values.Select(x => ToHistory(x, sizes)).ToArray();
    }

    private static BatchHistoryItem ToHistory(BatchEntity batch, Dictionary<string, long> outputSizes)
    {
        var source = batch.Jobs.Sum(x => x.MetadataSizeBytes ?? 0);
        var output = batch.Jobs.Sum(x => x.OutputArtifactKey is not null && outputSizes.TryGetValue(x.OutputArtifactKey, out var size) ? size : 0);
        decimal? reduction = source > 0 && output > 0 ? 100m * (source - output) / source : null;
        var presets = batch.Jobs.Select(x => x.PresetId).Distinct().ToArray();
        var publication = batch.Jobs.Count == 0 ? PublicationState.NotRequested
            : batch.Jobs.Select(x => Enum.Parse<PublicationState>(x.PublicationState)).Max();
        return new(new(batch.Id), batch.Name, Enum.Parse<SourceKind>(batch.SourceKind),
            Enum.Parse<BatchStatus>(batch.Status), batch.CreatedAt, batch.UpdatedAt, batch.Jobs.Count,
            source, output, reduction, presets.Length == 1 ? presets[0] : presets.Length == 0 ? "—" : "Mixed", publication);
    }

    private static OwnedArtifact[] KnownArtifacts(JobEntity job)
    {
        var batch = new BatchId(job.BatchId); var id = new JobId(job.Id);
        return Enum.GetValues<ArtifactKind>().SelectMany(kind =>
        {
            var allocation = ArtifactKeys(batch, id, kind);
            return new[] { new OwnedArtifact(batch, id, allocation.Partial), new OwnedArtifact(batch, id, allocation.Final) };
        }).DistinctBy(x => x.Artifact.Key).ToArray();
    }

    private static ArtifactAllocation ArtifactKeys(BatchId batch, JobId job, ArtifactKind kind)
    {
        var prefix = $"batches/{batch.Value:N}/jobs/{job.Value:N}";
        var (directory, file) = kind switch
        {
            ArtifactKind.Source => ("source", "input.bin"), ArtifactKind.Output => ("output", "result.mp4"),
            ArtifactKind.InputProbe => ("probe", "input.json"), ArtifactKind.OutputProbe => ("probe", "output.json"),
            ArtifactKind.FfmpegLog => ("logs", "ffmpeg.log"), _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var dot = file.LastIndexOf('.'); var partial = file[..dot] + ".partial" + file[dot..];
        return new(new($"{prefix}/{directory}/{partial}"), new($"{prefix}/{directory}/{file}"));
    }

    private static bool CanRetry(string state) => state is nameof(JobState.Failed) or nameof(JobState.Cancelled) or nameof(JobState.Interrupted);
    private static bool CanDelete(string state, string publication) => !ActiveStates.Contains(state)
        && publication != nameof(PublicationState.Publishing);
    private static bool IsReferencedByAnotherJob(JobEntity owner,
        IEnumerable<(Guid Id, string? SourceArtifactKey, string? OutputArtifactKey)> references)
    {
        var owned = KnownArtifacts(owner).Select(x => x.Artifact.Key).ToHashSet(StringComparer.Ordinal);
        return references.Any(x => x.Id != owner.Id
            && (x.SourceArtifactKey is not null && owned.Contains(x.SourceArtifactKey)
                || x.OutputArtifactKey is not null && owned.Contains(x.OutputArtifactKey)));
    }
    private static bool IsPartial(ArtifactRef artifact) => artifact.Key.Split('/').Last().Contains(".partial", StringComparison.Ordinal);
}
