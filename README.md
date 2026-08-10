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

No production code has been implemented yet.
