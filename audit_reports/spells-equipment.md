# Public SRD spells and equipment audit

Audit date: 2026-07-31  
Scope: the public packs listed by `content-public.json`, limited to the 617
spell definitions and 903 equipment definitions currently under
`NotOnlyFiendsStudio/Content/packs/srd_core/`. The local
`NotOnlyFiendsStudio/Content/srd_html/` mirror is the sole authority. No
content was edited.

The inventory was structurally checked for malformed profiles, negative
price/weight values, and missing required spell metadata. Source blocks were
then compared for the findings below, including their surrounding prose and
model semantics. `classLevels` accepts domain IDs, so the omitted domain
entries below are representable rather than schema limitations.

| Item | Field | JSON value | SRD value | SRD quote | Severity |
| --- | --- | --- | --- | --- | --- |
| `spell:blacklight` | `classLevels` | `Sor/Wiz 3` | `Darkness 3; Sor/Wiz 3` | `divineNewSpells.html#blacklight`: “Level: Darkness 3, Sor/Wiz 3” | HIGH |
| `spell:blacklight` | `area` | `A 20-ft-radious emanation centered on a creature, object, or a point in space` | `A 20-ft.-radius emanation centered on a creature, object, or point in space` | `divineNewSpells.html#blacklight`: “Area: A 20-ft.-radius emanation centered on a creature, object, or point in space” | LOW |
| `spell:blacklight` | `description` | empty | Missing SRD effect text | `divineNewSpells.html#blacklight`: “The caster creates an area of total darkness. The darkness is impenetrable to normal vision and darkvision, but the caster can see normally within the blacklit area.” | LOW |
| `spell:hardening` | `classLevels` | `Sor/Wiz 6` | `Sor/Wiz 6; Artifice 7` | `divineNewSpells.html#hardening`: “Level: Sor/Wiz 6, Artifice 7” | HIGH |
| `spell:hardening` | `description` | empty | Missing SRD effect text | `divineNewSpells.html#hardening`: “For every two caster levels, increase by 1 the hardness of the material targeted by the spell. This hardness increase improves only the material’s resistance to damage.” | LOW |
| `spell:maddening_scream` | `classLevels` | `Sor/Wiz 8` | `Sor/Wiz 8; Madness 8` | `divineNewSpells.html#maddening-scream`: “Level: Sor/Wiz 8, Madness 8” | HIGH |
| `spell:maddening_scream` | `description` | empty | Missing SRD effect text | `divineNewSpells.html#maddening-scream`: “The effect worsens the Armor Class of the creature by 4, makes Reflex saving throws impossible except on a roll of 20, and makes it impossible to use a shield.” | LOW |

## VERIFIED CLEAN

- `spell:shocking_grasp`: school, descriptor, Sor/Wiz 1 levels, V/S,
  touch target, duration, save, and SR all match `spellsS.html#shocking-grasp`.
  The source says: “Target: Creature or object touched” and “Spell
  Resistance: Yes.”
- `weapon:longsword`: 15 gp, 4 lb., Medium `1d8`, `19–20/x2`, and slashing
  profile match `weapons.html` (the source table reads: “Longsword ... 15 gp
  ... 1d8 ... 19–20/x2 ... 4 lb. ... Slashing”).
- `weapon:greataxe`: 20 gp, 12 lb., Medium `1d12`, `x3`, and slashing profile
  match `weapons.html` (source: “Greataxe ... 20 gp ... 1d12 ... x3 ... 12
  lb. ... Slashing”).
- `armor:full_plate`: 1,500 gp, +8 armor, max Dex +1, check penalty –6, and
  35% arcane spell failure match the `armor.html` table (source: “Full plate
  ... 1,500 gp ... +8 ... 1 ... –6 ... 35%”).
- `ring:protection_1` through `ring:protection_5`: values and prices match
  `magicItemsPRR.html#ring-of-protection`; the source specifies “a deflection
  bonus of +1 to +5 to AC” and lists 2,000/8,000/18,000/32,000/50,000 gp.

## UNVERIFIABLE

None in the findings set. The three missing spell descriptions are
verifiable omissions, not `UNVERIFIABLE`, because their full source prose is
present in the local mirror.

## Proposed focused regression assertions

- Assert `spell:blacklight` exposes `domain:darkness` at level 3 and has the
  corrected radius text.
- Assert `spell:hardening` exposes `domain:artifice` at level 7.
- Assert `spell:maddening_scream` exposes `domain:madness` at level 8.
- When tooltip/content presentation is covered, assert each of the three
  spells has a non-empty description containing its source-supported effect
  summary.
