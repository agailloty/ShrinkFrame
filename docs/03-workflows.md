# Workflows

## Browser upload batch

1. User creates a named batch and selects Browser Upload.
2. Files are posted to a dedicated streaming endpoint with per-file progress.
3. Server allocates opaque work-storage keys and writes with bounded buffers while hashing.
4. ffprobe validates the media. Invalid input bytes are deleted and an error job remains visible.
5. User chooses a global preset and optional different built-in preset per video.
6. Summary shows videos, effective options, estimated capacity, and warnings.
7. User confirms; jobs enter the durable queue.
8. Worker compresses and validates sequentially by default.
9. User downloads individual results or publishes selected results to a chosen enabled Immich connection.

## Immich batch

1. User chooses one enabled connection.
2. ShrinkFrame searches videos using explicit 50-item pagination and filters.
3. Thumbnails are proxied through authenticated server endpoints.
4. Selection persists across pages; only visible page may be selected in bulk.
5. User configures and confirms the batch.
6. Capacity admission estimates all source downloads and outputs. Insufficient space warns and permits explicit force.
7. All selected sources are downloaded before compression. A failed acquisition does not stop other jobs and is retryable.
8. Compression starts automatically after the acquisition phase.
9. Publication is manual and grouped. Results return only to their source connection.
10. New assets inherit name, capture date, description, coordinates, and source album membership.

## Compression

1. Load persisted job and atomically claim it.
2. Probe input and compare it to the acquisition snapshot.
3. Build FFmpeg arguments from validated value objects.
4. Run FFmpeg with structured progress and cancellation.
5. Probe output.
6. Run validation rules.
7. Move atomically from a partial artifact name to the final artifact key.
8. Mark `Ready` when smaller, otherwise `NotBeneficial`.

## Publication

1. Recheck artifact existence and validation status.
2. Require explicit override for `NotBeneficial`.
3. Upload multipart data to Immich with `_V` default suffix and MP4 extension.
4. Persist returned asset ID immediately.
5. Copy description/location using currently stable supported API operations; avoid deprecated endpoints when a stable replacement exists.
6. Add new asset to each source album.
7. Mark `Published` or `PartiallyPublished`.
8. Delete the downloaded Immich source copy after successful publication; retain output until manual deletion.

## Deletion

The Storage page lists jobs and artifact sizes. Deleting a job removes its known artifacts and its history after explicit confirmation. Deletion is constrained to opaque keys below the work root and must never follow symbolic links. Any failure stops the operation and leaves the database record for diagnosis.
