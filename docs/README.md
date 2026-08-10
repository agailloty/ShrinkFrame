# Documentation map

These documents are the durable project context. They are intentionally split so an agent can load only what its task needs.

| Document | Purpose |
|---|---|
| `00-product-brief.md` | Scope, users, outcomes, exclusions, success criteria |
| `01-architecture.md` | Solution boundaries, dependencies, runtime topology |
| `02-domain-and-state.md` | Entities, value objects, state machines, invariants |
| `03-workflows.md` | End-to-end upload, Immich, compression, publication flows |
| `04-immich-integration.md` | API contract, permissions, compatibility and secret handling |
| `05-media-processing.md` | ffprobe, FFmpeg, presets, progress and validation |
| `06-persistence-and-storage.md` | SQLite, work storage, capacity and deletion semantics |
| `07-security.md` | Threat model and mandatory controls |
| `08-user-experience.md` | Pages, wizard, behavior and English UI terminology |
| `09-testing.md` | Automated and manual verification strategy |
| `10-deployment.md` | Docker, configuration, health, CI and operations |
| `11-version-1-release-evidence.md` | Final success-criterion evidence and unresolved release blockers |
| `decisions.md` | Consolidated architecture decision record |
| `implementation-log.md` | Decisions and evidence produced during implementation |

When documents conflict, `decisions.md` wins unless a later dated entry in `implementation-log.md` explicitly supersedes it with justification.
