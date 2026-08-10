using ShrinkFrame.Domain;

namespace ShrinkFrame.Application;

public sealed record MediaStreamInfo(
    int Index, string CodecType, string CodecName, bool IsDefault, bool IsAttachedPicture,
    int? Width = null, int? Height = null, string? PixelFormat = null,
    string? SampleAspectRatio = null, decimal? FrameRate = null,
    int? Channels = null, int? SampleRate = null, int? Rotation = null, string? ColorTransfer = null)
{
    public bool IsHdr => ColorTransfer is "smpte2084" or "arib-std-b67";
    public IReadOnlyDictionary<string, bool> AllDispositions { get; init; } = new Dictionary<string, bool>();
}

public sealed record MediaProbeResult(
    string Container, TimeSpan Duration, DateTimeOffset? CaptureTime,
    double? Latitude, double? Longitude, int EffectiveRotation,
    IReadOnlyList<MediaStreamInfo> Streams, string RawJson)
{
    public MediaStreamInfo PrimaryVideo => Streams.Where(x => x.CodecType == "video" && !x.IsAttachedPicture)
        .OrderByDescending(x => x.IsDefault).First();
    public MediaStreamInfo? PrimaryAudio => Streams.Where(x => x.CodecType == "audio")
        .OrderByDescending(x => x.IsDefault).FirstOrDefault();
}

public sealed record MediaCompressionRequest(
    string InputPath, string PartialOutputPath, TimeSpan InputDuration,
    int InputWidth, int InputHeight, int EffectiveRotation,
    int VideoStreamIndex, int? AudioStreamIndex, string? AudioCodec,
    CompressionOptions Options, bool SourceIsHdr = false);

public sealed record MediaProcessResult(int ExitCode, bool Succeeded, bool Cancelled, string DiagnosticTail);

public interface IMediaProbe
{
    Task<MediaProbeResult> ProbeAsync(string inputPath, CancellationToken cancellationToken = default);
}

public interface IMediaCompressor
{
    Task<MediaProcessResult> CompressAsync(MediaCompressionRequest request,
        IProgress<CompressionProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed record MediaToolStatus(string FfmpegVersion, string FfprobeVersion, bool Available, string? Error);

public interface IMediaToolStatus
{
    MediaToolStatus Current { get; }
}
