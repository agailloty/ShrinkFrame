# ShrinkFrame

ShrinkFrame is a self-hosted Blazor application that streams browser or Immich videos to a server,
compresses them to validated H.264 MP4 with FFmpeg, and retains downloadable results. It is a proof of
concept for one trusted operator on a LAN or Tailscale network.

> ShrinkFrame has no authentication. Do not expose it directly to the public Internet.

## Run with Docker Compose

Requirements: Docker Engine with Compose v2, a Linux container host, and enough persistent storage (the
initial sizing assumption is 4 CPU cores, 8 GB RAM, and about 70 GB free).

```bash
docker compose up --build -d
docker compose ps
curl --fail http://localhost:5080/health/ready
```

Open `http://localhost:5080`. Compose creates the external-name Docker volume `shrinkframe-data`; do not
use `docker compose down --volumes` unless permanent data loss is intended. Set deployment-specific values
before starting, for example:

```bash
SHRINKFRAME_HTTP_PORT=5080 \
SHRINKFRAME_ALLOWED_HOSTS='shrinkframe.example.lan;192.0.2.10' \
SHRINKFRAME_ORIGIN='http://shrinkframe.example.lan:5080' \
docker compose up --build -d
```

The image runs as UID/GID `1654:1654`. A fresh named volume is initialized with the correct ownership. For
a bind mount, create an empty host directory, make it owned by `1654:1654`, and replace the Compose volume
mapping with `/absolute/host/path:/data`. Never put Immich API keys in Compose; add connections through the
UI so keys are encrypted with the persisted Data Protection key ring.

See [deployment and operations](docs/10-deployment.md) for configuration, backup/restore, upgrade, rollback,
disk pressure, log, shutdown, and reverse-proxy guidance. Release evidence and remaining blockers are in
[POC release evidence](docs/11-poc-release-evidence.md).

## Develop and verify

The repository pins stable .NET SDK `10.0.102` and targets `net10.0`.

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

The POC is CPU-only, single-node, and uses SQLite plus local artifacts. It does not replace or delete original
Immich assets. There is no GPU encoding, resumable transfer, arbitrary FFmpeg arguments, multi-instance
transfer, application authentication, or multi-node execution.

Licensed under the [MIT License](LICENSE).
