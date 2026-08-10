using ShrinkFrame.Domain;

namespace ShrinkFrame.Application;

public sealed record BatchSelection(string SourceId, string FileName, long? SizeBytes);
public sealed record BatchJobView(JobId Id, string SourceId, string FileName, long? SizeBytes,
    PresetId PresetId, CompressionOptions EffectiveOptions, JobState State);
public sealed record BatchWizardView(BatchId Id, string Name, SourceKind SourceKind, ConnectionId? ConnectionId,
    BatchStatus Status, CompressionOptions DefaultOptions, bool CapacityAdmissionOverride,
    IReadOnlyList<BatchJobView> Jobs, CapacityAdmission Capacity);
public sealed record BatchSettings(string Name, PresetId GlobalPresetId, CompressionOptions BatchOptions,
    IReadOnlyDictionary<JobId, PresetId> PerVideoPresets);

public interface IBatchWizard
{
    IReadOnlyList<BuiltInPreset> Presets { get; }
    Task<BatchWizardView> CreateAsync(SourceKind sourceKind, ConnectionId? connectionId, string? name = null, CancellationToken token = default);
    Task<BatchWizardView?> GetAsync(BatchId id, CancellationToken token = default);
    Task<BatchWizardView> AddImmichSelectionAsync(BatchId id, IReadOnlyCollection<BatchSelection> selection, CancellationToken token = default);
    Task<BatchWizardView> SaveSettingsAsync(BatchId id, BatchSettings settings, CancellationToken token = default);
    Task<BatchWizardView> ConfirmAsync(BatchId id, bool forceLowCapacity, CancellationToken token = default);
}

