using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace ShrinkFrame.Domain;

public sealed record CompressionOptions
{
    private static readonly Regex SuffixPattern = new("^_[A-Za-z0-9][A-Za-z0-9_-]{0,31}$", RegexOptions.CultureInvariant);
    public CompressionOptions(int crf, EncoderPreset encoderPreset, MaximumResolution maximumResolution, AudioMode audioMode, string suffix)
    {
        if (crf is < 18 or > 36) throw new DomainException(DomainErrors.InvalidCrf, "CRF must be between 18 and 36.");
        if (!Enum.IsDefined(encoderPreset)) throw new DomainException(DomainErrors.InvalidText, "Encoder preset is invalid.");
        if (!Enum.IsDefined(maximumResolution)) throw new DomainException(DomainErrors.InvalidResolution, "Maximum resolution is invalid.");
        if (!Enum.IsDefined(audioMode)) throw new DomainException(DomainErrors.InvalidAudio, "Audio mode is invalid.");
        if (suffix is null || !SuffixPattern.IsMatch(suffix)) throw new DomainException(DomainErrors.InvalidSuffix, "Suffix must begin with underscore and contain 2-33 safe characters.");
        Crf = crf; EncoderPreset = encoderPreset; MaximumResolution = maximumResolution; AudioMode = audioMode; Suffix = suffix;
    }
    public int Crf { get; }
    public EncoderPreset EncoderPreset { get; }
    public MaximumResolution MaximumResolution { get; }
    public AudioMode AudioMode { get; }
    public string Suffix { get; }
    public bool HasQualityWarning => Crf > 30;
}

public sealed record BuiltInPreset(PresetId Id, string Name, CompressionOptions Options);

public static class BuiltInPresets
{
    private static readonly ReadOnlyCollection<BuiltInPreset> Presets = Array.AsReadOnly(new[]
    {
        Make("archival-quality", "Archival Quality", 18, EncoderPreset.Slow, MaximumResolution.Keep),
        Make("high-quality", "High Quality", 21, EncoderPreset.Medium, MaximumResolution.Keep),
        Make("balanced", "Balanced", 24, EncoderPreset.Medium, MaximumResolution.Keep),
        Make("smaller-file", "Smaller File", 27, EncoderPreset.Medium, MaximumResolution.Keep),
        Make("full-hd", "Full HD", 23, EncoderPreset.Medium, MaximumResolution.P1080),
        Make("hd", "HD", 24, EncoderPreset.Medium, MaximumResolution.P720),
        Make("smallest-practical", "Smallest Practical", 30, EncoderPreset.Slow, MaximumResolution.P720),
    });
    public static IReadOnlyList<BuiltInPreset> All => Presets;
    public static BuiltInPreset Get(PresetId id) => Presets.SingleOrDefault(x => x.Id == id)
        ?? throw new DomainException(DomainErrors.InvalidIdentifier, "Unknown preset ID.");
    public static CompressionOptions Snapshot(PresetId id)
    {
        var o = Get(id).Options;
        return new(o.Crf, o.EncoderPreset, o.MaximumResolution, o.AudioMode, o.Suffix);
    }
    private static BuiltInPreset Make(string id, string name, int crf, EncoderPreset preset, MaximumResolution resolution)
        => new(new(id), name, new(crf, preset, resolution, AudioMode.Auto, "_V"));
}
