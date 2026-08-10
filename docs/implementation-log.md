# Implementation log

Add newest entries at the top. Each entry must include date, prompt number, summary, verification commands, and any deviation from `decisions.md`.

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
