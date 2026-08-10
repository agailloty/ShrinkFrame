# Architecture

## Style

ShrinkFrame is a modular monolith. One ASP.NET Core process hosts:

- the Blazor Web App;
- HTTP endpoints for streaming uploads, downloads, thumbnails, and health;
- application use cases;
- a queued `BackgroundService` worker;
- SQLite persistence;
- local work storage;
- Immich and FFmpeg adapters.

Version 1.0 uses one Docker container and one persistent volume. Logical isolation must allow the worker, database, and storage adapters to be replaced later without changing the domain.

## Solution projects

```text
src/
  ShrinkFrame.Domain/
  ShrinkFrame.Application/
  ShrinkFrame.Infrastructure/
  ShrinkFrame.Web/
tests/
  ShrinkFrame.Domain.Tests/
```

### Dependency direction

```text
Web ───────────────► Application ───────────────► Domain
 │                         ▲
 └────► Infrastructure ────┘
             │
             └───────────────────────────────► Domain
```

- `Domain` has no project dependencies and no EF Core, HTTP, filesystem, process, or UI types.
- `Application` depends only on `Domain` and defines ports/interfaces and use cases.
- `Infrastructure` implements application ports and depends on `Application` and `Domain`.
- `Web` is the composition root and depends on all projects.
- Domain tests depend only on `Domain`.

## Major application ports

- `IVideoSource`: search/get/materialize source assets.
- `IVideoPublisher`: publish compression artifacts.
- `IImmichConnectionRepository`: encrypted connection persistence through application DTOs.
- `IBatchRepository` and `ICompressionJobRepository`: durable state.
- `IWorkStorage`: safe artifact allocation, open/read/write/delete, and capacity reporting.
- `IMediaProbe`: ffprobe abstraction.
- `IMediaCompressor`: FFmpeg abstraction.
- `IClock`: deterministic time.
- `IDiskCapacityService`: admission decisions.
- `IJobProgressSink`: throttled durable progress.

Do not create a generic plug-in framework. The interfaces cover only the two version 1.0 sources and two destinations. Extend them when a real third integration exists.

## Scalability

Version 1.0 scales vertically:

- compression concurrency is configurable and defaults to one;
- acquisition can have a separate low concurrency;
- all work is persisted;
- media storage is abstracted;
- EF Core persistence is kept behind repositories to permit PostgreSQL later.

SQLite, local files, and in-process coordination make multiple replicas unsupported. The app must refuse or clearly document multiple replicas. Future horizontal scaling requires PostgreSQL, distributed leases, and shared/object storage.

## Large-transfer rule

Blazor coordinates the UI but never carries video bytes through its SignalR circuit. Dedicated ASP.NET Core HTTP endpoints stream uploads and downloads. Immich transfers stream between `HttpClient` and `IWorkStorage` without whole-file buffering.
