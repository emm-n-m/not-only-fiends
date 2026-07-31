# Extract Equipment from D&D 3.5e Source Material

## Task

Extract equipment (weapons, armor, shields, magic items, gear) from the provided source and output valid JSON matching the EquipmentDefinition schema. Output a JSON array.

## Output Format

- Root is a JSON array: `[{ ... }, { ... }]`
- Use **camelCase** for all property names and enum values
- Omit null/empty optional fields
- The `$type` discriminator field is **required** on all Permabuff objects

## Schema

Use `schemas/equipment.schema.json` (self-contained).

## Critical Rules

### ID Conventions

`<category>:<snake_case>` — examples:
- `weapon:longsword`, `weapon:dagger`, `weapon:greatsword`, `weapon:composite_longbow`
- `armor:full_plate`, `armor:chain_shirt`, `armor:padded`
- `shield:heavy_steel`, `shield:tower`
- `ring:protection_2`, `ring:protection_3`
- `wondrous:cloak_of_resistance_3`, `wondrous:amulet_natural_armor_1`, `wondrous:gauntlets_ogre_power`
- `potion:cure_light_wounds`, `rod:metamagic_extend`, `staff:fire`, `wand:magic_missile`, `scroll:fireball`
- `gear:backpack`, `gear:bedroll`, `gear:rope_silk_50ft`

For magic items with a numeric grade, suffix the number: `wondrous:cloak_of_resistance_1`, `wondrous:cloak_of_resistance_5`.

### Categories

Pick the most specific category:
- `weapon` — anything that produces a weapon profile (melee, ranged, thrown)
- `armor` — body armor (light/medium/heavy)
- `shield` — held shield (small, large, tower)
- `wondrous` — wondrous items (cloaks, amulets, gauntlets, headbands, periapts, bracers)
- `ring` — magic rings
- `rod`, `staff`, `wand`, `scroll`, `potion` — consumables and magic devices
- `gear` — mundane equipment
- `ammunition` — arrows, bolts, bullets, alchemical splash weapons
- `other` — anything that doesn't fit

### Slot

Map to the body slot used by the UI:
- `head`, `eyes`, `neck`, `shoulders`, `body`, `torso`, `waist`, `hands`, `ring`, `feet`
- `weapon` for held weapons; `shield` for shields
- Omit (`null`) for slotless items, consumables, and ammunition

### Price (in copper pieces)

The schema stores `priceCp` as copper pieces. 1 gp = 100 cp; 1 sp = 10 cp.
- "5 gp" → `priceCp: 500`
- "2 sp" → `priceCp: 20`
- "1,500 gp" → `priceCp: 150000`

### Weapons (`weapon` field)

Populate `weapon: { ... }` for category `weapon`. Read the source's Medium-size damage column (we don't track size-specific damage yet).

```json
{
  "id": "weapon:longsword",
  "name": "Longsword",
  "category": "weapon",
  "slot": "weapon",
  "weightLbs": 4,
  "priceCp": 1500,
  "weapon": {
    "damage": "1d8",
    "critRangeLow": 19,
    "critMultiplier": 2,
    "damageType": "slashing",
    "proficiency": "martial"
  }
}
```

For ranged: set `ranged: true`, `rangeFt`, and (if thrown) `thrown: true`.

### Armor (`armor` field)

Populate `armor: { ... }` for category `armor` or `shield`. Use **negative** `checkPenalty` (e.g., `-6` for full plate). Speed30/Speed20 are reduced speeds in heavy/medium armor for base 30/20 land speed.

```json
{
  "id": "armor:full_plate",
  "name": "Full Plate",
  "category": "armor",
  "slot": "body",
  "weightLbs": 50,
  "priceCp": 150000,
  "armor": {
    "kind": "heavy",
    "armorBonus": 8,
    "maxDex": 1,
    "checkPenalty": -6,
    "arcaneFailurePct": 35,
    "speed30": 20,
    "speed20": 15
  }
}
```

### Magic items — `grantedPermabuffs`

Translate item powers into permabuffs. Common mappings:

| Item type | Permabuff |
|---|---|
| Cloak of Resistance +N | `GrantTypedBonus(target=allSaves, bonusType=resistance, value=N)` |
| Ring of Protection +N | `GrantTypedBonus(target=ac, bonusType=deflection, value=N)` |
| Amulet of Natural Armor +N | `GrantTypedBonus(target=ac, bonusType=naturalEnhancement, value=N)` |
| Bracers of Armor +N | `GrantTypedBonus(target=ac, bonusType=armor, value=N)` (treats as worn armor) |
| Gauntlets of Ogre Power | `GrantTypedBonus(target=abilityStr, bonusType=enhancement, value=2)` |
| Gloves of Dexterity +N | `GrantTypedBonus(target=abilityDex, bonusType=enhancement, value=N)` |
| Belt of Giant Strength +N | `GrantTypedBonus(target=abilityStr, bonusType=enhancement, value=N)` |
| Headband of Intellect +N | `GrantTypedBonus(target=abilityInt, bonusType=enhancement, value=N)` |
| Periapt of Wisdom +N | `GrantTypedBonus(target=abilityWis, bonusType=enhancement, value=N)` |
| Cloak of Charisma +N | `GrantTypedBonus(target=abilityCha, bonusType=enhancement, value=N)` |
| Amulet of Health +N | `GrantTypedBonus(target=abilityCon, bonusType=enhancement, value=N)` |
| Magic weapon +N | Top-level `enhancementBonus: N`; special abilities such as flaming remain in the description until they have dedicated mechanics |

`Formula` values are strings — `{ "expression": "3" }` for a flat number.

### Bonus types — stacking

3.5e rule: only the *highest* bonus of each type applies, except **Dodge** and **Untyped** (both stack with everything). The engine handles stacking automatically; you just need to label each bonus with its correct `bonusType`.

### Examples

```json
[
  {
    "id": "wondrous:cloak_of_resistance_3",
    "name": "Cloak of Resistance +3",
    "category": "wondrous",
    "slot": "shoulders",
    "weightLbs": 1,
    "priceCp": 900000,
    "description": "Grants +3 resistance bonus on all saves.",
    "grantedPermabuffs": [
      {
        "$type": "GrantTypedBonus",
        "target": "allSaves",
        "bonusType": "resistance",
        "value": { "expression": "3" }
      }
    ]
  },
  {
    "id": "wondrous:gauntlets_ogre_power",
    "name": "Gauntlets of Ogre Power",
    "category": "wondrous",
    "slot": "hands",
    "weightLbs": 4,
    "priceCp": 400000,
    "description": "+2 enhancement bonus to Strength.",
    "grantedPermabuffs": [
      {
        "$type": "GrantTypedBonus",
        "target": "abilityStr",
        "bonusType": "enhancement",
        "value": { "expression": "2" }
      }
    ]
  }
]
```

## Source Text

[Paste D&D 3.5e equipment descriptions here]
