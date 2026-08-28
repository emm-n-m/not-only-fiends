---
name: pcg-rebuild-regression
description: Corpus-scale agent regression — fan out one agent per .pcg to rebuild every saved character through the REST API and diff the resulting sheets against the PCG import golden baseline. The agent-driven sibling of pcg-baseline. Use before publishing or after major API/engine changes.
---

# PCG Rebuild Regression (agent-driven)

Where [pcg-baseline](../pcg-baseline/SKILL.md) replays `.pcg` files through the import
pipeline, this test rebuilds each character **through the public API only** — one independent
worker per character when the host supports it, or equivalent sequential batches otherwise.
Each worker uses the `.pcg` for *what* to pick, with the API as sole authority on *how*.
It exercises the project's core claim (an agent can drive `/api/*` unaided) and catches both
API regressions and content drift the import path can't see.

Store artifacts, per-character reports, and comparison output under a dated directory in
`test-reports/` (gitignored because results name private campaign characters). Compare against
the committed golden baseline, not against a previous session's scoreboard.

## Prerequisites

- `.env` with `PCGEN_CHARACTERS_PATH` (the corpus) and `EXTRA_PACKS_PATH` (private packs +
  the baseline at `{EXTRA_PACKS_PATH}/test-reports/pcg_import_report.json`, UTF-8 BOM —
  parse with `encoding='utf-8-sig'`). Only the machine with the private corpus can run this.
- App on a spare port per [run-app](../run-app/SKILL.md); confirm `/api/health` lists the
  private packs.
- **Copy the `.pcg` files into the session scratchpad first** — independent workers may hit permission
  walls reading `/mnt/c/...` directly.

## Method

1. Pilot ONE mid-complexity character (a single-class caster with domains) and diff it before
   fanning out — every harness bug shows up in the pilot for 2% of the cost.
2. **Split planning from building** — the 2026-08 runs showed the two need different tiers.
   *Building* (driving the API tick loop from a known plan) is cheap-tier work: sonnet/luna
   is very sufficient, ~15 concurrent is fine (the app handles parallel builds).
   *Reconstruction planning* (reverse-engineering a `.pcg`'s fixed feat/spell lists into a
   legal tick schedule — spell pacing, prestige skill-rank prereqs, feat-slot budgets) needs
   a flagship model (Opus/GPT top tier), and flagship agents must run per-character or they
   blow usage limits. Cheap tiers asked to plan complete builds structurally but leave
   resources unspent, mis-schedule spells, and miss prestige prerequisites. For characters
   with prestige entries or nontrivial casting, have the flagship produce the per-level
   schedule and hand it to a cheap builder; simple characters (companions, low-HD martials)
   need no separate planning pass.
   Each agent gets the
   [api-build-character](../api-build-character/SKILL.md) protocol, its `.pcg` path, and must:
   name the character `API Test - <name>`, use the `.pcg`'s per-level HP rolls, attach the
   `.pcg`'s equipment, never substitute unfindable content (record it as unresolved instead),
   report sheet values via structured output
   (`built/hp/bab/saves/skillRanks/feats/classLevels/casterLevels/unresolved/friction/apiBugs/deleted`),
   and DELETE the character even on failure.
3. Diff the structured results against the PCG import golden baseline.
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
