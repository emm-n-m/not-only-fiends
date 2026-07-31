---
name: extract-equipment
description: Extract D&D 3.5e weapons, armor, shields, gear, magic items, cursed items, or artifacts from SRD HTML or supplement PDFs into validated EquipmentDefinition JSON. Use when adding or correcting equipment catalog content.
---

# Extract Equipment

Extract equipment for the NotOnlyFiendsStudio content pipeline. Treat the source, schema, and
equipment extraction prompt as authoritative.

## Workflow

1. Read `schemas/equipment.schema.json` and `schemas/prompts/extract-equipment.md` completely.
2. Prefer local SRD HTML under `NotOnlyFiendsStudio/Content/srd_html/`. Use a PDF for a
   supplement and PCGen LST only when the user explicitly selects that source.
3. Determine the requested item IDs. For a broad page with no stated scope, enumerate its
   item anchors/table entries and confirm the batch.
4. Inspect all existing equipment files before assigning IDs or adding variants.
5. Extract:
   - weapons: price, Medium damage, critical, range, weight, type, proficiency;
   - armor/shields: price, armor bonus, max Dex, check penalty, arcane failure, speed, weight;
   - magic items: slot, price, weight, full description, baseline enhancement, and persistent
     mechanics that the engine can represent.
6. Use top-level `enhancementBonus` for a named weapon's magic bonus or cursed penalty.
   Represent typed bonuses with the exact source bonus type.
7. Preserve unsupported, conditional, charged, intelligent, or single-use behavior accurately
   in `description`; do not substitute an always-on permabuff.
8. Write entries to the corresponding pack's `equipment/` directory, grouped by source page
   or coherent category.
9. Add focused catalog and replay assertions, validate JSON/schema, and run `dotnet test`.

## SRD source map

- `weapons.html`, `armor.html`, `goodsAndServices.html`
- `magicItemsAW.html`, `magicItemsPRR.html`, `magicItemsSSW.html`, `magicItemsWI.html`
- `magicItemsICA.html`
- `epicMagicItems.html`, `epicMagicItemsOther.html`, `epicArtifacts.html`

## Conventions

- Prefix IDs by category: `weapon:`, `armor:`, `shield:`, `wondrous:`, `ring:`, `rod:`,
  `staff:`, `wand:`, `scroll:`, `potion:`, `gear:`, or `ammunition:`.
- Store `priceCp` in copper pieces; 1 gp equals 100 cp.
- Store armor `checkPenalty` as a negative number.
- Use the Medium weapon-damage column.
- Emit one definition per finite named/graded variant.
- Do not enumerate generic magic modifiers or spell-derived consumables as thousands of static
  definitions unless the model has first been designed for them.
- Do not truncate composite item descriptions at nested table or subsection anchors.

## References

- `schemas/equipment.schema.json`
- `schemas/prompts/extract-equipment.md`
- `NotOnlyFiendsStudio/Content/packs/srd_core/equipment/`
- `NotOnlyFiendsStudio/Models/Equipment.cs`
