using ShrinkFrame.Application;
using ShrinkFrame.Domain;
using ShrinkFrame.Infrastructure.Immich;

namespace ShrinkFrame.Web.Immich;

public static class ImmichBrowserEndpoints
{
    public static IEndpointRouteBuilder MapImmichBrowser(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/immich/{connectionId:guid}/assets/{assetId:guid}/thumbnail", GetThumbnailAsync);
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
}
