# Immich integration

## Compatibility policy

Version 1.0 targets Immich 3.1.x and uses only a small handwritten typed `HttpClient`. On connection test, record the server version and report:

- compatible: tested supported major/minor;
- warning: same major but untested minor;
- incompatible: contract probe failed or known unsupported major.

Do not silently assume the latest online documentation matches the connected server. Keep Immich DTOs internal to Infrastructure and map them to application models.

Official documentation:

- API overview: <https://api.immich.app/endpoints>
- Authentication: <https://api.immich.app/authentication>

Contract audit: 2026-08-10. The endpoint pages below reported the v3 contract and `Status: Stable` unless noted otherwise. Immich's online documentation tracks the current API rather than a permanently frozen 3.1 schema, so implementation must still probe a connected 3.1.x server and test against its generated OpenAPI contract.

Prompt 07 implementation verification (2026-08-10) uses exactly `GET /api/server/ping`,
`GET /api/server/version`, and authenticated `GET /api/api-keys/me`, with the API key only in the
`x-api-key` header. DTO parsing is limited to ping `res`, numeric version `major`/`minor`/`patch`,
and current-key `id`/`name`/`permissions`. These contracts were verified against the official v3.1
endpoint/model pages listed below. No live Immich test instance was available in the development
environment, so an exact deployed patch version has not been claimed; live 3.1.x verification remains
a deployment check.

Every path below is relative to `{BaseUrl}/api`. Normalize the configured base URL so users may enter either the external site root or an API-root URL without producing `/api/api`.

## Authentication and required permissions

Send the API key only in the `x-api-key` request header. Never use the `apiKey` query parameter.

Required version 1.0 permissions:

| Permission | Use |
|---|---|
| `asset.read` | Search and retrieve source asset metadata |
| `asset.view` | Retrieve thumbnails |
| `asset.download` | Download original videos |
| `asset.upload` | Publish compressed videos |
| `album.read` | List albums and find source memberships |
| `albumAsset.create` | Add published assets to existing albums |

Connection testing calls public ping/version plus authenticated `GET /api-keys/me`, which returns current-key permissions. It must also perform harmless contract probes necessary to validate the configured base URL. A missing optional permission disables the related capability; missing core read/download permissions makes a connection unusable as a source, while missing upload/album-create permissions makes publication unavailable.

## Endpoint inventory

| Operation | Method and path | Permission | Notes |
|---|---|---|---|
| Ping | `GET /server/ping` | Public | Connectivity |
| Version | `GET /server/version` | Public | Compatibility |
| Current API key | `GET /api-keys/me` | Authenticated | Permission list |
| Search videos | `POST /search/metadata` | `asset.read` | Set `type=VIDEO`, page/size, dates, album, `withExif=true` |
| List albums | `GET /albums` | `album.read` | Also supports `assetId` to find memberships |
| Asset details | `GET /assets/{id}` | `asset.read` | Dates, filename, dimensions, EXIF |
| Thumbnail | `GET /assets/{id}/thumbnail` | `asset.view` | Proxy response; do not leak key |
| Original | `GET /assets/{id}/original` | `asset.download` | Stream to work storage |
| Upload | `POST /assets` | `asset.upload` | Multipart file plus dates/name/metadata |
| Add to album | `PUT /albums/{id}/assets` | `albumAsset.create` | Body `{ "ids": [newAssetId] }` |

### Verified v3.1 contract references

Accessed 2026-08-10:

| Operation | Official page | Verified request/response facts |
|---|---|---|
| Ping | <https://api.immich.app/endpoints/server/pingServer> | Stable, public `GET /server/ping`; response contains required string `res` |
| Version | <https://api.immich.app/endpoints/server/getServerVersion> | Stable, public `GET /server/version`; numeric `major`, `minor`, `patch`, and nullable numeric `prerelease` |
| Current API key | <https://api.immich.app/endpoints/api-keys/getMyApiKey> | Stable authenticated `GET /api-keys/me`; returns ID, name, timestamps, and `Permission[]` |
| Search metadata | <https://api.immich.app/endpoints/search/searchAssets> | Stable, `asset.read`; body supports `albumIds`, `takenAfter`, `takenBefore`, `order`, `page`, `size`, `type`, `withDeleted`, and `withExif`. Here `size` is page length, not file bytes. ShrinkFrame sends `withDeleted=false` and also rejects trashed response items defensively. |
| List albums/membership | <https://api.immich.app/endpoints/albums/getAllAlbums> | Stable, `album.read`; optional `assetId` finds containing albums |
| Asset details | <https://api.immich.app/endpoints/assets/getAssetInfo> | Stable, `asset.read`; returns asset, capture-date, dimension, and EXIF information |
| Thumbnail | <https://api.immich.app/endpoints/assets/viewAsset> | Stable, `asset.view` |
| Original | <https://api.immich.app/endpoints/assets/downloadAsset> | Stable, `asset.download` |
| Upload | <https://api.immich.app/endpoints/assets/uploadAsset> | Stable, `asset.upload`; multipart requires `assetData`, `fileCreatedAt`, and `fileModifiedAt`; accepts `filename` and metadata items |
| Check bulk upload | <https://api.immich.app/endpoints/assets/checkBulkUpload> | Stable, `asset.upload`; request items contain a client ID and base64/hex SHA-1 checksum; a duplicate result includes the existing asset ID |
| Add to album | <https://api.immich.app/endpoints/albums/addAssetsToAlbum> | Stable, `albumAsset.create`; body is required UUID array `ids` |
| Per-asset metadata update | <https://api.immich.app/endpoints/assets/updateAssetMetadata> | Stable, `asset.update`; body contains metadata key/value items, but the generic schema does not establish description/location keys |
| Deprecated asset updates | <https://api.immich.app/endpoints/assets/updateAssets> and <https://api.immich.app/endpoints/assets/updateAsset> | Deprecated in v3; these must not be used to copy description or coordinates |

