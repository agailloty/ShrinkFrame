# Testing and verification

## Automated POC scope

Only domain unit tests are mandatory. They must cover:

- every allowed and rejected job-state transition;
- batch source invariants;
- CRF and preset validation;
- maximum-resolution/no-upscale calculations as pure domain/application rules where applicable;
- duration tolerance boundary;
- blocking capture-date and rotation findings;
- `Ready` versus `NotBeneficial` classification;
- force-publication guard;
- partial-publication transitions;
- retry and interruption rules;
- capacity estimate and force-warning semantics;
- safe filename/suffix domain rules.

Tests use deterministic clocks and no network/filesystem/process dependencies.

## Mandatory milestone checks

Every prompt includes repeatable manual or CLI verification in addition to compilation. Agents must record actual commands/results in `implementation-log.md`.

Expected global checks as the solution grows:

```bash
dotnet restore ShrinkFrame.sln
dotnet build ShrinkFrame.sln --no-restore
dotnet test ShrinkFrame.sln --no-build
docker compose config
docker compose build
```

## Manual acceptance media

Maintain a documented, user-supplied test corpus outside Git:

- landscape H.264 MP4 with capture date;
- portrait phone MOV with rotation/display matrix;
- input whose audio can be copied;
- input requiring AAC conversion;
- video below each resolution threshold to prove no upscale;
- video likely to produce a larger output;
- corrupt or non-video file;
- metadata-rich video with coordinates;
- large file sufficient to observe streaming and cancellation.

Do not commit personal videos or secrets.

## Immich verification

Use a dedicated non-admin API key and test album. Verify search pagination, proxy thumbnails, original download, upload, metadata/date, album association, partial retry, and absence of original deletion. Record the tested Immich version.
