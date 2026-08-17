---
name: pcg-baseline
description: Run the PCG import golden-baseline regression over the configured real-character corpus — verify against the committed baseline, read the diff correctly, and accept intentional changes. Use after engine or content changes, especially anything touching content IDs or computed values.
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

   **Accepting is the user's call, not yours.** Surface the diff and its arithmetic and wait,
   unless they have already said to accept.

4. **Converted character JSONs** are written to `PCG_IMPORT_OUTPUT_PATH`, overwritten on
   every run. When that points at a git-tracked directory (it does here:
   `{EXTRA_PACKS_PATH}/deceit_characters/NotOnlyFiends`), `git diff` there is a second
   report — sheet-level drift the mapping-level baseline doesn't cover. Read it before
   accepting, and it is the user's call to accept, same as the baseline itself.

   The harness never writes to `CHARACTERS_PATH`; those are real characters the user edits
   in the app. If you catch anything reintroducing that, treat it as a bug.

## Check the baseline is the one you think it is

Before trusting a diff, confirm what it is measured against:

```bash
cd "$EXTRA_PACKS_PATH" && git status --short test-reports/
head -5 test-reports/pcg_import_report.diff.md   # "Baseline generated:" timestamp
```

A dirty `pcg_import_report.json` means somebody regenerated it — another agent, another machine,
or the user. Then "0 regressions" only means *0 since that run*, and a change of yours may already
be baked in. If you need the true cumulative diff, preserve the working baseline, restore the
committed one with a non-destructive, scoped workflow, and re-run.

## Read the diff for damage, not just for your change

The per-character sections are the point. These bug classes can be invisible to unit tests and
obvious here:

- **Cross-character contamination.** Four bards changed class when only one should have — a
  variant resolved for one character had leaked onto later ones through shared importer state.
  Any character in the diff you did not expect is this until proven otherwise.
- **A value that moved the wrong way.** A caster level split 13 → 7 + 6 because a rule keyed on
  a base class stopped matching. The headline said "0 regressions" throughout: it counts
  OK→WARN status changes, not wrong numbers.

So read every per-character block and account for each line, including in characters you were
not working on. `Audit signals added` is where new warnings hide.

## Gotcha

Content-convention changes (ID prefixes, renames) must also be applied to the private packs
repo (`EXTRA_PACKS_PATH`) — it is a separate git repo that main-repo sweeps do not touch.
A miss shows up here as OK→WARN regressions with private-pack names in the dropped tallies.

## When a test fails alongside the baseline

A test that *explains* why the engine differs from its source is a suspect, not a specification.
Tests can encode bugs as intentional behaviour, even with confident comments such as:

> "PCGen re-rolls a lich's hit dice as d12 … this engine keeps the bard's d6 driver and preserves
> the out-of-range rolls as source input rather than clamping them, warning once per affected
> level."

PCGen was right and the engine was wrong — the SRD lich template says to raise all Hit Dice.
Before updating an expected value, re-derive it from the source the way `AGENTS.md` §Assertion
discipline requires, and re-read the comment: if it argues that the .pcg is mistaken, check that
claim against the SRD first.
