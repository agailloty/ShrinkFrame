namespace ShrinkFrame.Infrastructure.Persistence;

internal sealed class ImmichConnectionEntity
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    public required string BaseUrl { get; set; }
    public bool AllowInvalidCertificate { get; set; }
    public bool Enabled { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset? LastTestedAt { get; set; }
    public string? DetectedVersion { get; set; }
    public required string Compatibility { get; set; }
    public string? LastTestError { get; set; }
    public byte[]? EncryptedApiKey { get; set; }
    public string? LastTestKeyId { get; set; }
    public string? LastTestKeyName { get; set; }
    public string? LastTestPermissions { get; set; }
}

internal sealed class BatchEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string SourceKind { get; set; }
    public Guid? ConnectionId { get; set; }
    public required string Status { get; set; }
    public bool CapacityAdmissionOverride { get; set; }
    public int DefaultCrf { get; set; }
    public required string DefaultEncoderPreset { get; set; }
    public required string DefaultMaximumResolution { get; set; }
    public required string DefaultAudioMode { get; set; }
    public required string DefaultSuffix { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<JobEntity> Jobs { get; set; } = [];
}

internal sealed class JobEntity
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public BatchEntity? Batch { get; set; }
    public required string SourceKind { get; set; }
    public required string SourceId { get; set; }
    public Guid? SourceConnectionId { get; set; }
    public required string PresetId { get; set; }
    public int Crf { get; set; }
    public required string EncoderPreset { get; set; }
    public required string MaximumResolution { get; set; }
    public required string AudioMode { get; set; }
    public required string Suffix { get; set; }
    public required string State { get; set; }
    public required string PublicationState { get; set; }
    public bool NotBeneficialPublicationOverride { get; set; }
    public string? PublishedAssetId { get; set; }
    public string? SourceArtifactKey { get; set; }
    public string? OutputArtifactKey { get; set; }
    public string? MetadataFileName { get; set; }
    public string? MetadataMimeType { get; set; }
    public long? MetadataSizeBytes { get; set; }
    public long? MetadataDurationTicks { get; set; }
    public int? MetadataWidth { get; set; }
    public int? MetadataHeight { get; set; }
    public string? MetadataVideoCodec { get; set; }
    public DateTimeOffset? MetadataCaptureTime { get; set; }
    public int? MetadataEffectiveRotation { get; set; }
    public string? MetadataDescription { get; set; }
    public double? MetadataLatitude { get; set; }
    public double? MetadataLongitude { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
    public List<JobAudioCodecEntity> AudioCodecs { get; set; } = [];
    public List<JobAlbumEntity> Albums { get; set; } = [];
    public List<ValidationFindingEntity> Findings { get; set; } = [];
    public JobProgressEntity? Progress { get; set; }
    public List<PublicationAttemptEntity> PublicationAttempts { get; set; } = [];
}

internal sealed class JobAudioCodecEntity { public Guid JobId { get; set; } public int Position { get; set; } public required string Codec { get; set; } public JobEntity? Job { get; set; } }
internal sealed class JobAlbumEntity { public Guid JobId { get; set; } public int Position { get; set; } public required string AlbumId { get; set; } public JobEntity? Job { get; set; } }
internal sealed class ValidationFindingEntity { public long Id { get; set; } public Guid JobId { get; set; } public required string Code { get; set; } public required string Severity { get; set; } public required string Message { get; set; } public JobEntity? Job { get; set; } }

internal sealed class JobProgressEntity
{
    public Guid JobId { get; set; }
    public JobEntity? Job { get; set; }
    public long? TransferBytes { get; set; }
    public long? TransferTotalBytes { get; set; }
    public double? CompressionPercentage { get; set; }
    public long? ProcessedTicks { get; set; }
    public double? Speed { get; set; }
    public long? ElapsedTicks { get; set; }
    public long? EstimatedRemainingTicks { get; set; }
    public double? FramesPerSecond { get; set; }
    public long? BitrateBitsPerSecond { get; set; }
    public long? OutputBytes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class PublicationAttemptEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public JobEntity? Job { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public required string Result { get; set; }
    public string? ErrorSummary { get; set; }
}
