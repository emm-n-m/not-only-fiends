# Not Only Fiends

A D&D 3.5e character building tool with mechanically accurate progression through epic levels.

Built for a campaign I run — handles the full complexity of 3.5e character advancement so players (and the DM) don't have to do it by hand.

## Content Policy

- Public by default: code, tooling, and explicitly approved SRD-safe packs
- Private by default: everything else unless reviewed and added to the public allowlist

The checked-in allowlist lives in [`content-public.json`](content-public.json). The bundled public pack set currently includes `srd_core`, `srd_epic`, `srd_monsters`, and `srd_unearthed_arcana`.

## What It Does

- Build characters level by level with correct BAB, saves, HP, skills, feats, and spellcasting
- Support for races, templates (e.g., Half-Fiend), racial HD, prestige classes, and multiclassing
- Accurate epic-level progression past level 20
- Data-driven content: classes, races, feats, and templates are JSON files, not hardcoded logic
- REST API for programmatic character evaluation and agent-oriented next-step planning
- SRD-safe content only in the engine; homebrew/private content is loaded from an external path

## Architecture

**Core principle: Store Inputs, Compute Everything.**

Only user decisions (race, class picks, feat choices, skill allocations) are saved. All derived stats are computed by replaying the character's HD timeline from scratch. No cached state.

Two active projects:

| Project | Role |
|---------|------|
| **NotOnlyFiendsStudio** | .NET 10.0 class library. Pure rules engine — no UI dependencies. **Produces** content. |
| **NotOnlyFiendsFeed** | Blazor Server app. Character builder UI + REST API. **Displays** content. |
| **NotOnlyFiendsStudio.Tests** | xUnit test suite (314+ tests). |

The engine is designed to be consumed by any .NET frontend. The Blazor app is one consumer; a Unity integration is planned.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full class hierarchy, replay algorithm, and formula DSL.

## Running with Docker

The simplest way to run. Build and start with Docker Compose:

```bash
docker compose up --build
```

The app is available at `http://localhost:5000`. Access it from any device on the same network using the host machine's IP.

### Character and pack storage

By default `docker-compose.yml` uses named volumes. To persist characters in a cloud-synced folder (OneDrive, Google Drive, etc.) or load private content packs, use bind mounts:

```yaml
services:
  app:
    build: .
    ports:
      - "5000:5000"
    volumes:
      - /path/to/your/characters:/data/characters
      - /path/to/your/extra-packs:/data/extra-packs
```

Or with `docker run`:

```bash
docker build -t not-only-fiends .

docker run -p 5000:5000 \
  -v ~/OneDrive/characters:/data/characters \
  -v ~/OneDrive/extra-packs:/data/extra-packs \
  not-only-fiends
```

## Running locally (development)

