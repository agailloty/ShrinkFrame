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

public sealed class ImmichPublicationTransport(IImmichConnectionRepository connections,
    IDataProtectionProvider protectionProvider, ImmichConnectionOptions options) : IImmichPublicationTransport
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("ShrinkFrame.Immich.ApiKey.v1");

    public Task<ImmichUploadCheck> CheckExistingAsync(ConnectionId connectionId, string clientAttemptId,
        string sha1Checksum, CancellationToken cancellationToken = default)
        => WithClientAsync(connectionId, async client =>
        {
            using var document = await client.JsonAsync(HttpMethod.Post, "api/assets/bulk-upload-check",
                new { assets = new[] { new { id = clientAttemptId, checksum = sha1Checksum } } }, cancellationToken);
            var result = document.RootElement.GetProperty("results").EnumerateArray()
                .SingleOrDefault(x => string.Equals(x.GetProperty("id").GetString(), clientAttemptId, StringComparison.Ordinal));
            if (result.ValueKind != JsonValueKind.Object)
                throw new ImmichPublicationTransportException("publication.contract.invalid", "Immich did not correlate the upload check response.");
            var assetId = result.TryGetProperty("assetId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            var trashed = result.TryGetProperty("isTrashed", out var value) && value.ValueKind == JsonValueKind.True;
            return new ImmichUploadCheck(assetId, trashed);
        }, cancellationToken);

    public Task<ImmichUploadResult> UploadAsync(ConnectionId connectionId, ImmichUploadRequest request,
        CancellationToken cancellationToken = default)
        => WithClientAsync(connectionId, async client =>
        {
            await using var stream = await request.OpenContent(cancellationToken);
            using var multipart = new MultipartFormDataContent();
            using var file = new StreamContent(stream, 128 * 1024);
            file.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            multipart.Add(file, "assetData", request.FileName);
            multipart.Add(new StringContent(request.FileCreatedAt.ToString("O"), Encoding.UTF8), "fileCreatedAt");
            multipart.Add(new StringContent(request.FileModifiedAt.ToString("O"), Encoding.UTF8), "fileModifiedAt");
            multipart.Add(new StringContent(request.FileName, Encoding.UTF8), "filename");
            using var document = await client.UploadAsync("api/assets", multipart, cancellationToken);
            var id = document.RootElement.GetProperty("id").GetString();
            var status = document.RootElement.GetProperty("status").GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(status))
                throw new ImmichPublicationTransportException("publication.contract.invalid", "Immich returned an invalid upload response.", true);
            return new ImmichUploadResult(id, status);
        }, cancellationToken);

    public Task AddToAlbumAsync(ConnectionId connectionId, string albumId, string assetId,
        CancellationToken cancellationToken = default)
        => WithClientAsync(connectionId, async client =>
        {
            if (!Guid.TryParse(albumId, out _) || !Guid.TryParse(assetId, out _))
                throw new ImmichPublicationTransportException("publication.album.invalid", "An album or asset identifier is invalid.");
            using var response = await client.SendAsync(HttpMethod.Put,
                $"api/albums/{Uri.EscapeDataString(albumId)}/assets", JsonContent.Create(new { ids = new[] { assetId } }), cancellationToken);
            await PublicationHttpClient.RequireSuccessAsync(response);
            return true;
        }, cancellationToken);

    private async Task<T> WithClientAsync<T>(ConnectionId id, Func<PublicationHttpClient, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var stored = await connections.GetAsync(id, cancellationToken)
            ?? throw new ImmichPublicationTransportException("publication.connection.missing", "The Immich connection no longer exists.");
        var permissions = (stored.Connection.LastTestPermissions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!stored.Connection.Enabled || stored.Connection.Compatibility != CompatibilityResult.Compatible || stored.ApiKeyEnvelope is null
            || !permissions.Contains("asset.upload", StringComparer.Ordinal) || !permissions.Contains("albumAsset.create", StringComparer.Ordinal))
            throw new ImmichPublicationTransportException("publication.connection.unavailable", "The Immich connection is not publish-capable.");
        byte[]? key = null;
        try
        {
            key = protector.Unprotect(stored.ApiKeyEnvelope.Payload);
            using var client = new PublicationHttpClient(stored.Connection.BaseUrl, stored.Connection.AllowInvalidCertificate,
                Encoding.UTF8.GetString(key), options);
            return await operation(client);
        }
        catch (CryptographicException exception)
        {
            throw new ImmichPublicationTransportException("connection.api_key.unavailable", "The saved API key cannot be decrypted.", false, exception);
        }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
    }
}

internal sealed class PublicationHttpClient : IDisposable
{
    private readonly HttpClient client;
    private readonly Uri origin;
    private readonly string apiKey;
    private readonly ImmichConnectionOptions options;
    public PublicationHttpClient(Uri origin, bool allowInvalidCertificate, string apiKey, ImmichConnectionOptions options)
    {
        this.origin = origin; this.apiKey = apiKey; this.options = options;
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (allowInvalidCertificate) handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<JsonDocument> JsonAsync(HttpMethod method, string path, object body, CancellationToken token)
    {
        using var response = await SendAsync(method, path, JsonContent.Create(body), token);
        await RequireSuccessAsync(response);
        return await ReadJsonAsync(response, token);
    }

    public async Task<JsonDocument> UploadAsync(string path, HttpContent body, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.PublicationTimeoutSeconds));
        HttpResponseMessage response;
        try { response = await SendCoreAsync(HttpMethod.Post, path, body, timeout.Token); }
        catch (OperationCanceledException exception) when (!token.IsCancellationRequested)
        { throw new ImmichPublicationTransportException("publication.upload.ambiguous", "The Immich upload timed out; retry will check its checksum before sending again.", true, exception); }
        catch (HttpRequestException exception)
        { throw new ImmichPublicationTransportException("publication.upload.ambiguous", "The Immich upload connection failed after it may have reached the server; retry will check its checksum first.", true, exception); }
        using (response)
        {
            if ((int)response.StatusCode is >= 300 and < 400)
                throw new ImmichPublicationTransportException("publication.upload.ambiguous", "Immich redirected an upload; its result is ambiguous and the body was not replayed.", true);
            await RequireSuccessAsync(response);
            return await ReadJsonAsync(response, token);
        }
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try { return await SendCoreAsync(method, path, content, timeout.Token); }
        catch (OperationCanceledException exception) when (!token.IsCancellationRequested)
        { throw new ImmichPublicationTransportException("connection.timeout", "The Immich request timed out.", false, exception); }
        catch (HttpRequestException exception)
        { throw new ImmichPublicationTransportException("connection.unreachable", "The Immich server could not be reached.", false, exception); }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, HttpContent? content, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, new Uri(origin, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Content = content;
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }

    public static async Task RequireSuccessAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ImmichPublicationTransportException("connection.api_key.rejected", "Immich rejected the saved API key.");
        if (!response.IsSuccessStatusCode)
            throw new ImmichPublicationTransportException("publication.http_error", $"Immich returned HTTP {(int)response.StatusCode}.");
        await Task.CompletedTask;
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > options.MaximumResponseBytes)
            throw new ImmichPublicationTransportException("publication.response.too_large", "Immich returned an unexpectedly large response.");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        using var limited = new LimitedReadStream(source, options.MaximumResponseBytes);
        try { return await JsonDocument.ParseAsync(limited, cancellationToken: token); }
        catch (JsonException exception)
        { throw new ImmichPublicationTransportException("publication.contract.invalid", "Immich returned invalid publication JSON.", false, exception); }
    }
    public void Dispose() => client.Dispose();
}
