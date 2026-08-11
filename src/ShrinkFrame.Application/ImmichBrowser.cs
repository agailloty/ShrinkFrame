using ShrinkFrame.Domain;

namespace ShrinkFrame.Application;

public enum ImmichVideoSort { TakenNewest, TakenOldest, SizeLargest, SizeSmallest }

public sealed record ImmichVideoSearch(ConnectionId ConnectionId, int Page = 1,
    DateTimeOffset? TakenAfter = null, DateTimeOffset? TakenBefore = null,
    string? AlbumId = null, ImmichVideoSort Sort = ImmichVideoSort.TakenNewest,
    long? PageMinimumBytes = null, long? PageMaximumBytes = null);

public sealed record ImmichVideoSummary(string AssetId, string FileName, string? MimeType,
    DateTimeOffset TakenAt, TimeSpan? Duration, int? Width, int? Height, long? SizeBytes);

public sealed record ImmichVideoPage(IReadOnlyList<ImmichVideoSummary> Items, int Page,
    int PageSize, int Total, int? NextPage, bool PageSizeRefinementApplied);

public sealed record ImmichAlbum(string Id, string Name, int AssetCount);

public sealed record ImmichVideoDetail(string AssetId, string FileName, string? MimeType,
    DateTimeOffset TakenAt, DateTimeOffset ModifiedAt, TimeSpan? Duration, int? Width,
    int? Height, long? SizeBytes, string? Description, double? Latitude, double? Longitude,
    IReadOnlyList<string> AlbumIds);

public sealed record ImmichThumbnail(Stream Content, string ContentType, long? ContentLength) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed record ImmichVideoContent(Stream Content, string ContentType, long? ContentLength,
    string? ContentRange, bool IsPartial, IAsyncDisposable Lifetime) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
        await Lifetime.DisposeAsync();
    }
}

public interface IImmichVideoBrowser
{
    Task<ImmichVideoPage> SearchAsync(ImmichVideoSearch search, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImmichAlbum>> ListAlbumsAsync(ConnectionId connectionId, CancellationToken cancellationToken = default);
    Task<ImmichVideoDetail> GetDetailAsync(ConnectionId connectionId, string assetId, CancellationToken cancellationToken = default);
    Task<ImmichThumbnail> OpenThumbnailAsync(ConnectionId connectionId, string assetId, CancellationToken cancellationToken = default);
    Task<ImmichVideoContent> OpenVideoAsync(ConnectionId connectionId, string assetId,
        string? rangeHeader, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> GetSelectionAsync(ConnectionId connectionId, CancellationToken cancellationToken = default);
    Task SetSelectedAsync(ConnectionId connectionId, IEnumerable<string> assetIds, bool selected, CancellationToken cancellationToken = default);
    Task ClearSelectionAsync(ConnectionId connectionId, CancellationToken cancellationToken = default);
}
