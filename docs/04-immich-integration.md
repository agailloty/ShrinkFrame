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

Do not use internal timeline endpoints or deprecated update endpoints. Before implementing description/location copying, verify the stable Immich 3.1 contract in official documentation or the connected server's generated client/source. If no stable operation exists, upload supported metadata and emit a clear warning rather than using deprecated behavior invisibly.

## Search behavior

- Explicit pagination, 50 results per page.
- Type must be Video.
- User filters: taken-after, taken-before, album ID, and size range.
- If `search/metadata` cannot filter byte size server-side, apply size filtering only when the returned contract contains size; otherwise use a documented follow-up or clearly label the filter as client-side within loaded results. Never fake global filtering.
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

An album-add failure retains the new asset and transitions to `PartiallyPublished`. Retry only missing album operations.
