---
name: pcg-rebuild-regression
description: Corpus-scale agent regression — fan out one agent per .pcg to rebuild every saved character through the REST API and diff the resulting sheets against the PCG import golden baseline. The agent-driven sibling of pcg-baseline. Use before publishing or after major API/engine changes.
---

# PCG Rebuild Regression (agent-driven)

Where [pcg-baseline](../pcg-baseline/SKILL.md) replays `.pcg` files through the import
pipeline, this test rebuilds each character **through the public API only** — one subagent per
character, guided by the `.pcg` for *what* to pick, with the API as sole authority on *how*.
It exercises the project's core claim (an agent can drive `/api/*` unaided) and catches both
API regressions and content drift the import path can't see.

Reference run 2026-08-08: 55 characters → **13 exact / 31 field-diffs / 11 incomplete**,
~37 min, ~5M sonnet tokens. Artifacts, per-agent reports, and the reusable comparison script
live in `test-reports/agent-api-rebuild-2026-08-08/` (gitignored — results name private
campaign characters; keep it that way). Compare a fresh run against that scoreboard.

## Prerequisites

- `.env` with `PCGEN_CHARACTERS_PATH` (the corpus) and `EXTRA_PACKS_PATH` (private packs +
  the baseline at `{EXTRA_PACKS_PATH}/test-reports/pcg_import_report.json`, UTF-8 BOM —
  parse with `encoding='utf-8-sig'`). Only the machine with the private corpus can run this.
- App on a spare port per [run-app](../run-app/SKILL.md); confirm `/api/health` lists the
  private packs.
- **Copy the `.pcg` files into the session scratchpad first** — subagents may hit permission
  walls reading `/mnt/c/...` directly.

## Method

1. Pilot ONE mid-complexity character (a single-class caster with domains) and diff it before
   fanning out — every harness bug shows up in the pilot for 2% of the cost.
2. Fan out one agent per `.pcg` (sonnet-tier is sufficient; ~15 concurrent is fine — the app
   handles parallel builds). Each agent gets the
   [api-build-character](../api-build-character/SKILL.md) protocol, its `.pcg` path, and must:
   name the character `API Test - <name>`, use the `.pcg`'s per-level HP rolls, attach the
   `.pcg`'s equipment, never substitute unfindable content (record it as unresolved instead),
   report sheet values via structured output
   (`built/hp/bab/saves/skillRanks/feats/classLevels/casterLevels/unresolved/friction/apiBugs/deleted`),
   and DELETE the character even on failure.
3. Diff against the baseline (`compare_results.py` in the reference-run directory does this).
4. Sweep: `GET /api/characters` must show zero `API Test -` characters. Kill the app by port.

## Reading the diff — normalization rules that are NOT regressions

- Baseline `skillRanks` are **half-ranks**; sheets report ranks → multiply agent values ×2.
- Baseline `classLevels` exclude racial HD; agent sheets include `racial_hd:*` entries.
- Sheet feat labels can carry suffixes (`feat:x (class bonus)`) that break set comparison.
- Save/HP drift of 1–5 points usually traces to equipment the agent couldn't attach —
  check the agent's `unresolved` list against `CONTENT_GAPS.md` before calling it drift.
- Agents sometimes report skill *totals* instead of *ranks* — sanity-check outliers against
  the rank cap (HD+3) before trusting them.

## Reading incomplete builds

**"Class X doesn't exist" claims from agents are usually wrong.** `next-step` hides
prerequisite-gated drivers, indistinguishably from missing content. Before filing a gap:
check the baseline entry's `replayWarnings` (a "prerequisite not met for X" there means the
*source character* never qualified and the engine is right), and/or build a minimal qualified
probe character by scripted ticks and confirm `next-step` offers the class. Genuine rule
conflicts (e.g. a fixed-alignment race in an alignment-restricted class) are source-data
findings, not bugs — record them in the private repo's `CONTENT_GAPS.md` so nobody "fixes"
content to make them build.

## Filing what you find

API behavior → `KNOWN_ISSUES.md` (check the existing "Agent-facing API issues" section first —
most silent-accept traps are already known). SRD content gaps → `CONTENT_GAPS.md` (public).
Third-party/campaign content gaps → `{EXTRA_PACKS_PATH}/CONTENT_GAPS.md` (private repo —
non-OGC references must not land in the public repo).