public sealed class BatchWizard(IBatchRepository batches, ICompressionJobRepository jobs,
    IDiskCapacityService capacity, TimeProvider time) : IBatchWizard
{
    private static readonly PresetId Balanced = new("balanced");
    public IReadOnlyList<BuiltInPreset> Presets => BuiltInPresets.All;

    public async Task<BatchWizardView> CreateAsync(SourceKind sourceKind, ConnectionId? connectionId, string? name = null, CancellationToken token = default)
    {
        var now = time.GetUtcNow();
        var generated = $"{(sourceKind == SourceKind.Immich ? "Immich" : "Browser upload")} {time.GetLocalNow():yyyy-MM-dd HH:mm}";
        var batch = new CompressionBatch(BatchId.New(), string.IsNullOrWhiteSpace(name) ? generated : name,
            sourceKind, connectionId, BuiltInPresets.Snapshot(Balanced), now);
        await batches.AddAsync(batch, token);
        return await ViewAsync(batch, token);
    }

    public async Task<BatchWizardView?> GetAsync(BatchId id, CancellationToken token = default)
    {
        var batch = await batches.GetAsync(id, token);
        return batch is null ? null : await ViewAsync(batch, token);
    }

    public async Task<BatchWizardView> AddImmichSelectionAsync(BatchId id, IReadOnlyCollection<BatchSelection> selection, CancellationToken token = default)
    {
        var batch = await RequiredDraftAsync(id, token);
        if (batch.SourceKind != SourceKind.Immich || batch.ConnectionId is null)
            throw new InvalidOperationException("The batch is not an Immich draft.");
        var existing = (await jobs.ListByBatchAsync(id, token)).Select(x => x.Value.Source.SourceId).ToHashSet(StringComparer.Ordinal);
        foreach (var item in selection.Where(x => !existing.Contains(x.SourceId)))
        {
            var source = VideoSourceRef.Immich(item.SourceId, batch.ConnectionId.Value);
            var job = new CompressionJob(JobId.New(), id, source, Balanced, batch.DefaultOptions, time.GetUtcNow());
            await jobs.AddAsync(job, token); batch.AddJob(job.Id, source, time.GetUtcNow());
        }
        await batches.UpdateAsync(batch, token);
        return await ViewAsync(batch, token);
    }

    public async Task<BatchWizardView> SaveSettingsAsync(BatchId id, BatchSettings settings, CancellationToken token = default)
    {
        var batch = await RequiredDraftAsync(id, token);
        _ = BuiltInPresets.Get(settings.GlobalPresetId);
        batch.Rename(settings.Name, time.GetUtcNow());
        batch.Configure(settings.BatchOptions, time.GetUtcNow());
        foreach (var stored in await jobs.ListByBatchAsync(id, token))
        {
            var presetId = settings.PerVideoPresets.GetValueOrDefault(stored.Value.Id, settings.GlobalPresetId);
            var selected = BuiltInPresets.Get(presetId);
            var effective = presetId == settings.GlobalPresetId ? settings.BatchOptions : BuiltInPresets.Snapshot(selected.Id);
            stored.Value.SelectOptions(selected.Id, effective, time.GetUtcNow());
            await jobs.UpdateAsync(stored.Value, stored.Version, token);
        }
        await batches.UpdateAsync(batch, token);
        return await ViewAsync(batch, token);
    }

    public async Task<BatchWizardView> ConfirmAsync(BatchId id, bool forceLowCapacity, CancellationToken token = default)
    {
        var batch = await RequiredDraftAsync(id, token);
        var storedJobs = await jobs.ListByBatchAsync(id, token);
        var admission = capacity.Evaluate(SourceBytes(storedJobs), forceLowCapacity);
        if (!admission.IsAdmitted) throw new InvalidOperationException("Insufficient capacity. Explicitly authorize the low-capacity override to continue.");
        if (forceLowCapacity && admission.HasWarning) batch.AuthorizeCapacityAdmissionOverride(time.GetUtcNow());
        foreach (var stored in storedJobs)
        {
            if (batch.SourceKind == SourceKind.BrowserUpload)
            {
                if (stored.Value.State != JobState.Probing || stored.Value.OriginalMetadata is null)
                    throw new InvalidOperationException("All browser videos must finish probing before confirmation.");
                stored.Value.TransitionTo(JobState.Queued, time.GetUtcNow());
            }
            else stored.Value.TransitionTo(JobState.Acquiring, time.GetUtcNow());
            await jobs.UpdateAsync(stored.Value, stored.Version, token);
        }
        batch.Confirm(time.GetUtcNow()); await batches.UpdateAsync(batch, token);
        return await ViewAsync(batch, token);
    }

    private async Task<CompressionBatch> RequiredDraftAsync(BatchId id, CancellationToken token)
    {
        var batch = await batches.GetAsync(id, token) ?? throw new KeyNotFoundException("Batch was not found.");
        if (batch.Status != BatchStatus.Draft) throw new InvalidOperationException("A confirmed batch cannot be edited.");
        return batch;
    }
    private async Task<BatchWizardView> ViewAsync(CompressionBatch batch, CancellationToken token)
    {
        var stored = await jobs.ListByBatchAsync(batch.Id, token);
        var views = stored.Select(x => new BatchJobView(x.Value.Id, x.Value.Source.SourceId,
            x.Value.OriginalMetadata?.FileName ?? x.Value.Source.SourceId, x.Value.OriginalMetadata?.SizeBytes,
            x.Value.PresetId, x.Value.EffectiveOptions, x.Value.State)).ToArray();
        return new(batch.Id, batch.Name, batch.SourceKind, batch.ConnectionId, batch.Status,
            batch.DefaultOptions, batch.CapacityAdmissionOverride, views, capacity.Evaluate(SourceBytes(stored), batch.CapacityAdmissionOverride));
    }
    private static long SourceBytes(IReadOnlyList<Versioned<CompressionJob>> values)
    {
        try { return values.Sum(x => x.Value.OriginalMetadata?.SizeBytes ?? 0L); }
        catch (OverflowException) { return long.MaxValue; }
    }
}
