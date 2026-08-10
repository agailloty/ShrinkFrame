using System.Buffers;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;
using ShrinkFrame.Infrastructure.Media;

namespace ShrinkFrame.Web.BrowserUploads;

public sealed record BrowserUploadResult(Guid JobId, string FileName, string State, long BytesReceived,
    string? Sha256, string? ErrorCode, string? ErrorMessage);

public sealed class BrowserUploadService(
    IBatchRepository batches,
    ICompressionJobRepository jobs,
    IWorkStorage storage,
    IArtifactPathResolver paths,
    IMediaProbe probe,
    TimeProvider time,
    IOptions<BrowserUploadOptions> optionsAccessor)
{
    private readonly BrowserUploadOptions options = optionsAccessor.Value;

    public async Task<BrowserUploadResult> UploadAsync(BatchId batchId, string displayFileName, string browserMimeType,
        long? contentLength, Stream requestBody, CancellationToken cancellationToken)
    {
        var batch = await batches.GetAsync(batchId, cancellationToken);
        if (batch is null || batch.SourceKind != SourceKind.BrowserUpload)
            return Error(Guid.Empty, displayFileName, "upload.batch.not_found", "The browser upload batch was not found.");

        displayFileName = DisplayName(displayFileName);
        var jobId = JobId.New();
        var preset = BuiltInPresets.Get(new PresetId("balanced"));
        var job = new CompressionJob(jobId, batchId, VideoSourceRef.Browser(displayFileName),
            preset.Id, preset.Options, time.GetUtcNow());
        var stored = await jobs.AddAsync(job, cancellationToken);
        batch.AddJob(jobId, job.Source, time.GetUtcNow());
        await batches.UpdateAsync(batch, cancellationToken);

        job.TransitionTo(JobState.Acquiring, time.GetUtcNow());
        var version = await jobs.UpdateAsync(job, stored.Version, cancellationToken);
        var allocation = storage.Allocate(batchId, jobId, ArtifactKind.Source);
        long total = 0;
        string? hashText = null;

        try
        {
            if (contentLength is > 0 && contentLength > options.MaximumFileSizeBytes)
                throw new UploadRejectedException("upload.file_too_large", "The file exceeds the configured per-file limit.");

            await using (var destination = await storage.OpenCreateNewAsync(allocation.Partial, cancellationToken))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(options.BufferSizeBytes);
                try
                {
                    while (true)
                    {
                        var read = await requestBody.ReadAsync(buffer.AsMemory(0, options.BufferSizeBytes), cancellationToken);
                        if (read == 0) break;
                        total = checked(total + read);
                        if (total > options.MaximumFileSizeBytes)
                            throw new UploadRejectedException("upload.file_too_large", "The file exceeds the configured per-file limit.");
                        hash.AppendData(buffer, 0, read);
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                    await destination.FlushAsync(cancellationToken);
                    hashText = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            await storage.FinalizeAsync(allocation.Partial, allocation.Final, cancellationToken);
            job.TransitionTo(JobState.Probing, time.GetUtcNow());
            version = await jobs.UpdateAsync(job, version, cancellationToken);

            MediaProbeResult probed;
            try
            {
                probed = await probe.ProbeAsync(paths.ResolveExisting(allocation.Final), cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidDataException or MediaProcessException)
            {
                await DeleteSourceAsync(batchId, jobId, allocation, CancellationToken.None);
                job.Fail("upload.not_video", "The uploaded file does not contain a playable video stream.", time.GetUtcNow());
                await jobs.UpdateAsync(job, version, CancellationToken.None);
                return Error(jobId.Value, displayFileName, "upload.not_video", "The file is not a supported video.", total, hashText);
            }

            var video = probed.PrimaryVideo;
            var metadata = new VideoMetadata(DisplayName(displayFileName), string.IsNullOrWhiteSpace(browserMimeType)
                ? "application/octet-stream" : browserMimeType, total, probed.Duration,
                video.Width ?? throw new InvalidDataException("Video width is unavailable."),
                video.Height ?? throw new InvalidDataException("Video height is unavailable."), video.CodecName,
                probed.Streams.Where(x => x.CodecType == "audio").Select(x => x.CodecName).ToArray(),
                probed.CaptureTime, probed.EffectiveRotation, latitude: probed.Latitude, longitude: probed.Longitude);
            job.RecordProbe(metadata, allocation.Final);
            await jobs.UpdateAsync(job, version, cancellationToken);
            await jobs.SaveProgressAsync(jobId, new(new TransferProgress(total, total), null, time.GetUtcNow()), cancellationToken);
            return new(jobId.Value, metadata.FileName, job.State.ToString(), total, hashText, null, null);
        }
        catch (OperationCanceledException)
        {
            await DeleteSourceAsync(batchId, jobId, allocation, CancellationToken.None);
            await MarkFailureAsync(jobId, "upload.aborted", "The upload was interrupted and must restart from zero.");
            throw;
        }
        catch (UploadRejectedException exception)
        {
            await DeleteSourceAsync(batchId, jobId, allocation, CancellationToken.None);
            await MarkFailureAsync(jobId, exception.Code, exception.Message);
            return Error(jobId.Value, displayFileName, exception.Code, exception.Message, total, hashText);
        }
        catch (Exception)
        {
            await DeleteSourceAsync(batchId, jobId, allocation, CancellationToken.None);
            await MarkFailureAsync(jobId, "upload.failed", "The upload could not be completed.");
            throw;
        }
    }

    private async Task MarkFailureAsync(JobId id, string code, string message)
    {
        var current = await jobs.GetAsync(id, CancellationToken.None);
        if (current is null || current.Value.State is not (JobState.Acquiring or JobState.Probing)) return;
        current.Value.Fail(code, message, time.GetUtcNow());
        await jobs.UpdateAsync(current.Value, current.Version, CancellationToken.None);
    }

    private Task<StorageDeletionReport> DeleteSourceAsync(BatchId batchId, JobId jobId, ArtifactAllocation allocation, CancellationToken token)
        => storage.DeleteKnownAsync([
            new(batchId, jobId, allocation.Partial), new(batchId, jobId, allocation.Final)], token);

    private static string DisplayName(string value)
    {
        var name = Path.GetFileName(value.Replace('\u0000', '_')).Trim();
        return string.IsNullOrWhiteSpace(name) ? "unnamed upload" : name[..Math.Min(name.Length, 500)];
    }

    private static BrowserUploadResult Error(Guid id, string name, string code, string message, long bytes = 0, string? hash = null)
        => new(id, DisplayName(name), JobState.Failed.ToString(), bytes, hash, code, message);

    private sealed class UploadRejectedException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
