# CLAUDE.md

See `AGENTS.md` for project overview, architecture, commands, and design decisions.

## Agents

Custom subagents in `.claude/agents/` for autonomous content pipeline work:

- **content-qa** — Post-extraction validation. Checks content JSON against schemas, validates cross-references (feat/class/race IDs), runs the test suite. Read-only, uses `sonnet` for speed.
- **content-extractor** — Full PDF extraction pipeline. Surveys a book's table of contents, identifies all extractable content types, invokes the appropriate `extract-*` skills for each, then delegates to `content-qa` for validation.
- **gap-filler** — End-to-end autonomous pipeline. Runs PCGen gap analysis to find missing content, cross-references gaps against provided source PDFs, extracts in priority order (classes/races → templates → feats → domains), updates ID mappings, and tracks buildability progress across iterations.

### Skills (invoked by agents or directly via `/skill-name`)

**Adding content** — `extract-class`, `extract-race`, `extract-feat`, `extract-spell`, `extract-template`, `extract-domain`, `extract-skill`, `extract-equipment`. Each reads a source (HTML preferred, PDF fallback), parses content, writes JSON to the appropriate `Content/` subdirectory, and runs tests.

**Auditing what exists** —

- `verify-content` — Diffs content JSON against the SRD HTML mirror and reports mismatches with quoted sources. Report-only; never edits. Ground truth is the mirror, never model recall — content with no SRD page is explicitly out of scope. Tier 1 (all 48 public-pack drivers) done 2026-07-27; races/feats/domains and spells remain.
- `verify-content-lst` — The private-pack sibling of `verify-content`: diffs extra-pack JSON against the PCGen LST data set (`PCGEN_DATA_PATH`). LSTs are community transcriptions, so findings carry a JSON-BUG / LST-SUSPECT / UNRESOLVABLE verdict. The two Fiendish Codex packs have no LSTs and are audited against the PDFs at `SOURCE_PDFS_PATH` instead.
- `audit-cosmetic-permabuffs` — Finds content whose mechanics live only in a `GrantAbility` description and were never encoded, so the engine treats them as flavour text. Compares each description against the permabuffs beside it, not against any source, and reports CONTENT-BUG / ENGINE-GAP / BY-DESIGN. A Codex-targeted version of the same procedure lives at `.codex/prompts/audit-cosmetic-permabuffs.md`; keep the two in step.
- `audit-agent-api` — Builds a character end-to-end using only the REST API, to find discoverability gaps, silently-accepted illegal input, and oversized payloads. Run after API or engine changes.
- `gap-analysis` — Runs PCGen character reconstruction tests to report buildability status and missing content.

Outstanding work from these audits is tracked in [KNOWN_ISSUES.md](KNOWN_ISSUES.md). Content
gaps are split by licensing: [CONTENT_GAPS.md](CONTENT_GAPS.md) tracks SRD-pack gaps here;
the private materials repo's `CONTENT_GAPS.md` tracks extra-pack gaps so non-OGC references
stay out of this repository.
