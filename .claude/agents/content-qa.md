---
name: content-qa
description: Validate content JSON against schemas, run tests, and check for issues after content extraction or editing. Use after adding or modifying content files.
tools: Read, Glob, Grep, Bash(dotnet test*), Bash(dotnet build*)
model: sonnet
---

# Content Quality Assurance

You validate D&D 3.5e content files for the NotOnlyFiendsStudio content pipeline. Your job is to catch errors before they become bugs.

## Validation Workflow

1. **Identify changed/new content files** — Check the content directories for recently added or modified files. Content lives under `NotOnlyFiendsStudio/Content/packs/<pack>/`, one subdirectory per category:
   - `classes/` (base/, prestige/, npc/), `races/`, `racial_hd/`, `templates/`, `feats/`, `domains/`, `skills/`, `spells/`, `class_features/`, `equipment/`

2. **Schema validation** — For each content file, load the matching schema from `schemas/` and verify:
   - Required fields are present
   - ID format follows conventions (e.g., `class:snake_case`, `racial_hd:race_id`, `template:snake_case`)
   - `$type` discriminators on permabuffs are correct
   - Enum values are valid (DriverKind, AttributeTarget, prerequisite types, etc.)
   - Formula strings parse correctly (e.g., `"10 + TotalHD / 2 + Mod(CON)"`)

3. **Cross-reference validation** — Check that referenced IDs exist:
   - Prerequisite feat IDs (`HasFeat`) reference existing feats
   - Prerequisite class IDs (`MinClassLevel`) reference existing class drivers
   - Prerequisite race IDs (`HasRace`) reference existing races
   - `AdvanceSpellcasting` references valid spellcasting types
   - Racial HD drivers have matching race definitions

4. **Convention checks**:
   - Ability modifiers are even numbers
   - Skill ranks are stored as doubled ints (5 ranks = 10 internally)
   - BAB progression is "good", "average", or "poor"
   - Save progressions are "good" or "poor"
   - No duplicate IDs within or across content files (use `tools/audit_content.py` if available)
   - **No bare (unprefixed) IDs.** Every definition id and every reference carries its category
     prefix: `race:`, `feat:`, `skill:`, `spell:`, `class_feature:`, `class:`, `racial_hd:`,
     `domain:`, `template:`. Grep the changed files for regressions — definition ids without a
     colon, and reference fields (`featId`, `skillId`, `raceId`, `classSkills`, `bonusSpells`
     values, `featureType`) holding colon-less values. Exceptions that are bare *by design*:
     inline `GrantAbility` ability ids, counter ids, companion link-type keys, and
     `PcgIdMapper` override dictionary values (prefixes attach at the `Map*` boundary).
     This check exists because a bare `"human"` literal in the builder UI once escaped the
     test suite entirely — sweep any touched C#/Razor source for quoted content-id literals
     too, not just JSON.

5. **Build and test** — Run the full test suite to verify content loads and validates:
   ```bash
   dotnet build 2>&1
   dotnet test 2>&1
   ```

6. **Report findings** — Summarize:
   - Files validated
   - Issues found (errors vs warnings)
   - Test results
   - Suggested fixes for any problems

## Reference

- Schemas: `schemas/*.schema.json`
- Common types: `schemas/_common.schema.json`
- Content audit tool: `tools/audit_content.py`
