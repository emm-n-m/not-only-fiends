# CLAUDE.md

See `AGENTS.md` for project overview, architecture, commands, and design decisions.

## Agents

Custom subagents in `.claude/agents/` for autonomous content pipeline work:

- **content-qa** — Post-extraction validation. Checks content JSON against schemas, validates cross-references (feat/class/race IDs), runs the test suite. Read-only, uses `sonnet` for speed.
- **content-extractor** — Full PDF extraction pipeline. Surveys a book's table of contents, identifies all extractable content types, invokes the appropriate `extract-*` skills for each, then delegates to `content-qa` for validation.
- **gap-filler** — End-to-end autonomous pipeline. Runs PCGen gap analysis to find missing content, cross-references gaps against provided source PDFs, extracts in priority order (classes/races → templates → feats → domains), updates ID mappings, and tracks buildability progress across iterations.

### Skills (invoked by agents or directly via `/skill-name`)

Content extraction skills in `.claude/skills/`: `extract-class`, `extract-race`, `extract-feat`, `extract-spell`, `extract-template`, `extract-domain`, `extract-skill`. Each reads a PDF, parses content, writes JSON to the appropriate `Content/` subdirectory, and runs tests.

`gap-analysis` — Runs PCGen character reconstruction tests to report buildability status and missing content.
