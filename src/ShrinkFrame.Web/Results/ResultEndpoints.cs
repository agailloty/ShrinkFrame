using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Web.ResultDelivery;

public static class ResultEndpoints
{
    public static IEndpointRouteBuilder MapResultDownloads(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/results/{jobId:guid}/download", async (Guid jobId, IResultDelivery results,
            IArtifactPathResolver paths, CancellationToken token) =>
        {
            var result = await results.GetDownloadAsync(JobId.From(jobId), token);
            if (result is null) return Results.NotFound();
            return Results.File(paths.ResolveExisting(result.Artifact), result.ContentType,
                result.FileName, enableRangeProcessing: true);
        }).WithName("DownloadResult");
        return endpoints;
    }
}
