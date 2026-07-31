---
name: pcg-baseline
description: Verify PCG import and replay against the real-character golden baseline, interpret drift, and accept only intentional changes. Use after converter, ID mapping, engine, or content changes that can affect imported characters.
---

# Verify the PCG Import Baseline

`PcgImportRegression` converts and evaluates every `.pcg` in `PCGEN_CHARACTERS_PATH`, then
compares mappings, warnings, and computed sheet values with the golden report in the private
packs repository.

## Preconditions

Require `.env` values for:

- `PCGEN_CHARACTERS_PATH`: real character corpus;
- `EXTRA_PACKS_PATH`: private packs and `test-reports/pcg_import_report.json`.

If either path is absent, report the skipped external regression. Do not update or fabricate a
baseline. Read report JSON with `utf-8-sig` because it may contain a UTF-8 BOM.

## Workflow

1. Run verify mode:

   ```bash
   dotnet test --filter PcgImportRegression
   ```

2. On failure, read
   `{EXTRA_PACKS_PATH}/test-reports/pcg_import_report.diff.md`.
3. Review `Regressions (OK → WARN)` and aggregate tally changes first. Then account for every
   per-character HP/BAB/save/rank/feat/class/caster-level difference against an intentional
   code or content change.
4. Treat warning rewording as paired added/resolved entries and compare substance.
5. Never accept drift that cannot be explained.
6. Only after review, and only when acceptance is within the user's request, run:

   ```bash
   UPDATE_PCG_BASELINE=1 dotnet test --filter PcgImportRegression
   dotnet test --filter PcgImportRegression
   ```

7. Report the exact intentional changes represented by the new baseline. The baseline lives
   in the separate private repository and should be committed there.

## Destructive option

`PCG_OVERWRITE_CHARACTERS=1` overwrites converted character files in `CHARACTERS_PATH` and can
destroy in-UI edits. Never set it without explicit user approval.

## Common failure mode

Content-ID or naming changes must also be reconciled with private packs. Main-repository
sweeps do not modify `EXTRA_PACKS_PATH`; missed private references appear as new dropped
content or warning regressions.
