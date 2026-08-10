using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ShrinkFrame.Application;

namespace ShrinkFrame.Infrastructure.Media;

public sealed class FfprobeMediaProbe(MediaToolOptions options) : IMediaProbe
{
    public async Task<MediaProbeResult> ProbeAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(inputPath)) throw new ArgumentException("Probe path must be absolute and server-generated.", nameof(inputPath));
        options.Validate();
        using var process = MediaProcess.Start(options.FfprobePath,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", inputPath]);
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var tail = new BoundedLineTail(options.DiagnosticTailLines);
        var stderr = ReadLinesAsync(process.StandardError, tail);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            MediaProcess.KillTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        var json = await stdout;
        await stderr;
        if (process.ExitCode != 0) throw new MediaProcessException("ffprobe", process.ExitCode, tail.ToString());
        return Map(json);
    }

    public static MediaProbeResult Map(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var format = root.GetProperty("format");
        var formatTags = Tags(format);
        var streams = new List<MediaStreamInfo>();
        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            var type = Text(stream, "codec_type") ?? "unknown";
            var disposition = stream.TryGetProperty("disposition", out var d) ? d : default;
            streams.Add(new(
                Integer(stream, "index") ?? -1, type, Text(stream, "codec_name") ?? "unknown",
                Integer(disposition, "default") == 1, Integer(disposition, "attached_pic") == 1,
                Integer(stream, "width"), Integer(stream, "height"), Text(stream, "pix_fmt"),
                Text(stream, "sample_aspect_ratio"), Rate(Text(stream, "avg_frame_rate") ?? Text(stream, "r_frame_rate")),
                Integer(stream, "channels"), IntegerFromText(stream, "sample_rate"), Rotation(stream), Text(stream, "color_transfer"))
                { AllDispositions = Dispositions(disposition) });
        }
        var video = streams.FirstOrDefault(x => x.CodecType == "video" && !x.IsAttachedPicture)
            ?? throw new InvalidDataException("ffprobe found no playable video stream.");
        var duration = Seconds(Text(format, "duration")) ?? TimeSpan.Zero;
        var capture = CaptureTime(formatTags, root.GetProperty("streams"));
        var location = Location(formatTags);
        return new(Text(format, "format_name") ?? "unknown", duration, capture, location?.Latitude, location?.Longitude,
            NormalizeRotation(video.Rotation ?? 0), streams, json);
    }

    private static async Task ReadLinesAsync(StreamReader reader, BoundedLineTail tail)
    {
        while (await reader.ReadLineAsync() is { } line) tail.Add(line);
    }
    private static Dictionary<string, string> Tags(JsonElement element)
    {
        if (!element.TryGetProperty("tags", out var tags)) return new(StringComparer.OrdinalIgnoreCase);
        return tags.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    }
    private static Dictionary<string, bool> Dispositions(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return new Dictionary<string, bool>();
        return element.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.ToString() == "1", StringComparer.Ordinal);
    }
    private static DateTimeOffset? CaptureTime(Dictionary<string, string> formatTags, JsonElement streamElements)
    {
        string[] names = ["creation_time", "com.apple.quicktime.creationdate", "date"];
        foreach (var name in names)
            if (formatTags.TryGetValue(name, out var text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var value)) return value;
        foreach (var stream in streamElements.EnumerateArray())
        {
            var tags = Tags(stream);
            foreach (var name in names)
                if (tags.TryGetValue(name, out var text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var value)) return value;
        }
        return null;
    }
    private static (double Latitude, double Longitude)? Location(Dictionary<string, string> tags)
    {
        foreach (var name in new[] { "location", "location-eng", "com.apple.quicktime.location.ISO6709" })
        {
            if (!tags.TryGetValue(name, out var value)) continue;
            var second = value.IndexOfAny(['+', '-'], 1);
            if (second > 0 && double.TryParse(value[..second], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude))
            {
                var end = value.IndexOfAny(['+', '-', '/'], second + 1);
                var longitudeText = end > second ? value[second..end] : value[second..].TrimEnd('/');
                if (double.TryParse(longitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) &&
                    latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180) return (latitude, longitude);
            }
        }
        return null;
    }
    private static int? Rotation(JsonElement stream)
    {
        if (stream.TryGetProperty("side_data_list", out var sideData))
            foreach (var item in sideData.EnumerateArray())
                if (Integer(item, "rotation") is int rotation) return rotation;
        var tags = Tags(stream);
        return tags.TryGetValue("rotate", out var text) && int.TryParse(text, CultureInfo.InvariantCulture, out var tagged) ? tagged : null;
    }
    private static int NormalizeRotation(int value)
    {
        var normalized = ((value % 360) + 360) % 360;
        return normalized switch { < 45 or >= 315 => 0, < 135 => 90, < 225 => 180, _ => 270 };
    }
    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) ? value.ToString() : null;
    private static int? Integer(JsonElement element, string name) => int.TryParse(Text(element, name),
        NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static int? IntegerFromText(JsonElement element, string name) => Integer(element, name);
    private static decimal? Rate(string? value)
    {
        var parts = value?.Split('/');
        return parts?.Length == 2 && decimal.TryParse(parts[0], CultureInfo.InvariantCulture, out var numerator) &&
            decimal.TryParse(parts[1], CultureInfo.InvariantCulture, out var denominator) && denominator != 0 ? numerator / denominator : null;
    }
    private static TimeSpan? Seconds(string? value) => double.TryParse(value, NumberStyles.Float,
        CultureInfo.InvariantCulture, out var seconds) ? TimeSpan.FromSeconds(seconds) : null;
}

public sealed class MediaProcessException(string tool, int exitCode, string diagnostics)
    : Exception($"{tool} exited with code {exitCode}. {diagnostics}")
{
    public int ExitCode { get; } = exitCode;
    public string Diagnostics { get; } = diagnostics;
}
