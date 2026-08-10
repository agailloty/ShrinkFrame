# Implementation log

## 2026-08-10 — Prompt 13: dashboard, history and storage

Summary:

- Replaced the operational placeholders with an empty/offline-safe dashboard, searchable batch history,
  batch/job detail, and storage inventory pages. Status and progress are always rendered as text rather
  than conveyed by color alone.
- Added Application-owned operational query/deletion contracts and an Infrastructure implementation for
  recent work, queue and connection health, source/output reduction accounting, findings, the latest 50
  bounded log entries, artifact ownership, ages, retry eligibility, and disk capacity.
- Added full work-root inventory for reconciliation. Durable job ownership is compared with exact
  server-generated source, output, probe, and log final/partial keys; unreferenced files are reported as
  orphans and never deleted automatically.
- Added explicitly confirmed job deletion. Active and publishing jobs are rejected; known artifacts are
  deleted individually through `IWorkStorage`; deletion stops on the first filesystem error and retains
  history with a stable retryable diagnostic. Database history is removed only after all file operations
  succeed.

Decision deviations:

- None.

Verification performed:

- `dotnet build ShrinkFrame.sln --configuration Release --no-restore` — completed with zero warnings and
  zero errors.
- `dotnet test ShrinkFrame.sln --configuration Release --no-build --no-restore` — passed: 205 tests,
  0 failed, 0 skipped (167 Domain and 38 Infrastructure).
- Added integration coverage for active and referenced-artifact deletion rejection, missing-confirmation
  rejection, successful exact artifact/history deletion while preserving another job, and orphan accounting
  without deletion.
- `git diff --check` — completed with no whitespace errors.

## 2026-08-10 — Prompt 10: durable worker orchestration

Summary:

- Added an isolated `BackgroundService` that polls active batches from SQLite, atomically claims work,
  downloads all Immich originals with bounded concurrency before opening the compression phase, and runs
  configurable compression concurrency (one by default).
- Added a streaming authenticated Immich-original source adapter, immediate capacity rechecks, ffprobe and
  FFmpeg composition, output probing/basic validation, atomic output finalization, and durable terminal state
  transitions. External HTTP, filesystem, probe, and process work occurs outside database transactions.
- Added application-shutdown plus durable job/batch cancellation. Active FFmpeg cancellation uses the existing
  process-tree kill and partial-output cleanup; acquired sources remain available for explicit retry.
- Added guarded acquisition and compression claims, startup recovery compatibility, explicit durable retry,
  persisted throttled progress, smoother singleton in-memory progress notifications, batch aggregates, and
  bounded 100-entry per-job logs. Added a Blazor processing view that restores persisted state/progress/logs
  after reconnect and exposes job/batch cancellation and retry.
- Added the `AddWorkerOrchestration` migration for cancellation requests and bounded job-log storage, plus
  SQLite integration coverage for exclusive acquisition claims, reconnect readback, cancellation, and retry.

Decision deviations:

- None. Acquisition claims use the existing guarded `Acquiring -> Probing` active-state transition as the
  durable ownership marker; probing follows the streamed download inside the same claimed operation.

Verification performed:

- `dotnet build ShrinkFrame.sln --configuration Release --no-restore` — completed with zero warnings/errors.
- `dotnet test ShrinkFrame.sln --configuration Release --no-restore` — passed 164 Domain and 35 Infrastructure tests.
- `git diff --check` — completed with no whitespace errors.
- Fresh Development startup applied `AddWorkerOrchestration`, initialized recovery/storage/media/worker services,
  and returned HTTP 200 from `/health`; the smoke process was then stopped.

Manual follow-up:

- A real Immich 3.1 server is required to exercise multiple original downloads, one failed asset, and retry.
- Repeat forced process termination/restart checks during acquisition, probing, compression, and validation on
  the deployment host; startup recovery and cancellation process-tree cleanup have automated component coverage.

## 2026-08-10 — Prompt 08: Immich video browser

Summary:

- Added typed application operations and handwritten internal Immich DTO mapping for 50-item video-only
  metadata search, albums, asset details, and thumbnails. Searches explicitly send `type=VIDEO`,
  `withDeleted=false`, and `withExif=true`, and defensively discard trashed or non-video response items.
