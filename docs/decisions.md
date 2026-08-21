# Consolidated decisions

Status: accepted for version 1.0. Change a decision only through a dated entry in `implementation-log.md` explaining the reason and impact.

## Product

- Name: ShrinkFrame.
- One user on LAN/Tailscale; no ShrinkFrame authentication.
- English UI, localization-ready.
- Dashboard home with recent work and New Batch action.
- Persistent, named batches; generated source/date name is editable.
- Linear wizard: source, selection, settings, summary, processing, publication.
- Browser upload and Immich input; local download and Immich publication.
- Multiple stored Immich instances; one instance per batch.
- Immich sources publish only to their source instance in version 1.0.
- Browser sources may publish to any enabled instance.
- Original Immich assets remain unchanged.
- Preserve name, capture date, description, location, and albums.
- Publish manually in a group; `NotBeneficial` requires force confirmation.

## Media

- User-selectable H.264 via `libx264` or H.265/HEVC via `libx265` in MP4; CPU only; `faststart` always enabled. H.265 uses the `hvc1` sample entry.
- Audio is stream-copied when MP4-compatible, otherwise transcoded to AAC.
- Global preset with a per-video alternative preset; no per-video free-form advanced values.
- Built-in immutable presets only; advanced batch values are temporary.
- CRF 18-36; warn above 30.
- Maximum resolutions Keep/2160p/1440p/1080p/720p/480p; never upscale; preserve portrait orientation.
- Duration tolerance: greater of one second or 0.5 percent.
- Capture-date or rotation loss blocks success; other metadata loss warns.

## Runtime and persistence

- Stable .NET 10 LTS; Blazor Web App, global Interactive Server.
- Bootstrap and native Blazor components.
- Modular monolith and one Docker container/process.
- SQLite behind repositories; migration path to PostgreSQL.
- Local filesystem behind `IWorkStorage`; migration path to shared/object storage.
- SQLite is queue source of truth; in-process isolated worker.
- One compression at a time by default; configurable vertical concurrency.
- All Immich sources are downloaded before batch compression begins.
- Active jobs become interrupted on restart and are manually retryable.
- Local result files persist until explicit deletion.
- Successfully published Immich source copies are removed locally.
- 20 GB configurable per-file limit; no fixed batch limit, capacity-based admission.
- Insufficient capacity warns but may be explicitly overridden.

## Security and operations

- HTTP only in version 1.0; document reverse-proxy HTTPS.
- API keys encrypted with ASP.NET Core Data Protection; persisted key ring.
- Saved keys cannot be displayed, only replaced.
- Invalid certificates rejected by default; explicit per-connection override.
- Dedicated streaming HTTP endpoints for large files.
- FFmpeg bundled from a version-pinned Ubuntu Noble package; non-root container using the official image `app` user (UID 1654), with host volume ownership documented. Arbitrary runtime UID/GID remapping is deferred.
- Structured console logs plus per-job summary.
- HTTP health check and detailed UI state.
- MIT license and GitHub Actions for build, domain tests, and Docker build.
- Automated test scope is domain unit tests; every milestone also supplies repeatable manual checks.
