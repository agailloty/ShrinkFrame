using System.Diagnostics;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;
using ShrinkFrame.Infrastructure.Media;

namespace ShrinkFrame.Infrastructure.Tests;

[TestClass]
public sealed class MediaInfrastructureTests
{
    [TestMethod]
    public async Task Compressor_cancellation_awaits_exit_and_removes_partial_output()
    {
        var root = Path.Combine(Path.GetTempPath(), $"shrinkframe-media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "fixture input & safe.mp4");
            var partial = Path.Combine(root, "result & safe.partial.mp4");
            await GenerateFixtureAsync(input);
            var compressor = new FfmpegMediaCompressor(new MediaToolOptions(), Builder());
            var request = new MediaCompressionRequest(input, partial, TimeSpan.FromSeconds(30), 1280, 720, 0,
                0, null, null, new CompressionOptions(24, EncoderPreset.Veryslow, MaximumResolution.Keep, AudioMode.Auto, "_V"));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                () => compressor.CompressAsync(request, cancellationToken: cancellation.Token));
            Assert.IsFalse(File.Exists(partial));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Argument_builder_keeps_metacharacter_paths_as_single_arguments_and_enforces_contract()
    {
        var input = Path.GetFullPath("fixture input & $(safe); [x].mov");
        var output = Path.GetFullPath("result & safe.partial.mp4");
        var args = Builder().Build(Request(input, output, 1920, 1080, 0, MaximumResolution.P720, "opus"));

        Assert.AreEqual(1, args.Count(x => x == input));
        Assert.AreEqual(1, args.Count(x => x == output));
        CollectionAssert.Contains(args.ToArray(), "libx264");
        CollectionAssert.Contains(args.ToArray(), "+faststart");
        CollectionAssert.Contains(args.ToArray(), "pipe:1");
        CollectionAssert.Contains(args.ToArray(), "scale=720:404:flags=lanczos");
        AssertSequence(args, "-map", "0:2");
        AssertSequence(args, "-map", "0:4");
        AssertSequence(args, "-c:a", "aac");
    }

    [TestMethod]
    public void Argument_builder_preserves_portrait_rotation_without_upscale_and_copies_compatible_audio()
    {
        var args = Builder().Build(Request(Path.GetFullPath("input.mp4"), Path.GetFullPath("result.partial.mp4"),
            1080, 1920, 90, MaximumResolution.P1080, "aac"));

        CollectionAssert.Contains(args.ToArray(), "scale=606:1080:flags=lanczos");
        AssertSequence(args, "-metadata:s:v:0", "rotate=90");
        AssertSequence(args, "-c:a", "copy");

        var small = Builder().Build(Request(Path.GetFullPath("small.mp4"), Path.GetFullPath("small.partial.mp4"),
            640, 360, 0, MaximumResolution.P1080, "aac"));
        CollectionAssert.Contains(small.ToArray(), "scale=640:360:flags=lanczos");
    }

    [TestMethod]
    public void Argument_builder_selects_libx265_and_hvc1_for_h265()
    {
        var request = Request(Path.GetFullPath("input.mp4"), Path.GetFullPath("result.partial.mp4"),
            1920, 1080, 0, MaximumResolution.Keep, "aac") with
        {
            Options = new CompressionOptions(24, EncoderPreset.Medium, MaximumResolution.Keep, AudioMode.Auto, "_V", VideoCodec.H265),
        };

        var args = Builder().Build(request);

        AssertSequence(args, "-c:v", "libx265");
        AssertSequence(args, "-tag:v", "hvc1");
        CollectionAssert.DoesNotContain(args.ToArray(), "libx264");
    }

    [TestMethod]
    public void Argument_builder_rejects_hdr_without_a_color_management_policy()
    {
        var request = Request(Path.GetFullPath("hdr.mp4"), Path.GetFullPath("hdr.partial.mp4"),
            3840, 2160, 0, MaximumResolution.P1080, "aac") with { SourceIsHdr = true };
        Assert.ThrowsExactly<NotSupportedException>(() => Builder().Build(request));
    }

    [TestMethod]
    public void Progress_is_emitted_only_for_complete_structured_blocks()
    {
        var parser = new FfmpegProgressParser(TimeSpan.FromSeconds(10));
        Assert.IsNull(parser.Accept("out_time_us=2500000"));
        Assert.IsNull(parser.Accept("speed=2.0x"));
        Assert.IsNull(parser.Accept("fps=30.5"));
        Assert.IsNull(parser.Accept("bitrate=800.0kbits/s"));
        Assert.IsNull(parser.Accept("total_size=12345"));
        var progress = parser.Accept("progress=continue");
        Assert.IsNotNull(progress);
        Assert.AreEqual(TimeSpan.FromSeconds(2.5), progress.Processed);
        Assert.AreEqual(25m, progress.Percentage);
        Assert.AreEqual(2.0m, progress.Speed);
        Assert.AreEqual(30.5m, progress.FramesPerSecond);
        Assert.AreEqual(800_000, progress.BitrateBitsPerSecond);
        Assert.AreEqual(12_345, progress.OutputBytes);
    }

    [TestMethod]
    public void Probe_json_maps_streams_quicktime_metadata_location_and_display_matrix_rotation()
    {
        const string json = """
        {"streams":[
          {"index":0,"codec_name":"mjpeg","codec_type":"video","width":100,"height":100,"disposition":{"default":0,"attached_pic":1}},
          {"index":2,"codec_name":"h264","codec_type":"video","width":1080,"height":1920,"pix_fmt":"yuv420p","sample_aspect_ratio":"1:1","avg_frame_rate":"30000/1001","disposition":{"default":1,"attached_pic":0},"side_data_list":[{"side_data_type":"Display Matrix","rotation":-90}]},
          {"index":4,"codec_name":"aac","codec_type":"audio","sample_rate":"48000","channels":2,"disposition":{"default":1,"attached_pic":0}}
        ],"format":{"format_name":"mov,mp4,m4a,3gp,3g2,mj2","duration":"12.5","tags":{"com.apple.quicktime.creationdate":"2024-01-02T03:04:05+02:00","com.apple.quicktime.location.ISO6709":"+48.8566+002.3522/"}}}
        """;

        var result = FfprobeMediaProbe.Map(json);
        Assert.AreEqual(3, result.Streams.Count);
        Assert.AreEqual(2, result.PrimaryVideo.Index);
        Assert.IsTrue(result.PrimaryVideo.AllDispositions["default"]);
        Assert.AreEqual(4, result.PrimaryAudio?.Index);
        Assert.AreEqual(270, result.EffectiveRotation);
        Assert.AreEqual(TimeSpan.FromSeconds(12.5), result.Duration);
        Assert.IsNotNull(result.Latitude);
        Assert.IsNotNull(result.Longitude);
        Assert.IsTrue(Math.Abs(48.8566 - result.Latitude.Value) < 0.00001);
        Assert.IsTrue(Math.Abs(2.3522 - result.Longitude.Value) < 0.00001);
        Assert.AreEqual(TimeSpan.FromHours(2), result.CaptureTime?.Offset);
    }

    private static FfmpegArgumentBuilder Builder() => new(new MediaToolOptions());
    private static async Task GenerateFixtureAsync(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg", UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
            "testsrc2=size=1280x720:rate=30:duration=30", "-c:v", "libx264", "-preset", "ultrafast", path })
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start fixture generator.");
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) Assert.Fail(await stderr);
        await stderr;
    }
    private static MediaCompressionRequest Request(string input, string output, int width, int height, int rotation,
        MaximumResolution maximum, string audioCodec) => new(input, output, TimeSpan.FromSeconds(10), width, height,
            rotation, 2, 4, audioCodec, new CompressionOptions(24, EncoderPreset.Medium, maximum, AudioMode.Auto, "_V"));
    private static void AssertSequence(IReadOnlyList<string> arguments, string first, string second)
    {
        Assert.IsTrue(arguments.Select((value, index) => (value, index))
            .Any(x => x.value == first && x.index + 1 < arguments.Count && arguments[x.index + 1] == second));
    }
}
