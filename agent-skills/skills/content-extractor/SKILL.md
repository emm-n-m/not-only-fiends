---
name: content-extractor
description: Orchestrate full content extraction from a D&D 3.5e source PDF. Reads the PDF, identifies all extractable content types (classes, races, feats, spells, templates, domains, skills), and extracts each type using the appropriate skill.
---

# Content Extractor — Full PDF Pipeline

You extract all D&D 3.5e content from a source PDF into engine-ready JSON files. You orchestrate the full pipeline: survey the PDF, identify what content it contains, extract each type, and validate the results.

## Arguments

The request should provide the PDF path and may optionally name content types or page ranges.

Examples:
- `sources/complete_warrior.pdf` — extract everything from the book
- `sources/complete_warrior.pdf classes feats` — extract only classes and feats
- `sources/complete_warrior.pdf classes:10-45 feats:71-120` — specific page ranges per type

## Workflow

### Phase 1: Survey

1. **Read the PDF table of contents** (typically pages 1-5) to identify chapters and page ranges.
2. **Catalog what's available** — Map chapters to content types:
   - Class chapters → `extract-class`
   - Race/monster chapters → `extract-race`
   - Feat chapters → `extract-feat`
   - Spell lists → `extract-spell`
   - Template entries → `extract-template`
   - Domain descriptions → `extract-domain`
   - Skill descriptions → `extract-skill`
3. **Check existing content** — Scan `NotOnlyFiendsStudio/Content/` to identify what's already extracted from this source, to avoid duplicate work.
4. **Present the extraction plan** to the user: which content types, page ranges, and estimated count. Wait for confirmation before proceeding.

### Phase 2: Extract

For each content type identified in Phase 1, invoke the corresponding extraction skill with the PDF path and page range. Work through them sequentially — each skill will:
- Read the relevant pages
- Parse content into JSON matching the schema
- Write the output file to the correct content directory
- Run `dotnet test` to verify

Follow the conventions and workflow defined in each skill's SKILL.md. The schemas and extraction prompts in `schemas/` are the authoritative reference.

### Phase 3: Validate

After all extractions are complete:
1. **Run the full test suite**: `dotnet test 2>&1`
2. **Run the `content-qa` skill** as a separate validation pass, either in the current context or
   through an independent worker when the host supports one
3. **Run gap analysis** to see if the new content unblocks any PCGen sample characters:
   ```bash
   dotnet test --filter "FullyQualifiedName~BuildabilityReport" --logger "console;verbosity=detailed" 2>&1 | grep -A 500 "BUILDABILITY REPORT"
   ```

### Phase 4: Report

Summarize what was extracted:
- Content files created (with paths)
- Number of items per type (e.g., "12 feats, 3 prestige classes, 1 template")
- Test results
- Any PCGen characters unblocked by the new content
- Issues or items that need manual review

## Source File Naming

Output files should be named after the source book in snake_case:
- `complete_warrior.json`
- `fiendish_codex_1.json`
- `monster_manual.json`

## Key Conventions

- All content files are `List<T>` JSON arrays (top-level array, not object)
- IDs follow the conventions in the relevant `extract-*` skill and JSON schema under `schemas/`
- Never overwrite existing hand-crafted content — PCGen-converted or hand-crafted files take priority
- If a source contains content that overlaps with existing files, note the overlap and skip those items
