# Content Gaps — SRD packs

Gaps in the bundled SRD packs, found by the 2026-08 corpus rebuild test (agents rebuilding
saved characters through the API alone) and confirmed against the golden baseline. Everything
here is SRD material; gaps in third-party/campaign packs are tracked in the private materials
repo's `CONTENT_GAPS.md` so this repository stays free of non-OGC references.

The broader extraction backlog (epic magic items, mundane gear, remaining monsters, psionics)
predates this test and still stands; the entries below are the specific items the rebuild test
proved are missing because a real character needed them.

## Magic item bonus variants

The catalog carries low bonus tiers of several stat/save items but not the higher SRD tiers a
mid-level character actually wears. Rebuilds could not attach:

- Amulet of Natural Armor +5 (higher tiers than the catalog's max)
- Belt of Giant Strength +6
- Bracers of Health +8 and Headband of Intellect +12 (epic tiers — part of the known epic
  magic item backlog)

Each missing tier shows up as a 1–5 point save/HP/AC drift between an API-built character and
the same character imported from PCGen.

## Enchanted weapon and armor variants

Base items exist (`weapon:longbow`, `weapon:flail_heavy`, `armor:mithral_shirt`) but there is no
content id for enhanced versions — Longbow +1 (Small), Heavy Flail +2 (cold iron), Mithral
Shirt +3 all failed to resolve. `EquipmentEntry` already has `enhancementBonusOverride` /
`priceCpOverride` fields that may cover this; if that is the intended mechanism, it needs to be
documented API-side (none of the 55 test agents discovered it), and material variants (cold
iron, adamantine) still have no representation.

## Racial HD driver overlap: elementals

Both a generic `racial_hd:elemental` and typed `racial_hd:elemental_air` / `racial_hd:elemental_water`
drivers exist, with different save progressions. An API builder picking the generic driver for a
water elemental gets different saves than the PCG import produced (observed on a small water
elemental familiar: Fort/Ref differed by 3). Either retire the generic driver or make the typed
ones the only offer for typed elemental races.

## Race data holes

- `race:nymph` — `automaticLanguages` is empty; the SRD nymph speaks Common and Sylvan. Bonus
  language selection is impossible when the race offers nothing.
- `race:sahuagin_mutant` — `naturalAttacks` is empty (talons/bite missing from the race
  record).
