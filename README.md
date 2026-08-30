# Not Only Fiends

A D&D 3.5e character building tool — and an agent-friendly toolkit for working with 3.5e rules content.

Two things in one repo:

1. **A character builder** with mechanically accurate progression through epic levels, including races, templates (e.g., Half-Fiend), racial HD, prestige classes, multiclassing, and spellcasting.
2. **A toolkit AI agents can use** to extend and operate on that builder — a REST API, a fully data-driven JSON content format, a PCGen `.pcg` importer, and first-class Codex/Claude Code skills for extracting and validating rules content.

Built initially for a campaign I run, but designed from the start so that both humans (via the Blazor UI) and AI agents (via the API and content pipeline) can collaborate on the same character data and the same rules engine.

## The campaign

The campaign revolves around **DECEIT** (Demonic Excellence Center for the Education of Infernal Temptresses) — an Abyssal-layer academy for succubi that didn't get the memo and operates more like an advanced magic research center than a prep school. The setting is a spin-off of a creative writing project.

The repo takes its name from **NOT-OnlyFiends**, the in-universe crystal-ball platform run by Imp, Professor of Divination at DECEIT.

See [WORLD.md](WORLD.md) for more.


## For humans

- Build characters level by level with correct BAB, saves, HP, skills, feats, and spellcasting
- Support for races, templates, racial HD, prestige classes, and multiclassing
- Accurate epic-level progression past level 20
- Per-tick choice validation: feats, skills, spells, domains, class feature picks
- Companion / familiar / cohort handling with master-level scaling
- PCGen `.pcg` import and export, with a compatibility report before any lossy download

## For agents

