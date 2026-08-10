using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Persistence;

internal static class PersistenceMapper
{
    internal static ImmichConnectionEntity ToEntity(StoredImmichConnection stored) => new()
    {
        Id = stored.Connection.Id.Value, DisplayName = stored.Connection.DisplayName,
        BaseUrl = stored.Connection.BaseUrl.AbsoluteUri, AllowInvalidCertificate = stored.Connection.AllowInvalidCertificate,
        Enabled = stored.Connection.Enabled, IsDefault = stored.Connection.IsDefault,
        LastTestedAt = stored.Connection.LastTestedAt, DetectedVersion = stored.Connection.DetectedVersion,
        Compatibility = stored.Connection.Compatibility.ToString(), LastTestError = stored.Connection.LastTestError,
        EncryptedApiKey = stored.ApiKeyEnvelope?.Payload.ToArray(),
        LastTestKeyId = stored.Connection.LastTestKeyId, LastTestKeyName = stored.Connection.LastTestKeyName,
        LastTestPermissions = stored.Connection.LastTestPermissions,
    };

    internal static StoredImmichConnection ToDomain(ImmichConnectionEntity entity) => new(
        ImmichConnection.Restore(ConnectionId.From(entity.Id), entity.DisplayName, new Uri(entity.BaseUrl),
            entity.AllowInvalidCertificate, entity.Enabled, entity.IsDefault, entity.LastTestedAt,
            entity.DetectedVersion, Parse<CompatibilityResult>(entity.Compatibility), entity.LastTestError,
            entity.LastTestKeyId, entity.LastTestKeyName, entity.LastTestPermissions),
        entity.EncryptedApiKey is null ? null : new EncryptedSecretEnvelope(entity.EncryptedApiKey));

    internal static BatchEntity ToEntity(CompressionBatch batch) => new()
    {
        Id = batch.Id.Value, Name = batch.Name, SourceKind = batch.SourceKind.ToString(),
        ConnectionId = batch.ConnectionId?.Value, Status = batch.Status.ToString(),
        CapacityAdmissionOverride = batch.CapacityAdmissionOverride,
        DefaultCrf = batch.DefaultOptions.Crf, DefaultEncoderPreset = batch.DefaultOptions.EncoderPreset.ToString(),
        DefaultMaximumResolution = batch.DefaultOptions.MaximumResolution.ToString(),
        DefaultAudioMode = batch.DefaultOptions.AudioMode.ToString(), DefaultSuffix = batch.DefaultOptions.Suffix,
        CreatedAt = batch.CreatedAt, UpdatedAt = batch.UpdatedAt,
    };

    internal static CompressionBatch ToDomain(BatchEntity entity) => CompressionBatch.Restore(
        BatchId.From(entity.Id), entity.Name, Parse<SourceKind>(entity.SourceKind),
        entity.ConnectionId.HasValue ? ConnectionId.From(entity.ConnectionId.Value) : null,
        Options(entity.DefaultCrf, entity.DefaultEncoderPreset, entity.DefaultMaximumResolution, entity.DefaultAudioMode, entity.DefaultSuffix),
        Parse<BatchStatus>(entity.Status), entity.CreatedAt, entity.UpdatedAt, entity.Jobs.Select(x => JobId.From(x.Id)),
        entity.CapacityAdmissionOverride);

    internal static JobEntity ToEntity(CompressionJob job, long version)
    {
        var metadata = job.OriginalMetadata;
        var entity = new JobEntity
        {
            Id = job.Id.Value, BatchId = job.BatchId.Value, SourceKind = job.Source.Kind.ToString(), SourceId = job.Source.SourceId,
            SourceConnectionId = job.Source.ConnectionId?.Value, PresetId = job.PresetId.Value,
            Crf = job.EffectiveOptions.Crf, EncoderPreset = job.EffectiveOptions.EncoderPreset.ToString(),
            MaximumResolution = job.EffectiveOptions.MaximumResolution.ToString(), AudioMode = job.EffectiveOptions.AudioMode.ToString(),
            Suffix = job.EffectiveOptions.Suffix, State = job.State.ToString(), PublicationState = job.PublicationState.ToString(),
            NotBeneficialPublicationOverride = job.NotBeneficialPublicationOverride, PublishedAssetId = job.PublishedAssetId,
            SourceArtifactKey = job.SourceArtifact?.Key, OutputArtifactKey = job.OutputArtifact?.Key,
            MetadataFileName = metadata?.FileName, MetadataMimeType = metadata?.MimeType, MetadataSizeBytes = metadata?.SizeBytes,
            MetadataDurationTicks = metadata?.Duration.Ticks, MetadataWidth = metadata?.Width, MetadataHeight = metadata?.Height,
            MetadataVideoCodec = metadata?.VideoCodec, MetadataCaptureTime = metadata?.CaptureTime,
            MetadataEffectiveRotation = metadata?.EffectiveRotation, MetadataDescription = metadata?.Description,
            MetadataLatitude = metadata?.Latitude, MetadataLongitude = metadata?.Longitude,
            CreatedAt = job.CreatedAt, UpdatedAt = job.UpdatedAt, Version = version,
        };
        if (metadata is not null)
        {
            entity.AudioCodecs.AddRange(metadata.AudioCodecs.Select((value, index) => new JobAudioCodecEntity { JobId = entity.Id, Position = index, Codec = value }));
            entity.Albums.AddRange(metadata.AlbumIds.Select((value, index) => new JobAlbumEntity { JobId = entity.Id, Position = index, AlbumId = value }));
        }
        entity.Findings.AddRange(job.Findings.Select(x => new ValidationFindingEntity { JobId = entity.Id, Code = x.Code, Severity = x.Severity.ToString(), Message = x.Message }));
        return entity;
    }

