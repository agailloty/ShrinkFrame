using Microsoft.EntityFrameworkCore;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Persistence;

public sealed class ImmichConnectionRepository(ShrinkFrameDbContext db) : IImmichConnectionRepository
{
    public async Task AddAsync(StoredImmichConnection connection, CancellationToken cancellationToken = default)
    {
        db.Connections.Add(PersistenceMapper.ToEntity(connection));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<StoredImmichConnection?> GetAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Connections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        return entity is null ? null : PersistenceMapper.ToDomain(entity);
    }

    public async Task UpdateAsync(StoredImmichConnection connection, CancellationToken cancellationToken = default)
    {
        var replacement = PersistenceMapper.ToEntity(connection);
        var entity = await db.Connections.SingleOrDefaultAsync(x => x.Id == replacement.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Immich connection was not found.");
        db.Entry(entity).CurrentValues.SetValues(replacement);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class BatchRepository(ShrinkFrameDbContext db) : IBatchRepository
{
    public async Task AddAsync(CompressionBatch batch, CancellationToken cancellationToken = default)
    {
        db.Batches.Add(PersistenceMapper.ToEntity(batch));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CompressionBatch?> GetAsync(BatchId id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Batches.AsNoTracking().Include(x => x.Jobs)
            .SingleOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        return entity is null ? null : PersistenceMapper.ToDomain(entity);
    }

    public async Task UpdateAsync(CompressionBatch batch, CancellationToken cancellationToken = default)
    {
        var replacement = PersistenceMapper.ToEntity(batch);
        var entity = await db.Batches.SingleOrDefaultAsync(x => x.Id == replacement.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Batch was not found.");
        db.Entry(entity).CurrentValues.SetValues(replacement);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class CompressionJobRepository(ShrinkFrameDbContext db) : ICompressionJobRepository
{
    public async Task<Versioned<CompressionJob>> AddAsync(CompressionJob job, CancellationToken cancellationToken = default)
    {
        const long initialVersion = 1;
        db.Jobs.Add(PersistenceMapper.ToEntity(job, initialVersion));
        await db.SaveChangesAsync(cancellationToken);
        return new(job, initialVersion);
    }

    public async Task<Versioned<CompressionJob>?> GetAsync(JobId id, CancellationToken cancellationToken = default)
    {
        var entity = await JobQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        return entity is null ? null : new(PersistenceMapper.ToDomain(entity), entity.Version);
    }

    public async Task<long> UpdateAsync(CompressionJob job, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var entity = await JobQuery().SingleOrDefaultAsync(x => x.Id == job.Id.Value, cancellationToken)
            ?? throw new KeyNotFoundException("Compression job was not found.");
        if (entity.Version != expectedVersion)
            throw new PersistenceConcurrencyException("Compression job changed after it was loaded.");

        var replacement = PersistenceMapper.ToEntity(job, checked(expectedVersion + 1));
        PersistenceMapper.Copy(replacement, entity);
        entity.Version = replacement.Version;
        entity.AudioCodecs.Clear(); entity.AudioCodecs.AddRange(replacement.AudioCodecs);
        entity.Albums.Clear(); entity.Albums.AddRange(replacement.Albums);
        entity.Findings.Clear(); entity.Findings.AddRange(replacement.Findings);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return entity.Version;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PersistenceConcurrencyException("Compression job changed while it was being saved.", exception);
        }
    }

    public async Task<Versioned<CompressionJob>?> TryClaimAsync(JobId id, JobState expectedState, long expectedVersion,
        DateTimeOffset claimedAt, CancellationToken cancellationToken = default)
    {
        if (expectedState != JobState.Queued)
            throw new ArgumentOutOfRangeException(nameof(expectedState), "Compression claims require the Queued state.");
        var newVersion = checked(expectedVersion + 1);
        var affected = await db.Jobs
            .Where(x => x.Id == id.Value && x.State == nameof(JobState.Queued) && x.Version == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.State, nameof(JobState.Compressing))
                .SetProperty(x => x.UpdatedAt, claimedAt)
                .SetProperty(x => x.Version, newVersion), cancellationToken);
        if (affected != 1) return null;
        db.ChangeTracker.Clear();
        return await GetAsync(id, cancellationToken);
    }

    public async Task SaveProgressAsync(JobId id, JobProgressSnapshot progress, CancellationToken cancellationToken = default)
    {
        var entity = await db.JobProgress.SingleOrDefaultAsync(x => x.JobId == id.Value, cancellationToken);
        if (entity is null)
        {
            entity = new JobProgressEntity { JobId = id.Value };
            db.JobProgress.Add(entity);
        }
        entity.TransferBytes = progress.Transfer?.BytesTransferred; entity.TransferTotalBytes = progress.Transfer?.TotalBytes;
        entity.CompressionPercentage = (double?)progress.Compression?.Percentage;
        entity.ProcessedTicks = progress.Compression?.Processed.Ticks; entity.Speed = (double?)progress.Compression?.Speed;
        entity.ElapsedTicks = progress.Compression?.Elapsed.Ticks; entity.EstimatedRemainingTicks = progress.Compression?.EstimatedRemaining?.Ticks;
        entity.FramesPerSecond = (double?)progress.Compression?.FramesPerSecond;
        entity.BitrateBitsPerSecond = progress.Compression?.BitrateBitsPerSecond; entity.OutputBytes = progress.Compression?.OutputBytes;
        entity.UpdatedAt = progress.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddPublicationAttemptAsync(PublicationAttempt attempt, CancellationToken cancellationToken = default)
    {
        db.PublicationAttempts.Add(new PublicationAttemptEntity
        {
            Id = attempt.Id, JobId = attempt.JobId.Value, StartedAt = attempt.StartedAt, CompletedAt = attempt.CompletedAt,
            Result = attempt.Result.ToString(), ErrorSummary = attempt.ErrorSummary,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<JobEntity> JobQuery() => db.Jobs
        .Include(x => x.AudioCodecs).Include(x => x.Albums).Include(x => x.Findings);
}