The permission names were checked against <https://api.immich.app/models/Permission>. `asset.update` is not a baseline permission because the stable upload contract accepts metadata and the application must not perform a post-upload mutation until the exact supported metadata keys are proven. If live 3.1 verification establishes that the stable per-asset metadata operation is required, add `asset.update` as an optional publication capability and record that contract before implementation.

Do not use internal timeline endpoints or deprecated update endpoints. Before implementing description/location copying, verify the stable Immich 3.1 contract in official documentation or the connected server's generated client/source. If no stable operation exists, upload supported metadata and emit a clear warning rather than using deprecated behavior invisibly.

## Search behavior

- Explicit pagination, 50 results per page.
- Type must be Video.
- User filters: taken-after, taken-before, and album ID. A byte-size refinement may be offered only for assets whose byte sizes have been obtained, and must be labeled as applying to the loaded page/results.
- `search/metadata` has no byte-size predicate (`size` means page length), and the audited asset DTO does not promise file byte size. Do not present a global byte-size filter. A later implementation may add a documented bounded metadata/download-information lookup and then filter only that known result set.
- Sort options must map to supported Immich `AssetOrder` values; unsupported size sorting must not be presented as server-global.
- Preserve selected asset IDs independently of page DTOs.
- Prompt 08 persists browser selection in SQLite as `(connectionId, assetId)`. Selection survives page,
  filter, refresh, and Interactive Server reconnect changes, while the connection key prevents a later
  batch from mixing sources. `Select Page` affects the visible post-refinement page only; no Select All
  Results operation exists.
- Supported server sorts are capture time ascending and descending, mapped to Immich `AssetOrder`
  `asc` and `desc`. Size sorting is implemented globally by walking every matching 50-item metadata
  page, reading optional `exifInfo.fileSizeInByte`, sorting the collected metadata, and repaginating
  it in ShrinkFrame. Assets with unknown sizes appear after all known-size assets.

## Transfers

- Use `HttpCompletionOption.ResponseHeadersRead`.
- Stream with bounded buffers and cancellation.
- Do not retry large transfer bodies automatically unless the retry is known to restart from zero and cleans the partial file.
- An interrupted version 1.0 transfer restarts from zero.
- Validate expected content length when present and probe the completed file.
- Thumbnail proxy applies bounded size, content-type allowlist, short cache headers, and cancellation.
- Browser playback proxies the original through ShrinkFrame, forwards at most one validated `Range` request, preserves `206`, `Content-Range`, and `Content-Length`, disables caching, and streams with a bounded buffer. The API key remains only in the server-side `x-api-key` header.

## Publication idempotency

Persist an upload-attempt identifier and returned asset ID as soon as available. Before retrying an uncertain upload, use Immich's supported bulk-upload/checksum facilities if the connected version contract is verified. Never blindly upload repeatedly after an ambiguous timeout.

Publication back to Immich preserves:

- `_V` suffixed MP4 filename;
- `fileCreatedAt` from authoritative source capture time;
- `fileModifiedAt` from the source snapshot or output semantics;
- description and coordinates when stable supported operations permit;
- membership in the same album IDs because source and destination instance are identical.

Prompt 12 re-verification on 2026-08-10 confirmed that the stable upload endpoint's generic metadata items
still do not define keys that populate Immich's EXIF description, latitude, or longitude fields. The stable
per-asset metadata endpoint likewise stores generic key/value metadata and does not define those EXIF fields.
The only documented direct asset mutation DTO is behind deprecated asset-update endpoints. ShrinkFrame does
not call them. It uploads the validated MP4 (which may contain preserved embedded tags), preserves filename and
dates through documented multipart fields, and records/displays `publication.metadata.not_guaranteed` whenever
the source snapshot has description or coordinates. Live verification may prove extraction from a particular
3.1.x build, but version 1.0 does not claim a stable API guarantee for these fields.

Before every upload or retry ShrinkFrame computes the output SHA-1 with a bounded streaming read and calls the
stable bulk-upload check. A persisted client attempt ID correlates the response. An existing non-trashed asset
ID is adopted without uploading. A timeout or transport failure while sending multipart is marked ambiguous;
retry runs the checksum check first and uploads again only when Immich says no asset exists. It never blindly
replays an ambiguous multipart body.

An album-add failure retains the new asset and transitions to `PartiallyPublished`. Retry only missing album operations.

## Preview file size

The browser reads the optional `exifInfo.fileSizeInByte` value returned by Immich when `withExif=true` and
shows it on preview cards and in the details modal. Missing, negative, non-integral, or out-of-range values
remain unknown; ShrinkFrame does not download an original merely to determine its size.
