# Version 1.0 release evidence

Date: 2026-08-10. This is an evidence ledger, not a claim that blocked scenarios passed.

## Automated and local evidence

The implementation log contains milestone-level evidence for upload streaming, persistence, recovery, media
process cancellation, validation, range downloads, storage deletion, health, secret handling, and original-asset
safety. Prompt 15 verification results are recorded in the latest implementation-log entry.

## Success-criterion reconciliation

| Product criterion | Evidence or blocker |
|---|---|
| Select/upload multiple videos | Browser streaming and persisted selection have automated/component evidence. Browser acceptance with the external media corpus remains blocked without an interactive browser run and corpus. |
| Acquire from one Immich instance | Adapter and queue behavior have automated coverage; live Immich 3.1.x search/download is blocked because no test instance or non-admin test key was supplied. |
| Compress to validated H.264 or H.265/HEVC MP4 | FFmpeg argument/probe/validation tests pass; H.264 has prior synthetic host-media evidence and H.265 container execution awaits Docker Engine. |
| Observe progress across restart | Durable progress/recovery tests pass; forced container restart during real compression awaits Docker Engine and large test media. |
| Download results | Range endpoint contract and automated coverage exist; browser download against a deployed container awaits Docker Engine/browser. |
| Publish selected results to source Immich | Grouped publication, per-result force, source-instance enforcement, checksum reconciliation, immediate asset-ID persistence, and partial album retry are implemented and covered with fakes. Live Immich acceptance remains blocked without a dedicated server/key. |
| Preserve name/date/description/location/albums | Filename, capture/modified dates, and album membership use stable documented contracts. Immich 3.1 exposes no stable direct description/coordinate mutation contract; ShrinkFrame records an explicit warning when those source fields exist. Live extraction/metadata comparison remains blocked. |
| Never expose credentials | Automated key-ring restart/redaction checks and Prompt 14 audit pass; Compose and CI contain no secrets. |
| Never modify/delete original assets | Source adapter has download-only behavior and no original delete/trash call exists. Live validation is blocked, but no original asset was modified or deleted during this release work. |

## Required manual acceptance before a version 1.0 release

Use only a dedicated Immich test library, non-admin API key, test album, and non-personal media corpus from
`docs/09-testing.md`. Record file hashes/asset IDs before and after.

1. Start from a new named volume; wait for `Healthy`, upload the browser corpus, validate progress, rejection,
   `Ready` and `NotBeneficial`, range download, explicit force, and confirmed local deletion.
2. Recreate (not remove) the container and prove database, `/data/keys`, source/output artifacts, history, and
   decryptable saved connection survive.
3. During a large encode, stop Compose. Confirm SIGTERM, no remaining FFmpeg process, no published partial,
   an interrupted durable job after restart, and a successful explicit retry.
4. Browse at least two Immich pages, acquire multiple originals, publish selected valid
   results, force one `NotBeneficial` result, inject one album-sync failure, and retry the partial publication.
5. Compare original asset IDs and metadata before/after. They must be unchanged and present. Verify each new
   asset's filename, capture date, description, location, and album memberships.
6. Record exact Docker/Compose/.NET/FFmpeg/ffprobe/Immich versions, commands, HTTP results, screenshots or logs,
   hashes/IDs, container recreation result, shutdown timing, and final Git status.

Until every blocked row is closed with evidence, this checkout is a release candidate, not a validated version 1.0 release.
