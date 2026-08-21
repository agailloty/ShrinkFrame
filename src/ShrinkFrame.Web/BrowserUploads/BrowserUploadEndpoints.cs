using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Web.BrowserUploads;

public static class BrowserUploadEndpoints
{
    public static IEndpointRouteBuilder MapBrowserUploads(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/browser-uploads/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { requestToken = tokens.RequestToken });
        });

        var group = endpoints.MapGroup("/api/browser-batches")
            .AddEndpointFilter<SameOriginFilter>();

        group.MapPost("/", async (CreateBrowserBatchRequest request, IBatchRepository batches, TimeProvider time,
            CancellationToken token) =>
        {
            var id = BatchId.New();
            var preset = BuiltInPresets.Get(new PresetId("compact"));
            var name = string.IsNullOrWhiteSpace(request.Name)
                ? $"Browser upload {time.GetLocalNow():yyyy-MM-dd HH:mm}"
                : request.Name.Trim();
            if (name.Length > 300)
                return ApiError(400, "upload.batch_name.invalid", "Batch name must be 300 characters or fewer.");
            await batches.AddAsync(new CompressionBatch(id, name, SourceKind.BrowserUpload, null, preset.Options,
                time.GetUtcNow()), token);
            return Results.Created($"/api/browser-batches/{id.Value}", new { batchId = id.Value, name });
        }).WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        group.MapGet("/{batchId:guid}", async (Guid batchId, IBatchRepository batches,
            ICompressionJobRepository jobs, CancellationToken token) =>
        {
            var id = BatchId.From(batchId);
            var batch = await batches.GetAsync(id, token);
            if (batch is null || batch.SourceKind != SourceKind.BrowserUpload)
                return ApiError(404, "upload.batch.not_found", "The browser upload batch was not found.");
            var items = await jobs.ListByBatchAsync(id, token);
            return Results.Ok(new
            {
                batchId,
                batch.Name,
                files = items.Select(x => new
                {
                    jobId = x.Value.Id.Value,
                    fileName = x.Value.OriginalMetadata?.FileName ?? x.Value.Source.SourceId,
                    state = x.Value.State.ToString(),
                    bytesReceived = x.Value.OriginalMetadata?.SizeBytes,
                    errorCode = x.Value.BlockingFindings.FirstOrDefault()?.Code,
                    errorMessage = x.Value.BlockingFindings.FirstOrDefault()?.Message,
                })
            });
        }).DisableAntiforgery();

        group.MapPost("/{batchId:guid}/files", async (Guid batchId, HttpContext context,
            BrowserUploadService uploads, IOptions<BrowserUploadOptions> configured) =>
        {
            var limits = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limits is { IsReadOnly: false }) limits.MaxRequestBodySize = configured.Value.MaximumFileSizeBytes + 1;
            var fileName = Uri.UnescapeDataString(context.Request.Headers["X-ShrinkFrame-File-Name"].ToString());
            if (string.IsNullOrWhiteSpace(fileName))
                return ApiError(400, "upload.filename.required", "A display filename is required.");
            var result = await uploads.UploadAsync(BatchId.From(batchId), fileName,
                context.Request.ContentType ?? "application/octet-stream", context.Request.ContentLength,
                context.Request.Body, context.RequestAborted);
            return result.ErrorCode switch
            {
                "upload.file_too_large" => Results.Json(result, statusCode: StatusCodes.Status413PayloadTooLarge),
                "upload.batch.not_found" => Results.Json(result, statusCode: StatusCodes.Status404NotFound),
                null => Results.Ok(result),
                _ => Results.Json(result, statusCode: StatusCodes.Status422UnprocessableEntity),
            };
        }).WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        return endpoints;
    }

    private static IResult ApiError(int status, string code, string message)
        => Results.Json(new { errorCode = code, errorMessage = message }, statusCode: status);
}

public sealed record CreateBrowserBatchRequest(string? Name);

public sealed class SameOriginFilter(IOptions<BrowserUploadOptions> configured) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            var antiforgery = context.HttpContext.Features.Get<IAntiforgeryValidationFeature>();
            if (antiforgery is { IsValid: false })
                return ApiError(StatusCodes.Status400BadRequest, "request.antiforgery.invalid",
                    "The request security token is missing or expired. Reload the page and try again.");
            var origin = request.Headers.Origin.ToString().TrimEnd('/');
            var allowed = configured.Value.AllowedOrigins.Any(value =>
                string.Equals(value.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase));
            if (!allowed || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase))
                return ApiError(StatusCodes.Status403Forbidden, "request.origin.rejected",
                    "The request Origin and Host are not allowed.");
        }
        return await next(context);
    }

    private static IResult ApiError(int status, string code, string message)
        => Results.Json(new { errorCode = code, errorMessage = message }, statusCode: status);
}
