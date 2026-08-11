# Deployment and operations

## Container topology

> **No authentication / LAN only:** anyone who can reach ShrinkFrame can upload data, consume CPU and
> disk, and publish through every configured Immich connection. Bind and firewall it to a trusted LAN or
> Tailscale network. Direct public Internet exposure is unsupported.

The version 1.0 Docker Compose defines one ShrinkFrame service, one persistent named volume mounted at `/data`, HTTP exposure, restart policy, and health check. No reverse proxy is bundled.

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

The official non-chiseled Noble runtime is required because FFmpeg is installed with apt. The finished
application runs as the image's non-root `app` user (UID/GID 1654). The image creates `/data`, `/data/keys`,
and `/data/work` with that ownership. Docker initializes a new named volume from those directory attributes.
For a bind mount, the operator must create the host directory and `chown 1654:1654` it before startup.
Arbitrary runtime UID/GID remapping is deferred because it conflicts with a strictly non-root startup unless
an entrypoint briefly runs privileged.

The Dockerfile pins the multi-architecture manifests resolved on 2026-08-10:

- SDK `10.0.302-noble`: `sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0`;
- ASP.NET `10.0.10-noble`: `sha256:f1126d438ccc359f51cc6d4701a8deae513856cf10f5fe645d29ea6403dcac6b`.

Primary references accessed 2026-08-10: <https://github.com/dotnet/dotnet-docker/issues/6860>, <https://github.com/dotnet/dotnet-docker/blob/main/README.sdk.md>, <https://github.com/dotnet/dotnet-docker/blob/main/README.aspnet.md>, and <https://packages.ubuntu.com/noble/ffmpeg>.

## Configuration

Configuration uses ASP.NET Core environment-variable names (double underscore means a configuration colon):

```text
ConnectionStrings__ShrinkFrame=Data Source=/data/shrinkframe.db;Default Timeout=5;Pooling=True
DataProtection__KeyRingPath=/data/keys
Storage__WorkRoot=/data/work
Storage__ReserveBytes=5368709120
BrowserUploads__MaximumFileSizeBytes=21474836480
BrowserUploads__AllowedOrigins__0=http://localhost:5080
Worker__CompressionConcurrency=1
Worker__AcquisitionConcurrency=2
Worker__ShutdownTimeoutSeconds=30
AllowedHosts=localhost;127.0.0.1
```

Do not configure Immich API keys through ordinary checked-in Compose values. Connections are added in the UI and encrypted using the persisted Data Protection key ring.

Compose exposes convenience variables `SHRINKFRAME_HTTP_PORT`, `SHRINKFRAME_ALLOWED_HOSTS`,
`SHRINKFRAME_ORIGIN`, `SHRINKFRAME_RESERVE_BYTES`, `SHRINKFRAME_MAX_INPUT_BYTES`,
`SHRINKFRAME_COMPRESSION_CONCURRENCY`, and `SHRINKFRAME_ACQUISITION_CONCURRENCY`. The checked-in values are
secret-free. Set host/origin values to the browser-visible address; origins include scheme and port.

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

`dotnet` is the container entrypoint and PID 1, so SIGTERM reaches the host directly. Compose allows 35 seconds,
five seconds longer than the application timeout. ShrinkFrame owns and terminates FFmpeg process trees. Use
`docker compose stop`; do not use `docker kill` for routine operation.

## Backup and restore

The named volume is the complete durable unit: SQLite database plus `-wal`/`-shm` sidecars when present,
Data Protection keys, and job artifacts. For a consistent backup, stop the service, archive the entire volume,
then restart it. Never copy only `shrinkframe.db` while the application is running and never omit `/data/keys`;
without the original keys, saved Immich credentials cannot be decrypted.

Restore only while the service is stopped, into an empty volume owned by UID/GID 1654. Restore the whole
archive together, verify ownership, start the same application version, and wait for readiness. Keep backups
access-controlled because the database contains encrypted credentials and the key ring needed to decrypt them.

## Upgrade and rollback

1. Stop ShrinkFrame and take a complete volume backup.
2. Record the current image ID and Compose file, then build/pull the reviewed pinned image.
3. Run `docker compose up -d`, wait for readiness, and inspect migration/startup logs.
4. Exercise one non-personal test asset before resuming normal use.

Database migrations are forward-only. Rollback means stopping the new container, restoring the pre-upgrade
volume backup, restoring the earlier Compose/image version, and starting it. Do not run an older binary against
a database already migrated by a newer release.

## Disk pressure and logs

`/health/details` reports available and reserve bytes. `Degraded` means the configured reserve is breached;
stop adding work, delete completed jobs through the Storage UI, or enlarge/move the volume. Do not manually
remove files below `/data/work`, because SQLite owns artifact references. If the filesystem is full, stop the
service before remediation and make a backup when space permits.

Application logs are structured JSON on stdout/stderr. Use `docker compose logs --since 1h shrinkframe` and
configure Docker daemon log rotation appropriate to the host. Job summaries are intentionally bounded. Logs
must not contain API keys; treat any accidental credential output as a rotation incident.

## Reverse proxy guidance

Version 1.0 is HTTP-only. Documentation must show generic reverse-proxy requirements: WebSockets, large request bodies, long upload/download timeouts, response streaming, forwarded headers, and HTTPS. Do not ship insecure universal proxy snippets without explaining their limits.

## CI

GitHub Actions on pull request, branch push, and tag push:

1. restore with locked dependencies when lock files are adopted;
2. build Release;
3. run domain tests;
4. build Docker image;
5. validate Compose configuration;
6. never require Immich secrets.

After the verification job succeeds for a tag push, a separate least-privilege job builds the same Dockerfile
and publishes `ghcr.io/<owner>/<repository>:<git-tag>`. The GHCR path is lowercased and `/` in the Git tag is
normalized with other unsupported characters to `-`. Publication authenticates with the automatic,
repository-scoped `GITHUB_TOKEN`; only that job receives `packages: write`. The image includes OCI source,
revision, and version labels. No mutable `latest` tag is produced implicitly.

The workflow pins third-party action commits, uses the stable SDK selected by `global.json`, validates Compose,
builds the digest-pinned image, checks UID/GID, prints FFmpeg/ffprobe versions, and verifies `libx264`. No Immich
server or credential is required.
