# AGENTS.md

This file provides guidance to AI coding assistants when working with code in this repository.

## Project Overview

D&D 3.5e character building tool with mechanically accurate progression through epic levels. Three projects in one solution (`NotOnlyFiends.sln`):

- **NotOnlyFiendsStudio** — .NET 10.0 class library. Pure rules logic, no UI dependencies. Produces content.
- **NotOnlyFiendsFeed** — Blazor Server app. Serves the UI (character builder, sheet, import, settings) and REST API. Loads content from filesystem via `ServerContentService`. Displays content.
- **NotOnlyFiendsStudio.Tests** — xUnit test suite (314+ tests).

## Commands

```bash
dotnet build                                    # Build all projects
dotnet test                                     # Run all tests
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"  # Run a single test
dotnet test --filter "FullyQualifiedName~ClassName"             # Run a single test class
```

### Assertion discipline

An assertion is only as good as the authority behind it, and the failure mode to guard
against is updating a number to match new output until the test asserts nothing.

- **Never change an expected value to match what the code now prints.** Re-derive it from
  the SRD, or from the source file the test reads, and say which in the diff. If neither
  can justify the new value, the code is wrong, not the test.
- **Prefer deriving over transcribing.** `Assert.Equal(AverageBab(6), …)` and
  `Assert.Equal(source.Languages.Distinct().Count(), …)` stay correct and keep testing
  something; a bare `4` or `11` decays into "the input hasn't changed".
- **Exact-value tests read frozen inputs**, never live external data — see the fixture
  entry under External Data. Snapshot-style expectations belong in a baseline file with an
  accept-the-diff workflow (`PcgImportRegression`), not hand-written in a test.

## Architecture

**Core Principle: Store Inputs, Compute Everything.** Only user decisions are persisted. All derived values are computed by ordered replay of the character's HD timeline. There is no cached state.

### Three Layers

1. **Object Definitions** (`NotOnlyFiendsStudio/Models/`) — Abstract classes and interfaces defining the system's grammar (Driver, Permabuff, Prerequisite, Character, CharacterState, Formula).
2. **Content Data** (`NotOnlyFiendsStudio/Content/`) — JSON files deserialized into Layer 1 types. SRD-safe content only. Organized: `classes/`, `races/`, `racial_hd/`, `templates/`, `feats/`, `domains/`, `spells/`, `skills/`.
3. **Character Instances** — Character JSON save files with user choices. Created by consumer projects (Blazor UI).

The engine (Layers 1+2) has zero knowledge of any specific character or UI.

### Key Files

