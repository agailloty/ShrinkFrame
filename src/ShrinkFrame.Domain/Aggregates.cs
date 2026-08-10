using System.Collections.ObjectModel;

namespace ShrinkFrame.Domain;

public sealed class ImmichConnection
{
    public ImmichConnection(ConnectionId id, string displayName, Uri baseUrl, bool allowInvalidCertificate, bool enabled, bool isDefault)
    {
        if (string.IsNullOrWhiteSpace(displayName) || !baseUrl.IsAbsoluteUri) throw new DomainException(DomainErrors.InvalidText, "Connection name and absolute URL are required.");
        Id = id; DisplayName = displayName.Trim(); BaseUrl = new Uri(baseUrl.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/");
        AllowInvalidCertificate = allowInvalidCertificate; Enabled = enabled; IsDefault = isDefault;
    }
    public ConnectionId Id { get; }
    public string DisplayName { get; private set; }
    public Uri BaseUrl { get; private set; }
    public bool AllowInvalidCertificate { get; private set; }
    public bool Enabled { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTimeOffset? LastTestedAt { get; private set; }
    public string? DetectedVersion { get; private set; }
    public CompatibilityResult Compatibility { get; private set; }
    public string? LastTestError { get; private set; }
    public void RecordTest(DateTimeOffset at, string? version, CompatibilityResult result, string? error)
        => (LastTestedAt, DetectedVersion, Compatibility, LastTestError) = (at, version, result, error);
}

public sealed class CompressionBatch
{
    private readonly List<JobId> jobIds = [];
    public CompressionBatch(BatchId id, string name, SourceKind sourceKind, ConnectionId? connectionId, CompressionOptions defaultOptions, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(DomainErrors.InvalidText, "Batch name is required.");
        if ((sourceKind == SourceKind.Immich) != connectionId.HasValue) throw new DomainException(DomainErrors.InvalidBatchSource, "Immich batches require exactly one connection; browser batches require none.");
        Id = id; Name = name.Trim(); SourceKind = sourceKind; ConnectionId = connectionId; DefaultOptions = defaultOptions with { }; CreatedAt = UpdatedAt = now;
    }
    public BatchId Id { get; }
    public string Name { get; private set; }
    public SourceKind SourceKind { get; }
    public ConnectionId? ConnectionId { get; }
    public BatchStatus Status { get; private set; }
    public CompressionOptions DefaultOptions { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ReadOnlyCollection<JobId> JobIds => jobIds.AsReadOnly();
    public void AddJob(JobId id, VideoSourceRef source, DateTimeOffset now)
    {
        if (source.Kind != SourceKind || source.ConnectionId != ConnectionId) throw new DomainException(DomainErrors.InvalidBatchSource, "Job source must match its batch.");
        if (!jobIds.Contains(id)) jobIds.Add(id);
        UpdatedAt = now;
    }
}

public sealed class CompressionJob
{
    private static readonly Dictionary<JobState, JobState[]> Allowed = new()
    {
        [JobState.Draft] = [JobState.Acquiring, JobState.Cancelled],
        [JobState.Acquiring] = [JobState.Probing, JobState.Failed, JobState.Cancelled, JobState.Interrupted],
        [JobState.Probing] = [JobState.Queued, JobState.Failed, JobState.Cancelled, JobState.Interrupted],
        [JobState.Queued] = [JobState.Compressing, JobState.Cancelled],
        [JobState.Compressing] = [JobState.Validating, JobState.Failed, JobState.Cancelled, JobState.Interrupted],
        [JobState.Validating] = [JobState.Ready, JobState.NotBeneficial, JobState.Failed, JobState.Interrupted],
        [JobState.Failed] = [JobState.Acquiring, JobState.Queued],
        [JobState.Cancelled] = [JobState.Acquiring, JobState.Queued],
        [JobState.Interrupted] = [JobState.Acquiring, JobState.Queued],
    };
    private readonly List<ValidationFinding> findings = [];
    public CompressionJob(JobId id, BatchId batchId, VideoSourceRef source, PresetId presetId, CompressionOptions effectiveOptions, DateTimeOffset now)
        => (Id, BatchId, Source, PresetId, EffectiveOptions, CreatedAt, UpdatedAt) = (id, batchId, source, presetId, effectiveOptions with { }, now, now);
    public JobId Id { get; }
    public BatchId BatchId { get; }
    public VideoSourceRef Source { get; }
    public PresetId PresetId { get; }
    public CompressionOptions EffectiveOptions { get; }
    public JobState State { get; private set; }
    public PublicationState PublicationState { get; private set; }
    public bool NotBeneficialPublicationOverride { get; private set; }
    public string? PublishedAssetId { get; private set; }
    public VideoMetadata? OriginalMetadata { get; private set; }
    public ArtifactRef? SourceArtifact { get; private set; }
    public ArtifactRef? OutputArtifact { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ReadOnlyCollection<ValidationFinding> Findings => findings.AsReadOnly();
    public IEnumerable<ValidationFinding> Warnings => findings.Where(x => !x.IsBlocking);
    public IEnumerable<ValidationFinding> BlockingFindings => findings.Where(x => x.IsBlocking);

    public void TransitionTo(JobState target, DateTimeOffset now)
    {
        if (!Allowed.TryGetValue(State, out var targets) || !targets.Contains(target)) throw new DomainException(DomainErrors.InvalidJobTransition, $"Transition from {State} to {target} is not allowed.");
        if (target == JobState.Queued && OriginalMetadata is null) throw new DomainException(DomainErrors.JobNotValidated, "A probed video is required before queueing.");
        if (target is JobState.Ready or JobState.NotBeneficial) throw new DomainException(DomainErrors.JobNotValidated, "Successful state is assigned only by validation.");
        State = target; UpdatedAt = now;
    }
    public void RecordProbe(VideoMetadata metadata, ArtifactRef sourceArtifact) => (OriginalMetadata, SourceArtifact) = (metadata, sourceArtifact);
    public void CompleteValidation(long outputBytes, ArtifactRef output, IEnumerable<ValidationFinding> validationFindings, DateTimeOffset now)
    {
        if (State != JobState.Validating || OriginalMetadata is null) throw new DomainException(DomainErrors.InvalidJobTransition, "Job must be validating with probed metadata.");
        findings.Clear(); findings.AddRange(validationFindings);
        State = MediaPolicies.ClassifyValidatedOutput(OriginalMetadata.SizeBytes, outputBytes, findings);
        OutputArtifact = output; UpdatedAt = now;
    }
    public void AuthorizeNotBeneficialPublication()
    {
        if (State != JobState.NotBeneficial) throw new DomainException(DomainErrors.InvalidPublicationTransition, "Override applies only to NotBeneficial results.");
        NotBeneficialPublicationOverride = true;
    }
    public void BeginPublication(DateTimeOffset now)
    {
        if (State is not (JobState.Ready or JobState.NotBeneficial) || PublicationState is not (PublicationState.NotRequested or PublicationState.PartiallyPublished or PublicationState.Failed))
            throw new DomainException(DomainErrors.InvalidPublicationTransition, "Publication cannot begin in the current state.");
        if (State == JobState.NotBeneficial && !NotBeneficialPublicationOverride) throw new DomainException(DomainErrors.PublicationOverrideRequired, "NotBeneficial publication requires explicit override.");
        PublicationState = PublicationState.Publishing; UpdatedAt = now;
    }
    public void RecordPublishedAsset(string assetId)
    {
        if (PublicationState != PublicationState.Publishing || string.IsNullOrWhiteSpace(assetId)) throw new DomainException(DomainErrors.InvalidPublicationTransition, "A publishing job and asset ID are required.");
        PublishedAssetId = assetId.Trim();
    }
    public void CompletePublication(bool albumsSynchronized, DateTimeOffset now)
    {
        if (PublicationState != PublicationState.Publishing || string.IsNullOrWhiteSpace(PublishedAssetId)) throw new DomainException(DomainErrors.InvalidPublicationTransition, "Published asset must be recorded first.");
        PublicationState = albumsSynchronized ? PublicationState.Published : PublicationState.PartiallyPublished; UpdatedAt = now;
    }
    public void FailPublication(DateTimeOffset now)
    {
        if (PublicationState != PublicationState.Publishing) throw new DomainException(DomainErrors.InvalidPublicationTransition, "Only active publication can fail.");
        PublicationState = PublicationState.Failed; UpdatedAt = now;
    }
}
