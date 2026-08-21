namespace ShrinkFrame.Domain.Tests;

[TestClass]
public sealed class OptionsAndPolicyTests
{
    [TestMethod]
    public void OutputValidationAcceptsDurationBoundaryPortraitAndNoUpscale()
    {
        var captured = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var input = new VideoValidationSnapshot("mov", TimeSpan.FromSeconds(200), 1080, 1920, "hevc", captured, 90);
        var output = new VideoValidationSnapshot("mov,mp4,m4a,3gp,3g2,mj2", TimeSpan.FromSeconds(201), 720, 1280, "h264", captured, 90);
        var findings = OutputValidationPolicy.Validate(input, output,
            new CompressionOptions(24, EncoderPreset.Medium, MaximumResolution.P1440, AudioMode.Auto, "_V"));
        Assert.IsFalse(findings.Any(x => x.IsBlocking));
    }

    [TestMethod]
    public void OutputValidationBlocksContainerDurationUpscaleCaptureDateAndRotation()
    {
        var captured = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var input = new VideoValidationSnapshot("mov", TimeSpan.FromSeconds(10), 640, 360, "h264", captured, 90);
        var output = new VideoValidationSnapshot("matroska", TimeSpan.FromSeconds(11.001), 642, 362, "vp9", null, 0);
        var codes = OutputValidationPolicy.Validate(input, output,
            new CompressionOptions(24, EncoderPreset.Medium, MaximumResolution.Keep, AudioMode.Auto, "_V"))
            .Where(x => x.IsBlocking).Select(x => x.Code).ToHashSet();
        foreach (var code in new[] { "validation.container", "validation.codec", "validation.duration",
            "validation.dimensions", "validation.capture_date.lost", "validation.rotation.changed" })
            Assert.Contains(code, codes);
    }

    [TestMethod]
    public void OutputValidationRequiresTheSelectedVideoCodec()
    {
        var input = new VideoValidationSnapshot("mp4", TimeSpan.FromSeconds(10), 640, 360, "h264", null, 0);
        var hevc = new VideoValidationSnapshot("mp4", TimeSpan.FromSeconds(10), 640, 360, "hevc", null, 0);
        var options = new CompressionOptions(24, EncoderPreset.Medium, MaximumResolution.Keep, AudioMode.Auto, "_V", VideoCodec.H265);

        Assert.IsFalse(OutputValidationPolicy.Validate(input, hevc, options).Any(x => x.IsBlocking));
        Assert.IsTrue(OutputValidationPolicy.Validate(input, hevc with { VideoCodec = "h264" }, options)
            .Any(x => x.Code == "validation.codec"));
    }

    [TestMethod]
    public void OtherMetadataLossWarnsWithoutBlocking()
    {
        var input = new VideoValidationSnapshot("mp4", TimeSpan.FromSeconds(3), 640, 360, "h264", null, 0, 1, 2, true);
        var output = new VideoValidationSnapshot("mp4", TimeSpan.FromSeconds(3), 640, 360, "h264", null, 0);
        var findings = OutputValidationPolicy.Validate(input, output,
            new CompressionOptions(24, EncoderPreset.Medium, MaximumResolution.Keep, AudioMode.Auto, "_V"));
        Assert.HasCount(2, findings);
        Assert.IsTrue(findings.All(x => x.Severity == FindingSeverity.Warning));
    }
    [TestMethod]
    [DataRow(18, false)] [DataRow(30, false)] [DataRow(31, true)] [DataRow(36, true)]
    public void Crf_boundaries_are_valid(int crf, bool warning)
        => Assert.AreEqual(warning, Options(crf).HasQualityWarning);

    [TestMethod]
    [DataRow(17)] [DataRow(37)]
    public void Crf_outside_range_is_rejected(int crf) => AssertCode(DomainErrors.InvalidCrf, () => Options(crf));

    [TestMethod]
    [DataRow("_V")] [DataRow("_version-2")] [DataRow("_1")]
    public void Safe_suffix_is_accepted(string suffix) => Assert.AreEqual(suffix, Options(suffix: suffix).Suffix);

    [TestMethod]
    [DataRow("")] [DataRow("V")] [DataRow("_")] [DataRow("_bad name")] [DataRow("../x")] [DataRow("_toolongtoolongtoolongtoolongtoolong")]
    public void Unsafe_suffix_is_rejected(string suffix) => AssertCode(DomainErrors.InvalidSuffix, () => Options(suffix: suffix));

    [TestMethod]
    public void Invalid_resolution_and_audio_values_are_rejected()
    {
        AssertCode(DomainErrors.InvalidResolution, () => new CompressionOptions(24, EncoderPreset.Medium, (MaximumResolution)42, AudioMode.Auto, "_V"));
        AssertCode(DomainErrors.InvalidAudio, () => new CompressionOptions(24, EncoderPreset.Medium, MaximumResolution.Keep, (AudioMode)42, "_V"));
        AssertCode(DomainErrors.InvalidText, () => new CompressionOptions(24, EncoderPreset.Medium, MaximumResolution.Keep, AudioMode.Auto, "_V", (VideoCodec)42));
    }