- Added global taken-period/album filters and supported capture-time ascending/descending sorts. Original
  byte size is not promised by the v3.1 metadata asset DTO, so the UI honestly omits global size filtering
  and sorting; the application model supports only a known-size, current-page refinement.
- Added a Bootstrap gallery, explicit previous/next paging, proxied lazy thumbnails, details panel without
  playback, Select current page, and Clear selection. SQLite stores source-stable asset IDs keyed by
  connection, preserving selection through paging, filters, refresh, and Blazor reconnect without a
  cross-connection selection path or Select All Results operation.
- Added a same-origin thumbnail endpoint with no credential-bearing inputs. The server authenticates to
  Immich using the decrypted saved key in the `x-api-key` header, validates an image content-type allowlist,
  caps thumbnails at 5 MiB, copies with a 64 KiB buffer, propagates cancellation, and returns a short
  browser cache policy.
- Disabled/deleted/unvalidated or version-mismatched connections, rejected request contracts, missing
  assets, key failures, timeouts, and upstream errors return stable actionable codes without key material.

Decision deviations:

- None. No live Immich instance was available; the connected 3.1.x contract probe remains a deployment
  check, while the official current-v3 contract was re-audited on 2026-08-10.

Verification performed:

- `dotnet build ShrinkFrame.sln --configuration Release --no-restore` — completed with zero warnings/errors.
- `dotnet test ShrinkFrame.sln --configuration Release --no-build --no-restore` — completed successfully.
- `git diff --check` — completed without whitespace errors.

Manual follow-up:

- Against the deployment's compatible Immich 3.1.x server, browse at least two 50-item pages, combine
  date and album filters, select on both pages, refresh/reconnect, inspect that browser requests contain
  only ShrinkFrame thumbnail URLs, and disable/delete the connection mid-flow. Record the exact patch and
  any generated OpenAPI differences.

## 2026-08-10 — Prompt 07: encrypted Immich connection management

Summary:

- Added multi-instance connection add/edit/test/enable/disable/delete/default workflows and a complete
  Interactive Server Connections UI. Saved keys are replace-only and connection views expose only a
  `HasApiKey` flag plus non-secret test metadata.
- Protected API keys with a purpose-scoped ASP.NET Core Data Protector backed by the configured persisted
  key ring. Temporary UTF-8 buffers are zeroed, decrypted values stay inside the probe call, and key-ring
  loss returns `connection.api_key.unavailable` with recovery guidance.
- Added strict site-root or `/api` URL normalization, HTTP(S)-only and no-credential validation, bounded
  timeout/response policies, disabled automatic redirects, same-origin redirect enforcement, and an
  explicit per-connection invalid-certificate override with persistent UI warnings.
- Added handwritten ping, server-version, and current-key operations; v3.1 compatibility evaluation;
  API-key identity and permission persistence; core source, optional source-feature, and publication
  permission classification.
- Added a migration for non-secret connection test details and guarded deletion when a nonterminal Immich
  batch still references the connection, returning guidance to disable it instead.

Decision deviations:

- None. A live Immich instance was not available, so no deployed patch version is claimed; the exact
  official v3.1 contracts implemented are recorded in `docs/04-immich-integration.md` and live-instance
  scenarios remain a deployment-host check.

Official contracts verified 2026-08-10:

- Immich stable v3.1 ping, version, and current API key pages already inventoried in
  `docs/04-immich-integration.md`; implemented paths and response fields match that audit exactly.

Verification performed:

- `dotnet restore ShrinkFrame.sln` — completed successfully.
- `dotnet build ShrinkFrame.sln --configuration Release --no-restore` — completed with zero warnings/errors.
- `dotnet test ShrinkFrame.sln --configuration Release --no-build --no-restore` — passed 163 Domain and
  32 Infrastructure tests.
- Automated connection checks cover accepted/malformed/credential-bearing URLs, encrypted-envelope and
  response redaction, persisted-key-ring restart, changed-key-ring recovery error, permission capability
  classification, and active-work deletion rejection.

