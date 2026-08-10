using System.Collections.ObjectModel;

namespace ShrinkFrame.Domain;

public enum SourceKind { BrowserUpload, Immich }
public enum BatchStatus { Draft, Acquiring, Processing, Completed, Cancelled }
public enum JobState { Draft, Acquiring, Probing, Queued, Compressing, Validating, Ready, NotBeneficial, Failed, Cancelled, Interrupted }
public enum PublicationState { NotRequested, Publishing, Published, PartiallyPublished, Failed }
public enum FindingSeverity { Warning, Blocking }
public enum MaximumResolution { Keep = 0, P2160 = 2160, P1440 = 1440, P1080 = 1080, P720 = 720, P480 = 480 }
public enum AudioMode { Auto, Copy, Aac }
public enum EncoderPreset { Ultrafast, Superfast, Veryfast, Faster, Fast, Medium, Slow, Slower, Veryslow }
public enum CompatibilityResult { Unknown, Compatible, Warning, Incompatible }

public sealed record VideoSourceRef
{
    private VideoSourceRef(SourceKind kind, string sourceId, ConnectionId? connectionId)
        => (Kind, SourceId, ConnectionId) = (kind, sourceId, connectionId);
    public SourceKind Kind { get; }
    public string SourceId { get; }
    public ConnectionId? ConnectionId { get; }
    public static VideoSourceRef Browser(string uploadId) => new(SourceKind.BrowserUpload, Required(uploadId), null);
    public static VideoSourceRef Immich(string assetId, ConnectionId connectionId) => new(SourceKind.Immich, Required(assetId), connectionId);
    private static string Required(string value) => string.IsNullOrWhiteSpace(value)
        ? throw new DomainException(DomainErrors.InvalidText, "Source ID is required.") : value.Trim();
}

public sealed record VideoMetadata
{
    public VideoMetadata(string fileName, string mimeType, long sizeBytes, TimeSpan duration, int width, int height,
        string videoCodec, IReadOnlyList<string>? audioCodecs, DateTimeOffset? captureTime, int effectiveRotation,
        string? description = null, double? latitude = null, double? longitude = null, IReadOnlyList<string>? albumIds = null,
        DateTimeOffset? fileModifiedTime = null)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(mimeType) || string.IsNullOrWhiteSpace(videoCodec))
            throw new DomainException(DomainErrors.InvalidText, "Media text fields are required.");
        if (sizeBytes < 0) throw new DomainException(DomainErrors.InvalidSize, "Media size cannot be negative.");
        if (duration < TimeSpan.Zero) throw new DomainException(DomainErrors.InvalidDuration, "Duration cannot be negative.");
        if (width <= 0 || height <= 0) throw new DomainException(DomainErrors.InvalidDimensions, "Dimensions must be positive.");
        if (effectiveRotation is not (0 or 90 or 180 or 270)) throw new DomainException(DomainErrors.InvalidDimensions, "Rotation must be 0, 90, 180, or 270.");
        FileName = fileName.Trim(); MimeType = mimeType.Trim(); SizeBytes = sizeBytes; Duration = duration;
        Width = width; Height = height; VideoCodec = videoCodec.Trim(); CaptureTime = captureTime; EffectiveRotation = effectiveRotation;
        Description = description; Latitude = latitude; Longitude = longitude;
        FileModifiedTime = fileModifiedTime;
        AudioCodecs = Array.AsReadOnly((audioCodecs ?? []).ToArray());
        AlbumIds = Array.AsReadOnly((albumIds ?? []).ToArray());
    }
    public string FileName { get; }
    public string MimeType { get; }
    public long SizeBytes { get; }
    public TimeSpan Duration { get; }
    public int Width { get; }
    public int Height { get; }
    public string VideoCodec { get; }
    public ReadOnlyCollection<string> AudioCodecs { get; }
    public DateTimeOffset? CaptureTime { get; }
    public int EffectiveRotation { get; }
    public string? Description { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }
    public ReadOnlyCollection<string> AlbumIds { get; }
    public DateTimeOffset? FileModifiedTime { get; }
}

public sealed record ArtifactRef
{
    public ArtifactRef(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.Contains(':') || key.Contains("..", StringComparison.Ordinal) || key.Contains('\\'))
            throw new DomainException(DomainErrors.InvalidArtifact, "Artifact key must be a safe opaque relative key.");
        Key = key;
    }
    public string Key { get; }
}

public sealed record ValidationFinding(string Code, FindingSeverity Severity, string Message)
{
    public bool IsBlocking => Severity == FindingSeverity.Blocking;
    public static ValidationFinding CaptureDateLost() => new("validation.capture_date.lost", FindingSeverity.Blocking, "Capture date was not retained.");
    public static ValidationFinding CaptureDateChanged() => new("validation.capture_date.changed", FindingSeverity.Blocking, "Capture date changed.");
    public static ValidationFinding RotationChanged() => new("validation.rotation.changed", FindingSeverity.Blocking, "Effective rotation changed.");
}

public sealed record VideoValidationSnapshot(
    string Container, TimeSpan Duration, int Width, int Height, string VideoCodec,
    DateTimeOffset? CaptureTime, int EffectiveRotation, double? Latitude = null,
    double? Longitude = null, bool HasAudio = false);

public sealed record TransferProgress(long BytesTransferred, long? TotalBytes);
public sealed record CompressionProgress(decimal? Percentage, TimeSpan Processed, decimal? Speed, TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining, decimal? FramesPerSecond, long? BitrateBitsPerSecond, long? OutputBytes);

public sealed record Publication(string? PublishedAssetId, IReadOnlyList<string> PendingAlbumIds);
