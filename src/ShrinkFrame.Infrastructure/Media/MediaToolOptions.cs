namespace ShrinkFrame.Infrastructure.Media;

public sealed class MediaToolOptions
{
    public const string SectionName = "MediaTools";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
    public int DiagnosticTailLines { get; set; } = 200;
    public int AacBitrateKbps { get; set; } = 192;
    public int? ThreadCount { get; set; }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FfmpegPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(FfprobePath);
        if (DiagnosticTailLines is < 10 or > 10_000) throw new ArgumentOutOfRangeException(nameof(DiagnosticTailLines));
        if (AacBitrateKbps is < 64 or > 512) throw new ArgumentOutOfRangeException(nameof(AacBitrateKbps));
        if (ThreadCount is <= 0) throw new ArgumentOutOfRangeException(nameof(ThreadCount));
    }
}
