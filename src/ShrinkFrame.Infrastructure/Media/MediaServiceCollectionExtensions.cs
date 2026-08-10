using Microsoft.Extensions.DependencyInjection;
using ShrinkFrame.Application;

namespace ShrinkFrame.Infrastructure.Media;

public static class MediaServiceCollectionExtensions
{
    public static IServiceCollection AddMediaTools(this IServiceCollection services, MediaToolOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<FfmpegArgumentBuilder>();
        services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();
        services.AddSingleton<IMediaCompressor, FfmpegMediaCompressor>();
        services.AddSingleton<MediaToolStatusProvider>();
        services.AddSingleton<IMediaToolStatus>(provider => provider.GetRequiredService<MediaToolStatusProvider>());
        services.AddHostedService<MediaToolStartupService>();
        return services;
    }
}