- **`NotOnlyFiendsStudio/Studio/ReplayStudio.cs`** — Core evaluation: `Evaluate(Character, int? upToHD)` → `CharacterState`. Applies race → templates → ability scores → tick-by-tick HD progression.
- **`NotOnlyFiendsStudio/Studio/ContentRegistry.cs`** — Loads, indexes, and validates all content JSON. Uses `ContentTypeHandler<T>` for extensible loading. Implements `IContentLookup`.
- **`NotOnlyFiendsStudio/Studio/ContentTypeHandler.cs`** — Generic content loading handlers. Each handler knows its directory, deserializes `List<T>`, registers items.
- **`NotOnlyFiendsStudio/Models/Permabuff.cs`** — Permabuff hierarchy. Atomic, permanent, irreversible modifications applied via `PermabuffContext` (state + rules + content). Named to distinguish from temporary D&D buffs.
- **`NotOnlyFiendsStudio/Models/Driver.cs`** — Unified `HDDriver` with `DriverKind` enum (Class/RacialHD). All progression sources share the same interface.
- **`NotOnlyFiendsStudio/Models/GameRules.cs`** — Data-driven rules (`GameRules` with `Standard35e()` factory), `PermabuffContext`, `IContentLookup` interface.
- **`NotOnlyFiendsStudio/Models/Formula.cs`** — String DSL (`"10 + TotalHD / 2 + Mod(CON)"`) parsed at content load, tree-walked at evaluation.
- **`NotOnlyFiendsStudio/Models/Character.cs`** — Character save format, TickChoices (ability increases, feats, skills, spells, class feature choices), PermanentEvent, EquipmentEntry.
- **`NotOnlyFiendsStudio/Models/Pack.cs`** — PackManifest (pack metadata, dependencies, priority), PackConfig, PackEntry.
- **`NotOnlyFiendsFeed/Components/Pages/BuilderView.razor`** (+ `.razor.cs` code-behind) — Full character builder UI, organised into six tabs: Summary & Progression, Feats & Special Abilities, Skills, Spells, Equipment, Companions. Markup lives in the `.razor`, all logic in the partial class beside it.
- **`NotOnlyFiendsFeed/Components/Pages/SheetView.razor`** (+ `.razor.cs`) — Read-only character sheet on the same tab vocabulary. Level slider and summary strip stay pinned above the tabs.
- **`NotOnlyFiendsFeed/Components/TabStrip.razor`** + **`CharacterTabs.cs`** — Shared tab bar and the `CharacterTab` enum both pages use. Tabs are driven by a C# field and `@onclick`; `bootstrap.bundle.js` is **not** loaded, so `data-bs-toggle` does nothing and `.tab-content`/`.tab-pane` must be avoided (they carry `display: none` with no JS to undo it).
- **`NotOnlyFiendsFeed/Components/Pages/SettingsView.razor`** — Read-only view of loaded packs and content summary.
- **`NotOnlyFiendsFeed/Services/ServerContentService.cs`** — Singleton content loader. Dual-mode: local dev (finds solution root, reads `content-public.json` + `.env`) or Docker (env vars `Content__BundledPacksPath`, `Content__ExtraPacksPath`, `Content__CharactersPath`).
- **`NotOnlyFiendsFeed/Services/CharacterStore.cs`** — Server-side character persistence. Atomic writes (File.Move pattern) for cloud-drive sync safety.
- **`NotOnlyFiendsFeed/Services/AgentApiService.cs`** — All REST API logic: catalog, evaluate, next-step, character CRUD, mutations.
- **`NotOnlyFiendsFeed/Services/BrowserFileService.cs`** — Scoped IJSRuntime wrapper for browser file download/upload.

### Important Design Decisions

