---
name: extract-skill
description: Extract D&D 3.5e skills and their subspecialties or synergies from SRD HTML or supplement PDFs into validated SkillDefinition JSON. Use when adding skills or correcting key abilities, trained-only flags, armor-check penalties, or synergy data.
---

# Extract Skills

Extract skill definitions for the NotOnlyFiendsStudio content pipeline.

## Workflow

1. Read `schemas/skill.schema.json` and `schemas/prompts/extract-skill.md` completely.
2. Prefer local SRD HTML:
   - `skillsAll.html` for complete entries;
   - `skillsStr.html`, `skillsDex.html`, `skillsCon.html`, `skillsInt.html`,
     `skillsWis.html`, and `skillsCha.html` for ability shards;
   - `epicSkills.html` and `psionicSkills.html` for those extensions.
   Use PDF only for supplements without HTML.
3. Resolve the requested skills. If scope is omitted, enumerate the source anchors and confirm
   the batch.
4. Parse the canonical name/anchor, key ability, trained-only flag, armor-check-penalty flag,
   check/action/retry text, and synergies.
5. Expand Craft, Knowledge, Perform, and Profession subspecialties according to the source and
   repository conventions. Set `parentSkill` to the umbrella.
6. Cross-check new IDs against every existing `classSkills`, racial-HD skill list, feat
   prerequisite, and PCGen mapping before writing.
7. Write core definitions to
   `NotOnlyFiendsStudio/Content/packs/srd_core/skills/srd.json`; write supplements to their
   pack's `skills/` directory.
8. Add focused assertions for flags, parent umbrellas, and synergies. Run focused tests,
   strict validation, then `dotnet test`.

## Conventions

- Skill IDs: `skill:<snake_case>`.
- Subspecialty IDs: `<parent>_<subcategory>`, such as `knowledge_arcana`.
- `parentSkill` values are bare umbrella names used by class-skill expansion.
- Preserve source distinctions among a standalone skill, a parent umbrella, and a named
  subspecialty.

## References

- `schemas/skill.schema.json`
- `schemas/prompts/extract-skill.md`
- `NotOnlyFiendsStudio/Content/packs/srd_core/skills/srd.json`
- `NotOnlyFiendsStudio/Models/Skill.cs`
