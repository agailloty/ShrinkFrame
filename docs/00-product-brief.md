# Product brief

## Vision

ShrinkFrame is a self-hosted web application that makes large personal videos smaller without requiring users to understand FFmpeg. It acquires videos from a browser or Immich, processes them on the server, validates the outputs, and lets the user download or publish the new assets.

## Target user and environment

- One technically capable user on a trusted LAN or Tailscale network.
- Linux host with Docker Engine.
- Initial sizing: 4 CPU cores, 8 GB RAM, approximately 70 GB available storage.
- No public Internet exposure and no application-level authentication in the POC.

## POC capabilities

1. Manage multiple Immich connections: add, edit, test, disable, delete, and select a default.
2. Create a named batch from either browser uploads or one Immich instance.
3. Browse Immich videos with explicit pagination and filters for taken period and album, supported server sort orders, and a clearly page-scoped size refinement when byte size is available. Immich 3.1 has no server-global byte-size metadata-search filter.
4. Preserve selection when changing pages.
5. Upload multiple browser files with per-file progress; interrupted uploads restart from zero.
6. Probe all inputs with ffprobe and reject non-video content.
7. Apply a global built-in preset, with a different built-in preset selectable per video.
8. Expose advanced batch parameters: CRF, encoder speed, maximum resolution, keep-resolution option, audio mode, and filename suffix.
9. Acquire all selected Immich videos before starting compression.
10. Run one compression at a time by default and expose structured progress.
11. Validate output duration, codec, dimensions, capture date, rotation, and file size.
12. Mark a valid output that is not smaller as `NotBeneficial`.
13. Download results using HTTP range-capable endpoints.
14. Publish selected valid outputs to Immich; a `NotBeneficial` output requires explicit force confirmation.
15. Preserve name, capture date, description, location, and album membership when publishing back to the source Immich instance.
16. Keep original Immich assets unchanged.
17. Show batch history, per-job logs, storage usage, and controlled deletion.
18. Survive restarts by marking active work interrupted and allowing retry.

## Explicit exclusions

- No replacement, trashing, or deletion of original Immich assets.
- No transfer from one Immich instance to another.
- No resumable browser or Immich downloads.
- No GPU/hardware encoding.
- No HEVC or AV1 output.
- No arbitrary user-supplied FFmpeg arguments.
- No custom saved presets.
- No horizontal multi-node execution.
- No application accounts, roles, or permissions.
- No email, webhook, or browser notifications.
- No ZIP generation for multiple outputs.

## POC success criteria

On the target Linux Docker host, a user can select multiple videos from one Immich instance or upload them from a browser, compress each to validated H.264 MP4, observe progress across restarts, download results, and publish selected results to the correct Immich instance without exposing credentials or modifying original assets.
