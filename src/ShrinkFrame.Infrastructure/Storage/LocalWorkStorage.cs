using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Storage;

public sealed class LocalWorkStorage : IWorkStorage, IWorkStorageStartupValidator, IArtifactPathResolver
{
    private readonly string root;
    private readonly string rootPrefix;
    private readonly int bufferSize;

    public LocalWorkStorage(WorkStorageOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkRoot);
        if (options.BufferSizeBytes is < 4096 or > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "Storage buffer must be between 4 KiB and 1 MiB.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.WorkRoot));
        rootPrefix = root + Path.DirectorySeparatorChar;
        bufferSize = options.BufferSizeBytes;
    }

    public ArtifactAllocation Allocate(BatchId batchId, JobId jobId, ArtifactKind kind)
    {
        var prefix = $"batches/{batchId.Value:N}/jobs/{jobId.Value:N}";
        var (directory, file) = kind switch
        {
            ArtifactKind.Source => ("source", "input.bin"),
            ArtifactKind.Output => ("output", "result.mp4"),
            ArtifactKind.InputProbe => ("probe", "input.json"),
            ArtifactKind.OutputProbe => ("probe", "output.json"),
            ArtifactKind.FfmpegLog => ("logs", "ffmpeg.log"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var final = new ArtifactRef($"{prefix}/{directory}/{file}");
        var dot = file.LastIndexOf('.');
        var partialFile = dot < 0 ? file + ".partial" : file[..dot] + ".partial" + file[dot..];
        return new(new ArtifactRef($"{prefix}/{directory}/{partialFile}"), final);
    }

    public Task<Stream> OpenCreateNewAsync(ArtifactRef partialArtifact, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePartial(partialArtifact);
        var path = Resolve(partialArtifact, allowMissingLeaf: true);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        EnsureNoLinks(path, allowMissingLeaf: true);
        Stream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task<Stream> OpenReadAsync(ArtifactRef artifact, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(artifact, allowMissingLeaf: false);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public string ResolveExisting(ArtifactRef artifact) => Resolve(artifact, allowMissingLeaf: false);

    public async Task<long> CopyToNewAsync(Stream source, ArtifactRef partialArtifact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var path = Resolve(partialArtifact, allowMissingLeaf: true);
        try
        {
            await using var destination = await OpenCreateNewAsync(partialArtifact, cancellationToken);
            var buffer = new byte[bufferSize];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total = checked(total + read);
            }
            await destination.FlushAsync(cancellationToken);
            return total;
        }
        catch
        {
            TryDeletePartial(path);
            throw;
        }
    }

    public Task<long> FinalizeAsync(ArtifactRef partialArtifact, ArtifactRef finalArtifact, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePartial(partialArtifact);
        if (IsPartial(finalArtifact)) throw new ArgumentException("Final artifact key cannot be partial.", nameof(finalArtifact));
        var partialPath = Resolve(partialArtifact, allowMissingLeaf: false);
        var finalPath = Resolve(finalArtifact, allowMissingLeaf: true);
        if (!string.Equals(Path.GetDirectoryName(partialPath), Path.GetDirectoryName(finalPath), PathComparison))
            throw new ArgumentException("Finalize must stay in the artifact directory.");
        EnsureNoLinks(finalPath, allowMissingLeaf: true);
        File.Move(partialPath, finalPath, overwrite: false);
        return Task.FromResult(new FileInfo(finalPath).Length);
    }

    public Task<StorageDeletionReport> DeleteKnownAsync(IReadOnlyCollection<OwnedArtifact> artifacts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var results = new List<ArtifactDeletionResult>(artifacts.Count);
        foreach (var owned in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                EnsureOwned(owned);
                var path = Resolve(owned.Artifact, allowMissingLeaf: true);
                if (File.Exists(path)) File.Delete(path);
                results.Add(new(owned.Artifact, true, null));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                results.Add(new(owned.Artifact, false, "storage.delete.failed"));
                break;
            }
        }
        return Task.FromResult(new StorageDeletionReport(results));
    }

    public Task<StorageInventory> InventoryAsync(IReadOnlyCollection<OwnedArtifact> artifacts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var items = new List<ArtifactInventoryItem>();
        long total = 0;
        foreach (var owned in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureOwned(owned);
            var path = Resolve(owned.Artifact, allowMissingLeaf: true);
            if (!File.Exists(path)) continue;
            var bytes = new FileInfo(path).Length;
            total = checked(total + bytes);
            items.Add(new(owned.JobId, owned.Artifact, bytes, IsPartial(owned.Artifact)));
        }
        return Task.FromResult(new StorageInventory(total, items));
    }

    public Task<IReadOnlyList<UnownedArtifactInventoryItem>> InventoryAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<UnownedArtifactInventoryItem>();
        if (!Directory.Exists(root)) return Task.FromResult<IReadOnlyList<UnownedArtifactInventoryItem>>(results);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckLink(directory);
            foreach (var child in Directory.EnumerateDirectories(directory)) { CheckLink(child); pending.Push(child); }
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                CheckLink(file);
                var info = new FileInfo(file);
                var key = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                results.Add(new(new ArtifactRef(key), info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
            }
        }
        return Task.FromResult<IReadOnlyList<UnownedArtifactInventoryItem>>(results.OrderBy(x => x.Artifact.Key).ToArray());
    }

    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);
        EnsureNoLinks(root, allowMissingLeaf: false);
        var probe = Path.Combine(root, $".writable-{Guid.NewGuid():N}.tmp");
        await using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1, FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
    }

    private string Resolve(ArtifactRef artifact, bool allowMissingLeaf)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var key = artifact.Key;
        if (Path.IsPathFullyQualified(key) || key.Contains('\\') || key.Contains(':')) throw InvalidKey();
        var segments = key.Split('/');
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) throw InvalidKey();
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        if (!path.StartsWith(rootPrefix, PathComparison)) throw InvalidKey();
        EnsureNoLinks(path, allowMissingLeaf);
        return path;
    }

    private void EnsureNoLinks(string path, bool allowMissingLeaf)
    {
        var relative = Path.GetRelativePath(root, path);
        var current = root;
        CheckLink(current);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                if (allowMissingLeaf) break;
                throw new FileNotFoundException("Artifact does not exist.");
            }
            CheckLink(current);
        }
    }

    private static void CheckLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Symbolic links and reparse points are not permitted in work storage.");
    }

    private static bool IsPartial(ArtifactRef artifact) => artifact.Key.Split('/').Last().Contains(".partial", StringComparison.Ordinal);
    private static void EnsurePartial(ArtifactRef artifact)
    {
        if (!IsPartial(artifact)) throw new ArgumentException("Create-new writes require a partial artifact key.", nameof(artifact));
    }
    private static void EnsureOwned(OwnedArtifact owned)
    {
        var expected = $"batches/{owned.BatchId.Value:N}/jobs/{owned.JobId.Value:N}/";
        if (!owned.Artifact.Key.StartsWith(expected, StringComparison.Ordinal) ||
            !KnownArtifactTails.Contains(owned.Artifact.Key[expected.Length..]))
            throw new ArgumentException("Artifact is not owned by the specified job.");
    }
    private static readonly HashSet<string> KnownArtifactTails = new(StringComparer.Ordinal)
    {
        "source/input.bin", "source/input.partial.bin",
        "output/result.mp4", "output/result.partial.mp4",
        "probe/input.json", "probe/input.partial.json",
        "probe/output.json", "probe/output.partial.json",
        "logs/ffmpeg.log", "logs/ffmpeg.partial.log",
    };
    private static void TryDeletePartial(string path)
    {
        try
        {
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static ArgumentException InvalidKey() => new("Artifact key is not a safe relative storage key.");
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

public sealed class WorkStorageStartupService(IWorkStorageStartupValidator validator, ILogger<WorkStorageStartupService> logger) : IHostedService
{
    private static readonly Action<ILogger, Exception?> LogValidated = LoggerMessage.Define(
        LogLevel.Information, new EventId(1101, "WorkStorageValidated"),
        "Work storage writable-path validation succeeded.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await validator.ValidateAsync(cancellationToken);
        LogValidated(logger, null);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
