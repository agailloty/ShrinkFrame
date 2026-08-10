# Deployment and operations

## Container topology

> **No authentication / LAN only:** anyone who can reach ShrinkFrame can upload data, consume CPU and
> disk, and publish through every configured Immich connection. Bind and firewall it to a trusted LAN or
> Tailscale network. Direct public Internet exposure is unsupported.

The POC Docker Compose defines one ShrinkFrame service, one persistent named volume mounted at `/data`, HTTP exposure, restart policy, and health check. No reverse proxy is bundled.

The final runtime image:

- uses a pinned ASP.NET Core 10 runtime base;
- includes pinned ffmpeg/ffprobe versions;
- runs non-root;
- supports a documented host UID/GID volume-permission strategy;
- writes only to `/data` and necessary temporary paths;
- exposes a health endpoint;
- uses an init or correct process behavior so FFmpeg children receive termination.

## Audited image and FFmpeg policy

Audit date: 2026-08-10.

.NET 10 official Linux container tags use Ubuntu 24.04 (`noble`); Microsoft explicitly does not publish Debian images for .NET 10. Use matching Ubuntu-based stages, not the unqualified moving tags:

- build stage: `mcr.microsoft.com/dotnet/sdk:10.0.302-noble`;
- runtime stage: `mcr.microsoft.com/dotnet/aspnet:10.0.10-noble`;
- record and pin each multi-architecture manifest digest in the Dockerfile at Prompt 15, after Docker Engine is available to resolve the registry manifests;
- update SDK/runtime servicing versions together through a reviewed dependency update, rerun the full build/media smoke tests, and never select a preview tag.

The Noble archive provides `ffmpeg` and `ffprobe` together in package `ffmpeg` version `7:6.1.1-3ubuntu5`, including libx264 support through its packaged dependencies. Prompt 15 must install that exact Debian package version (`apt-get install ffmpeg=7:6.1.1-3ubuntu5`), verify `ffmpeg -version`, `ffprobe -version`, and the presence of encoder `libx264`, then clean apt lists. If that exact version is no longer resolvable, do not silently float: update this policy and the implementation log to a reviewed Noble security/update version and repeat media compatibility tests.

The official non-chiseled Noble runtime is required because FFmpeg is installed with apt. Run the finished application as the image's non-root `app` user (UID 1654). The POC host must pre-create/chown the mounted data directory for UID 1654; arbitrary runtime UID/GID remapping is deferred because it conflicts with a strictly non-root startup unless an entrypoint briefly runs privileged.

Primary references accessed 2026-08-10: <https://github.com/dotnet/dotnet-docker/issues/6860>, <https://github.com/dotnet/dotnet-docker/blob/main/README.sdk.md>, <https://github.com/dotnet/dotnet-docker/blob/main/README.aspnet.md>, and <https://packages.ubuntu.com/noble/ffmpeg>.

## Configuration

Configuration uses environment variables with validated options. At minimum:

```text
ShrinkFrame__DataPath=/data
ShrinkFrame__DatabasePath=/data/shrinkframe.db
ShrinkFrame__WorkPath=/data/work
ShrinkFrame__MaxInputBytes=21474836480
ShrinkFrame__CompressionConcurrency=1
ShrinkFrame__AcquisitionConcurrency=2
ShrinkFrame__ShutdownTimeoutSeconds=30
ShrinkFrame__SystemReserveBytes=10737418240
ShrinkFrame__AllowedHosts=...
```

Do not configure Immich API keys through ordinary checked-in Compose values. Connections are added in the UI and encrypted using the persisted Data Protection key ring.

## Health

- `/health/live`: process liveness only.
- `/health/ready`: database reachable/migrated, work path writable, ffmpeg and ffprobe executable.
- `/health/details`: the same readiness decision with component details and disk byte counts.
- Low disk is degraded readiness/detail, not necessarily liveness failure.
- Immich connection outages do not make ShrinkFrame itself unhealthy.

`AllowedHosts` must list the deployment hostnames/IP addresses. `BrowserUploads:AllowedOrigins` must list
their complete HTTP(S) origins, including non-default ports. The checked-in defaults accept loopback only.

## Shutdown

Stop accepting new work, signal running media processes, wait up to the configured 30 seconds, kill remaining process trees, clean partial outputs when safe, and persist active work as Interrupted. Never claim graceful completion when the process was killed.

## Reverse proxy guidance

The POC is HTTP-only. Documentation must show generic requirements for a future proxy: WebSockets, large request bodies, long upload/download timeouts, response streaming, forwarded headers, and HTTPS. Do not ship insecure universal proxy snippets without explaining their limits.

## CI

GitHub Actions on pull request/push:

1. restore with locked dependencies when lock files are adopted;
2. build Release;
3. run domain tests;
4. build Docker image;
5. validate Compose configuration;
6. never require Immich secrets.