    [TestMethod]
    public void Built_in_presets_are_complete_and_snapshots_are_independent()
    {
        Assert.AreEqual(7, BuiltInPresets.All.Count);
        var first = BuiltInPresets.Snapshot(new("balanced"));
        var second = BuiltInPresets.Snapshot(new("balanced"));
        Assert.AreNotSame(first, second);
        Assert.AreEqual(first, second);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<BuiltInPreset>)BuiltInPresets.All).Add(BuiltInPresets.All[0]));
    }

    [TestMethod]
    [DataRow(1920, 1080, MaximumResolution.P1080, 1080, 606)]
    [DataRow(3840, 2160, MaximumResolution.P1080, 1080, 606)]
    [DataRow(2160, 3840, MaximumResolution.P1080, 606, 1080)]
    [DataRow(853, 480, MaximumResolution.Keep, 852, 480)]
    [DataRow(640, 360, MaximumResolution.P1080, 640, 360)]
    [DataRow(1001, 1000, MaximumResolution.P720, 720, 718)]
    public void Dimensions_never_upscale_and_are_even(int width, int height, MaximumResolution max, int expectedWidth, int expectedHeight)
    {
        var result = MediaPolicies.TargetDimensions(width, height, max);
        Assert.AreEqual(new Dimensions(expectedWidth, expectedHeight), result);
        Assert.AreEqual(0, result.Width % 2); Assert.AreEqual(0, result.Height % 2);
        Assert.IsTrue(result.Width <= width && result.Height <= height);
    }

    [TestMethod]
    [DataRow(100, 1)] [DataRow(200, 1)] [DataRow(400, 2)]
    public void Duration_tolerance_uses_one_second_or_half_percent(double seconds, double expected)
        => Assert.AreEqual(TimeSpan.FromSeconds(expected), MediaPolicies.DurationTolerance(TimeSpan.FromSeconds(seconds)));

    [TestMethod]
    public void Duration_tolerance_boundary_is_inclusive()
    {
        Assert.IsTrue(MediaPolicies.IsDurationWithinTolerance(TimeSpan.FromSeconds(400), TimeSpan.FromSeconds(402)));
        Assert.IsFalse(MediaPolicies.IsDurationWithinTolerance(TimeSpan.FromSeconds(400), TimeSpan.FromSeconds(402.001)));
    }

    [TestMethod]
    public void Output_classification_respects_size_and_blocking_findings()
    {
        Assert.AreEqual(JobState.Ready, MediaPolicies.ClassifyValidatedOutput(100, 99, []));
        Assert.AreEqual(JobState.NotBeneficial, MediaPolicies.ClassifyValidatedOutput(100, 100, [new("metadata.description.lost", FindingSeverity.Warning, "warning")]));
        Assert.AreEqual(JobState.NotBeneficial, MediaPolicies.ClassifyValidatedOutput(100, 101, []));
        AssertCode(DomainErrors.BlockingFindings, () => MediaPolicies.ClassifyValidatedOutput(100, 50, [ValidationFinding.CaptureDateLost()]));
        AssertCode(DomainErrors.BlockingFindings, () => MediaPolicies.ClassifyValidatedOutput(100, 50, [ValidationFinding.RotationChanged()]));
    }

    [TestMethod]
    public void Capacity_warning_requires_force()
    {
        var warning = new CapacityDecision(101, 100, false);
        Assert.IsTrue(warning.HasWarning); Assert.IsFalse(warning.IsAllowed);
        AssertCode(DomainErrors.CapacityOverrideRequired, warning.EnsureAllowed);
        new CapacityDecision(101, 100, true).EnsureAllowed();
        Assert.IsTrue(new CapacityDecision(100, 100, false).IsAllowed);
    }

    [TestMethod]
    public void Audio_auto_mode_uses_compatibility_table()
    {
        Assert.AreEqual(AudioMode.Copy, MediaPolicies.ResolveAudioMode(AudioMode.Auto, "aac"));
        Assert.AreEqual(AudioMode.Copy, MediaPolicies.ResolveAudioMode(AudioMode.Auto, "EAC3"));
        Assert.AreEqual(AudioMode.Aac, MediaPolicies.ResolveAudioMode(AudioMode.Auto, "opus"));
        Assert.AreEqual(AudioMode.Aac, MediaPolicies.ResolveAudioMode(AudioMode.Aac, "aac"));
    }

    [TestMethod]
    public void Output_filename_is_safe_and_uses_mp4_extension()
    {
        Assert.AreEqual("holiday.final_V.mp4", MediaPolicies.BuildOutputFileName("holiday.final.mov", "_V"));
        Assert.AreEqual("clip_V.mp4", MediaPolicies.BuildOutputFileName("clip", "_V"));
        AssertCode(DomainErrors.InvalidText, () => MediaPolicies.BuildOutputFileName("../clip.mov", "_V"));
        AssertCode(DomainErrors.InvalidSuffix, () => MediaPolicies.BuildOutputFileName("clip.mov", " bad"));
    }

    private static CompressionOptions Options(int crf = 24, string suffix = "_V") => new(crf, EncoderPreset.Medium, MaximumResolution.Keep, AudioMode.Auto, suffix);
    internal static void AssertCode(string expected, Action action) => Assert.AreEqual(expected, Assert.ThrowsExactly<DomainException>(action).Code);
}
