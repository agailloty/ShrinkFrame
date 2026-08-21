# Domain and state

## Aggregates

### ImmichConnection

Fields: ID, display name, normalized base URL, encrypted API key envelope, allow-invalid-certificate flag, enabled flag, default flag, last tested time, detected version, compatibility result, and last test error. Plaintext keys never leave the infrastructure boundary after submission.

### CompressionBatch

Fields: ID, generated/modifiable name, source kind, optional Immich connection ID, status, default processing options snapshot, created/updated timestamps, and child job IDs.

A batch has exactly one source kind. An Immich batch references exactly one connection. Mixing Immich instances in one batch is invalid.

### CompressionJob

Fields include ID, batch ID, source reference, original metadata snapshot, selected preset snapshot, effective options, state, progress, artifact references, sizes, timestamps, validation findings, publication state, published asset ID, and summarized log.

Never store raw video bytes, full API keys, or unconstrained filesystem paths in an aggregate.

## Value objects

- `VideoSourceRef`: kind, source-specific ID, connection ID when applicable.
- `VideoMetadata`: filename, MIME type, size, duration, dimensions, codecs, capture time, effective rotation, description, coordinates, album references.
- `CompressionOptions`: video codec, CRF, encoder preset, maximum resolution, audio mode, suffix.
- `ArtifactRef`: opaque storage key, never an absolute path.
- `ValidationFinding`: code, severity, message.
- `TransferProgress` and `CompressionProgress`.

## Presets

Built-in presets are immutable and identified by stable IDs. A job stores the effective options snapshot, not only the preset ID.

Initial presets:

| ID | Name | Codec | CRF | encoder preset | Maximum |
|---|---|---|---:|---|---|
| `compact` | Compact | H.265 | 30 | medium | Keep |
| `archival-quality` | Archival Quality | H.264 | 18 | slow | Keep |
| `high-quality` | High Quality | H.264 | 21 | medium | Keep |
| `balanced` | Balanced | H.264 | 24 | medium | Keep |
| `smaller-file` | Smaller File | H.264 | 27 | medium | Keep |
| `full-hd` | Full HD | H.264 | 23 | medium | 1080p |
| `hd` | HD | H.264 | 24 | medium | 720p |
| `smallest-practical` | Smallest Practical | H.264 | 30 | slow | 720p |

Allowed CRF range is 18 through 36 inclusive; warn when above 30. Available maximum resolutions: Keep, 2160p, 1440p, 1080p, 720p, 480p. Never upscale.

## Job state machine

```text
Draft -> Acquiring -> Probing -> Queued -> Compressing -> Validating
      -> Ready | NotBeneficial | Failed | Cancelled | Interrupted

Ready | NotBeneficial -> Publishing -> Published | PartiallyPublished | PublicationFailed
PartiallyPublished | PublicationFailed -> Publishing (retry)
Interrupted | Failed | Cancelled -> Acquiring/Queued (explicit retry)
```

State changes must be explicit domain operations with guarded transitions. A restart converts active states (`Acquiring`, `Probing`, `Compressing`, `Validating`, `Publishing`) to `Interrupted` during startup recovery.

## Invariants

- Only probed videos enter `Queued`.
- Only a successfully validated output becomes `Ready` or `NotBeneficial`.
- Capture-date or effective-rotation loss is a blocking validation error.
- `NotBeneficial` cannot publish without an explicit persisted override.
- Published asset IDs are recorded before album synchronization begins.
- Album synchronization failure produces `PartiallyPublished` and is retryable; the new asset is retained.
- Original Immich asset IDs are never passed to delete/trash operations.
- Cancellation kills the FFmpeg process tree, deletes partial output, and keeps the acquired source for retry.