Manual follow-up:

- Exercise valid, invalid-key, missing-permission, unreachable, invalid-certificate opt-in, and restart
  scenarios against the deployment's supported Immich 3.1.x instance. Record its exact patch version and
  generated OpenAPI differences, if any, before treating that instance as production-ready.

## 2026-08-10 — Prompt 06: browser streaming upload

Summary:

- Added raw-body ASP.NET Core endpoints for persistent browser batches and one independently streamed
  request per file, with a configurable 20 GiB default limit, a pooled bounded buffer, byte counting,
  SHA-256 tracking, create-new partial writes, and atomic finalization.
- Integrated ffprobe after upload finalization. Playable media records its metadata and opaque source
  artifact; invalid media, oversized bodies, connection aborts, and acquisition failures retain a
  stable error job while removing partial and finalized source bytes.
- Added repository batch-job listing and a server-only artifact path resolver so refresh restoration and
  process invocation do not expose physical paths through browser DTOs.
- Replaced the New Batch placeholder with an accessible drag/drop and multi-picker UI. JavaScript sends
  `File` objects directly to HTTP, reports per-file progress to Blazor, supports remove/retry, restarts
  retries from zero, and restores the current session batch from SQLite after refresh/reconnect.
- Protected batch creation and upload with ASP.NET Core antiforgery endpoint metadata plus explicit
  configured Origin and matching Host validation. Filenames remain display metadata and never select a
  storage path.

Decision deviations:

- None. Completed browser acquisitions remain in `Probing` until the later wizard milestone performs
  the documented confirmation/queue transition; no compression worker is started by this milestone.

Official behavior verified 2026-08-10:

- ASP.NET Core 10 antiforgery middleware validates POST endpoints carrying `IAntiforgeryMetadata` with
  `RequiresValidation=true`: <https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0>
- Kestrel defaults request bodies to approximately 28.6 MiB and permits a pre-read per-request override
  through `IHttpMaxRequestBodySizeFeature`: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/security-considerations?view=aspnetcore-10.0>

Verification performed:

- `dotnet build ShrinkFrame.sln --configuration Release --no-restore` — completed with zero warnings/errors.
- `dotnet test ShrinkFrame.sln --configuration Release --no-restore` — passed 163 Domain and 20 Infrastructure tests.
- Valid 10,995-byte H.264 MP4 named `video & [x].mp4` — probed successfully, returned its byte count and
  SHA-256, persisted as a separate job, and left exactly one finalized artifact.
- Invalid text upload — returned HTTP 422, retained `upload.not_video`, preserved the metacharacter display
  name, and left no artifact.
- Configured 1,024-byte limit with the valid 10,995-byte MP4 — returned HTTP 413, retained
  `upload.file_too_large`, and left no artifact.
- Foreign `Origin` with a valid antiforgery token — returned HTTP 403 and created no batch.

Manual follow-up:

- Browser UI drag/drop, live progress, deliberate mid-transfer abort, reconnect rendering, and bounded
  process-memory observation require an interactive browser and should be repeated on the deployment host.

## 2026-08-10 — Prompt 04: work storage and capacity

Summary:

- Added Application storage contracts for server-generated allocation, create-new/open, bounded copy,
  atomic finalize, ownership-scoped deletion and inventory, path-free inventory DTOs, capacity reporting,
  and structured admission reasons.
- Implemented canonical-root local storage with strict key validation, partial/final distinction,
  cancellation cleanup, byte counts, symlink/reparse-point rejection, and non-recursive known-artifact deletion.
- Added configurable capacity reporting using `source * 2.2 + reserve`, an injectable reporter seam,
  non-forceable arithmetic-overflow decisions, and a durable batch capacity-admission override with migration.
- Added startup work-root creation/writability validation and Development-local storage configuration.
- Documented repeatable manual safety checks; no Storage UI was implemented.

Decision deviations:

- None. Deletion intentionally removes known files individually and stops at the first failure, avoiding
  recursive directory deletion entirely.

Verification performed:

- `dotnet build ShrinkFrame.sln --configuration Release --no-restore` — completed with zero warnings/errors.
- `dotnet test ShrinkFrame.sln --configuration Release --no-restore` — automated storage, capacity,
  persistence, and domain coverage passed.

Add newest entries at the top. Each entry must include date, prompt number, summary, verification commands, and any deviation from `decisions.md`.

## 2026-08-10 — Prompt 03: SQLite persistence and durable queue

Summary:

- Added EF-free Application ports for connection, batch, job, progress, publication-attempt, initialization, and startup-recovery persistence.
- Added EF Core SQLite entities, explicit mappings, committed initial migration, UTC-tick timestamps, string enum storage, metadata/audio/album/finding/progress/publication-attempt tables, queue/history/source indexes, and opaque artifact-key columns.
- Added invariant-checking internal domain rehydration used only by Infrastructure so repository reads cannot construct invalid successful or publication states.
- Added application-managed optimistic versions, stale-write detection, and an atomic `Queued` plus expected-version guarded update that claims a job by moving it to `Compressing`.
- Added startup migration/WAL/busy-timeout/foreign-key initialization and idempotent recovery of acquisition, probing, compression, validation, and publication work. The one-process SQLite assumption and short-transaction boundary are documented.
- Kept API-key plaintext out of the model. The only secret persistence field is an opaque encrypted byte envelope; encryption/decryption remains Prompt 07.
- Added real-file SQLite integration tests for migrations/schema safety, repository round trips, optimistic concurrency, exclusive claim, WAL, and two recovery passes.

Decision deviations:

- None. EF Core `10.0.2` is used with `SQLitePCLRaw.bundle_e_sqlite3` `3.0.5` explicitly selected because EF's default native bundle resolved a version covered by high-severity advisory `GHSA-2m69-gcr7-jv3q`.

Official behavior verified 2026-08-10:

- EF Core SQLite provider limitations and migration locking: <https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations>
- EF Core runtime migration guidance: <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying>
- EF Core guarded `ExecuteUpdate` concurrency pattern: <https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete>
- EF Core application-managed concurrency tokens for SQLite: <https://learn.microsoft.com/en-us/ef/core/saving/concurrency>

Verification performed:

- `dotnet restore ShrinkFrame.sln` — completed successfully after approved NuGet access; no vulnerability warnings remain.
- `dotnet build ShrinkFrame.sln --configuration Release --no-restore` — completed with zero warnings and zero errors.
- `dotnet test ShrinkFrame.sln --configuration Release --no-build` — passed: 161 domain tests and 4 SQLite integration tests.
- `dotnet list ShrinkFrame.sln package --vulnerable --include-transitive --no-restore` — no vulnerable packages reported in any project.
- Fresh temporary database migration/schema inspection — migration created all expected tables and indexes; WAL and foreign-key checks passed; no video-byte or absolute-path columns exist.
- Two consecutive recovery passes — first pass interrupted the active integration-test job; second pass changed zero rows, proving idempotence.
- Two consecutive real Web startups against the same temporary database — both `/health` requests returned `Healthy`; the second startup reported the schema current and recovered zero jobs.

## 2026-08-10 — Prompt 02: domain model and tests

Summary:

- Implemented a persistence-ignorant domain containing typed connection, batch, job, and preset identifiers; connection metadata; batch and compression-job aggregates; source, media, progress, artifact, finding, publication, and option value objects; and stable machine-readable domain error codes.
- Added explicit guarded job and publication operations, including probed-input queue guards, validation-only successful completion, restart interruption and retry paths, published-asset-before-album-completion ordering, partial-publication retry, and an explicit persisted override before publishing a `NotBeneficial` result.
- Added seven immutable built-in presets and per-batch/per-job effective option copies so later preset changes cannot alter existing snapshots.
- Added pure validation and policy rules for CRF 18–36 with warnings above 30, safe suffixes and output filenames, maximum-resolution scaling on the long display dimension, even dimensions without upscaling, MP4 audio compatibility selection, duration tolerance, output benefit classification, blocking versus warning findings, and forced capacity admission.
- Replaced the placeholder test with exhaustive allowed/rejected job-transition matrix coverage and boundary tests for all mandatory Prompt 02 behaviors.