    internal static CompressionJob ToDomain(JobEntity entity)
    {
        var source = Parse<SourceKind>(entity.SourceKind) switch
        {
            SourceKind.BrowserUpload => VideoSourceRef.Browser(entity.SourceId),
            SourceKind.Immich => VideoSourceRef.Immich(entity.SourceId, ConnectionId.From(entity.SourceConnectionId
                ?? throw new InvalidOperationException("Persisted Immich source has no connection."))),
            _ => throw new InvalidOperationException("Persisted source kind is invalid."),
        };
        VideoMetadata? metadata = null;
        if (entity.MetadataFileName is not null)
        {
            metadata = new VideoMetadata(entity.MetadataFileName, Required(entity.MetadataMimeType), entity.MetadataSizeBytes ?? -1,
                TimeSpan.FromTicks(entity.MetadataDurationTicks ?? -1), entity.MetadataWidth ?? -1, entity.MetadataHeight ?? -1,
                Required(entity.MetadataVideoCodec), entity.AudioCodecs.OrderBy(x => x.Position).Select(x => x.Codec).ToArray(),
                entity.MetadataCaptureTime, entity.MetadataEffectiveRotation ?? -1, entity.MetadataDescription,
                entity.MetadataLatitude, entity.MetadataLongitude, entity.Albums.OrderBy(x => x.Position).Select(x => x.AlbumId).ToArray());
        }
        return CompressionJob.Restore(JobId.From(entity.Id), BatchId.From(entity.BatchId), source, new PresetId(entity.PresetId),
            Options(entity.Crf, entity.EncoderPreset, entity.MaximumResolution, entity.AudioMode, entity.Suffix),
            Parse<JobState>(entity.State), Parse<PublicationState>(entity.PublicationState), entity.NotBeneficialPublicationOverride,
            entity.PublishedAssetId, metadata, Artifact(entity.SourceArtifactKey), Artifact(entity.OutputArtifactKey),
            entity.CreatedAt, entity.UpdatedAt, entity.Findings.Select(x => new ValidationFinding(x.Code, Parse<FindingSeverity>(x.Severity), x.Message)));
    }

    internal static void Copy(JobEntity source, JobEntity target)
    {
        target.SourceKind = source.SourceKind; target.SourceId = source.SourceId; target.SourceConnectionId = source.SourceConnectionId;
        target.PresetId = source.PresetId; target.Crf = source.Crf; target.EncoderPreset = source.EncoderPreset;
        target.MaximumResolution = source.MaximumResolution; target.AudioMode = source.AudioMode; target.Suffix = source.Suffix;
        target.State = source.State; target.PublicationState = source.PublicationState;
        target.NotBeneficialPublicationOverride = source.NotBeneficialPublicationOverride; target.PublishedAssetId = source.PublishedAssetId;
        target.SourceArtifactKey = source.SourceArtifactKey; target.OutputArtifactKey = source.OutputArtifactKey;
        target.MetadataFileName = source.MetadataFileName; target.MetadataMimeType = source.MetadataMimeType; target.MetadataSizeBytes = source.MetadataSizeBytes;
        target.MetadataDurationTicks = source.MetadataDurationTicks; target.MetadataWidth = source.MetadataWidth; target.MetadataHeight = source.MetadataHeight;
        target.MetadataVideoCodec = source.MetadataVideoCodec; target.MetadataCaptureTime = source.MetadataCaptureTime;
        target.MetadataEffectiveRotation = source.MetadataEffectiveRotation; target.MetadataDescription = source.MetadataDescription;
        target.MetadataLatitude = source.MetadataLatitude; target.MetadataLongitude = source.MetadataLongitude; target.UpdatedAt = source.UpdatedAt;
    }

    private static CompressionOptions Options(int crf, string encoder, string resolution, string audio, string suffix)
        => new(crf, Parse<EncoderPreset>(encoder), Parse<MaximumResolution>(resolution), Parse<AudioMode>(audio), suffix);
    private static ArtifactRef? Artifact(string? key) => key is null ? null : new ArtifactRef(key);
    private static T Parse<T>(string value) where T : struct, Enum => Enum.TryParse<T>(value, false, out var result) && Enum.IsDefined(result)
        ? result : throw new InvalidOperationException($"Persisted {typeof(T).Name} value is invalid.");
    private static string Required(string? value) => value ?? throw new InvalidOperationException("Persisted metadata is incomplete.");
}
