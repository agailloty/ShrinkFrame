# Immich integration

## Compatibility policy

The POC targets Immich 3.1.x and uses only a small handwritten typed `HttpClient`. On connection test, record the server version and report:

- compatible: tested supported major/minor;
- warning: same major but untested minor;
- incompatible: contract probe failed or known unsupported major.

Do not silently assume the latest online documentation matches the connected server. Keep Immich DTOs internal to Infrastructure and map them to application models.

Official documentation:

- API overview: <https://api.immich.app/endpoints>
- Authentication: <https://api.immich.app/authentication>

Contract audit: 2026-08-10. The endpoint pages below reported the v3 contract and `Status: Stable` unless noted otherwise. Immich's online documentation tracks the current API rather than a permanently frozen 3.1 schema, so implementation must still probe a connected 3.1.x server and test against its generated OpenAPI contract.

Every path below is relative to `{BaseUrl}/api`. Normalize the configured base URL so users may enter either the external site root or an API-root URL without producing `/api/api`.

## Authentication and required permissions

Send the API key only in the `x-api-key` request header. Never use the `apiKey` query parameter.

Required POC permissions:

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
| Search metadata | <https://api.immich.app/endpoints/search/searchAssets> | Stable, `asset.read`; body supports `albumIds`, `takenAfter`, `takenBefore`, `order`, `page`, `size`, `type`, and `withExif`. Here `size` is page length, not file bytes. |
| List albums/membership | <https://api.immich.app/endpoints/albums/getAllAlbums> | Stable, `album.read`; optional `assetId` finds containing albums |
| Asset details | <https://api.immich.app/endpoints/assets/getAssetInfo> | Stable, `asset.read`; returns asset, capture-date, dimension, and EXIF information |
| Thumbnail | <https://api.immich.app/endpoints/assets/viewAsset> | Stable, `asset.view` |
| Original | <https://api.immich.app/endpoints/assets/downloadAsset> | Stable, `asset.download` |
| Upload | <https://api.immich.app/endpoints/assets/uploadAsset> | Stable, `asset.upload`; multipart requires `assetData`, `fileCreatedAt`, and `fileModifiedAt`; accepts `filename` and metadata items |
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

## Transfers

- Use `HttpCompletionOption.ResponseHeadersRead`.
- Stream with bounded buffers and cancellation.
- Do not retry large transfer bodies automatically unless the retry is known to restart from zero and cleans the partial file.
- An interrupted POC transfer restarts from zero.
- Validate expected content length when present and probe the completed file.
- Thumbnail proxy applies bounded size, content-type allowlist, short cache headers, and cancellation.

## Publication idempotency

Persist an upload-attempt identifier and returned asset ID as soon as available. Before retrying an uncertain upload, use Immich's supported bulk-upload/checksum facilities if the connected version contract is verified. Never blindly upload repeatedly after an ambiguous timeout.

Publication back to Immich preserves:

- `_V` suffixed MP4 filename;
- `fileCreatedAt` from authoritative source capture time;
- `fileModifiedAt` from the source snapshot or output semantics;
- description and coordinates when stable supported operations permit;
- membership in the same album IDs because source and destination instance are identical.

Description and coordinate preservation is an unresolved Prompt 12 contract gate. The v3 upload endpoint accepts generic metadata items, but its published schema does not define keys that prove these fields will populate Immich's description/location model. Prompt 12 must verify this against an actual supported 3.1.x server or its version-matched generated OpenAPI/client tests. If no stable mechanism is proven, publication must warn and remain incomplete against the product preservation criterion; deprecated `PUT /assets` and `PUT /assets/{id}` are not an allowed fallback.

An album-add failure retains the new asset and transitions to `PartiallyPublished`. Retry only missing album operations.