Decision deviations:

- None. Maximum-resolution labels are interpreted exactly as documented: the enum value caps the long display dimension, including portrait inputs.

Verification performed:

- `dotnet restore ShrinkFrame.sln` — completed successfully after approved NuGet network access.
- `dotnet test tests/ShrinkFrame.Domain.Tests/ShrinkFrame.Domain.Tests.csproj --configuration Release --no-restore` — passed: 161 tests, 0 failed, 0 skipped.
- `dotnet build ShrinkFrame.sln --configuration Release --no-restore` — completed with zero warnings and zero errors.

## 2026-08-10 — Prompt 01: solution bootstrap

Summary:

- Added a stable SDK policy selecting .NET SDK `10.0.102` with latest-patch roll-forward and prerelease disabled.
- Created the Domain, Application, Infrastructure, Web, and Domain.Tests projects and enforced the documented modular-monolith project-reference graph. Domain has no project dependencies; Domain.Tests references only Domain.
- Added repository-wide nullable reference types, implicit usings, deterministic builds, recommended analyzers, enforced code style, and warnings-as-errors. Central package management pins the MSTest SDK metapackage.
- Configured a Blazor Web App with global Interactive Server rendering, Bootstrap placeholder navigation, English request/localization infrastructure, JSON console logging, a persisted configurable Data Protection key ring, validated storage/worker options, and an HTTP health endpoint.
- Added placeholder pages for Dashboard, New Batch, Batches, Connections, Storage, and Settings/About. The UI and README state that the unauthenticated POC is restricted to trusted LAN/Tailscale use.
- Updated root development commands. No media, Immich, persistence, filesystem adapter, or business-domain features were implemented.

Decision deviations:

- None.

Verification performed:

- `dotnet --version` — completed with stable SDK `10.0.102` selected by `global.json`.
- `dotnet restore ShrinkFrame.sln` — completed successfully; NuGet access required an approved network-enabled retry after the sandbox blocked `api.nuget.org`.
- `dotnet build ShrinkFrame.sln --configuration Release --no-restore` — completed with zero warnings and zero errors.
- `dotnet test ShrinkFrame.sln --configuration Release --no-build` — passed: 1 test, 0 failed, 0 skipped.
- Project-reference inspection — confirmed Domain has none; Application references Domain; Infrastructure references Application and Domain; Web references all three; Domain.Tests references only Domain.
- Domain forbidden-type scan for EF, `HttpClient`, process, filesystem, ASP.NET Core, and Blazor namespaces — no matches.
- Local startup smoke check using `http://127.0.0.1:5080` with a workspace-local Data Protection key-ring override — `/health` returned `Healthy`; `/` returned HTTP 200 and contained Dashboard, New Batch, and Settings/About navigation.
- `git diff --check` — completed with no whitespace errors.
- Secret-pattern scan — no API keys, connection strings, passwords, or secrets found in added source/configuration files.

## 2026-08-10 — Prompt 00: foundation audit

Summary:

- Confirmed that the repository contains documentation and an empty solution only; no product source code, project files, global SDK pin, container files, or unrelated working-tree changes existed before this audit.
- Stable .NET SDK `10.0.102` is installed, but without `global.json` the CLI selects `10.0.400-preview.0.26322.102`. Prompt 01 must create a stable pin compatible with the installed stable feature band (initially `10.0.102`, with an appropriate roll-forward policy) before generating source. The preview SDK is not accepted for generation.
- Docker CLI `29.6.1` and Docker Compose `v5.3.0` are installed. Docker Engine was unavailable at `npipe:////./pipe/docker_engine`; its daemon state was not changed. Image pulls, digest resolution, Compose configuration, and container/FFmpeg execution remain blocked until the user starts/provides an Engine.
- Verified current .NET 10 official documentation for Blazor global Interactive Server rendering, unbuffered multipart upload streaming, hosted `BackgroundService` cancellation/scoped-service behavior, and persisted Data Protection keys in Docker. The documented architecture remains executable.
- Audited the official Immich v3 endpoint pages and recorded exact URLs, DTO facts, permissions, status, and access date in `04-immich-integration.md`. All planned inventory endpoints are stable. The old asset update endpoints are deprecated. Description/location copying through a stable v3.1 mechanism remains a Prompt 12 live-server contract gate.
- Corrected the Immich byte-size filtering promise: metadata search's `size` is page length, not bytes, and no server-global byte-size filter is documented.
- Corrected the container policy for .NET 10's Ubuntu Noble-only official images, set exact audited SDK/runtime tags and FFmpeg package version, and deferred digest capture until an Engine/registry inspection is available.

