using Microsoft.EntityFrameworkCore;

namespace ShrinkFrame.Infrastructure.Persistence;

public sealed class ShrinkFrameDbContext(DbContextOptions<ShrinkFrameDbContext> options) : DbContext(options)
{
    internal DbSet<ImmichConnectionEntity> Connections => Set<ImmichConnectionEntity>();
    internal DbSet<BatchEntity> Batches => Set<BatchEntity>();
    internal DbSet<JobEntity> Jobs => Set<JobEntity>();
    internal DbSet<JobAudioCodecEntity> JobAudioCodecs => Set<JobAudioCodecEntity>();
    internal DbSet<JobAlbumEntity> JobAlbums => Set<JobAlbumEntity>();
    internal DbSet<ValidationFindingEntity> ValidationFindings => Set<ValidationFindingEntity>();
    internal DbSet<JobProgressEntity> JobProgress => Set<JobProgressEntity>();
    internal DbSet<PublicationAttemptEntity> PublicationAttempts => Set<PublicationAttemptEntity>();
    internal DbSet<ImmichBrowserSelectionEntity> ImmichBrowserSelections => Set<ImmichBrowserSelectionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShrinkFrameDbContext).Assembly);
    }
}
