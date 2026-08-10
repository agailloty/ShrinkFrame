using System.Diagnostics;
using System.Globalization;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Media;

public sealed class FfmpegProgressParser(TimeSpan duration)
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    private readonly Stopwatch elapsed = Stopwatch.StartNew();

    public CompressionProgress? Accept(string line)
    {
        var separator = line.IndexOf('=');
        if (separator <= 0) return null;
        values[line[..separator]] = line[(separator + 1)..];
        if (!line.StartsWith("progress=", StringComparison.Ordinal)) return null;
        var processed = ParseProcessed();
        decimal? percentage = duration > TimeSpan.Zero ? Math.Min(100m, (decimal)(processed / duration) * 100m) : null;
        var speed = Decimal("speed", trim: 'x');
        TimeSpan? remaining = speed > 0 && processed < duration
            ? TimeSpan.FromTicks((long)((duration - processed).Ticks / (double)speed.Value)) : null;
        return new(percentage, processed, speed, elapsed.Elapsed, remaining, Decimal("fps"),
            ParseBitrate(), Long("total_size"));
    }

    private TimeSpan ParseProcessed()
    {
        if (Long("out_time_us") is long microseconds) return TimeSpan.FromTicks(microseconds * 10);
        return TimeSpan.TryParse(values.GetValueOrDefault("out_time"), CultureInfo.InvariantCulture, out var parsed) ? parsed : TimeSpan.Zero;
    }
    private long? ParseBitrate()
    {
        var text = values.GetValueOrDefault("bitrate");
        if (text?.EndsWith("kbits/s", StringComparison.Ordinal) == true &&
            decimal.TryParse(text[..^7], NumberStyles.Float, CultureInfo.InvariantCulture, out var kbps)) return (long)(kbps * 1000);
        return null;
    }
    private decimal? Decimal(string key, char? trim = null)
    {
        var text = values.GetValueOrDefault(key);
        if (trim is char suffix) text = text?.TrimEnd(suffix);
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
    private long? Long(string key) => long.TryParse(values.GetValueOrDefault(key), NumberStyles.Integer,
        CultureInfo.InvariantCulture, out var value) ? value : null;
}
