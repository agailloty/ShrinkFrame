using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ShrinkFrame.Infrastructure.Persistence;

internal static class ConfigurationExtensions
{
    internal static PropertyBuilder<DateTimeOffset> UtcTicks(this PropertyBuilder<DateTimeOffset> property)
        => property.HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));

    internal static PropertyBuilder<DateTimeOffset?> NullableUtcTicks(this PropertyBuilder<DateTimeOffset?> property)
        => property.HasConversion(new ValueConverter<DateTimeOffset?, long?>(
            value => value.HasValue ? value.Value.UtcTicks : null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null));
}

internal sealed class ImmichConnectionConfiguration : IEntityTypeConfiguration<ImmichConnectionEntity>
{
    public void Configure(EntityTypeBuilder<ImmichConnectionEntity> builder)
    {
        builder.ToTable("ImmichConnections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(200);
        builder.Property(x => x.BaseUrl).HasMaxLength(2048);
        builder.Property(x => x.Compatibility).HasMaxLength(32);
        builder.Property(x => x.DetectedVersion).HasMaxLength(100);
        builder.Property(x => x.LastTestError).HasMaxLength(2000);
        builder.Property(x => x.LastTestKeyId).HasMaxLength(200);
        builder.Property(x => x.LastTestKeyName).HasMaxLength(500);
        builder.Property(x => x.LastTestPermissions).HasMaxLength(4000);
        builder.Property(x => x.LastTestedAt).NullableUtcTicks();
        builder.HasIndex(x => x.IsDefault).HasFilter("IsDefault = 1").IsUnique();
    }
}

internal sealed class BatchConfiguration : IEntityTypeConfiguration<BatchEntity>
{
    public void Configure(EntityTypeBuilder<BatchEntity> builder)
    {
        builder.ToTable("Batches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(300);
        builder.Property(x => x.SourceKind).HasMaxLength(32);
        builder.Property(x => x.Status).HasMaxLength(32);
        builder.Property(x => x.DefaultEncoderPreset).HasMaxLength(32);
        builder.Property(x => x.DefaultVideoCodec).HasMaxLength(16);
        builder.Property(x => x.DefaultMaximumResolution).HasMaxLength(32);
        builder.Property(x => x.DefaultAudioMode).HasMaxLength(32);
        builder.Property(x => x.DefaultSuffix).HasMaxLength(33);
        builder.Property(x => x.CreatedAt).UtcTicks();
        builder.Property(x => x.UpdatedAt).UtcTicks();
        builder.HasIndex(x => new { x.UpdatedAt, x.Id }).HasDatabaseName("IX_Batches_History");
        builder.HasMany(x => x.Jobs).WithOne(x => x.Batch).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JobConfiguration : IEntityTypeConfiguration<JobEntity>
{
    public void Configure(EntityTypeBuilder<JobEntity> builder)
    {
        builder.ToTable("Jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceKind).HasMaxLength(32);
        builder.Property(x => x.SourceId).HasMaxLength(500);
        builder.Property(x => x.PresetId).HasMaxLength(100);
        builder.Property(x => x.EncoderPreset).HasMaxLength(32);
        builder.Property(x => x.VideoCodec).HasMaxLength(16);
        builder.Property(x => x.MaximumResolution).HasMaxLength(32);
        builder.Property(x => x.AudioMode).HasMaxLength(32);
        builder.Property(x => x.Suffix).HasMaxLength(33);
        builder.Property(x => x.State).HasMaxLength(32);
        builder.Property(x => x.PublicationState).HasMaxLength(32);
        builder.Property(x => x.SourceArtifactKey).HasMaxLength(1000);
        builder.Property(x => x.OutputArtifactKey).HasMaxLength(1000);
        builder.Property(x => x.MetadataFileName).HasMaxLength(500);
        builder.Property(x => x.MetadataMimeType).HasMaxLength(200);
        builder.Property(x => x.MetadataVideoCodec).HasMaxLength(100);
        builder.Property(x => x.CreatedAt).UtcTicks();
        builder.Property(x => x.UpdatedAt).UtcTicks();
        builder.Property(x => x.MetadataCaptureTime).NullableUtcTicks();
        builder.Property(x => x.MetadataFileModifiedTime).NullableUtcTicks();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.State, x.UpdatedAt, x.Id }).HasDatabaseName("IX_Jobs_Queue");
        builder.HasIndex(x => new { x.BatchId, x.CreatedAt, x.Id }).HasDatabaseName("IX_Jobs_BatchHistory");
        builder.HasIndex(x => new { x.SourceKind, x.SourceConnectionId, x.SourceId }).HasDatabaseName("IX_Jobs_SourceDuplicate");
    }
}

internal sealed class JobLogConfiguration : IEntityTypeConfiguration<JobLogEntity>
{
    public void Configure(EntityTypeBuilder<JobLogEntity> builder)
    {
        builder.ToTable("JobLogs"); builder.HasKey(x => x.Id);
        builder.Property(x => x.At).UtcTicks(); builder.Property(x => x.Level).HasMaxLength(16);
        builder.Property(x => x.Code).HasMaxLength(100); builder.Property(x => x.Message).HasMaxLength(1000);
        builder.HasIndex(x => new { x.JobId, x.At });
        builder.HasOne(x => x.Job).WithMany(x => x.Logs).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JobAudioCodecConfiguration : IEntityTypeConfiguration<JobAudioCodecEntity>
{
    public void Configure(EntityTypeBuilder<JobAudioCodecEntity> builder)
    {
        builder.ToTable("JobAudioCodecs"); builder.HasKey(x => new { x.JobId, x.Position });
        builder.Property(x => x.Codec).HasMaxLength(100);
        builder.HasOne(x => x.Job).WithMany(x => x.AudioCodecs).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JobAlbumConfiguration : IEntityTypeConfiguration<JobAlbumEntity>
{
    public void Configure(EntityTypeBuilder<JobAlbumEntity> builder)
    {
        builder.ToTable("JobAlbums"); builder.HasKey(x => new { x.JobId, x.Position });
        builder.Property(x => x.AlbumId).HasMaxLength(500);
        builder.HasOne(x => x.Job).WithMany(x => x.Albums).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ValidationFindingConfiguration : IEntityTypeConfiguration<ValidationFindingEntity>
{
    public void Configure(EntityTypeBuilder<ValidationFindingEntity> builder)
    {
        builder.ToTable("ValidationFindings"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(200); builder.Property(x => x.Severity).HasMaxLength(32); builder.Property(x => x.Message).HasMaxLength(2000);
        builder.HasOne(x => x.Job).WithMany(x => x.Findings).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JobProgressConfiguration : IEntityTypeConfiguration<JobProgressEntity>
{
    public void Configure(EntityTypeBuilder<JobProgressEntity> builder)
    {
        builder.ToTable("JobProgress"); builder.HasKey(x => x.JobId); builder.Property(x => x.UpdatedAt).UtcTicks();
        builder.HasOne(x => x.Job).WithOne(x => x.Progress).HasForeignKey<JobProgressEntity>(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PublicationAttemptConfiguration : IEntityTypeConfiguration<PublicationAttemptEntity>
{
    public void Configure(EntityTypeBuilder<PublicationAttemptEntity> builder)
    {
        builder.ToTable("PublicationAttempts"); builder.HasKey(x => x.Id); builder.Property(x => x.Result).HasMaxLength(32); builder.Property(x => x.ErrorSummary).HasMaxLength(2000);
        builder.Property(x => x.StartedAt).UtcTicks(); builder.Property(x => x.CompletedAt).NullableUtcTicks();
        builder.HasIndex(x => new { x.JobId, x.StartedAt });
        builder.HasOne(x => x.Job).WithMany(x => x.PublicationAttempts).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PublicationCheckpointConfiguration : IEntityTypeConfiguration<PublicationCheckpointEntity>
{
    public void Configure(EntityTypeBuilder<PublicationCheckpointEntity> builder)
    {
        builder.ToTable("PublicationCheckpoints"); builder.HasKey(x => x.JobId);
        builder.Property(x => x.ClientAttemptId).HasMaxLength(100);
        builder.Property(x => x.Sha1Checksum).HasMaxLength(100);
        builder.Property(x => x.PendingAlbumIdsJson).HasMaxLength(8000);
        builder.Property(x => x.WarningsJson).HasMaxLength(4000);
        builder.HasOne(x => x.Job).WithOne(x => x.PublicationCheckpoint)
            .HasForeignKey<PublicationCheckpointEntity>(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ImmichConnectionEntity>().WithMany().HasForeignKey(x => x.DestinationConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ImmichBrowserSelectionConfiguration : IEntityTypeConfiguration<ImmichBrowserSelectionEntity>
{
    public void Configure(EntityTypeBuilder<ImmichBrowserSelectionEntity> builder)
    {
        builder.ToTable("ImmichBrowserSelections");
        builder.HasKey(x => new { x.ConnectionId, x.AssetId });
        builder.Property(x => x.AssetId).HasMaxLength(100);
        builder.Property(x => x.SelectedAt).UtcTicks();
        builder.HasOne<ImmichConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}
