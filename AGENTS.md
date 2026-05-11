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
- **`NotOnlyFiendsFeed/Components/Pages/BuilderView.razor`** — Full character builder UI: race/template/ability setup, HD timeline with per-tick feat/skill/spell/domain selection, permanent events, equipment.
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
- **Docker support**: Multi-stage Dockerfile. Bundled packs baked into image. Characters and extra packs volume-mounted. `docker-compose.yml` at repo root.

See `ARCHITECTURE.md` for the full class hierarchy, replay algorithm, and formula DSL grammar.

## External Data (symlinks)

- **`pcgen_data/`** → PCGen 3.5e LST data files (Wizards, third-party publishers). Contains subdirectories per publisher (e.g., `wizards_of_the_coast/`, `12_to_midnight/`).
- **PCGen `.pcg` characters** → path configured via `PCGEN_CHARACTERS_PATH` in `.env`. Used by the PCGen reconstruction test suite, which skips automatically when the path is unset or the directory is missing.
- **PCG import regression** → `PcgImportRegression` test runs the converter over every `.pcg` file and compares against a golden baseline stored in `{EXTRA_PACKS_PATH}/test-reports/`. Run after any change that could affect PCG import (converter, id mapper, new/changed content that touches mapped names). On mismatch the test writes `pcg_import_report.diff.md` and fails with review instructions. Re-run with `UPDATE_PCG_BASELINE=1` to accept intentional changes.
- **`sources/`** → Source PDFs (gitignored). Drop PDFs here for content extraction.
