# Content Gaps — SRD packs

Gaps in the bundled SRD packs, found by the 2026-08 corpus rebuild test (agents rebuilding
saved characters through the API alone) and confirmed against the golden baseline. Everything
here is SRD material; gaps in third-party/campaign packs are tracked in the private materials
repo's `CONTENT_GAPS.md` so this repository stays free of non-OGC references.

The broader extraction backlog (epic magic items, mundane gear, remaining monsters, psionics)
predates this test and still stands; the entries below are the specific items the rebuild test
proved are missing because a real character needed them.

## Enchanted weapon and armor variants

Base items exist (`weapon:longbow`, `weapon:flail_heavy`, `armor:mithral_shirt`) but there is no
content id for enhanced versions — Longbow +1 (Small), Heavy Flail +2 (cold iron), Mithral
Shirt +3 all failed to resolve. `EquipmentEntry` already has `enhancementBonusOverride` /
`priceCpOverride` fields that may cover this; if that is the intended mechanism, it needs to be
documented API-side (none of the 55 test agents discovered it), and material variants (cold
iron, adamantine) still have no representation.

## Unearthed Arcana variant classes

`unearthedCoreClass.html` presents "sixteen variant versions of the standard character classes",
plus one simple variant per class. The table below lists the remaining named variants.

Each variant is its own driver, not a flag on the base class. That is the existing convention
here (the paladin variants, the cloistered cleric) and it is the only shape that can express what
a variant *loses* — the engine has no way to take a granted ability away after the fact.

**Named variants** — PCGen gives each its own class row, so the class name resolves them and no
importer change is needed.

| Variant | Base | Status |
|:--|:--|:--|
| Totem Barbarian (ape, bear, boar, dragon, eagle, horse, jaguar, lion, serpent, wolf) | Barbarian | missing — ten totems, each a different feature swap |
| Bardic Sage | Bard | missing — casts off Intelligence, one bonus divination per level, adds 13 spells to the list |
| Divine Bard | Bard | missing — casts off Wisdom, spells become divine, adds 20 spells to the list |
| Savage Bard | Bard | missing — illiterate, good Fort/Will, alters class skills, removes 6 spells and adds 17 |
| Druidic Avenger | Druid | missing |
| Thug | Fighter | missing |
| Monk Fighting Styles | Monk | missing — several, and UA bars multiclassing between them |
| Urban Ranger | Ranger | missing — also needs the Urban Tracking feat |
| Wilderness Rogue | Rogue | missing |
| Battle Sorcerer | Sorcerer | missing — d8, cleric BAB, one fewer spell known and per day |
| Domain Wizard | Wizard | missing — needs arcane domains, which are themselves unextracted |

**Simple variants** — one per class, each swapping named features for another class's. Completed
variants are omitted. PCGen
records these as an alternate class feature *on the base class*, so extracting the content is only
half the job: each also needs an entry in `PcgIdMapper.ClassSelectingAcf` keyed by the ACF's
PCGen KEY, or the import silently builds the base class and drops the row as an unmatched
selection.

| Gains | Loses | Base | PCGen ACF key | Status |
|:--|:--|:--|:--|:--|
| Favored enemy; archery combat style chain (as ranger) | Rage chain | Barbarian | `Barbarian ~ Favored Enemy` | missing |
| Smite evil or good, aura of courage (as paladin) | Turn undead | Cleric | `Cleric ~ Smite Evil`, `Cleric ~ Smite Good` | missing |
| Monk AC bonus and fast movement; favored enemy, swift tracker, Track (as ranger) | Armor and shield proficiency, all wild shape | Druid | `Druid ~ Monk AC` | missing |
| Sneak attack (as rogue) | Bonus feats | Fighter | `Fighter ~ Sneak Attack` | missing |
| Damage reduction (as barbarian) | Unarmored speed bonus and unarmored AC bonus (Wisdom to AC is kept) | Monk | `Monk ~ Damage Reduction` | missing |
| Favored enemy (as ranger, restricted list) | Lay on hands, turn undead, remove disease | Paladin | `Paladin ~ Favored Enemy` | missing |
| Wild shape (as druid, Small/Medium animals); fast movement (as barbarian) | Combat style chain | Ranger | `Ranger ~ Wild Shape` | missing |
| Bonus feats (as fighter) | Sneak attack | Rogue | `Rogue ~ Fighter Bonus Feats` | missing |
| Animal companion at half class level (as druid) | Familiar | Sorcerer | `Sorcerer ~ Animal Companion` | missing |
| Fighter bonus feat list, at 1st and every five wizard levels | Scribe Scroll, wizard bonus feat list | Wizard | `Wizard ~ Bonus Feat List` | missing |
| Animal companion at half class level (as druid) | Familiar | Wizard | `Wizard ~ Animal Companion` | missing — UA notes wizards may also take the sorcerer variant |

PCGen carries one more that UA's variant chapter does not: `Rage Choice ~ Whirling Frenzy` and
`Rage Choice ~ Hunter`, from the variant-rage rules elsewhere in the book.

**One known LST disagreement.** PCGen encodes the druid-like bard's companion at full bard level
in the bard-variant pool (`BONUS:VAR|CompanionLVL|BardLVL`) and at half in the generic ACF pool
(`BONUS:VAR|AnimalCompanionLVL|BardLVL/2`). The engine follows the full-level reading: UA states
the halving explicitly for the sorcerer and wizard variants and not for the bard.

## Racial HD driver overlap: elementals

Both a generic `racial_hd:elemental` and typed `racial_hd:elemental_air` / `racial_hd:elemental_water`
drivers exist, with different save progressions. An API builder picking the generic driver for a
water elemental gets different saves than the PCG import produced (observed on a small water
elemental familiar: Fort/Ref differed by 3). Either retire the generic driver or make the typed
ones the only offer for typed elemental races.
