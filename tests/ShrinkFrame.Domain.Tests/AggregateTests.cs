namespace ShrinkFrame.Domain.Tests;

[TestClass]
public sealed class AggregateTests
{
    [TestMethod]
    public void Batch_persists_capacity_override_as_aggregate_state()
    {
        var batch = new CompressionBatch(BatchId.New(), "batch", SourceKind.BrowserUpload, null,
            BuiltInPresets.Get(new PresetId("balanced")).Options, Now);
        batch.AuthorizeCapacityAdmissionOverride(Now.AddMinutes(1));
        Assert.IsTrue(batch.CapacityAdmissionOverride);
        Assert.AreEqual(Now.AddMinutes(1), batch.UpdatedAt);
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Batch_enforces_source_invariants()
    {
        var connection = ConnectionId.New();
        _ = new CompressionBatch(BatchId.New(), "Upload", SourceKind.BrowserUpload, null, Options(), Now);
        _ = new CompressionBatch(BatchId.New(), "Immich", SourceKind.Immich, connection, Options(), Now);
        OptionsAndPolicyTests.AssertCode(DomainErrors.InvalidBatchSource, () => new CompressionBatch(BatchId.New(), "Bad", SourceKind.Immich, null, Options(), Now));
        OptionsAndPolicyTests.AssertCode(DomainErrors.InvalidBatchSource, () => new CompressionBatch(BatchId.New(), "Bad", SourceKind.BrowserUpload, connection, Options(), Now));
    }

    [TestMethod]
    public void Batch_rejects_jobs_from_another_source_or_connection()
    {
        var connection = ConnectionId.New();
        var batch = new CompressionBatch(BatchId.New(), "Immich", SourceKind.Immich, connection, Options(), Now);
        batch.AddJob(JobId.New(), VideoSourceRef.Immich("a", connection), Now);
        OptionsAndPolicyTests.AssertCode(DomainErrors.InvalidBatchSource, () => batch.AddJob(JobId.New(), VideoSourceRef.Browser("u"), Now));
        OptionsAndPolicyTests.AssertCode(DomainErrors.InvalidBatchSource, () => batch.AddJob(JobId.New(), VideoSourceRef.Immich("a", ConnectionId.New()), Now));
    }

    [TestMethod]
    public void Job_effective_options_are_a_snapshot()
    {
        var selected = BuiltInPresets.Snapshot(new("balanced"));
        var job = NewJob(selected);
        Assert.AreNotSame(selected, job.EffectiveOptions);
        Assert.AreEqual(selected, job.EffectiveOptions);
    }

    [TestMethod]
    [DynamicData(nameof(AllowedTransitions))]
    public void Every_documented_job_transition_is_allowed(JobState from, JobState to)
    {
        var job = JobAt(from);
        if (from == JobState.Validating && to is JobState.Ready or JobState.NotBeneficial)
            job.CompleteValidation(to == JobState.Ready ? 99 : 100, new("output/a"), [], Now.AddMinutes(1));
        else
        {
            if (to == JobState.Queued && job.OriginalMetadata is null) job.RecordProbe(Metadata(), new("source/retry"));
            job.TransitionTo(to, Now.AddMinutes(1));
        }
        Assert.AreEqual(to, job.State);
    }

    [TestMethod]
    [DynamicData(nameof(RejectedTransitions))]
    public void Every_undocumented_job_transition_is_rejected(JobState from, JobState to)
        => OptionsAndPolicyTests.AssertCode(DomainErrors.InvalidJobTransition, () => JobAt(from).TransitionTo(to, Now));

    [TestMethod]
    public void Queue_requires_probe_and_success_requires_validation_operation()
    {
        var job = NewJob(); job.TransitionTo(JobState.Acquiring, Now); job.TransitionTo(JobState.Probing, Now);
        OptionsAndPolicyTests.AssertCode(DomainErrors.JobNotValidated, () => job.TransitionTo(JobState.Queued, Now));
        job.RecordProbe(Metadata(), new("source/a")); job.TransitionTo(JobState.Queued, Now);
        job.TransitionTo(JobState.Compressing, Now); job.TransitionTo(JobState.Validating, Now);
        OptionsAndPolicyTests.AssertCode(DomainErrors.JobNotValidated, () => job.TransitionTo(JobState.Ready, Now));
        job.CompleteValidation(99, new("output/a"), [], Now);
        Assert.AreEqual(JobState.Ready, job.State);
    }

    [TestMethod]
    public void Warnings_and_blocking_findings_are_separate()
    {
        var job = ValidatingJob();
        var warning = new ValidationFinding("metadata.description.lost", FindingSeverity.Warning, "Description missing.");
        job.CompleteValidation(50, new("output/a"), [warning], Now);
        Assert.AreEqual(1, job.Warnings.Count()); Assert.AreEqual(0, job.BlockingFindings.Count());
    }

    [TestMethod]
    public void Not_beneficial_publication_needs_explicit_override()
    {
        var job = ValidatingJob(); job.CompleteValidation(100, new("output/a"), [], Now);
        OptionsAndPolicyTests.AssertCode(DomainErrors.PublicationOverrideRequired, () => job.BeginPublication(Now));
        job.AuthorizeNotBeneficialPublication(); job.BeginPublication(Now);
        Assert.AreEqual(PublicationState.Publishing, job.PublicationState);
    }

    [TestMethod]
    public void Ready_publication_records_asset_before_completion()
    {
        var job = ReadyJob(); job.BeginPublication(Now);
        OptionsAndPolicyTests.AssertCode(DomainErrors.InvalidPublicationTransition, () => job.CompletePublication(true, Now));
        job.RecordPublishedAsset("new-asset"); job.CompletePublication(true, Now);
        Assert.AreEqual(PublicationState.Published, job.PublicationState);
    }

    [TestMethod]
    public void Partial_and_failed_publications_are_retryable()
    {
        var partial = ReadyJob(); partial.BeginPublication(Now); partial.RecordPublishedAsset("asset"); partial.CompletePublication(false, Now);
        Assert.AreEqual(PublicationState.PartiallyPublished, partial.PublicationState); partial.BeginPublication(Now);
        var failed = ReadyJob(); failed.BeginPublication(Now); failed.FailPublication(Now);
        Assert.AreEqual(PublicationState.Failed, failed.PublicationState); failed.BeginPublication(Now);
    }

    [TestMethod]
    public void Published_and_unvalidated_jobs_cannot_start_publication()
    {
        var published = ReadyJob(); published.BeginPublication(Now); published.RecordPublishedAsset("asset"); published.CompletePublication(true, Now);
        OptionsAndPolicyTests.AssertCode(DomainErrors.InvalidPublicationTransition, () => published.BeginPublication(Now));
        OptionsAndPolicyTests.AssertCode(DomainErrors.InvalidPublicationTransition, () => NewJob().BeginPublication(Now));
    }

    public static IEnumerable<object[]> AllowedTransitions()
    {
        foreach (var pair in TransitionMap()) foreach (var target in pair.Value) yield return [pair.Key, target];
    }

    public static IEnumerable<object[]> RejectedTransitions()
    {
        var map = TransitionMap();
        foreach (var from in Enum.GetValues<JobState>())
        foreach (var to in Enum.GetValues<JobState>())
            if (!map.TryGetValue(from, out var targets) || !targets.Contains(to)) yield return [from, to];
    }

    private static Dictionary<JobState, JobState[]> TransitionMap() => new()
    {
        [JobState.Draft] = [JobState.Acquiring, JobState.Cancelled],
        [JobState.Acquiring] = [JobState.Probing, JobState.Failed, JobState.Cancelled, JobState.Interrupted],
        [JobState.Probing] = [JobState.Queued, JobState.Failed, JobState.Cancelled, JobState.Interrupted],
        [JobState.Queued] = [JobState.Compressing, JobState.Cancelled],
        [JobState.Compressing] = [JobState.Validating, JobState.Failed, JobState.Cancelled, JobState.Interrupted],
        [JobState.Validating] = [JobState.Ready, JobState.NotBeneficial, JobState.Failed, JobState.Interrupted],
        [JobState.Failed] = [JobState.Acquiring, JobState.Queued],
        [JobState.Cancelled] = [JobState.Acquiring, JobState.Queued],
        [JobState.Interrupted] = [JobState.Acquiring, JobState.Queued],
    };

    private static CompressionJob JobAt(JobState state)
    {
        if (state == JobState.Draft) return NewJob();
        if (state is JobState.Ready or JobState.NotBeneficial)
        {
            var validated = ValidatingJob(); validated.CompleteValidation(state == JobState.Ready ? 99 : 100, new("output/a"), [], Now); return validated;
        }
        var job = NewJob();
        job.TransitionTo(JobState.Acquiring, Now);
        if (state == JobState.Acquiring) return job;
        if (state is JobState.Failed or JobState.Cancelled or JobState.Interrupted) { job.TransitionTo(state, Now); return job; }
        job.TransitionTo(JobState.Probing, Now); if (state == JobState.Probing) return job;
        job.RecordProbe(Metadata(), new("source/a")); job.TransitionTo(JobState.Queued, Now); if (state == JobState.Queued) return job;
        job.TransitionTo(JobState.Compressing, Now); if (state == JobState.Compressing) return job;
        job.TransitionTo(JobState.Validating, Now); return job;
    }

    private static CompressionJob ValidatingJob() => JobAt(JobState.Validating);
    private static CompressionJob ReadyJob() { var job = ValidatingJob(); job.CompleteValidation(99, new("output/a"), [], Now); return job; }
    private static CompressionJob NewJob(CompressionOptions? options = null) => new(JobId.New(), BatchId.New(), VideoSourceRef.Browser("upload"), new("balanced"), options ?? Options(), Now);
    private static CompressionOptions Options() => new(24, EncoderPreset.Medium, MaximumResolution.Keep, AudioMode.Auto, "_V");
    private static VideoMetadata Metadata() => new("video.mov", "video/quicktime", 100, TimeSpan.FromMinutes(1), 1920, 1080, "h264", ["aac"], Now, 0);
}
