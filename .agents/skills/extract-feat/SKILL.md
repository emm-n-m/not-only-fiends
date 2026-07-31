---
name: extract-feat
description: Extract D&D 3.5e feats from SRD HTML or supplement PDFs into validated FeatDefinition JSON. Use when adding feats, restoring prerequisites, classifying feat types, or modeling persistent feat effects.
---

# Extract Feats

Extract feat content for the NotOnlyFiendsStudio content pipeline. Ground every field in the
selected source.

## Workflow

1. Read `schemas/feat.schema.json` and `schemas/prompts/extract-feat.md` completely.
2. Prefer SRD HTML under `NotOnlyFiendsStudio/Content/srd_html/`; use PDF only for supplements
   without an HTML source.
3. Resolve the requested feats. If scope is broad or omitted, enumerate anchors and confirm
   the intended batch.
4. Parse each heading/category, prerequisite block, benefit, normal rule, and special text.
5. Convert every expressible prerequisite into a typed prerequisite. If a requirement cannot
   be represented exactly, report it and preserve it in the description; do not silently
   omit or weaken it.
6. Set `repeatable` only when the source says the feat can be taken more than once.
7. Add `grantedPermabuffs` only for persistent mechanics represented accurately by the engine.
8. Write core feats under `NotOnlyFiendsStudio/Content/packs/srd_core/feats/`, grouped by the
   repository's existing category files. Write supplement feats to their own pack.
9. Add focused prerequisite/type assertions and run focused tests, strict validation, then
   `dotnet test`.

## SRD source map

- `featsAll.html`, `featsGen.html`, `featsFtb.html`
- `featsItc.html`, `featsMtm.html`, `divineFeats.html`
- `epicFeats.html`, `monsterFeats.html`, `psionicFeats.html`

## Prerequisite conventions

- Ability minimum: `MinAbility`
- BAB minimum: `MinBAB`
- Skill ranks: `MinSkillRanks` using whole printed ranks
- Class level: `MinClassLevel`
- Hit Dice: `MinHD`
- Spell level: `CanCastSpellLevel`
- A named feat: `HasFeat`
- Multiple selections from a feat family: `HasFeatSelections`

Use the actual schema/model for the complete current prerequisite set; do not rely on a stale
hard-coded list in this skill.

## References

- `schemas/feat.schema.json`
- `schemas/prompts/extract-feat.md`
- `NotOnlyFiendsStudio/Models/Prerequisite.cs`
- `NotOnlyFiendsStudio/Content/packs/srd_core/feats/`
