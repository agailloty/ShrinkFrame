# Persistence and storage

## SQLite

Use EF Core with SQLite. Repositories defined by Application hide EF entities. Migrations are committed and applied automatically at startup under a single-instance lock suitable for the POC.

Store:

- Immich connection metadata and encrypted secret payloads;
- batches and option snapshots;
- jobs and state transitions;
- source metadata snapshots and album IDs;
- opaque artifact keys and sizes;
- throttled progress and timestamps;
- validation findings;
- publication attempts and returned asset IDs;
- bounded job-log summaries.

Do not store video bytes, thumbnails, raw FFmpeg output without bounds, or plaintext secrets.

SQLite is the durable queue. Claim work with a guarded update including expected state/concurrency token so a job is never intentionally owned twice. Multi-replica operation is unsupported.

### Runtime configuration and inspection

The application uses `ConnectionStrings:ShrinkFrame`. The checked-in production default is
`Data Source=/data/shrinkframe.db;Default Timeout=5;Pooling=True`; Development uses
`.local/shrinkframe.db`. Startup applies committed migrations, switches the database to WAL mode,
sets a five-second busy timeout, enables foreign keys, and then marks active work interrupted.
Startup migration and recovery assume exactly one ShrinkFrame process. Do not run multiple app
instances against the same SQLite file. EF Core's SQLite migration lock is a safety net, not support
for multiple application replicas.

Database transactions are restricted to short persistence operations and guarded state changes.
Network transfers, filesystem I/O, ffprobe, and FFmpeg must run outside database transactions.

For a disposable developer database:

```powershell
$db = Join-Path $env:TEMP "shrinkframe-inspect.db"
dotnet ef database update --project src/ShrinkFrame.Infrastructure/ShrinkFrame.Infrastructure.csproj --startup-project src/ShrinkFrame.Web/ShrinkFrame.Web.csproj --context ShrinkFrameDbContext --connection "Data Source=$db;Default Timeout=5;Pooling=False"
sqlite3 $db ".tables"
sqlite3 $db ".schema Jobs"
sqlite3 $db "PRAGMA journal_mode; PRAGMA foreign_key_check; SELECT MigrationId FROM __EFMigrationsHistory;"
```

The `sqlite3` commands require the SQLite CLI. With DB Browser for SQLite, open the same disposable
file read-only and inspect `Jobs`, its indexes, and `__EFMigrationsHistory`. Stored artifact values
must be opaque relative keys; no video payload or absolute-path column is part of the schema.

## Work storage layout

The implementation is behind `IWorkStorage`. A suggested physical layout is:

```text
/data/
  shrinkframe.db
  keys/
  work/
    batches/{batch-id}/jobs/{job-id}/
      source/input.bin
      output/result.partial.mp4
      output/result.mp4
      probe/input.json
      probe/output.json
      logs/ffmpeg.log
```

Database code stores only logical keys such as `batches/.../result.mp4`.

## Safety

- Generate all directories and filenames on the server.
- Sanitize original names only for display and final download headers.
- Resolve every physical path under a configured, canonical work root.
- Reject traversal, absolute paths, alternate data streams, and invalid segments.
- Do not follow symbolic links or Linux mount surprises during deletion.
- Write uploads with create-new semantics where possible.
- Finalize artifacts through atomic rename within the same filesystem.

## Capacity

- Default maximum input file: 20 GB, configurable.
- No fixed batch cap.
- Admission estimates source bytes plus expected output and working margin.
- Default estimate: `source bytes * 2.2 + configured system reserve`.
- Insufficient space produces a visible warning and an explicit persisted force override.
- Recheck capacity immediately before every acquisition and compression. A prior override does not make an impossible write safe; true out-of-space conditions still fail.

The dashboard and Storage page show total, free, reserved/estimated, and artifact usage.

### Work-storage safety checks

Prompt 04 implements local storage with server-generated opaque keys, create-new partial writes,
bounded 128 KiB streaming by default, and same-directory atomic finalization. The configured root is
canonicalized once and every operation revalidates containment and rejects symbolic links or Windows
reparse points. Inventory DTOs expose logical keys and byte counts only; they never expose the host
root or a physical path. Controlled deletion accepts only artifacts whose logical batch/job prefix
matches the supplied durable owner, processes them in order, stops at the first failure, and reports
each attempted result. It deletes files individually and never performs recursive deletion.

Repeat these manual checks on the target filesystem when changing mounts, container users, or storage
configuration:

1. Point `Storage:WorkRoot` at an empty disposable directory and start the app. Confirm startup logs
   `WorkStorageValidated`; a read-only directory must prevent startup.
2. Allocate an output, write its partial key, finalize it, read it, inventory it, then delete it. Confirm
   create-new rejects a second writer and the final key has no `.partial` marker.
3. Submit logical keys containing `..`, an absolute path, backslashes, or `:` through a storage test
   seam. Confirm every operation rejects them and no file appears outside the work root.
4. Place a symlink/reparse point in a disposable job directory that targets outside the root. Confirm
   read, write, inventory, finalization, and deletion reject it. Remove the link manually afterward;
   do not use a recursive cleanup command through the adapter.
5. Cancel a bounded copy after at least one buffer. Confirm the partial file is removed and no final
   artifact exists.
6. Inject a capacity reporter with 2,199 free bytes and evaluate a 1,000-byte source with zero reserve.
   Confirm the 2,200-byte estimate warns, requires an explicit force value, and that the force value is
   saved on the batch. Overflow remains non-admissible even when force is requested.

## Retention and deletion

- Browser-uploaded sources remain while they can support retry/recompression.
- Downloaded Immich sources are deleted after successful publication.
- Outputs remain until manual deletion.
- Partial artifacts are cleaned after failure/cancellation when safe.
- Storage deletion removes known artifacts and the related history after explicit confirmation.
- If artifact deletion fails, keep the database record and report the error; never hide orphaned data.
