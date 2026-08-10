using ShrinkFrame.Domain;

namespace ShrinkFrame.Application;

public sealed record ImmichConnectionInput(string DisplayName, string BaseUrl, string? ApiKey,
    bool AllowInvalidCertificate, bool Enabled, bool IsDefault);

public sealed record ImmichCapabilities(bool CanUseAsSource, bool CanPublish,
    IReadOnlyList<string> MissingSourcePermissions, IReadOnlyList<string> MissingPublishPermissions);

public sealed record ImmichConnectionView(ConnectionId Id, string DisplayName, string BaseUrl,
    bool AllowInvalidCertificate, bool Enabled, bool IsDefault, bool HasApiKey,
    DateTimeOffset? LastTestedAt, string? Version, CompatibilityResult Compatibility,
    string? KeyId, string? KeyName, IReadOnlyList<string> Permissions,
    ImmichCapabilities Capabilities, string? ErrorCode, string? ErrorMessage);

public interface IImmichConnectionManager
{
    Task<IReadOnlyList<ImmichConnectionView>> ListAsync(CancellationToken cancellationToken = default);
    Task<ImmichConnectionView> AddAsync(ImmichConnectionInput input, CancellationToken cancellationToken = default);
    Task<ImmichConnectionView> UpdateAsync(ConnectionId id, ImmichConnectionInput input, CancellationToken cancellationToken = default);
    Task<ImmichConnectionView> TestAsync(ConnectionId id, CancellationToken cancellationToken = default);
    Task SetDefaultAsync(ConnectionId id, CancellationToken cancellationToken = default);
    Task DeleteAsync(ConnectionId id, CancellationToken cancellationToken = default);
}

public sealed class ImmichConnectionException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}
