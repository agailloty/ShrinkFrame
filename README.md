# ShrinkFrame

ShrinkFrame is a self-hosted Blazor application for acquiring large videos, compressing them with FFmpeg, and publishing the results. The proof of concept supports browser uploads and Immich as input sources, local downloads and Immich as destinations.

The repository currently contains the architecture and the ordered implementation prompts. Start with [AGENTS.md](AGENTS.md), then read [docs/README.md](docs/README.md). Implementation agents must execute the prompts in [prompts/README.md](prompts/README.md) in order.

## POC constraints

- .NET 10 LTS, stable SDK only.
- Blazor Web App with global Interactive Server rendering.
- Modular monolith, one ASP.NET Core process and one Docker container.
- SQLite is the source of truth; local filesystem stores video artifacts.
- One compression worker by default; vertical scaling is supported through configuration.
- FFmpeg and ffprobe are bundled in the Linux image; CPU encoding only.
- H.264 in MP4, with `faststart` always enabled.
- No ShrinkFrame authentication in the POC; LAN/Tailscale only.
- Multiple Immich connections, encrypted API keys, source-instance publication only.
- Original Immich assets are never deleted or replaced.
- MIT license.

## Development

The repository pins a stable .NET 10 SDK feature band. From the repository root:

```powershell
dotnet restore ShrinkFrame.sln
dotnet build ShrinkFrame.sln --configuration Release --no-restore
dotnet test ShrinkFrame.sln --configuration Release --no-build
$env:DataProtection__KeyRingPath = ".local/keys"
dotnet run --project src/ShrinkFrame.Web/ShrinkFrame.Web.csproj --no-build --configuration Release --urls http://localhost:5080
```

Open `http://localhost:5080` to view the placeholder shell or request `http://localhost:5080/health` for the startup smoke check.

The POC has no authentication. Run it only on a trusted LAN or Tailscale network; public exposure is unsupported.
