namespace ShrinkFrame.Domain;

public sealed class DomainException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public static class DomainErrors
{
    public const string InvalidIdentifier = "domain.identifier.invalid";
    public const string InvalidText = "domain.text.invalid";
    public const string InvalidBatchSource = "batch.source.invalid";
    public const string InvalidJobTransition = "job.transition.invalid";
    public const string InvalidPublicationTransition = "publication.transition.invalid";
    public const string PublicationOverrideRequired = "publication.override.required";
    public const string JobNotValidated = "job.validation.required";
    public const string InvalidCrf = "options.crf.invalid";
    public const string InvalidSuffix = "options.suffix.invalid";
    public const string InvalidResolution = "options.resolution.invalid";
    public const string InvalidAudio = "options.audio.invalid";
    public const string InvalidDimensions = "media.dimensions.invalid";
    public const string InvalidDuration = "media.duration.invalid";
    public const string InvalidSize = "media.size.invalid";
    public const string InvalidArtifact = "artifact.key.invalid";
    public const string BlockingFindings = "validation.blocking_findings";
    public const string CapacityOverrideRequired = "capacity.override.required";
}