- **Permabuffs** are the atomic unit of character modification. Applied via `PermabuffContext` (state + rules + content). Polymorphic JSON via `[JsonDerivedType]` with `$type` discriminator.
- **GameRules** encapsulates all D&D 3.5e parameters (epic threshold, feat schedules, BAB/save formulas). `Standard35e()` factory for default rules.
- **Content pipeline** uses generic `ContentTypeHandler<T>`. All content files are `List<T>` format. Multiple content roots merge (later overrides earlier by ID).
- **HDDriver** is the unified driver class with `DriverKind` enum (Class/RacialHD). No separate ClassDriver/RacialHDDriver.
- **AttributeTarget** enum replaces magic strings in ModifyAttribute/SetAttribute. Typed sub-properties for resistance elements and ability scores.
- **FeatSlots** are typed (`List<FeatSlot>` with optional `Restriction`), not just int counters.
- **Skill ranks** stored as doubled ints (half-ranks). 5 ranks = 10 internally. Display divides by 2. `MinSkillRanks.Value` in content is authored in **whole ranks** (the comparison doubles internally) — never pre-double in JSON.
- **Template scaling** uses declarative mutations (POST/DELETE/PUT semantics). Thresholds fire once at exact HD. `ScalingFormulas` recalculate every tick.
- **Epic progression**: +1 epic attack at odd HD, +1 epic saves at even HD past `rules.EpicThreshold`. BAB/saves stop advancing from classes at threshold.
- **Equipment is post-tick only** — never retroactively affects per-level calculations.
- **PermanentEvents** (Tomes, Wish inherent bonuses) are slotted between ticks and affect all subsequent ticks.
- **Racial HD** are HDDrivers with `Kind = RacialHD` and a `HasRace` prerequisite — same replay mechanics as class levels.
- **Prestige class spell advancement**: `AdvanceSpellcasting` permabuff. One matching class → auto-advance. Multiple → user selects via `ClassFeatureChoices["advance_spellcasting"]`.
- **GrantBonusFeat cascade**: Automatically applies a feat's `GrantedPermabuffs` via `ctx.Content` lookup.
- **Content validation**: `ContentRegistry.Validate()` checks cross-references (broken driver/feat references) at load time.
- **Auto-evaluate**: `OnCharacterChanged()` triggers `RefreshPerTickData()` + `EvaluateCharacter()` on every input change. No manual "Evaluate" needed.
- **Per-tick data consolidation**: Single `RefreshPerTickData()` loop evaluates character at each HD once and populates spell summaries, skill info, available feats, and caster info.
- **PermabuffContext.CurrentTickChoices**: Passes per-tick user choices into permabuff execution so `AdvanceSpellcasting` can consume `ClassFeatureChoices["advance_spellcasting"]`.
- **Prerequisite-filtered feats**: `GetAvailableFeats(state)` returns only feats whose prerequisites are met at each HD. UI calls this per-tick.
- **Tabs are facets over the HD timeline**, not just groupings of cards. The builder renders *one* tick loop shared by five tabs; each tab shows only its own facet of a tick (Summary: driver/ability increase/domains/wizard schools/class-feature picks; Feats; Skills; Spells; Companions) and `TickShowsFacet()` skips ticks with nothing to show for it. A "Show all HD" checkbox is the escape hatch for any tick a facet filter wrongly hides.
- **Docker support**: Multi-stage Dockerfile. Bundled packs baked into image. Characters and extra packs volume-mounted. `docker-compose.yml` at repo root.

See `ARCHITECTURE.md` for the full class hierarchy, replay algorithm, and formula DSL grammar.

## External Data (symlinks)

- **PCGen LST data** → path configured via `PCGEN_DATA_PATH` in `.env`. PCGen 3.5e LST data files (Wizards, third-party publishers), subdirectories per publisher (e.g., `wizards_of_the_coast/`, `12_to_midnight/`). Ground truth for auditing private-pack content.
- **PCGen `.pcg` characters** → path configured via `PCGEN_CHARACTERS_PATH` in `.env`. Used by the PCGen reconstruction test suite, which skips automatically when the path is unset or the directory is missing. This is a **live working directory the user edits in PCGen**, so only corpus sweeps and the baseline harness may read it — see the fixture rule below.
- **Frozen `.pcg` fixtures** → `{EXTRA_PACKS_PATH}/test-fixtures/pcg/`, reached via `TestContentHelper.PcgFixture(name)` and gated by `[RequiresPcgFixturesFact]`. Any test asserting **exact values for a named character** reads these committed copies, never the live directory, so a character edited in PCGen can't fail the suite for a non-code reason. Refreshing a fixture is a deliberate commit in the materials repo; see the README there.
- **PCG import regression** → `PcgImportRegression` test runs the converter over every `.pcg` file and compares against a golden baseline stored in `{EXTRA_PACKS_PATH}/test-reports/`. Run after any change that could affect PCG import (converter, id mapper, new/changed content that touches mapped names). On mismatch the test writes `pcg_import_report.diff.md` and fails with review instructions. Re-run with `UPDATE_PCG_BASELINE=1` to accept intentional changes.
- **`sources/`** → Source PDFs (gitignored). Drop PDFs here for content extraction. Long-term storage of owned PDFs is configured via `SOURCE_PDFS_PATH` in `.env` — for books with no PCGen LST data (e.g. the Fiendish Codices), these PDFs are the audit ground truth.