Official Microsoft references accessed 2026-08-10:

- Blazor render modes: <https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0>
- Streaming uploads: <https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0>
- Hosted services: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0>
- Data Protection configuration and Docker key persistence: <https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0>

Decision deviations:

- Replaced the planned configurable container UID/GID strategy with the official non-root `app` user (UID 1654) plus documented host volume ownership. This avoids a privileged startup entrypoint and preserves the non-root invariant.
- Narrowed the POC Immich size-filter capability to a clearly labeled loaded-result refinement because Immich 3.1 metadata search has no byte-size predicate. This prevents misleading global filtering.

Verification performed:

- `dotnet --info` — completed; default selected SDK is preview, stable `10.0.102` is also installed.
- `docker version` — client reported `29.6.1`; server connection failed because the Engine is not running/available.
- `docker compose version` — completed: `Docker Compose version v5.3.0`.
- `docker info --format '{{json .ServerVersion}}'` — failed to connect to the Docker Engine; no daemon state was changed.
- `git diff --check` — completed with no whitespace errors after documentation edits.
- `git status --short` — only the six intended documentation files were modified: `docs/00-product-brief.md`, `docs/04-immich-integration.md`, `docs/08-user-experience.md`, `docs/10-deployment.md`, `docs/decisions.md`, and `docs/implementation-log.md`.

Unresolved blockers:

- Docker Engine availability is required for image digest resolution and all container verification.
- Prompt 12 requires a real supported Immich 3.1.x test server (or its version-matched generated contract) to prove stable description/location preservation.
## 2026-08-10 — Prompt 05: ffprobe and FFmpeg infrastructure

Summary:

- Added Application-owned probing, compression, process-result, structured-progress, stream metadata, and startup-status contracts.
- Added shell-free ffprobe JSON probing with bounded diagnostics, cancellable process-tree termination, default-stream selection, QuickTime capture-date and ISO 6709 location mapping, stream/disposition details, and display-matrix/tag rotation normalization.
- Added a typed-only FFmpeg argument builder using `ProcessStartInfo.ArgumentList`, deliberate video/audio/global metadata/chapter mapping, `libx264`, `yuv420p`, `+faststart`, machine progress, configurable AAC bitrate/thread count, compatible-audio copy, and AAC fallback.
- Applied the existing long-display-edge scaling policy with even dimensions, portrait/rotation handling, and no upscaling. PQ and HLG inputs are explicitly rejected because the POC has no validated HDR preservation or tone-mapping policy.
- Added bounded concurrent stdout/stderr readers, exit-code/output checks, process-tree cancellation, awaited exit/readers, and mandatory removal of failed or cancelled `.partial` output. Finalization remains a separate storage/validation operation.
- Added startup version validation and media-tool health details, plus synthetic-fixture manual commands and automated cancellation coverage.

Decision deviations:

- None. Resolution labels continue to cap the long display dimension exactly as established in Prompt 02; for example, a 1920×1080 input under the 720 setting becomes 720×404.

Verification performed:

- `ffmpeg -version` and `ffprobe -version` — both reported `N-117403-g496b8d7a13-20241007`; FFmpeg includes `libx264`.
- Synthetic probe — a generated 640×360 MOV with PCM audio, creation time, and filename `fixture input & safe [x].mov` mapped as H.264 video plus PCM audio without shell interpretation.
- Synthetic compression — completed with exit code 0; structured progress reported `out_time_us=2933333`, `speed=11.2x`, and `total_size=190353`; final probe reported H.264, `yuv420p`, 480×270, 3.000 seconds, and 190353 bytes. FFmpeg reported moving the `moov` atom to the file beginning for faststart.
- Cancellation test — cancelled a `veryslow` encode, awaited process termination, and confirmed no `.partial.mp4` remained.
- `dotnet build ShrinkFrame.sln --configuration Release --no-restore` — completed with zero warnings and zero errors.
- `dotnet test ShrinkFrame.sln --configuration Release --no-build` — passed: 181 tests, 0 failed, 0 skipped (162 Domain and 19 Infrastructure).
- `git diff --check` — completed with no whitespace errors.
## 2026-08-10 — Prompt 09: persistent batch wizard and settings

