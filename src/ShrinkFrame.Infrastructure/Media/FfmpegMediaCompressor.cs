using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Media;

public sealed class FfmpegMediaCompressor(MediaToolOptions options, FfmpegArgumentBuilder argumentBuilder) : IMediaCompressor
{
    public async Task<MediaProcessResult> CompressAsync(MediaCompressionRequest request,
        IProgress<CompressionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var arguments = argumentBuilder.Build(request);
        EnsurePartialOutput(request.PartialOutputPath);
        DeletePartial(request.PartialOutputPath);
        using var process = MediaProcess.Start(options.FfmpegPath, arguments);
        var tail = new BoundedLineTail(options.DiagnosticTailLines);
        var parser = new FfmpegProgressParser(request.InputDuration);
        var stdout = ReadProgressAsync(process.StandardOutput, parser, progress);
        var stderr = ReadDiagnosticsAsync(process.StandardError, tail);
        var cancelled = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            MediaProcess.KillTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }
        finally
        {
            await Task.WhenAll(stdout, stderr);
        }

        if (cancelled)
        {
            DeletePartial(request.PartialOutputPath);
            throw new OperationCanceledException(cancellationToken);
        }
        if (process.ExitCode != 0)
        {
            DeletePartial(request.PartialOutputPath);
            return new(process.ExitCode, false, false, tail.ToString());
        }
        if (!File.Exists(request.PartialOutputPath) || new FileInfo(request.PartialOutputPath).Length == 0)
        {
            DeletePartial(request.PartialOutputPath);
            return new(process.ExitCode, false, false, "FFmpeg reported success but produced no output.");
        }
        return new(process.ExitCode, true, false, tail.ToString());
    }

    private static async Task ReadProgressAsync(StreamReader reader, FfmpegProgressParser parser,
        IProgress<CompressionProgress>? progress)
    {
        while (await reader.ReadLineAsync() is { } line)
            if (parser.Accept(line) is { } update) progress?.Report(update);
    }
    private static async Task ReadDiagnosticsAsync(StreamReader reader, BoundedLineTail tail)
    {
        while (await reader.ReadLineAsync() is { } line) tail.Add(line);
    }
    private static void EnsurePartialOutput(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetFileName(path).Contains(".partial.", StringComparison.Ordinal))
            throw new ArgumentException("FFmpeg output must be an absolute server-generated partial path.", nameof(path));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }
    private static void DeletePartial(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
