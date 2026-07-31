---
name: extract-class
description: Extract D&D 3.5e base classes, prestige classes, NPC classes, or racial HD drivers from SRD HTML or supplement PDFs into validated HDDriver JSON. Use when adding or reconstructing class progression content.
---

# Extract Classes

Extract class content for the NotOnlyFiendsStudio content pipeline. Treat the source, schema,
and extraction prompt as authoritative; do not fill rules gaps from memory.

## Workflow

1. Read `schemas/hddriver.schema.json` and `schemas/prompts/extract-class.md` completely.
2. Resolve the source:
   - Prefer the local SRD HTML under `NotOnlyFiendsStudio/Content/srd_html/`.
   - Use PDF only for supplements without an HTML source.
   - For racial HD, use `monsterTypes.html` and the creature entry.
   - If no source or content IDs are supplied and scope cannot be inferred, ask for them.
3. Inspect existing destination content and avoid duplicate IDs.
4. Extract hit die, skill points, class skills, maximum level, BAB/save progressions,
   prerequisites, spellcasting progression, and every level feature.
5. Derive BAB and saves from the source table. Verify both the first-level row and the final
   progression rather than relying on the class's apparent archetype.
6. Represent persistent mechanics with structured permabuffs. Use `GrantAbility` only when
   the engine has no more specific representation, and preserve the source rule in its body.
7. Write one JSON array file per class:
   - base classes: `NotOnlyFiendsStudio/Content/packs/srd_core/classes/base/`
   - prestige classes: `NotOnlyFiendsStudio/Content/packs/srd_core/classes/prestige/`
   - NPC classes: `NotOnlyFiendsStudio/Content/packs/srd_core/classes/npc/`
   - racial HD: `NotOnlyFiendsStudio/Content/packs/srd_core/racial_hd/`
   - supplements: the corresponding pack directory
8. Add focused rules/content assertions for mechanically important extraction decisions.
9. Run the focused tests, strict content validation, then `dotnet test`.

## Source landmarks

- Base classes: `barbarian.html`, `bard.html`, `cleric.html`, `druid.html`, `fighter.html`,
  `monk.html`, `paladin.html`, `ranger.html`, `rogue.html`, `sorcerer.html`, `wizard.html`.
- Prestige classes use dedicated camel-case files such as `eldritchKnight.html`,
  `mysticTheurge.html`, and `shadowdancer.html`.
- NPC classes share `npcClasses.html`.
- Creature types and racial HD defaults live in `monsterTypes.html`.

## Conventions

- Use `class:<snake_case>` and `racial_hd:<snake_case>` IDs.
- Use `kind: "Class"` or `kind: "RacialHD"`.
- Put `$type` on every polymorphic prerequisite and permabuff.
- Author `MinSkillRanks.value` in whole ranks; replay doubles it internally.
- `HasFeat` on a selectable base feat also matches its `{featId}_*` selections.
- Use `UpdateSpellcasting` for a base class's own progression and `AdvanceSpellcasting` only
  where the source advances an existing class.

## References

- `schemas/hddriver.schema.json`
- `schemas/prompts/extract-class.md`
- `NotOnlyFiendsStudio/Models/Driver.cs`
- `NotOnlyFiendsStudio/Models/Permabuff.cs`