- **REST API** at `/api/*` — content catalog, character CRUD, deterministic evaluation, next-step previews, simulation
- **JSON content format** — every class, race, feat, template, domain, spell, and skill is a plain JSON file. Multiple packs merge automatically; later files override earlier ones by ID
- **Portable agent skills** (`agent-skills/skills/`) — one canonical workflow set projected into both Codex and Claude Code, including extraction, verification, API audits, PCGen diagnosis, and content QA
- **Orchestrating subagents** (`.claude/agents/`) — `content-extractor` runs the full extraction pipeline for a book; `content-qa` validates JSON against schemas and runs the test suite; `gap-filler` runs end-to-end from "what's missing?" to "fill it from this PDF"
- **`gap-analysis` skill** — runs PCGen character reconstruction tests to report which characters are buildable and what content is blocking each one
- **PCGen import as a batch tool** — drop `.pcg` files into a directory, run one test, and get engine-format JSON character files out (see [PCGen import](#pcgen-import) below)

## Architecture

**Core principle: Store Inputs, Compute Everything.**

Only user decisions (race, class picks, feat choices, skill allocations) are saved. All derived stats are computed by replaying the character's HD timeline from scratch. No cached state.

| Project | Role |
|---------|------|
| **NotOnlyFiendsStudio** | .NET 10.0 class library. Pure rules engine — no UI dependencies. **Produces** content. |
| **NotOnlyFiendsFeed** | Blazor Server app. Character builder UI + REST API. **Displays** content. |
| **NotOnlyFiendsStudio.Tests** | xUnit test suite with more than 1,000 test cases. |

The engine is designed to be consumed by any .NET frontend. The Blazor app is the current consumer.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full class hierarchy, replay algorithm, and formula DSL.

## Content Policy

- Public by default: code, tooling, and explicitly approved SRD-safe packs
- Private by default: everything else unless reviewed and added to the public allowlist

The checked-in allowlist lives in [`content-public.json`](content-public.json). The bundled public pack set currently includes `srd_core`, `srd_epic`, `srd_monsters`, and `srd_unearthed_arcana`. SRD content is distributed under the Open Game License v1.0a — see [OGL.md](OGL.md).

See [CONTENT_POLICY.md](CONTENT_POLICY.md) for the full policy.

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
# PCG_IMPORT_OUTPUT_PATH=C:\path\to\private-packs-repo\converted-characters
```

- `EXTRA_PACKS_PATH` — directory containing additional pack folders (each with a `pack.json`)
- `CHARACTERS_PATH` — directory for server-side character persistence (supports cloud drive sync). Real characters only; no test writes here
- `PCGEN_CHARACTERS_PATH` — optional, only affects PCGen reconstruction tests (they skip when unset)
- `PCG_IMPORT_OUTPUT_PATH` — optional, where the baseline harness writes converted characters. Overwritten on every run, so keep it distinct from `CHARACTERS_PATH`

The same `.env` file is used by `docker compose` for volume bind mounts — one config for both workflows.

## REST API

The same app serves both the Blazor UI and the REST API. Key endpoints:

- `GET /api/health` — loaded packs, content counts, and whether the character store is configured (`status` is `degraded` when `CHARACTERS_PATH` is unset)
- `GET /api/rules` — game rules constants (epic threshold, feat schedule, etc.)
- `GET /api/content/catalog` — machine-friendly content summary
- `GET /api/content/{races|drivers|templates|feats|deities|salient-divine-abilities|domains|skills|class-features|spells}` — typed content discovery and lookup
- `POST /api/characters/evaluate` — deterministic replay result for a supplied character JSON
- `POST /api/characters/next-step` — preview every legal next HD choice with pending decisions
- `GET /api/characters`, `POST /api/characters`, `PUT /api/characters/{id}`, `DELETE /api/characters/{id}` — character CRUD
- `POST /api/characters/{id}/ticks`, `PATCH /api/characters/{id}/ticks/{index}`, `DELETE /api/characters/{id}/ticks/last` — tick-level mutations
- `POST /api/characters/{id}/simulate` — try a tick without persisting
- `POST /api/characters/export-pcg` — export a supplied character draft and return UTF-8 `.pcg` content plus a fidelity report
- `GET /api/characters/{id}/export-pcg` — export a stored character through the same pipeline

OpenAPI metadata is served at `/openapi/v1.json`.

### Sizing the next-step response

`GET /api/characters/{id}/next-step` returns a preview of every legal next HD. Each
preview carries its own feat / domain / class-feature choices, so inlining full option
lists repeats the whole feat catalogue once per candidate driver — that alone was ~99%
of the payload. Two knobs control it:

- `optionDetail=none|ids|full` (default `none`) — how much option data to inline into
  the previews. At every level each choice group reports `optionCount`, so you can see
  how many options exist without paying for the list.
- `driverIds=class:wizard,class:fighter` — restrict previews to specific drivers.

The intended flow is to read the cheap summary, then re-request only the drivers you're
actually weighing:

```bash
curl "$API/api/characters/$ID/next-step"                                    #  ~93 KB, all drivers
curl "$API/api/characters/$ID/next-step?driverIds=class:wizard&optionDetail=full"   # options for one
```

`currentPendingChoices` always carries full options regardless of `optionDetail` — it
describes a single state rather than one per driver, and it's what actually has to be
filled in. Pass `includePreviews=false` to drop the previews entirely (~19 KB).

## Agent skills

The canonical workflows live under [`agent-skills/skills/`](agent-skills/skills/). A deterministic
tool projects them into `.agents/skills/` for Codex and `.claude/skills/` for Claude Code, so users
get the same named capabilities and safety rules on either host. See the
[agent skill architecture](agent-skills/README.md) for the layout and contribution workflow.

**Extraction skills** (`extract-*`) — read a 3.5e source (HTML preferred, PDF fallback), parse it into
engine JSON matching the relevant schema, and write into the appropriate `Content/` subdirectory:

- `extract-class` — base and prestige classes (HDDrivers)
- `extract-race` — playable races + racial HD
- `extract-feat` — feat definitions with prerequisites and granted permabuffs
- `extract-template` — templates (e.g., Half-Fiend, Vampire)
- `extract-domain` — cleric domains
- `extract-spell` — spell definitions
- `extract-skill` — skill definitions

**Analysis and operation skills:**

- `gap-analysis` — runs the PCGen reconstruction test suite and reports which characters are buildable, what content is blocking each one, and prioritized gaps to fill
- `verify-content` / `verify-content-lst` — audit public or private content against its configured source of truth
- `audit-agent-api` / `api-build-character` — exercise the REST API as an external agent
- `debug-pcg-import` / `pcg-baseline` / `pcg-rebuild-regression` — diagnose and regression-test PCGen workflows
- `content-extractor` / `content-qa` / `gap-filler` — compose extraction and validation into end-to-end workflows

**Claude orchestration adapters** (`.claude/agents/`):

- `content-extractor` — surveys a book's table of contents, identifies all extractable types, and runs the appropriate extraction skills for each
- `content-qa` — read-only validation pass: checks JSON against schemas, validates cross-references, runs the test suite
- `gap-filler` — full pipeline from `gap-analysis` → cross-reference gaps against a provided source PDF → extract in priority order → update ID mappings → re-run analysis

Schemas for each content type live in [`schemas/`](schemas/).

After editing a canonical skill, regenerate and check both projections:

```bash
python3 tools/sync_agent_skills.py
python3 tools/sync_agent_skills.py --check
```

## PCGen import and export

The builder's **Export PCGen** action writes PCGen's version-2 save format. Clean exports download
immediately. If an engine input has no safe PCGen representation—such as a mid-career template,
permanent event, or custom equipment permabuff—the builder shows a field-level compatibility
report and requires an explicit **Download partial .pcg anyway** action. Exports are UTF-8 and use
the PCGen campaign keys declared by the loaded packs' `pcgenCampaigns` manifest property.

The REST API exposes the same result as JSON at `POST /api/characters/export-pcg` for an unsaved
draft and `GET /api/characters/{id}/export-pcg` for a stored character. `status` is `exact`,
`partial`, or `blocked`; blocked results contain no PCG content.

### Import regression

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

**As a conversion shortcut:** the same test is the fastest way to convert PCGen characters — even a single one. Drop the `.pcg` file into `PCGEN_CHARACTERS_PATH`, run the verify command, and the converted `Character` JSON lands in `PCG_IMPORT_OUTPUT_PATH`, named from the character's name (not the `.pcg` stem — `CharacterStore` derives ids from `Character.Name`). Beats the web UI's click → file-picker → save loop, and scales to any number of characters for free. Copy the ones you want to play into `CHARACTERS_PATH`.

Every run overwrites the whole output directory. That is deliberate: point `PCG_IMPORT_OUTPUT_PATH` at a git-tracked directory and `git diff` becomes the report of what an engine or content change did to every converted sheet — the sheet-level counterpart to `pcg_import_report.json`, which only covers import mappings.

The harness never writes to `CHARACTERS_PATH`. Those are real characters edited in the UI, and ids are keyed on character name, so a corpus character sharing a name with a real one would silently overwrite it. If `PCG_IMPORT_OUTPUT_PATH` isn't set, converted files fall back to `{EXTRA_PACKS_PATH}/test-reports/converted/`.

## Project structure

```
NotOnlyFiendsStudio/
  Models/       # Core types: Character, CharacterState, Driver, Permabuff, etc.
  Studio/       # ReplayEngine (ReplayStudio), ContentRegistry, ContentTypeHandler
  Content/      # SRD content packs as JSON

NotOnlyFiendsFeed/
  Components/   # Blazor pages (Builder, Sheet, Import, Settings) and layout
  Services/     # ServerContentService, CharacterStore, AgentApiService, BrowserFileService
  Contracts/    # REST API DTOs
  Program.cs    # Host setup, service registration, API endpoints
  wwwroot/      # Static assets (CSS, JS, icons)

NotOnlyFiendsStudio.Tests/
                # xUnit tests covering replay, drivers, permabuffs, content, API, etc.

agent-skills/
  skills/       # Canonical, platform-neutral agent workflows

.agents/
  skills/       # Generated Codex projection

.claude/
  agents/       # Subagents (content-extractor, content-qa, gap-filler)
  skills/       # Generated Claude Code projection
  settings.json # Shared project settings for Claude Code

schemas/        # JSON schemas + per-skill prompts for content extraction
tools/          # Python helpers, including the agent-skill parity generator
```

## Content format

All game content is defined in JSON files. Every file is a JSON array (`List<T>`), and multiple files of the same type are merged automatically. Later files override earlier ones by ID, which is how homebrew overrides work.

```
Content/packs/srd_core/
  classes/base/fighter.json     # List<Driver>
  races/human.json              # List<RaceDefinition>
  feats/general.json            # List<FeatDefinition>
  templates/half_fiend.json     # List<TemplateDriver>
  racial_hd/outsider.json       # List<Driver>
  deities/pantheon.json         # List<DeityDefinition> (setting/private packs)
  salient_divine_abilities/     # List<SalientDivineAbilityDefinition>
  domains/srd.json              # List<DomainDefinition>
  spells/srd.json               # List<SpellDefinition>
  skills/srd.json               # List<SkillDefinition>
```

Schemas for each type live in [`schemas/`](schemas/) and double as the contract the agent extraction skills produce against.

Deities are a supported pack content type, but `srd_core` intentionally ships no named
pantheon: those names and profiles are closed D&D content rather than part of the Revised SRD.
Setting and private packs can supply them with [`deity.schema.json`](schemas/deity.schema.json).

Characters that are themselves deities use `Character.Divinity`, independently of the patron in
`Character.Deity`. The builder supports divine ranks 0–20, form, titles, portfolio, symbol, favored
weapon, granted domains, and salient divine abilities. Replay derives the SRD divine chassis:
maximum hit points, speed, AC/attack/save/check bonuses, immunities, DR, fire resistance, SR,
domain powers and at-will domain spells, aura/senses/realm limits, and the rank-based salient
ability budget. The 99 common salient abilities are extracted from the bundled SRD mirror into
the epic pack; regenerate them with `python3 tools/extract_salient_divine_abilities.py`.

## License

Source code and tooling: [MIT](LICENSE).

Game content derived from the d20 System Reference Document is licensed separately under the Open Game License v1.0a — see [OGL.md](OGL.md).

## AI disclosure

This project was developed with assistance from AI tools, primarily [Claude](https://claude.ai) by Anthropic. AI was used for code generation, architecture design, debugging, documentation, test authoring, and content extraction throughout development.

All AI-generated code has been reviewed and approved by the project author. The author maintains full responsibility for the codebase and its correctness.