Summary:

- Added a shared application batch-wizard use case for browser and Immich sources, including editable generated names, immutable built-in preset catalog exposure, draft settings, per-video built-in overrides, persisted summaries, capacity admission, and explicit confirmation.
- Added aggregate guards that prevent wizard edits after confirmation and resolve each job's effective option snapshot before its state leaves the editable phase.
- Completed the Source → Selection → Compression → Summary Blazor flow. Browser upload state reconnects to the persisted batch; Immich selection is converted to application-owned selection records rather than exposing infrastructure DTOs to the page.
- Added accessible CRF validation/warning, typed x264 speed, maximum resolution, audio and suffix controls. No arbitrary FFmpeg argument field exists.
- Confirmation moves probed browser jobs to `Queued` and Immich jobs to `Acquiring`; no job is queued or acquired from wizard actions before explicit confirmation.

Decision deviations:

- None. Immich source byte sizes remain unknown when Immich does not return them, and the summary labels those values as unknown rather than inventing an estimate.
## 2026-08-10 — Prompt 11: validation and result delivery

Summary:

- Added a domain output-validation policy for MP4/H.264, the greater-of-one-second-or-0.5-percent duration tolerance, positive/even/no-upscale dimensions within the selected maximum, authoritative capture date, effective rotation, and warning-only location/audio metadata loss.
- Persisted input and output ffprobe JSON snapshots as bounded work-storage artifacts. Validation failures retain their exact blocking findings, remove the partial video, and never finalize it; warning-only valid outputs finalize atomically and classify by byte size as `Ready` or retained `NotBeneficial`.
- Added application result delivery with safe generated MP4 download names, artifact ownership/existence checks, persistent explicit `NotBeneficial` publication authorization, and recompression as a distinct queued job/options snapshot that reuses the retained source without altering the prior output or history.
- Added an individual physical-file HTTP endpoint with `video/mp4`, framework-generated content length and Content-Disposition, and range processing. Blazor renders links and controls only; it never carries video bytes. No ZIP or bulk download was added.
- Expanded processing/results UI with persisted progress refresh, findings, individual downloads, recompression, explicit later-publication force, and expandable FPS/bitrate/bounded-log details.

Decision deviations:

- None. Publication transport remains Prompt 12; this milestone only persists the explicit force prerequisite for a later `NotBeneficial` publication.

Framework contract verification:

- Verified on 2026-08-10 against Microsoft Learn for ASP.NET Core 10 (`Microsoft.AspNetCore.App.Ref v10.0.0`) that `Results.File(string path, ..., fileDownloadName, ..., enableRangeProcessing: true)` writes a physical file, uses the supplied download name for Content-Disposition, and supports satisfiable/unsatisfiable ranges with HTTP 206/416: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.results.file?view=aspnetcore-10.0>.

Verification performed:

- Added automated domain coverage for the exact duration boundary, portrait maximum dimensions, no-upscale rejection, MP4/H.264 enforcement, warning-only metadata loss, blocking capture-date/rotation loss, and equal/larger `NotBeneficial` classification.
- Added persistence/application coverage proving recompression creates a distinct queued job and option snapshot, retains the original result artifact, and reuses the retained source artifact.
- `dotnet build ShrinkFrame.sln --configuration Release --no-restore` — completed with zero warnings and zero errors.
- `dotnet test ShrinkFrame.sln --configuration Release --no-build` — passed: 203 tests, 0 failed, 0 skipped (167 Domain and 36 Infrastructure).
- `git diff --check` — completed with no whitespace errors before this log entry.
