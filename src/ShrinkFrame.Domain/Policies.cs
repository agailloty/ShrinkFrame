namespace ShrinkFrame.Domain;

public readonly record struct Dimensions(int Width, int Height);

public static class MediaPolicies
{
    private static readonly HashSet<string> Mp4CompatibleAudioCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "aac", "alac", "mp3", "ac3", "eac3",
    };

    public static TimeSpan DurationTolerance(TimeSpan inputDuration)
    {
        if (inputDuration < TimeSpan.Zero) throw new DomainException(DomainErrors.InvalidDuration, "Duration cannot be negative.");
        return TimeSpan.FromSeconds(Math.Max(1, inputDuration.TotalSeconds * 0.005));
    }

    public static bool IsDurationWithinTolerance(TimeSpan input, TimeSpan output)
        => output >= TimeSpan.Zero && (input - output).Duration() <= DurationTolerance(input);

    public static Dimensions TargetDimensions(int width, int height, MaximumResolution maximum)
    {
        if (width <= 0 || height <= 0) throw new DomainException(DomainErrors.InvalidDimensions, "Dimensions must be positive.");
        if (!Enum.IsDefined(maximum)) throw new DomainException(DomainErrors.InvalidResolution, "Maximum resolution is invalid.");
        var limit = (int)maximum;
        var scale = limit == 0 || Math.Max(width, height) <= limit ? 1d : limit / (double)Math.Max(width, height);
        static int Even(double value) => Math.Max(2, (int)Math.Floor(value / 2d) * 2);
        return new(Even(width * scale), Even(height * scale));
    }

    public static JobState ClassifyValidatedOutput(long inputBytes, long outputBytes, IEnumerable<ValidationFinding> findings)
    {
        if (inputBytes < 0 || outputBytes <= 0) throw new DomainException(DomainErrors.InvalidSize, "File sizes are invalid.");
        if (findings.Any(x => x.IsBlocking)) throw new DomainException(DomainErrors.BlockingFindings, "Blocking findings prevent successful validation.");
        return outputBytes < inputBytes ? JobState.Ready : JobState.NotBeneficial;
    }

    public static AudioMode ResolveAudioMode(AudioMode requested, string sourceCodec)
    {
        if (!Enum.IsDefined(requested) || string.IsNullOrWhiteSpace(sourceCodec)) throw new DomainException(DomainErrors.InvalidAudio, "Audio selection and source codec are required.");
        if (requested != AudioMode.Auto) return requested;
        return Mp4CompatibleAudioCodecs.Contains(sourceCodec) ? AudioMode.Copy : AudioMode.Aac;
    }

    public static string BuildOutputFileName(string sourceFileName, string suffix)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName) || sourceFileName.IndexOfAny(['/', '\\', ':']) >= 0 ||
            sourceFileName.Any(char.IsControl) || sourceFileName is "." or "..")
            throw new DomainException(DomainErrors.InvalidText, "Source filename must be a safe leaf name.");
        _ = new CompressionOptions(24, EncoderPreset.Medium, MaximumResolution.Keep, AudioMode.Auto, suffix);
        var dot = sourceFileName.LastIndexOf('.');
        var stem = dot > 0 ? sourceFileName[..dot] : sourceFileName;
        return $"{stem}{suffix}.mp4";
    }
}

public static class OutputValidationPolicy
{
    public static IReadOnlyList<ValidationFinding> Validate(
        VideoValidationSnapshot input, VideoValidationSnapshot output, CompressionOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        var findings = new List<ValidationFinding>();
        var formats = output.Container.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!formats.Contains("mp4", StringComparer.OrdinalIgnoreCase))
            findings.Add(new("validation.container", FindingSeverity.Blocking, "Output container is not MP4."));
        var expectedCodec = options.VideoCodec == VideoCodec.H265 ? "hevc" : "h264";
        if (!output.VideoCodec.Equals(expectedCodec, StringComparison.OrdinalIgnoreCase))
            findings.Add(new("validation.codec", FindingSeverity.Blocking,
                $"Output video codec is not {options.VideoCodec switch { VideoCodec.H265 => "H.265/HEVC", _ => "H.264" }}."));
        if (!MediaPolicies.IsDurationWithinTolerance(input.Duration, output.Duration))
            findings.Add(new("validation.duration", FindingSeverity.Blocking, "Output duration is outside tolerance."));

        var target = MediaPolicies.TargetDimensions(input.Width, input.Height, options.MaximumResolution);
        if (output.Width <= 0 || output.Height <= 0 || output.Width % 2 != 0 || output.Height % 2 != 0 ||
            output.Width > input.Width || output.Height > input.Height || output.Width > target.Width || output.Height > target.Height)
            findings.Add(new("validation.dimensions", FindingSeverity.Blocking, "Output dimensions are invalid, odd, or upscaled."));
        if (input.CaptureTime.HasValue && !output.CaptureTime.HasValue)
            findings.Add(ValidationFinding.CaptureDateLost());
        else if (input.CaptureTime.HasValue && output.CaptureTime != input.CaptureTime)
            findings.Add(ValidationFinding.CaptureDateChanged());
        if (output.EffectiveRotation != input.EffectiveRotation)
            findings.Add(ValidationFinding.RotationChanged());
        if ((input.Latitude.HasValue || input.Longitude.HasValue) && (!output.Latitude.HasValue || !output.Longitude.HasValue))
            findings.Add(new("validation.metadata.location_lost", FindingSeverity.Warning, "Location metadata was not retained."));
        if (input.HasAudio && !output.HasAudio)
            findings.Add(new("validation.metadata.audio_lost", FindingSeverity.Warning, "The source audio stream was not retained."));
        return findings;
    }
}

public readonly record struct CapacityDecision(long RequiredBytes, long AvailableBytes, bool IsForced)
{
    public bool HasWarning => RequiredBytes > AvailableBytes;
    public bool IsAllowed => !HasWarning || IsForced;
    public void EnsureAllowed()
    {
        if (!IsAllowed) throw new DomainException(DomainErrors.CapacityOverrideRequired, "Insufficient capacity requires explicit override.");
    }
}
