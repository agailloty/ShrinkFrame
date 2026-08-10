using ShrinkFrame.Application;
using ShrinkFrame.Domain;
using ShrinkFrame.Infrastructure.Immich;

namespace ShrinkFrame.Web.Immich;

public static class ImmichBrowserEndpoints
{
    public static IEndpointRouteBuilder MapImmichBrowser(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/immich/{connectionId:guid}/assets/{assetId:guid}/thumbnail", GetThumbnailAsync);
        endpoints.MapGet("/api/immich/{connectionId:guid}/assets/{assetId:guid}/video", GetVideoAsync);
        return endpoints;
    }

    private static async Task<IResult> GetThumbnailAsync(Guid connectionId, Guid assetId,
        IImmichVideoBrowser browser, HttpResponse response, CancellationToken cancellationToken)
    {
        try
        {
            var thumbnail = await browser.OpenThumbnailAsync(new ConnectionId(connectionId), assetId.ToString(), cancellationToken);
            response.Headers.CacheControl = "private, max-age=300";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.Stream(thumbnail.Content, thumbnail.ContentType, lastModified: null,
                entityTag: null, enableRangeProcessing: false);
        }
        catch (ImmichConnectionException exception)
        {
            var status = exception.Code switch
            {
                "immich.asset.unavailable" => StatusCodes.Status404NotFound,
                "connection.deleted" or "connection.disabled" => StatusCodes.Status410Gone,
                "immich.thumbnail.too_large" => StatusCodes.Status413PayloadTooLarge,
                _ => StatusCodes.Status502BadGateway,
            };
            return Results.Json(new { code = exception.Code, message = exception.Message }, statusCode: status);
        }
    }

    private static async Task<IResult> GetVideoAsync(Guid connectionId, Guid assetId,
        IImmichVideoBrowser browser, HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var video = await browser.OpenVideoAsync(new ConnectionId(connectionId), assetId.ToString(),
                request.Headers.Range.ToString(), cancellationToken);
            return new ImmichVideoProxyResult(video);
        }
        catch (ImmichConnectionException exception)
        {
            var status = exception.Code switch
            {
                "immich.asset.unavailable" => StatusCodes.Status404NotFound,
                "connection.deleted" or "connection.disabled" => StatusCodes.Status410Gone,
                "immich.video.range_invalid" => StatusCodes.Status400BadRequest,
                "immich.video.range_unsatisfiable" => StatusCodes.Status416RangeNotSatisfiable,
                _ => StatusCodes.Status502BadGateway,
            };
            return Results.Json(new { code = exception.Code, message = exception.Message }, statusCode: status);
        }
    }
}

internal sealed class ImmichVideoProxyResult(ImmichVideoContent video) : IResult
{
    public async Task ExecuteAsync(HttpContext context)
    {
        await using (video)
        {
            context.Response.StatusCode = video.IsPartial ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
            context.Response.ContentType = video.ContentType;
            context.Response.ContentLength = video.ContentLength;
            context.Response.Headers.AcceptRanges = "bytes";
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (video.ContentRange is not null) context.Response.Headers.ContentRange = video.ContentRange;
            await video.Content.CopyToAsync(context.Response.Body, 64 * 1024, context.RequestAborted);
        }
    }
}
