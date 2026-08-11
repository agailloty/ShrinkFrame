using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Immich;

public sealed class ImmichVideoBrowser(IImmichConnectionRepository connections,
    IImmichBrowserSelectionRepository selections, IDataProtectionProvider protectionProvider,
    ImmichConnectionOptions options) : IImmichVideoBrowser
{
    private const int PageSize = 50;
    private const int MaximumThumbnailBytes = 5 * 1024 * 1024;
    private readonly IDataProtector protector = protectionProvider.CreateProtector("ShrinkFrame.Immich.ApiKey.v1");

    public async Task<ImmichVideoPage> SearchAsync(ImmichVideoSearch search, CancellationToken cancellationToken = default)
    {
        if (search.Page < 1) throw new ImmichConnectionException("immich.search.page_invalid", "Page must be at least one.");
        if (search.Page > int.MaxValue / PageSize) throw new ImmichConnectionException("immich.search.page_invalid", "Page is too large.");
        if (search.TakenAfter > search.TakenBefore) throw new ImmichConnectionException("immich.search.period_invalid", "Taken after must precede taken before.");
        return await WithClientAsync(search.ConnectionId, async client =>
        {
            if (search.Sort is ImmichVideoSort.SizeLargest or ImmichVideoSort.SizeSmallest)
            {
                var all = new List<ImmichVideoSummary>();
                var immichPage = 1;
                int? nextPage;
                do
                {
                    var pageResult = await FetchPageAsync(client, search, immichPage, cancellationToken);
                    all.AddRange(pageResult.Items);
                    nextPage = pageResult.NextPage;
                    if (nextPage is not null && nextPage <= immichPage)
                        throw new ImmichConnectionException("connection.version_mismatch", "Immich returned an invalid next page value. Retest the connection.");
                    immichPage = nextPage ?? 0;
                } while (nextPage is not null);

                var sorted = SortBySize(all, search.Sort == ImmichVideoSort.SizeLargest);
                var items = sorted.Skip((search.Page - 1) * PageSize).Take(PageSize).ToArray();
                int? localNextPage = search.Page * PageSize < sorted.Count ? search.Page + 1 : null;
                return new ImmichVideoPage(items, search.Page, PageSize, sorted.Count, localNextPage, false);
            }

            var result = await FetchPageAsync(client, search, search.Page, cancellationToken);
            var mapped = result.Items.ToArray();
            var refined = search.PageMinimumBytes is not null || search.PageMaximumBytes is not null;
            if (refined)
                mapped = mapped.Where(x => x.SizeBytes is not null
                    && (search.PageMinimumBytes is null || x.SizeBytes >= search.PageMinimumBytes)
                    && (search.PageMaximumBytes is null || x.SizeBytes <= search.PageMaximumBytes)).ToArray();
            return new ImmichVideoPage(mapped, search.Page, PageSize, result.Total, result.NextPage, refined);
        }, cancellationToken);
    }

    private static async Task<SearchPage> FetchPageAsync(BrowserHttpClient client, ImmichVideoSearch search,
        int page, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = "VIDEO", ["withExif"] = true, ["withDeleted"] = false,
            ["page"] = page, ["size"] = PageSize,
            ["order"] = search.Sort == ImmichVideoSort.TakenOldest ? "asc" : "desc",
        };
        if (search.TakenAfter is not null) body["takenAfter"] = search.TakenAfter.Value;
        if (search.TakenBefore is not null) body["takenBefore"] = search.TakenBefore.Value;
        if (!string.IsNullOrWhiteSpace(search.AlbumId)) body["albumIds"] = new[] { search.AlbumId };
        using var document = await client.JsonAsync(HttpMethod.Post, "api/search/metadata", body, cancellationToken);
        var assets = document.RootElement.GetProperty("assets");
        return new SearchPage(assets.GetProperty("items").EnumerateArray().Where(IsVisibleVideo).Select(MapSummary).ToArray(),
            assets.GetProperty("total").GetInt32(), ReadNullablePage(assets, "nextPage"));
    }

    internal static IReadOnlyList<ImmichVideoSummary> SortBySize(IEnumerable<ImmichVideoSummary> items, bool descending)
        => (descending
                ? items.OrderByDescending(x => x.SizeBytes.HasValue).ThenByDescending(x => x.SizeBytes)
                : items.OrderByDescending(x => x.SizeBytes.HasValue).ThenBy(x => x.SizeBytes))
            .ThenBy(x => x.AssetId, StringComparer.Ordinal).ToArray();

    public Task<IReadOnlyList<ImmichAlbum>> ListAlbumsAsync(ConnectionId connectionId, CancellationToken cancellationToken = default)
        => WithClientAsync<IReadOnlyList<ImmichAlbum>>(connectionId, async client =>
        {
            using var document = await client.JsonAsync(HttpMethod.Get, "api/albums", null, cancellationToken);
            return document.RootElement.EnumerateArray().Select(x => new ImmichAlbum(
                RequiredString(x, "id"), RequiredString(x, "albumName"), x.GetProperty("assetCount").GetInt32()))
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }, cancellationToken);

    public Task<ImmichVideoDetail> GetDetailAsync(ConnectionId connectionId, string assetId, CancellationToken cancellationToken = default)
        => WithClientAsync(connectionId, async client =>
        {
            ValidateAssetId(assetId);
            using var asset = await client.JsonAsync(HttpMethod.Get, $"api/assets/{Uri.EscapeDataString(assetId)}", null, cancellationToken);
            if (!IsVisibleVideo(asset.RootElement)) throw new ImmichConnectionException("immich.asset.unavailable", "The video is no longer available.");
            using var albums = await client.JsonAsync(HttpMethod.Get, $"api/albums?assetId={Uri.EscapeDataString(assetId)}", null, cancellationToken);
            var x = asset.RootElement;
            var exif = x.TryGetProperty("exifInfo", out var e) && e.ValueKind == JsonValueKind.Object ? e : default;
            return new ImmichVideoDetail(assetId, RequiredString(x, "originalFileName"), OptionalString(x, "originalMimeType"),
                RequiredDate(x, "fileCreatedAt"), RequiredDate(x, "fileModifiedAt"), ReadDuration(x),
                OptionalInt(x, "width"), OptionalInt(x, "height"), ReadFileSize(exif), OptionalString(exif, "description"),
                OptionalDouble(exif, "latitude"), OptionalDouble(exif, "longitude"),
                albums.RootElement.EnumerateArray().Select(a => RequiredString(a, "id")).ToArray());
        }, cancellationToken);

    public Task<ImmichThumbnail> OpenThumbnailAsync(ConnectionId connectionId, string assetId, CancellationToken cancellationToken = default)
        => WithClientAsync(connectionId, async client =>
        {
            ValidateAssetId(assetId);
            using var response = await client.SendAsync(HttpMethod.Get, $"api/assets/{Uri.EscapeDataString(assetId)}/thumbnail", null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) throw new ImmichConnectionException("immich.asset.unavailable", "The video is no longer available.");
            await RequireSuccessAsync(response);
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not ("image/jpeg" or "image/png" or "image/webp" or "image/avif"))
                throw new ImmichConnectionException("immich.thumbnail.content_type", "Immich returned an unsupported thumbnail content type.");
            if (response.Content.Headers.ContentLength > MaximumThumbnailBytes)
                throw new ImmichConnectionException("immich.thumbnail.too_large", "The thumbnail exceeds the response limit.");
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new MemoryStream();
            var chunk = new byte[64 * 1024];
            while (true)
            {
                var count = await source.ReadAsync(chunk, cancellationToken);
                if (count == 0) break;
                if (buffer.Length + count > MaximumThumbnailBytes) throw new ImmichConnectionException("immich.thumbnail.too_large", "The thumbnail exceeds the response limit.");
                await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
            }
            buffer.Position = 0;
            return new ImmichThumbnail(buffer, type, buffer.Length);
        }, cancellationToken);

    public async Task<ImmichVideoContent> OpenVideoAsync(ConnectionId connectionId, string assetId,
        string? rangeHeader, CancellationToken cancellationToken = default)
    {
        ValidateAssetId(assetId);
        RangeHeaderValue? range = null;
        if (!string.IsNullOrWhiteSpace(rangeHeader)
            && (!RangeHeaderValue.TryParse(rangeHeader, out range) || range.Ranges.Count != 1))
            throw new ImmichConnectionException("immich.video.range_invalid", "The requested video byte range is invalid.");

        var stored = await RequireConnectionAsync(connectionId, cancellationToken);
        byte[]? keyBytes = null;
        BrowserHttpClient? client = null;
        HttpResponseMessage? response = null;
        try
        {
            keyBytes = protector.Unprotect(stored.ApiKeyEnvelope!.Payload);
            client = new BrowserHttpClient(stored.Connection.BaseUrl, stored.Connection.AllowInvalidCertificate,
                Encoding.UTF8.GetString(keyBytes), options);
            response = await client.SendAsync(HttpMethod.Get,
                $"api/assets/{Uri.EscapeDataString(assetId)}/original", null, cancellationToken, range);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new ImmichConnectionException("immich.asset.unavailable", "The video is no longer available.");
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                throw new ImmichConnectionException("immich.video.range_unsatisfiable", "The requested video byte range is unavailable.");
            await RequireSuccessAsync(response);
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is null || (!type.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                && type != "application/octet-stream"))
                throw new ImmichConnectionException("immich.video.content_type", "Immich returned an unsupported video content type.");
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var lifetime = new VideoStreamLifetime(response, client);
            response = null;
            client = null;
            return new ImmichVideoContent(stream, type, lifetime.Response.Content.Headers.ContentLength,
                lifetime.Response.Content.Headers.ContentRange?.ToString(),
                lifetime.Response.StatusCode == HttpStatusCode.PartialContent, lifetime);
        }
        catch (CryptographicException exception)
        {
            throw new ImmichConnectionException("connection.api_key.unavailable", "The saved API key cannot be decrypted.", exception);
        }
        finally
        {
            response?.Dispose();
            client?.Dispose();
            if (keyBytes is not null) CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    public async Task<IReadOnlySet<string>> GetSelectionAsync(ConnectionId connectionId, CancellationToken cancellationToken = default)
    {
        await RequireConnectionAsync(connectionId, cancellationToken);
        return await selections.ListAsync(connectionId, cancellationToken);
    }

    public async Task SetSelectedAsync(ConnectionId connectionId, IEnumerable<string> assetIds, bool selected, CancellationToken cancellationToken = default)
    {
        await RequireConnectionAsync(connectionId, cancellationToken);
        var ids = assetIds.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var id in ids) ValidateAssetId(id);
        if (selected) await selections.AddAsync(connectionId, ids, cancellationToken);
        else await selections.RemoveAsync(connectionId, ids, cancellationToken);
    }

    public async Task ClearSelectionAsync(ConnectionId connectionId, CancellationToken cancellationToken = default)
    {
        await RequireConnectionAsync(connectionId, cancellationToken);
        await selections.ClearAsync(connectionId, cancellationToken);
    }

    private async Task<T> WithClientAsync<T>(ConnectionId id, Func<BrowserHttpClient, Task<T>> action, CancellationToken cancellationToken)
    {
        var stored = await RequireConnectionAsync(id, cancellationToken);
        byte[]? keyBytes = null;
        try
        {
            keyBytes = protector.Unprotect(stored.ApiKeyEnvelope!.Payload);
            using var client = new BrowserHttpClient(stored.Connection.BaseUrl, stored.Connection.AllowInvalidCertificate,
                Encoding.UTF8.GetString(keyBytes), options);
            return await action(client);
        }
        catch (CryptographicException exception) { throw new ImmichConnectionException("connection.api_key.unavailable", "The saved API key cannot be decrypted.", exception); }
        finally { if (keyBytes is not null) CryptographicOperations.ZeroMemory(keyBytes); }
    }

    private async Task<StoredImmichConnection> RequireConnectionAsync(ConnectionId id, CancellationToken cancellationToken)
    {
        var stored = await connections.GetAsync(id, cancellationToken)
            ?? throw new ImmichConnectionException("connection.deleted", "The Immich connection was deleted. Choose another connection.");
        if (!stored.Connection.Enabled) throw new ImmichConnectionException("connection.disabled", "The Immich connection was disabled. Choose another connection.");
        if (stored.Connection.Compatibility != CompatibilityResult.Compatible)
            throw new ImmichConnectionException("connection.version_mismatch", "Retest this connection with a supported Immich 3.1 server before browsing.");
        if (stored.ApiKeyEnvelope is null) throw new ImmichConnectionException("connection.api_key.required", "This connection has no saved API key.");
        return stored;
    }

    private static bool IsVisibleVideo(JsonElement x) => OptionalString(x, "type") == "VIDEO"
        && (!x.TryGetProperty("isTrashed", out var trashed) || trashed.ValueKind != JsonValueKind.True);
    private static ImmichVideoSummary MapSummary(JsonElement x) => new(RequiredString(x, "id"), RequiredString(x, "originalFileName"),
        OptionalString(x, "originalMimeType"), RequiredDate(x, "fileCreatedAt"), ReadDuration(x), OptionalInt(x, "width"), OptionalInt(x, "height"),
        x.TryGetProperty("exifInfo", out var exif) ? ReadFileSize(exif) : null);
    internal static int? ReadNullablePage(JsonElement x, string name)
    {
        if (!x.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        return null;
    }
    private static string RequiredString(JsonElement x, string name) => x.GetProperty(name).GetString() ?? throw new JsonException($"{name} is missing.");
    private static string? OptionalString(JsonElement x, string name) => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? OptionalInt(JsonElement x, string name) => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
    private static double? OptionalDouble(JsonElement x, string name) => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
    private static long? ReadFileSize(JsonElement x) => x.ValueKind == JsonValueKind.Object
        && x.TryGetProperty("fileSizeInByte", out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var bytes) && bytes >= 0 ? bytes : null;
    private static DateTimeOffset RequiredDate(JsonElement x, string name) => x.GetProperty(name).GetDateTimeOffset();
    private static TimeSpan? ReadDuration(JsonElement x) => x.TryGetProperty("duration", out var value) && value.ValueKind == JsonValueKind.Number ? TimeSpan.FromMilliseconds(value.GetDouble()) : null;
    private static void ValidateAssetId(string id) { if (!Guid.TryParse(id, out _)) throw new ImmichConnectionException("immich.asset_id.invalid", "The asset identifier is invalid."); }
    private static async Task RequireSuccessAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new ImmichConnectionException("connection.api_key.rejected", "Immich rejected the saved API key.");
        if (!response.IsSuccessStatusCode) throw new ImmichConnectionException("immich.http_error", $"Immich returned HTTP {(int)response.StatusCode}.");
        await Task.CompletedTask;
    }
    private sealed record SearchPage(IReadOnlyList<ImmichVideoSummary> Items, int Total, int? NextPage);
}

