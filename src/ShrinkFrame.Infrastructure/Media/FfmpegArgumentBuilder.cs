using System.Globalization;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Media;

public sealed class FfmpegArgumentBuilder(MediaToolOptions options)
{
    public IReadOnlyList<string> Build(MediaCompressionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        options.Validate();
        if (!Path.IsPathFullyQualified(request.InputPath) || !Path.IsPathFullyQualified(request.PartialOutputPath))
            throw new ArgumentException("Media process paths must be absolute and server-generated.");
        if (request.VideoStreamIndex < 0 || request.AudioStreamIndex < 0) throw new ArgumentOutOfRangeException(nameof(request));
        if (request.EffectiveRotation is not (0 or 90 or 180 or 270)) throw new ArgumentOutOfRangeException(nameof(request));
        if (request.SourceIsHdr) throw new NotSupportedException(
            "HDR input is not supported in version 1.0 because no validated HDR preservation or tone-mapping policy is configured.");

        var target = DisplayTarget(request);
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-nostats", "-y", "-noautorotate", "-i", request.InputPath,
            "-map", $"0:{request.VideoStreamIndex}",
        };
        if (request.AudioStreamIndex is int audioIndex) args.AddRange(["-map", $"0:{audioIndex}"]);
        var encoder = request.Options.VideoCodec == VideoCodec.H265 ? "libx265" : "libx264";
        args.AddRange(["-map_metadata", "0", "-map_chapters", "0", "-c:v", encoder,
            "-preset", request.Options.EncoderPreset.ToString().ToLowerInvariant(),
            "-crf", request.Options.Crf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p", "-vf", $"scale={target.Width}:{target.Height}:flags=lanczos",
            "-metadata:s:v:0", $"rotate={request.EffectiveRotation}"]);
        if (request.Options.VideoCodec == VideoCodec.H265) args.AddRange(["-tag:v", "hvc1"]);
        if (request.AudioStreamIndex is int)
        {
            var mode = MediaPolicies.ResolveAudioMode(request.Options.AudioMode, request.AudioCodec ?? "unknown");
            args.AddRange(mode == AudioMode.Copy
                ? ["-c:a", "copy"]
                : ["-c:a", "aac", "-b:a", $"{options.AacBitrateKbps}k"]);
        }
        if (options.ThreadCount is int threads) args.AddRange(["-threads", threads.ToString(CultureInfo.InvariantCulture)]);
        args.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-f", "mp4", request.PartialOutputPath]);
        return args;
    }

    private static Dimensions DisplayTarget(MediaCompressionRequest request)
    {
        var swapsAxes = request.EffectiveRotation is 90 or 270;
        var displayWidth = swapsAxes ? request.InputHeight : request.InputWidth;
        var displayHeight = swapsAxes ? request.InputWidth : request.InputHeight;
        var displayTarget = MediaPolicies.TargetDimensions(displayWidth, displayHeight, request.Options.MaximumResolution);
        return swapsAxes ? new(displayTarget.Height, displayTarget.Width) : displayTarget;
    }
}
