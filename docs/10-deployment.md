# Deployment and operations

## Container topology

The POC Docker Compose defines one ShrinkFrame service, one persistent named volume mounted at `/data`, HTTP exposure, restart policy, and health check. No reverse proxy is bundled.

The final runtime image:

- uses a pinned ASP.NET Core 10 runtime base;
- includes pinned ffmpeg/ffprobe versions;
- runs non-root;
- supports a documented host UID/GID volume-permission strategy;
- writes only to `/data` and necessary temporary paths;
- exposes a health endpoint;
- uses an init or correct process behavior so FFmpeg children receive termination.

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

- Liveness: process responds and database loop is not deadlocked.
- Readiness: database reachable/migrated, work path writable, ffmpeg and ffprobe executable.
- Low disk is degraded readiness/detail, not necessarily liveness failure.
- Immich connection outages do not make ShrinkFrame itself unhealthy.

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
