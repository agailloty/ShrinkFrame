# User experience

## Navigation

- Dashboard
- New Batch
- Batches
- Connections
- Storage
- Settings/About

Use Bootstrap and native Blazor components. UI text is English but stored in localization-ready resources or centralized components from the beginning.

## Dashboard

Show storage summary, active batch/job, queue count, recent batches, connection health summary, and a prominent New Batch action.

## New Batch wizard

### Step 1: Source

Choose Browser Upload or one enabled Immich connection. Generate an editable batch name from source and local date/time.

### Step 2: Select videos

Browser: drag-and-drop plus native multi-file picker, per-file upload progress, remove/retry controls.

Immich: 50-item explicit pages, filters for taken period and album, supported sort orders, thumbnails through ShrinkFrame, detail panel, persistent cross-page selection, Select Page and Clear Selection. A byte-size refinement is shown only when sizes are known and is explicitly scoped to loaded results; it is never presented as a server-global filter. No video playback in version 1.0.

### Step 3: Compression

Choose one global built-in preset. Each row may select a different built-in preset, but per-video advanced free-form changes are not supported. Batch advanced controls: CRF, encoder preset, maximum resolution/keep, audio mode, suffix. `faststart` is always on and not shown as optional.

### Step 4: Summary

Mandatory summary of selected videos, effective options, estimated storage, warnings, and publish possibilities. Capacity warning has an explicit force checkbox/action.

### Step 5: Processing

Show acquisition for all inputs first, then automatic compression. One job card/row exposes status, percentage, processed time, speed, ETA, sizes and expandable technical data (FPS/bitrate/log summary). Failures do not stop other jobs.

### Step 6: Publication

Select valid results for grouped publication or download individually. `NotBeneficial` requires explicit force confirmation. Display returned asset ID and album synchronization status. A partial publication has Retry synchronization.

## Connections

Add/edit/test/disable/delete multiple connections and select a default. Test shows connectivity, Immich version, current API key identity, permissions, and capability status. After saving, the API key field appears empty with a Replace action; the key is never revealed.

## History and storage

Batch history supports search and filters and shows dates, source, status, sizes, reduction, preset and publication. Storage shows total/free/application usage and artifact ownership. Deleting removes both job history and local files after confirmation.

## Accessibility and resilience

- All operations usable by keyboard.
- Progress has text, not color alone.
- Long-running state comes from the server database, not only the current Blazor circuit.
- Reconnecting or opening another tab restores current state.
- Errors provide a stable code, human explanation, and retry action when safe.
