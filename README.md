# ShrinkFrame

ShrinkFrame is a self-hosted Blazor application that streams browser or Immich videos to a server,
compresses them to validated H.264 or H.265/HEVC MP4 with FFmpeg, and retains downloadable results. Version 1.0.0 is
designed for one trusted operator on a LAN or Tailscale network.

> ShrinkFrame has no authentication. Do not expose it directly to the public Internet.

## Run with Docker Compose

Requirements: Docker Engine with Compose v2, a Linux container host, and enough persistent storage (the
initial sizing assumption is 4 CPU cores, 8 GB RAM, and about 70 GB free).

```bash
docker compose pull
docker compose up -d
docker compose ps
curl --fail http://localhost:5080/health/ready
```

Open `http://localhost:5080`. Compose creates the external-name Docker volume `shrinkframe-data`; do not
use `docker compose down --volumes` unless permanent data loss is intended. Set deployment-specific values
before starting. The easiest approach is to copy the complete example and edit its hostname/origin:

```bash
cp .env.example .env
```

Alternatively, export values in the current shell:

```bash
export SHRINKFRAME_HTTP_PORT=5080
export SHRINKFRAME_ALLOWED_HOSTS='shrinkframe.example.lan;192.0.2.10'
export SHRINKFRAME_ORIGIN='http://shrinkframe.example.lan:5080'
docker compose pull
docker compose up -d
```

The image runs as UID/GID `1654:1654`. A fresh named volume is initialized with the correct ownership. For
a bind mount, create an empty host directory, make it owned by `1654:1654`, and replace the Compose volume
mapping with `/absolute/host/path:/data`. Never put Immich API keys in Compose; add connections through the
UI so keys are encrypted with the persisted Data Protection key ring.

See [deployment and operations](docs/10-deployment.md) for configuration, backup/restore, upgrade, rollback,
disk pressure, log, shutdown, and reverse-proxy guidance. Release evidence and remaining blockers are in
[version 1.0 release evidence](docs/11-version-1-release-evidence.md).

## Published container images

Pushing a Git tag runs the full CI verification and then publishes the application to GitHub Container
Registry with that tag. Repository names are normalized to lowercase for GHCR; characters that container tags
do not support are represented as `-`. For example, Git tag `v1.0.0` publishes:

```bash
docker pull ghcr.io/agailloty/shrinkframe:v1.0.0
```

The workflow uses GitHub's repository-scoped `GITHUB_TOKEN`; no registry credential needs to be configured.
New GHCR packages are private by default, so change the package visibility in GitHub if anonymous pulls are
required.

## Develop and verify

The repository pins stable .NET SDK `10.0.302` and targets `net10.0`.

```powershell
dotnet restore ShrinkFrame.sln
dotnet build ShrinkFrame.sln --configuration Release --no-restore
dotnet test ShrinkFrame.sln --configuration Release --no-build --no-restore
docker compose config --quiet
docker build --tag shrinkframe:local .
```

For a host process, provide workspace-local database, key-ring, and work-root settings rather than writing
to `/data`. Architecture and contributor rules begin in [AGENTS.md](AGENTS.md); documentation is indexed in
[docs/README.md](docs/README.md).

## Scope and license

Version 1.0 is CPU-only, single-node, and uses SQLite plus local artifacts. It does not replace or delete original
Immich assets. There is no GPU encoding, resumable transfer, arbitrary FFmpeg arguments, multi-instance
transfer, application authentication, or multi-node execution.

Licensed under the [MIT License](LICENSE).
