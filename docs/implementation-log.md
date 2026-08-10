# Implementation log

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
