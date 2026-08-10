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
        if (string.IsNullOrWhiteSpace(sourceFileName) || sourceFileName.IndexOfAny(['/', '\\', ':']) >= 0 || sourceFileName is "." or "..")
            throw new DomainException(DomainErrors.InvalidText, "Source filename must be a safe leaf name.");
        _ = new CompressionOptions(24, EncoderPreset.Medium, MaximumResolution.Keep, AudioMode.Auto, suffix);
        var dot = sourceFileName.LastIndexOf('.');
        var stem = dot > 0 ? sourceFileName[..dot] : sourceFileName;
        return $"{stem}{suffix}.mp4";
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