internal sealed class BrowserHttpClient : IDisposable
{
    private readonly HttpClient client;
    private readonly Uri origin;
    private readonly string apiKey;
    private readonly int maximumJsonBytes;
    public BrowserHttpClient(Uri origin, bool allowInvalidCertificate, string apiKey, ImmichConnectionOptions options)
    {
        this.origin = origin; this.apiKey = apiKey; maximumJsonBytes = options.MaximumResponseBytes;
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (allowInvalidCertificate) handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
    }
    public async Task<JsonDocument> JsonAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new ImmichConnectionException("immich.asset.unavailable", "The requested Immich resource is no longer available.");
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            throw new ImmichConnectionException("connection.version_mismatch", "Immich rejected the documented v3.1 request contract. Retest the connection.");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new ImmichConnectionException("connection.api_key.rejected", "Immich rejected the saved API key.");
        if (!response.IsSuccessStatusCode) throw new ImmichConnectionException("immich.http_error", $"Immich returned HTTP {(int)response.StatusCode}.");
        if (response.Content.Headers.ContentLength > maximumJsonBytes) throw new ImmichConnectionException("connection.response_too_large", "Immich returned an unexpectedly large response.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limited = new LimitedReadStream(stream, maximumJsonBytes);
        try { return await JsonDocument.ParseAsync(limited, cancellationToken: cancellationToken); }
        catch (JsonException exception) { throw new ImmichConnectionException("connection.contract.invalid", "Immich returned an unexpected v3.1 response.", exception); }
    }
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken, RangeHeaderValue? range = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(origin, path));
        var accept = method == HttpMethod.Get && path.EndsWith("thumbnail", StringComparison.Ordinal) ? "image/*"
            : method == HttpMethod.Get && path.EndsWith("original", StringComparison.Ordinal) ? "video/*"
            : "application/json";
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.Range = range;
        if (body is not null) request.Content = JsonContent.Create(body);
        try { return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested) { throw new ImmichConnectionException("connection.timeout", "The Immich request timed out.", exception); }
        catch (HttpRequestException exception) { throw new ImmichConnectionException("connection.unreachable", "The Immich server could not be reached.", exception); }
    }
    public void Dispose() => client.Dispose();
}

internal sealed class VideoStreamLifetime(HttpResponseMessage response, BrowserHttpClient client) : IAsyncDisposable
{
    public HttpResponseMessage Response { get; } = response;

    public ValueTask DisposeAsync()
    {
        Response.Dispose();
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
