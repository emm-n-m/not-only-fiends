---
name: extract-spell
description: Extract D&D 3.5e spells from SRD HTML or supplement PDFs into validated SpellDefinition JSON. Use when adding spells or correcting class/domain levels, schools, components, range, targets, duration, saves, spell resistance, or descriptions.
---

# Extract Spells

Extract spell content for the NotOnlyFiendsStudio pipeline. The selected source is authoritative
for both the stat block and class/domain assignments.

## Workflow

1. Read `schemas/spell.schema.json` and `schemas/prompts/extract-spell.md` completely.
2. Prefer the local alphabetical SRD shards under
   `NotOnlyFiendsStudio/Content/srd_html/`: `spellsAtoB.html`, `spellsC.html`,
   `spellsDtoE.html`, `spellsFtoG.html`, `spellsHtoL.html`, `spellsMtoO.html`,
   `spellsPtoR.html`, `spellsS.html`, and `spellsTtoZ.html`.
3. Use class/domain spell-index pages to corroborate lists, not as a substitute for the full
   spell stat block. Use PDFs only for supplements without HTML.
4. Resolve requested spell IDs. Confirm scope before extracting an entire shard.
5. Parse school, subschool, descriptors, every class/domain level, components, casting time,
   range, target/area/effect, duration, saving throw, spell resistance, and full description.
6. Expand `Sor/Wiz N` to both sorcerer and wizard. Preserve alternative components such as
   `M/DF` accurately; if the model cannot express a source distinction, report it rather than
   flattening it silently.
7. Inspect all existing spell files before choosing IDs. Keep established repository
   normalization for reversed qualifiers and possessives.
8. Write one JSON array file per spell under the pack's `spells/` directory.
9. Add focused assertions for assignments and unusual stat-block fields. Run focused tests,
   strict validation, then `dotnet test`.

## Conventions

- Spell IDs: `spell:<snake_case>`.
- Class IDs: `class:<snake_case>`; domain IDs: `domain:<snake_case>`.
- Preserve SRD range wording and dismissible `(D)` duration markers.
- Keep exactly one of target, area, or effect when the schema/source calls for that shape.
- Never guess a class/domain level from spell power or edition memory.

## References

- `schemas/spell.schema.json`
- `schemas/prompts/extract-spell.md`
- `NotOnlyFiendsStudio/Content/packs/srd_core/spells/`
- `NotOnlyFiendsStudio/Models/Spell.cs`