Requires [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build       # Build all projects
dotnet test        # Run all tests
```

To run the app:

```bash
dotnet run --project NotOnlyFiendsFeed
```

Content loads directly from the filesystem — no build-time manifest generation needed. The app reads `content-public.json` for the bundled pack allowlist and loads packs from `NotOnlyFiendsStudio/Content/packs/`.

### Private/extra packs (local dev)

To load additional packs or configure character storage, create `.env` in the repo root (gitignored):

```env
CHARACTERS_PATH=C:\path\to\cloud-synced\characters
EXTRA_PACKS_PATH=C:\path\to\private-packs-repo
# PCGEN_CHARACTERS_PATH=C:\path\to\pcgen_characters
```

- `EXTRA_PACKS_PATH` — directory containing additional pack folders (each with a `pack.json`)
- `CHARACTERS_PATH` — directory for server-side character persistence (supports cloud drive sync)
- `PCGEN_CHARACTERS_PATH` — optional, only affects PCGen reconstruction tests (they skip when unset)

The same `.env` file is used by `docker compose` for volume bind mounts — one config for both workflows.

### PCG import regression (golden baseline)

`PcgImportRegression` runs the PCGen importer over every `.pcg` file in `PCGEN_CHARACTERS_PATH` and compares the result to a committed baseline stored in `{EXTRA_PACKS_PATH}/test-reports/`. Useful signal after:

- editing `PcgConverter.cs` or `PcgIdMapper.cs`
- adding or changing content that could affect mappings (new homebrew, merged content packs, fresh PDF extractions)
- anything else that could silently change what imports cleanly

**Workflow — before committing content or converter changes:**

```bash
# Verify: compare against the current baseline. Passes when nothing changed.
dotnet test --filter "FullyQualifiedName~PcgImportRegression"
```

If the test fails, review the generated diff (paths are printed in the failure message):

- `pcg_import_report.diff.md` — human-readable delta: regressions (OK → WARN), improvements (WARN → OK), aggregate tally changes (e.g. `Knowledge (Fey): 2 → 0`), and added/resolved parse failures
- `pcg_import_report.latest.{json,md}` — fresh run output, alongside the untouched baseline

If the diff reflects intentional changes, accept the new state as the baseline:

```bash
# Update mode: overwrite baseline, clear the diff file
UPDATE_PCG_BASELINE=1 dotnet test --filter "FullyQualifiedName~PcgImportRegression"
```

Commit the updated baseline in the private packs repo (`EXTRA_PACKS_PATH`). The test auto-skips when `PCGEN_CHARACTERS_PATH` is unset, so this is a no-op for contributors without the private `.pcg` corpus.

**As a conversion shortcut:** the same test is also the fastest way to convert PCGen characters — even a single one. Drop the `.pcg` file into `PCGEN_CHARACTERS_PATH`, run the verify command, and the converted `Character` JSON is written directly to `CHARACTERS_PATH` with a filename matching the `.pcg` stem — ready for the Feed app to pick up. Beats the web UI's click → file-picker → save loop, and scales to any number of characters for free.

Existing files are preserved on re-runs so UI edits aren't clobbered. To force re-conversion (e.g. after improving a mapping):

```bash
PCG_OVERWRITE_CHARACTERS=1 dotnet test --filter "FullyQualifiedName~PcgImportRegression"
```

If `CHARACTERS_PATH` isn't set, converted files fall back to `{EXTRA_PACKS_PATH}/test-reports/converted/`.

### REST API

The same app serves both the Blazor UI and the REST API. Key endpoints:

- `GET /api/health` — loaded packs and content counts
- `GET /api/content/catalog` — machine-friendly content summary
- `GET /api/content/*` — typed content discovery and lookup
- `POST /api/characters/evaluate` — deterministic replay result for a supplied character JSON
- `POST /api/characters/next-step` — preview every legal next HD choice with pending decisions

## Project Structure

```
NotOnlyFiendsStudio/
  Models/       # Core types: Character, CharacterState, Driver, Permabuff, etc.
  Studio/       # ReplayStudio, ContentRegistry, ContentTypeHandler
  Content/      # SRD content packs as JSON

NotOnlyFiendsFeed/
  Components/   # Blazor pages (Builder, Sheet, Import, Settings) and layout
  Services/     # ServerContentService, CharacterStore, AgentApiService, BrowserFileService
  Contracts/    # REST API DTOs
  Program.cs    # Host setup, service registration, API endpoints
  wwwroot/      # Static assets (CSS, JS, icons)

NotOnlyFiendsStudio.Tests/
                # xUnit tests covering replay, drivers, permabuffs, content, API, etc.
```

## Content Format

All game content is defined in JSON files. Every file is a JSON array (`List<T>`), and multiple files of the same type are merged automatically. Later files override earlier ones by ID, which is how homebrew overrides work.

```
Content/packs/srd_core/
  classes/base/fighter.json     # List<Driver>
  races/human.json              # List<RaceDefinition>
  feats/general.json            # List<FeatDefinition>
  templates/half_fiend.json     # List<TemplateDriver>
  racial_hd/outsider.json       # List<Driver>
  domains/srd.json              # List<DomainDefinition>
  spells/srd.json               # List<SpellDefinition>
  skills/srd.json               # List<SkillDefinition>
```

## License

No license is currently applied. All rights reserved.

## AI Disclosure

This project was developed with assistance from AI tools, primarily [Claude](https://claude.ai) by Anthropic. AI was used for code generation, architecture design, debugging, documentation, and test authoring throughout development.

All AI-generated code has been reviewed and approved by the project author. The author maintains full responsibility for the codebase and its correctness.
