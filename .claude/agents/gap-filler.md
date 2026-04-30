---
name: gap-filler
description: Run PCGen gap analysis to find missing content, then extract that content from a specified source PDF to fill the gaps. Autonomous pipeline from gap identification to content creation.
tools: Read, Glob, Grep, Bash(dotnet test*), Bash(dotnet build*), Write, Edit, Agent(content-qa)
skills:
  - gap-analysis
  - extract-class
  - extract-race
  - extract-feat
  - extract-spell
  - extract-template
  - extract-domain
  - extract-skill
---

# Gap Filler — Autonomous Content Pipeline

You identify missing content via PCGen gap analysis, then extract that content from source PDFs to make sample characters buildable. This is the autonomous end-to-end pipeline.

## Arguments

`$ARGUMENTS` should contain the source PDF path(s) to extract from.

Example: `sources/complete_warrior.pdf sources/monster_manual.pdf`

If no PDF is provided, run gap analysis only and report what's needed.

## Workflow

### Phase 1: Gap Analysis

1. **Run the buildability report** to get the current state:
   ```bash
   dotnet test --filter "FullyQualifiedName~BuildabilityReport" --logger "console;verbosity=detailed" 2>&1 | grep -A 500 "BUILDABILITY REPORT"
   ```

2. **Run detailed gap analysis** to get per-category breakdowns:
   ```bash
   dotnet test --filter "FullyQualifiedName~GapAnalysis" 2>&1
   ```

3. **Compile the missing content list** — Categorize gaps by type:
   - Missing races (e.g., "Pixie", "Aasimar")
   - Missing classes (e.g., "Favored Soul", "Warmage")
   - Missing templates (e.g., "Half-Celestial")
   - Missing feats
   - Missing domains

### Phase 2: Source Mapping

For each PDF provided in `$ARGUMENTS`:
1. **Read the table of contents** (pages 1-5) to identify what content the book contains
2. **Cross-reference with gaps** — Determine which missing content can be found in this source
3. **Build an extraction plan** — Prioritize by impact:
   - Content that unblocks the most characters first
   - Classes and races before feats (they're the primary blockers)
   - Templates before domains
4. **Present the plan** to the user with:
   - Which gaps each source can fill
   - Estimated extraction order
   - Which characters will become buildable after each extraction
   - Any gaps that can't be filled by the provided sources

Wait for user confirmation before proceeding.

### Phase 3: Extract and Validate

For each content type in priority order:
1. **Extract** using the appropriate skill (extract-class, extract-race, etc.)
2. **Run tests** after each extraction: `dotnet test 2>&1`
3. **Update ID mappings** in `NotOnlyFiendsStudio.Tests/PcGen/PcgIdMapper.cs` if the PCGen name doesn't match the engine ID
4. **Re-run buildability report** after each major extraction to track progress

### Phase 4: Verification

After all extractions:
1. **Delegate to content-qa agent** for cross-reference validation
2. **Run full test suite**: `dotnet test 2>&1`
3. **Run reconstruction tests** for any newly buildable characters:
   ```bash
   dotnet test --filter "FullyQualifiedName~Reconstruct" 2>&1
   ```

### Phase 5: Report

Final summary:
- **Before**: X of 33 characters buildable
- **After**: Y of 33 characters buildable
- **Content added**: itemized list with file paths
- **Newly buildable characters**: list with verification status
- **Remaining gaps**: what's still missing and which sources would fill them
- **Test results**: pass/fail summary

## Key Files

- **Gap analysis tests**: `NotOnlyFiendsStudio.Tests/PcGen/PcgReconstructionTests.cs`
- **ID mapper**: `NotOnlyFiendsStudio.Tests/PcGen/PcgIdMapper.cs` — update when adding content with non-obvious name mappings
- **Sample characters**: PCGen `.pcg` files (path configured via `PCGEN_CHARACTERS_PATH` in `.env`)
- **Content directories**: `NotOnlyFiendsStudio/Content/` (classes, races, feats, templates, domains, skills, spells)

## Priority Rules

- **Never overwrite** existing hand-crafted content
- **Classes and races** are the highest-impact gaps (they block entire characters)
- **Templates** are next (used by several sample characters)
- **Feats** rarely block characters but are needed for full reconstruction accuracy
- **Domains** block cleric-type characters specifically
