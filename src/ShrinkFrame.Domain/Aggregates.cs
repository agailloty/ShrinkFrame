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
    public string? LastTestKeyId { get; private set; }
    public string? LastTestKeyName { get; private set; }
    public string? LastTestPermissions { get; private set; }
    public void Update(string displayName, Uri baseUrl, bool allowInvalidCertificate, bool enabled, bool isDefault)
    {
        if (string.IsNullOrWhiteSpace(displayName) || !baseUrl.IsAbsoluteUri) throw new DomainException(DomainErrors.InvalidText, "Connection name and absolute URL are required.");
        DisplayName = displayName.Trim(); BaseUrl = baseUrl; AllowInvalidCertificate = allowInvalidCertificate;
        Enabled = enabled; IsDefault = isDefault;
    }
    public void RecordTest(DateTimeOffset at, string? version, CompatibilityResult result, string? error,
        string? keyId = null, string? keyName = null, string? permissions = null)
        => (LastTestedAt, DetectedVersion, Compatibility, LastTestError, LastTestKeyId, LastTestKeyName, LastTestPermissions)
            = (at, version, result, error, keyId, keyName, permissions);

    internal static ImmichConnection Restore(ConnectionId id, string displayName, Uri baseUrl,
        bool allowInvalidCertificate, bool enabled, bool isDefault, DateTimeOffset? lastTestedAt,
        string? detectedVersion, CompatibilityResult compatibility, string? lastTestError,
        string? lastTestKeyId = null, string? lastTestKeyName = null, string? lastTestPermissions = null)
    {
        var connection = new ImmichConnection(id, displayName, baseUrl, allowInvalidCertificate, enabled, isDefault);
        if (lastTestedAt.HasValue)
            connection.RecordTest(lastTestedAt.Value, detectedVersion, compatibility, lastTestError,
                lastTestKeyId, lastTestKeyName, lastTestPermissions);
        return connection;
    }
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
    public CompressionOptions DefaultOptions { get; private set; }
    public bool CapacityAdmissionOverride { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ReadOnlyCollection<JobId> JobIds => jobIds.AsReadOnly();
    public void AddJob(JobId id, VideoSourceRef source, DateTimeOffset now)
    {
        EnsureDraft();
        if (source.Kind != SourceKind || source.ConnectionId != ConnectionId) throw new DomainException(DomainErrors.InvalidBatchSource, "Job source must match its batch.");
        if (!jobIds.Contains(id)) jobIds.Add(id);
        UpdatedAt = now;
    }
    public void Rename(string name, DateTimeOffset now)
    {
        EnsureDraft();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 300)
            throw new DomainException(DomainErrors.InvalidText, "Batch name is required and must be 300 characters or fewer.");
        Name = name.Trim(); UpdatedAt = now;
    }
    public void Configure(CompressionOptions options, DateTimeOffset now)
    {
        EnsureDraft(); DefaultOptions = options with { }; UpdatedAt = now;
    }
    public void Confirm(DateTimeOffset now)
    {
        EnsureDraft();
        if (jobIds.Count == 0) throw new DomainException(DomainErrors.InvalidText, "Select at least one video before confirmation.");
        Status = SourceKind == SourceKind.Immich ? BatchStatus.Acquiring : BatchStatus.Processing;
        UpdatedAt = now;
    }
    public void AuthorizeCapacityAdmissionOverride(DateTimeOffset now)
    {
        EnsureDraft();
        CapacityAdmissionOverride = true;
        UpdatedAt = now;
    }
    public void MarkProcessing(DateTimeOffset now)
    {
        if (Status != BatchStatus.Acquiring) throw new DomainException(DomainErrors.InvalidJobTransition, "Only an acquiring batch can begin processing.");
        Status = BatchStatus.Processing; UpdatedAt = now;
    }
    public void Complete(DateTimeOffset now)
    {
        if (Status is not (BatchStatus.Acquiring or BatchStatus.Processing)) throw new DomainException(DomainErrors.InvalidJobTransition, "Only active batches can complete.");
        Status = BatchStatus.Completed; UpdatedAt = now;
    }
    public void Cancel(DateTimeOffset now)
    {
        if (Status is BatchStatus.Completed or BatchStatus.Cancelled) return;
        Status = BatchStatus.Cancelled; UpdatedAt = now;
    }
    private void EnsureDraft()
    {
        if (Status != BatchStatus.Draft)
            throw new DomainException(DomainErrors.InvalidJobTransition, "A confirmed batch cannot be edited through the wizard.");
    }

    internal static CompressionBatch Restore(BatchId id, string name, SourceKind sourceKind,
        ConnectionId? connectionId, CompressionOptions defaultOptions, BatchStatus status,
        DateTimeOffset createdAt, DateTimeOffset updatedAt, IEnumerable<JobId> jobs, bool capacityAdmissionOverride = false)
    {
        var batch = new CompressionBatch(id, name, sourceKind, connectionId, defaultOptions, createdAt)
        {
            Status = status,
            UpdatedAt = updatedAt,
            CapacityAdmissionOverride = capacityAdmissionOverride,
        };
        batch.jobIds.AddRange(jobs.Distinct());
        return batch;
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
    public PresetId PresetId { get; private set; }
    public CompressionOptions EffectiveOptions { get; private set; }
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

    public void SelectOptions(PresetId presetId, CompressionOptions effectiveOptions, DateTimeOffset now)
    {
        if (State is not (JobState.Draft or JobState.Probing))
            throw new DomainException(DomainErrors.InvalidJobTransition, "Options cannot change after confirmation.");
        PresetId = presetId; EffectiveOptions = effectiveOptions with { }; UpdatedAt = now;
    }

    public void TransitionTo(JobState target, DateTimeOffset now)
    {
        if (!Allowed.TryGetValue(State, out var targets) || !targets.Contains(target)) throw new DomainException(DomainErrors.InvalidJobTransition, $"Transition from {State} to {target} is not allowed.");
        if (target == JobState.Queued && OriginalMetadata is null) throw new DomainException(DomainErrors.JobNotValidated, "A probed video is required before queueing.");
        if (target is JobState.Ready or JobState.NotBeneficial) throw new DomainException(DomainErrors.JobNotValidated, "Successful state is assigned only by validation.");
        State = target; UpdatedAt = now;
    }
    public void RecordProbe(VideoMetadata metadata, ArtifactRef sourceArtifact) => (OriginalMetadata, SourceArtifact) = (metadata, sourceArtifact);
    public void Cancel(DateTimeOffset now)
    {
        if (State is JobState.Ready or JobState.NotBeneficial) throw new DomainException(DomainErrors.InvalidJobTransition, "A completed job cannot be cancelled.");
        if (State is JobState.Failed or JobState.Interrupted or JobState.Draft or JobState.Acquiring or JobState.Probing or JobState.Queued or JobState.Compressing or JobState.Validating)
        { State = JobState.Cancelled; UpdatedAt = now; }
    }
    public void FailProcessing(string code, string message, DateTimeOffset now)
    {
        if (State is not (JobState.Compressing or JobState.Validating))
            throw new DomainException(DomainErrors.InvalidJobTransition, "Only compression or validation can fail here.");
        findings.Clear(); findings.Add(new ValidationFinding(code, FindingSeverity.Blocking, message));
        State = JobState.Failed; UpdatedAt = now;
    }
    public void Retry(DateTimeOffset now)
    {
        if (State is not (JobState.Failed or JobState.Cancelled or JobState.Interrupted))
            throw new DomainException(DomainErrors.InvalidJobTransition, "Only failed, cancelled, or interrupted jobs can be retried.");
        State = SourceArtifact is null ? JobState.Acquiring : JobState.Queued;
        findings.Clear(); UpdatedAt = now;
    }
    public void Fail(string code, string message, DateTimeOffset now)
    {
        if (State is not (JobState.Acquiring or JobState.Probing))
            throw new DomainException(DomainErrors.InvalidJobTransition, "Only acquisition or probing can fail here.");
        findings.Clear();
        findings.Add(new ValidationFinding(code, FindingSeverity.Blocking, message));
        State = JobState.Failed;
        UpdatedAt = now;
    }
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


    internal static CompressionJob Restore(JobId id, BatchId batchId, VideoSourceRef source,
        PresetId presetId, CompressionOptions effectiveOptions, JobState state,
        PublicationState publicationState, bool publicationOverride, string? publishedAssetId,
        VideoMetadata? originalMetadata, ArtifactRef? sourceArtifact, ArtifactRef? outputArtifact,
        DateTimeOffset createdAt, DateTimeOffset updatedAt, IEnumerable<ValidationFinding> restoredFindings)
    {
        if (!Enum.IsDefined(state) || !Enum.IsDefined(publicationState))
            throw new DomainException(DomainErrors.InvalidJobTransition, "Persisted job state is invalid.");
        if (state is JobState.Queued or JobState.Compressing or JobState.Validating or JobState.Ready or JobState.NotBeneficial
            && originalMetadata is null)
            throw new DomainException(DomainErrors.JobNotValidated, "Persisted active or completed job requires probed metadata.");
        if (state is JobState.Ready or JobState.NotBeneficial && outputArtifact is null)
            throw new DomainException(DomainErrors.JobNotValidated, "Persisted successful job requires an output artifact.");
        if (publicationState is PublicationState.Published or PublicationState.PartiallyPublished
            && string.IsNullOrWhiteSpace(publishedAssetId))
            throw new DomainException(DomainErrors.InvalidPublicationTransition, "Persisted publication requires an asset ID.");
        if (publicationOverride && state != JobState.NotBeneficial)
            throw new DomainException(DomainErrors.InvalidPublicationTransition, "Persisted publication override is invalid.");

        var job = new CompressionJob(id, batchId, source, presetId, effectiveOptions, createdAt)
        {
            State = state,
            PublicationState = publicationState,
            NotBeneficialPublicationOverride = publicationOverride,
            PublishedAssetId = publishedAssetId,
            OriginalMetadata = originalMetadata,
            SourceArtifact = sourceArtifact,
            OutputArtifact = outputArtifact,
            UpdatedAt = updatedAt,
        };
        job.findings.AddRange(restoredFindings);
        return job;
    }
}
