using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;

namespace ShrinkFrame.Infrastructure.Immich;

public sealed class ImmichConnectionOptions
{
    public const string SectionName = "ImmichConnections";
    public int TimeoutSeconds { get; set; } = 10;
    public int MaximumResponseBytes { get; set; } = 1_048_576;
    public int PublicationTimeoutSeconds { get; set; } = 3600;
}

public sealed class ImmichConnectionManager(
    IImmichConnectionRepository repository,
    IDataProtectionProvider protectionProvider,
    TimeProvider timeProvider,
    ImmichConnectionOptions options) : IImmichConnectionManager
{
    private const string ProtectorPurpose = "ShrinkFrame.Immich.ApiKey.v1";
    private static readonly string[] SourcePermissions = ["asset.read", "asset.view", "asset.download", "album.read"];
    private static readonly string[] CoreSourcePermissions = ["asset.read", "asset.download"];
    private static readonly string[] PublishPermissions = ["asset.upload", "albumAsset.create"];
    private readonly IDataProtector protector = protectionProvider.CreateProtector(ProtectorPurpose);

    public async Task<IReadOnlyList<ImmichConnectionView>> ListAsync(CancellationToken cancellationToken = default)
        => (await repository.ListAsync(cancellationToken)).Select(ToView).ToArray();

    public async Task<ImmichConnectionView> AddAsync(ImmichConnectionInput input, CancellationToken cancellationToken = default)
    {
        ValidateDefault(input);
        var uri = NormalizeUrl(input.BaseUrl);
        if (string.IsNullOrWhiteSpace(input.ApiKey)) throw new ImmichConnectionException("connection.api_key.required", "An API key is required.");
        var connection = new ImmichConnection(ConnectionId.New(), input.DisplayName, uri, input.AllowInvalidCertificate, input.Enabled, false);
        var stored = new StoredImmichConnection(connection, Protect(input.ApiKey));
        await repository.AddAsync(stored, cancellationToken);
        if (input.IsDefault)
        {
            await repository.SetDefaultAsync(connection.Id, cancellationToken);
            connection.Update(input.DisplayName, uri, input.AllowInvalidCertificate, input.Enabled, true);
        }
        return ToView(stored);
    }

    public async Task<ImmichConnectionView> UpdateAsync(ConnectionId id, ImmichConnectionInput input, CancellationToken cancellationToken = default)
    {
        ValidateDefault(input);
        var stored = await RequiredAsync(id, cancellationToken);
        var uri = NormalizeUrl(input.BaseUrl);
        stored.Connection.Update(input.DisplayName, uri, input.AllowInvalidCertificate, input.Enabled, false);
        var envelope = string.IsNullOrWhiteSpace(input.ApiKey) ? stored.ApiKeyEnvelope : Protect(input.ApiKey);
        await repository.UpdateAsync(new(stored.Connection, envelope), cancellationToken);
        if (input.IsDefault)
        {
            await repository.SetDefaultAsync(id, cancellationToken);
            stored.Connection.Update(input.DisplayName, uri, input.AllowInvalidCertificate, input.Enabled, true);
        }
        return ToView(new(stored.Connection, envelope));
    }

    public async Task<ImmichConnectionView> TestAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        var stored = await RequiredAsync(id, cancellationToken);
        if (stored.ApiKeyEnvelope is null) throw new ImmichConnectionException("connection.api_key.required", "This connection has no saved API key.");
        byte[]? keyBytes = null;
        string? apiKey = null;
        try
        {
            try
            {
                keyBytes = protector.Unprotect(stored.ApiKeyEnvelope.Payload);
                apiKey = Encoding.UTF8.GetString(keyBytes);
            }
            catch (CryptographicException exception)
            {
                throw new ImmichConnectionException("connection.api_key.unavailable",
                    "The saved API key cannot be decrypted. Restore the persisted Data Protection key ring or replace the API key.", exception);
            }

            using var client = new ImmichProbeClient(stored.Connection.BaseUrl, stored.Connection.AllowInvalidCertificate, options);
            await client.PingAsync(cancellationToken);
            var version = await client.VersionAsync(cancellationToken);
            var key = await client.CurrentKeyAsync(apiKey, cancellationToken);
            var versionText = $"{version.Major}.{version.Minor}.{version.Patch}";
            var compatibility = version.Major == 3 && version.Minor == 1 ? CompatibilityResult.Compatible
                : version.Major == 3 ? CompatibilityResult.Warning : CompatibilityResult.Incompatible;
            var permissions = key.Permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            stored.Connection.RecordTest(timeProvider.GetUtcNow(), versionText, compatibility, null,
                key.Id, key.Name, string.Join(',', permissions));
        }
        catch (ImmichConnectionException exception) when (exception.Code != "connection.api_key.unavailable")
        {
            stored.Connection.RecordTest(timeProvider.GetUtcNow(), null, CompatibilityResult.Incompatible,
                $"{exception.Code}|{exception.Message}");
        }
        finally
        {
            apiKey = null;
            if (keyBytes is not null) CryptographicOperations.ZeroMemory(keyBytes);
        }
        await repository.UpdateAsync(stored, cancellationToken);
        return ToView(stored);
    }

    public Task SetDefaultAsync(ConnectionId id, CancellationToken cancellationToken = default)
        => repository.SetDefaultAsync(id, cancellationToken);

    public async Task DeleteAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        if (await repository.IsRequiredByActiveWorkAsync(id, cancellationToken))
            throw new ImmichConnectionException("connection.in_use", "Active batches or jobs still require this connection. Disable it instead.");
        await repository.DeleteAsync(id, cancellationToken);
    }

    public static Uri NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ImmichConnectionException("connection.url.invalid", "Enter an absolute HTTP or HTTPS URL.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new ImmichConnectionException("connection.url.credentials", "Credentials are not allowed in the URL.");
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ImmichConnectionException("connection.url.invalid", "Query strings and fragments are not allowed in the URL.");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Equals("/api", StringComparison.OrdinalIgnoreCase)) path = "";
        if (path.Length != 0) throw new ImmichConnectionException("connection.url.path", "Use the Immich site root or its /api URL.");
        return new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port, "/").Uri;
    }

    private EncryptedSecretEnvelope Protect(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try { return new(protector.Protect(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void ValidateDefault(ImmichConnectionInput input)
    {
        if (input.IsDefault && !input.Enabled)
            throw new ImmichConnectionException("connection.default.disabled", "Only an enabled connection can be the default.");
    }

    private async Task<StoredImmichConnection> RequiredAsync(ConnectionId id, CancellationToken cancellationToken)
        => await repository.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Immich connection was not found.");

    private static ImmichConnectionView ToView(StoredImmichConnection stored)
    {
        var connection = stored.Connection;
        var permissions = (connection.LastTestPermissions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sourceMissing = SourcePermissions.Except(permissions, StringComparer.Ordinal).ToArray();
        var coreSourceMissing = CoreSourcePermissions.Except(permissions, StringComparer.Ordinal).ToArray();
        var publishMissing = PublishPermissions.Except(permissions, StringComparer.Ordinal).ToArray();
        string? code = null, message = null;
        if (connection.LastTestError is not null)
        {
            var parts = connection.LastTestError.Split('|', 2); code = parts[0]; message = parts.Length == 2 ? parts[1] : parts[0];
        }
        return new(connection.Id, connection.DisplayName, connection.BaseUrl.AbsoluteUri,
            connection.AllowInvalidCertificate, connection.Enabled, connection.IsDefault, stored.ApiKeyEnvelope is not null,
            connection.LastTestedAt, connection.DetectedVersion, connection.Compatibility,
            connection.LastTestKeyId, connection.LastTestKeyName, permissions,
            new(coreSourceMissing.Length == 0, publishMissing.Length == 0, sourceMissing, publishMissing), code, message);
    }
}

internal sealed class ImmichProbeClient : IDisposable
{
    private readonly Uri origin;
    private readonly HttpClient client;
    private readonly ImmichConnectionOptions options;
    public ImmichProbeClient(Uri origin, bool allowInvalidCertificate, ImmichConnectionOptions options)
    {
        this.origin = origin; this.options = options;
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (allowInvalidCertificate)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("api/server/ping", null, cancellationToken);
        if (!document.RootElement.TryGetProperty("res", out var result) || result.ValueKind != JsonValueKind.String)
            throw new ImmichConnectionException("connection.contract.invalid", "Immich ping returned an unexpected response.");
    }

    public async Task<ServerVersion> VersionAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("api/server/version", null, cancellationToken);
        try { return new(document.RootElement.GetProperty("major").GetInt32(), document.RootElement.GetProperty("minor").GetInt32(), document.RootElement.GetProperty("patch").GetInt32()); }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new ImmichConnectionException("connection.contract.invalid", "Immich version returned an unexpected response.", exception); }
    }

    public async Task<CurrentKey> CurrentKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("api/api-keys/me", apiKey, cancellationToken);
        try
        {
            return new(document.RootElement.GetProperty("id").GetString() ?? "",
                document.RootElement.GetProperty("name").GetString() ?? "",
                document.RootElement.GetProperty("permissions").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length != 0).ToArray());
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        { throw new ImmichConnectionException("connection.contract.invalid", "Current API key returned an unexpected response.", exception); }
    }

    private async Task<JsonDocument> GetJsonAsync(string path, string? apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(origin, path);
        for (var redirect = 0; redirect <= 3; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (apiKey is not null) request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            HttpResponseMessage response;
            try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            { throw new ImmichConnectionException("connection.timeout", "The Immich request timed out.", exception); }
            catch (HttpRequestException exception)
            { throw new ImmichConnectionException("connection.unreachable", "The Immich server could not be reached. Check the URL and certificate setting.", exception); }
            using (response)
            {
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null)
                {
                    var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                    if (!SameOrigin(origin, next)) throw new ImmichConnectionException("connection.redirect_origin", "Immich redirected the request to a different origin.");
                    uri = next; continue;
                }
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new ImmichConnectionException("connection.api_key.rejected", "Immich rejected the saved API key.");
                if (!response.IsSuccessStatusCode)
                    throw new ImmichConnectionException("connection.http_error", $"Immich returned HTTP {(int)response.StatusCode}.");
                var length = response.Content.Headers.ContentLength;
                if (length > options.MaximumResponseBytes) throw new ImmichConnectionException("connection.response_too_large", "Immich returned an unexpectedly large response.");
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var limited = new LimitedReadStream(stream, options.MaximumResponseBytes);
                try { return await JsonDocument.ParseAsync(limited, cancellationToken: cancellationToken); }
                catch (JsonException exception) { throw new ImmichConnectionException("connection.contract.invalid", "Immich returned invalid JSON.", exception); }
            }
        }
        throw new ImmichConnectionException("connection.redirect_limit", "Immich returned too many redirects.");
    }

    private static bool SameOrigin(Uri left, Uri right) => left.Scheme == right.Scheme && left.IdnHost == right.IdnHost && left.Port == right.Port;
    public void Dispose() => client.Dispose();
    internal sealed record ServerVersion(int Major, int Minor, int Patch);
    internal sealed record CurrentKey(string Id, string Name, IReadOnlyList<string> Permissions);
}

internal sealed class LimitedReadStream(Stream inner, long maximum) : Stream
{
    private long read;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var count = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, maximum - read + 1)], cancellationToken);
        read += count; if (read > maximum) throw new ImmichConnectionException("connection.response_too_large", "Immich returned an unexpectedly large response."); return count;
    }
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException(); public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}

public static class ImmichServiceCollectionExtensions
{
    public static IServiceCollection AddImmichConnections(this IServiceCollection services, ImmichConnectionOptions options)
    {
        if (options.TimeoutSeconds is < 1 or > 120 || options.MaximumResponseBytes is < 1024 or > 10_485_760
            || options.PublicationTimeoutSeconds is < 30 or > 86_400)
            throw new InvalidOperationException("Immich connection options are outside the supported bounds.");
        services.AddSingleton(options);
        services.AddScoped<IImmichConnectionManager, ImmichConnectionManager>();
        services.AddScoped<IImmichVideoBrowser, ImmichVideoBrowser>();
        services.AddScoped<IVideoSource, ImmichVideoSource>();
        services.AddScoped<IImmichPublicationTransport, ImmichPublicationTransport>();
        return services;
    }
}
