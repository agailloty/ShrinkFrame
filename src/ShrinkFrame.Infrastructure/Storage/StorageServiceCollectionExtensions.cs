using Microsoft.Extensions.DependencyInjection;
using ShrinkFrame.Application;

namespace ShrinkFrame.Infrastructure.Storage;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddLocalWorkStorage(this IServiceCollection services, WorkStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton<LocalWorkStorage>();
        services.AddSingleton<IWorkStorage>(provider => provider.GetRequiredService<LocalWorkStorage>());
        services.AddSingleton<IWorkStorageStartupValidator>(provider => provider.GetRequiredService<LocalWorkStorage>());
        services.AddSingleton<IArtifactPathResolver>(provider => provider.GetRequiredService<LocalWorkStorage>());
        services.AddSingleton<IStorageCapacityReporter, LocalStorageCapacityReporter>();
        services.AddSingleton<IDiskCapacityService, DiskCapacityService>();
        services.AddHostedService<WorkStorageStartupService>();
        return services;
    }
}
