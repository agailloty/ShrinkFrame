using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Immich;

public sealed class ImmichVideoSource(IImmichConnectionRepository connections, IImmichVideoBrowser browser,
    IDataProtectionProvider protectionProvider) : IVideoSource
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("ShrinkFrame.Immich.ApiKey.v1");

    public Task<ImmichVideoDetail> GetDetailAsync(VideoSourceRef source, CancellationToken token = default)
    {
        RequireImmich(source);
        return browser.GetDetailAsync(source.ConnectionId!.Value, source.SourceId, token);
    }

    public async Task<SourceDownload> OpenOriginalAsync(VideoSourceRef source, CancellationToken token = default)
    {
        RequireImmich(source);
        var stored = await connections.GetAsync(source.ConnectionId!.Value, token)
            ?? throw new ImmichConnectionException("immich.connection.missing", "The source connection no longer exists.");
        if (!stored.Connection.Enabled || stored.ApiKeyEnvelope is null)
            throw new ImmichConnectionException("immich.connection.disabled", "The source connection is disabled or has no saved API key.");
        byte[]? key = null;
        try
        {
            key = protector.Unprotect(stored.ApiKeyEnvelope.Payload);
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            if (stored.Connection.AllowInvalidCertificate)
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(stored.Connection.BaseUrl,
                $"api/assets/{Uri.EscapeDataString(source.SourceId)}/original"));
            request.Headers.TryAddWithoutValidation("x-api-key", Encoding.UTF8.GetString(key));
            HttpResponseMessage response;
            try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); }
            catch { request.Dispose(); client.Dispose(); throw; }
            request.Dispose();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
            {
                var status = response.StatusCode; response.Dispose(); client.Dispose();
                throw new ImmichConnectionException("immich.download.failed", $"Immich original download returned HTTP {(int)status}.");
            }
            var stream = await response.Content.ReadAsStreamAsync(token);
            var name = response.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"') ?? source.SourceId;
            var mime = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return new SourceDownload(new OwnedHttpStream(stream, response, client), response.Content.Headers.ContentLength, name, mime);
        }
        catch (CryptographicException exception)
        { throw new ImmichConnectionException("connection.api_key.unavailable", "The saved API key cannot be decrypted.", exception); }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
    }

    private static void RequireImmich(VideoSourceRef source)
    {
        if (source.Kind != SourceKind.Immich || source.ConnectionId is null)
            throw new ArgumentException("An Immich source is required.", nameof(source));
    }

    private sealed class OwnedHttpStream(Stream inner, HttpResponseMessage response, HttpClient client) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => inner.ReadAsync(buffer, token);
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken token) => inner.CopyToAsync(destination, bufferSize, token);
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); response.Dispose(); client.Dispose(); } base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); response.Dispose(); client.Dispose(); await base.DisposeAsync(); GC.SuppressFinalize(this); }
    }
}
