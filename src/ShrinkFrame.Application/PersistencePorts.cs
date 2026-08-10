using ShrinkFrame.Domain;

namespace ShrinkFrame.Application;

public sealed record EncryptedSecretEnvelope(byte[] Payload)
{
    public byte[] Payload { get; } = Payload?.ToArray() ?? throw new ArgumentNullException(nameof(Payload));
}

public sealed record StoredImmichConnection(ImmichConnection Connection, EncryptedSecretEnvelope? ApiKeyEnvelope);
public sealed record Versioned<T>(T Value, long Version);

public sealed record JobProgressSnapshot(
    TransferProgress? Transfer,
    CompressionProgress? Compression,
    DateTimeOffset UpdatedAt);

public sealed record PublicationAttempt(
    Guid Id,
    JobId JobId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    PublicationState Result,
    string? ErrorSummary);

public interface IImmichConnectionRepository
{
    Task AddAsync(StoredImmichConnection connection, CancellationToken cancellationToken = default);
    Task<StoredImmichConnection?> GetAsync(ConnectionId id, CancellationToken cancellationToken = default);
    Task UpdateAsync(StoredImmichConnection connection, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredImmichConnection>> ListAsync(CancellationToken cancellationToken = default);
    Task SetDefaultAsync(ConnectionId id, CancellationToken cancellationToken = default);
    Task<bool> IsRequiredByActiveWorkAsync(ConnectionId id, CancellationToken cancellationToken = default);
    Task DeleteAsync(ConnectionId id, CancellationToken cancellationToken = default);
}

public interface IImmichBrowserSelectionRepository
{
    Task<IReadOnlySet<string>> ListAsync(ConnectionId connectionId, CancellationToken cancellationToken = default);
    Task AddAsync(ConnectionId connectionId, IEnumerable<string> assetIds, CancellationToken cancellationToken = default);
    Task RemoveAsync(ConnectionId connectionId, IEnumerable<string> assetIds, CancellationToken cancellationToken = default);
    Task ClearAsync(ConnectionId connectionId, CancellationToken cancellationToken = default);
}

public interface IBatchRepository
{
    Task AddAsync(CompressionBatch batch, CancellationToken cancellationToken = default);
    Task<CompressionBatch?> GetAsync(BatchId id, CancellationToken cancellationToken = default);
    Task UpdateAsync(CompressionBatch batch, CancellationToken cancellationToken = default);
}

public interface ICompressionJobRepository
{
    Task<Versioned<CompressionJob>> AddAsync(CompressionJob job, CancellationToken cancellationToken = default);
    Task<Versioned<CompressionJob>?> GetAsync(JobId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Versioned<CompressionJob>>> ListByBatchAsync(BatchId batchId, CancellationToken cancellationToken = default);
    Task<long> UpdateAsync(CompressionJob job, long expectedVersion, CancellationToken cancellationToken = default);
    Task<Versioned<CompressionJob>?> TryClaimAsync(JobId id, JobState expectedState, long expectedVersion,
        DateTimeOffset claimedAt, CancellationToken cancellationToken = default);
    Task SaveProgressAsync(JobId id, JobProgressSnapshot progress, CancellationToken cancellationToken = default);
    Task AddPublicationAttemptAsync(PublicationAttempt attempt, CancellationToken cancellationToken = default);
}

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IStartupRecovery
{
    Task<int> RecoverInterruptedJobsAsync(DateTimeOffset recoveredAt, CancellationToken cancellationToken = default);
}

public sealed class PersistenceConcurrencyException : InvalidOperationException
{
    public PersistenceConcurrencyException(string message) : base(message) { }
    public PersistenceConcurrencyException(string message, Exception innerException) : base(message, innerException) { }
}
