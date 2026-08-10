using Microsoft.EntityFrameworkCore;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Persistence;

public sealed class WorkerStore(ShrinkFrameDbContext db) : IWorkerStore
{
    private static readonly string[] Terminal = [nameof(JobState.Ready), nameof(JobState.NotBeneficial), nameof(JobState.Failed), nameof(JobState.Cancelled), nameof(JobState.Interrupted)];

    public async Task<IReadOnlyList<BatchId>> ListActiveBatchesAsync(CancellationToken token = default)
        => (await db.Batches.AsNoTracking().Where(x => x.Status == nameof(BatchStatus.Acquiring) || x.Status == nameof(BatchStatus.Processing))
            .OrderBy(x => x.CreatedAt).Select(x => x.Id).ToListAsync(token)).Select(BatchId.From).ToArray();

    public async Task<IReadOnlyList<WorkerJob>> ListJobsAsync(BatchId batchId, CancellationToken token = default)
        => (await db.Jobs.AsNoTracking().Where(x => x.BatchId == batchId.Value).OrderBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.BatchId, x.SourceKind, x.State, x.Version }).ToListAsync(token))
            .Select(x => new WorkerJob(JobId.From(x.Id), BatchId.From(x.BatchId), Enum.Parse<SourceKind>(x.SourceKind), Enum.Parse<JobState>(x.State), x.Version)).ToArray();

    public Task<Versioned<CompressionJob>?> TryClaimAcquisitionAsync(JobId id, long version, DateTimeOffset now, CancellationToken token = default)
        => ClaimAsync(id, JobState.Acquiring, JobState.Probing, version, now, token);

    public Task<Versioned<CompressionJob>?> TryClaimCompressionAsync(JobId id, long version, DateTimeOffset now, CancellationToken token = default)
        => ClaimAsync(id, JobState.Queued, JobState.Compressing, version, now, token);

    private async Task<Versioned<CompressionJob>?> ClaimAsync(JobId id, JobState expected, JobState target, long version, DateTimeOffset now, CancellationToken token)
    {
        var affected = await db.Jobs.Where(x => x.Id == id.Value && x.State == expected.ToString() && x.Version == version && !x.CancellationRequested)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, target.ToString()).SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.Version, version + 1), token);
        if (affected != 1) return null;
        db.ChangeTracker.Clear();
        var entity = await db.Jobs.AsNoTracking().Include(x => x.AudioCodecs).Include(x => x.Albums).Include(x => x.Findings).SingleAsync(x => x.Id == id.Value, token);
        return new(PersistenceMapper.ToDomain(entity), entity.Version);
    }

    public Task<bool> IsCancellationRequestedAsync(JobId id, CancellationToken token = default)
        => db.Jobs.AsNoTracking().Where(x => x.Id == id.Value).Select(x => x.CancellationRequested).SingleAsync(token);

    public async Task AppendLogAsync(JobId id, JobLogEntry entry, CancellationToken token = default)
    {
        db.JobLogs.Add(new() { JobId = id.Value, At = entry.At, Level = entry.Level[..Math.Min(16, entry.Level.Length)], Code = entry.Code[..Math.Min(100, entry.Code.Length)], Message = entry.Message[..Math.Min(1000, entry.Message.Length)] });
        await db.SaveChangesAsync(token);
        var old = await db.JobLogs.Where(x => x.JobId == id.Value).OrderByDescending(x => x.At).ThenByDescending(x => x.Id).Skip(100).Select(x => x.Id).ToListAsync(token);
        if (old.Count > 0) await db.JobLogs.Where(x => old.Contains(x.Id)).ExecuteDeleteAsync(token);
    }

    public async Task<JobRuntimeView?> GetRuntimeAsync(JobId id, CancellationToken token = default)
    {
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id.Value, token); if (job is null) return null;
        var p = await db.JobProgress.AsNoTracking().SingleOrDefaultAsync(x => x.JobId == id.Value, token);
        JobProgressSnapshot? progress = p is null ? null : new(
            p.TransferBytes.HasValue ? new(p.TransferBytes.Value, p.TransferTotalBytes) : null,
            p.ProcessedTicks.HasValue ? new((decimal?)p.CompressionPercentage, TimeSpan.FromTicks(p.ProcessedTicks.Value), (decimal?)p.Speed,
                TimeSpan.FromTicks(p.ElapsedTicks ?? 0), p.EstimatedRemainingTicks.HasValue ? TimeSpan.FromTicks(p.EstimatedRemainingTicks.Value) : null,
                (decimal?)p.FramesPerSecond, p.BitrateBitsPerSecond, p.OutputBytes) : null, p.UpdatedAt);
        var logs = await db.JobLogs.AsNoTracking().Where(x => x.JobId == id.Value).OrderBy(x => x.At).ThenBy(x => x.Id)
            .Select(x => new JobLogEntry(x.At, x.Level, x.Code, x.Message)).ToListAsync(token);
        return new(id, Enum.Parse<JobState>(job.State), progress, logs);
    }

    public async Task<BatchProgressView?> GetBatchProgressAsync(BatchId id, CancellationToken token = default)
    {
        var batch = await db.Batches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id.Value, token); if (batch is null) return null;
        var states = await db.Jobs.AsNoTracking().Where(x => x.BatchId == id.Value).Select(x => x.State).ToListAsync(token);
        var finished = states.Count(x => Terminal.Contains(x)); var total = states.Count;
        return new(id, Enum.Parse<BatchStatus>(batch.Status), total, finished, states.Count(x => x == nameof(JobState.Failed)),
            states.Count(x => x == nameof(JobState.Cancelled)), total == 0 ? 0 : decimal.Round(100m * finished / total, 1));
    }

    public Task SetBatchStatusAsync(BatchId id, BatchStatus expected, BatchStatus target, DateTimeOffset now, CancellationToken token = default)
        => db.Batches.Where(x => x.Id == id.Value && x.Status == expected.ToString()).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, target.ToString()).SetProperty(x => x.UpdatedAt, now), token);

    public async Task RequestJobCancellationAsync(JobId id, DateTimeOffset now, CancellationToken token = default)
    {
        await db.Jobs.Where(x => x.Id == id.Value && (x.State == nameof(JobState.Draft) || x.State == nameof(JobState.Queued)))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, nameof(JobState.Cancelled)).SetProperty(x => x.CancellationRequested, true).SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.Version, x => x.Version + 1), token);
        await db.Jobs.Where(x => x.Id == id.Value && (x.State == nameof(JobState.Acquiring) || x.State == nameof(JobState.Probing) || x.State == nameof(JobState.Compressing) || x.State == nameof(JobState.Validating)))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CancellationRequested, true).SetProperty(x => x.UpdatedAt, now), token);
    }
    public async Task RequestBatchCancellationAsync(BatchId id, DateTimeOffset now, CancellationToken token = default)
    {
        await db.Batches.Where(x => x.Id == id.Value && x.Status != nameof(BatchStatus.Completed)).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, nameof(BatchStatus.Cancelled)).SetProperty(x => x.UpdatedAt, now), token);
        await db.Jobs.Where(x => x.BatchId == id.Value && (x.State == nameof(JobState.Draft) || x.State == nameof(JobState.Queued)))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, nameof(JobState.Cancelled)).SetProperty(x => x.CancellationRequested, true).SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.Version, x => x.Version + 1), token);
        await db.Jobs.Where(x => x.BatchId == id.Value && (x.State == nameof(JobState.Acquiring) || x.State == nameof(JobState.Probing) || x.State == nameof(JobState.Compressing) || x.State == nameof(JobState.Validating)))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CancellationRequested, true).SetProperty(x => x.UpdatedAt, now), token);
    }
    public async Task RetryAsync(JobId id, DateTimeOffset now, CancellationToken token = default)
    {
        var entity = await db.Jobs.Include(x => x.AudioCodecs).Include(x => x.Albums).Include(x => x.Findings).SingleAsync(x => x.Id == id.Value, token);
        var job = PersistenceMapper.ToDomain(entity); job.Retry(now); var replacement = PersistenceMapper.ToEntity(job, entity.Version + 1);
        PersistenceMapper.Copy(replacement, entity); entity.Version++; entity.CancellationRequested = false; entity.Findings.Clear(); await db.SaveChangesAsync(token);
        await db.Batches.Where(x => x.Id == entity.BatchId && (x.Status == nameof(BatchStatus.Completed) || x.Status == nameof(BatchStatus.Cancelled)))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, entity.SourceArtifactKey == null ? nameof(BatchStatus.Acquiring) : nameof(BatchStatus.Processing)).SetProperty(x => x.UpdatedAt, now), token);
    }
}
