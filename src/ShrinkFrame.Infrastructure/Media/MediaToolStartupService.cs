using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShrinkFrame.Application;

namespace ShrinkFrame.Infrastructure.Media;

public sealed class MediaToolStatusProvider : IMediaToolStatus
{
    public MediaToolStatus Current { get; internal set; } = new("Unknown", "Unknown", false, "Startup check has not run.");
}

public sealed class MediaToolStartupService(MediaToolOptions options, MediaToolStatusProvider status,
    ILogger<MediaToolStartupService> logger) : IHostedService
{
    private static readonly Action<ILogger, string, string, Exception?> LogAvailable = LoggerMessage.Define<string, string>(
        LogLevel.Information, new EventId(1201, "MediaToolsAvailable"),
        "Media tools available: {FfmpegVersion}; {FfprobeVersion}");
    private static readonly Action<ILogger, Exception?> LogUnavailable = LoggerMessage.Define(
        LogLevel.Critical, new EventId(1202, "MediaToolsUnavailable"),
        "Required FFmpeg tools are unavailable.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var ffmpeg = await VersionAsync(options.FfmpegPath, cancellationToken);
            var ffprobe = await VersionAsync(options.FfprobePath, cancellationToken);
            status.Current = new(ffmpeg, ffprobe, true, null);
            LogAvailable(logger, ffmpeg, ffprobe, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            status.Current = new("Unavailable", "Unavailable", false, exception.Message);
            LogUnavailable(logger, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<string> VersionAsync(string executable, CancellationToken cancellationToken)
    {
        using var process = MediaProcess.Start(executable, ["-version"]);
        var firstLine = await process.StandardOutput.ReadLineAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            MediaProcess.KillTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await stderr;
            throw;
        }
        await stderr;
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(firstLine))
            throw new InvalidOperationException($"{Path.GetFileName(executable)} version check failed with exit code {process.ExitCode}.");
        return firstLine;
    }
}
