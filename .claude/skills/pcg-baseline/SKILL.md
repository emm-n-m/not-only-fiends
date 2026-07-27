---
name: pcg-baseline
description: Run the PCG import golden-baseline regression (54 real characters) — verify against the committed baseline, read the diff correctly, and accept intentional changes. Use after engine or content changes, especially anything touching content IDs or computed values.
allowed-tools: Bash(dotnet test*), Bash(dotnet build*), Read, Glob, Grep
---

# PCG Import Golden Baseline

`PcgImportRegression` replays every `.pcg` in `PCGEN_CHARACTERS_PATH` through the import +
evaluate pipeline and compares against a committed baseline: import mapping fidelity
(dropped feats/classes/skills, warnings) **and** computed sheet values
(hp/bab/saves/skillRanks/feats/classLevels/casterLevels). It is the main defense against
content drift — a rules or content fix silently changing saved characters.

## Prerequisites

`.env` at the solution root must set `PCGEN_CHARACTERS_PATH` (the corpus) and
`EXTRA_PACKS_PATH` (private packs repo — also where the baseline lives:
`{EXTRA_PACKS_PATH}/test-reports/pcg_import_report.json`). Only the machine with the
private corpus can run this; the dev VM cannot. Report JSON is written with a UTF-8 BOM —
parse with `encoding='utf-8-sig'`.

## Workflow

1. **Verify mode** (default):
   ```bash
   dotnet test --filter PcgImportRegression
   ```
   Pass → no drift, done. Fail → read
   `{EXTRA_PACKS_PATH}/test-reports/pcg_import_report.diff.md`.

2. **Read the diff headline first.** `Regressions (OK → WARN)` and `Aggregate tally changes`
   are the alarming sections — new dropped content or new warnings mean something stopped
   resolving (e.g. an ID convention change that missed a consumer). Per-character
   hp/bab/saves changes are the *point* of the harness: match each against a change you
   intended. Warning-text rewording (same substance, new spelling) shows as paired
   added/resolved entries — read both sides before judging.

3. **Accept intentional changes**:
   ```bash
   UPDATE_PCG_BASELINE=1 dotnet test --filter PcgImportRegression
   dotnet test --filter PcgImportRegression   # confirm clean
   ```
   Commit the updated baseline in the private repo with a message saying *why* the values
   moved. Never update to silence a diff you can't explain.

4. **Converted character JSONs** are written to `CHARACTERS_PATH` for the Feed app but
   existing files are preserved (in-UI edits). `PCG_OVERWRITE_CHARACTERS=1` forces
   re-conversion — ask the user first; it clobbers their edits.

## Gotcha

Content-convention changes (ID prefixes, renames) must also be applied to the private packs
repo (`EXTRA_PACKS_PATH`) — it is a separate git repo that main-repo sweeps do not touch.
A miss shows up here as OK→WARN regressions with private-pack names in the dropped tallies.
