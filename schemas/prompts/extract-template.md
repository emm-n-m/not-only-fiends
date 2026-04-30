# Extract Templates from D&D 3.5e Source Material

## Task

Extract template data from the provided source text and output valid JSON matching the TemplateDriver schema. Output a JSON array (even for a single template).

## Output Format

- Root is a JSON array: `[{ ... }]`
- Use **camelCase** for all property names and enum values
- Omit null/empty optional fields
- The `$type` discriminator field is **required** on all Permabuff objects

## Schema

Use `schemas/template.schema.json` (self-contained, paste it into your context alongside this prompt).

## Critical Rules

### ID Conventions
- Format: `"template:snake_case_name"` (e.g., `template:half_fiend`, `template:half_dragon`, `template:vampire`)

### Three Categories of Template Effects

**1. One-Time (top-level fields + creationPermabuffs)**
Applied once at character creation:
- `typeOverride` — changes creature type (e.g., `"outsider"`)
- `subtypeAdditions` — adds subtypes (e.g., `["native"]`)
- `abilityModifiers` — permanent ability score changes (`{ "str": 4, "dex": 4, "con": 2, "int": 4, "wis": 0, "cha": 2 }`)
- `naturalArmor` — additive natural armor bonus
- `speedModifiers` — movement changes (`{ "fly": 60 }`)
- `naturalAttacks` — natural weapons
- `creationPermabuffs` — any other one-time effects (darkvision, immunities, resistances, special abilities)

**2. Threshold (scalingPermabuffs)**
Fire ONCE when `totalHD` reaches exactly the key value:
- Keys are HD thresholds as strings: `"1"`, `"3"`, `"5"`, `"7"`, etc.
- Used for SLAs that unlock at specific HD (e.g., Half-Fiend gets Darkness at 1 HD, Desecrate at 3 HD)
- Each threshold fires only once — if a character reaches HD 5, the threshold `"5"` fires

**3. Continuous (scalingFormulas)**
Recalculated every tick:
- Used for values that change with HD (e.g., `SR = TotalHD + 10`)
- Uses the Formula DSL with SetAttribute semantics (absolute value, not additive)
- Variables: `TotalHD`, `BaseBAB`, `EffectiveBAB`, `SpellResistance`
- Functions: `Mod(ability)`, `Score(ability)`, `min(a,b)`, `max(a,b)`

### Ability Modifiers
Include ALL six scores (set unmodified ones to 0). Use `null` if no changes:
```json
"abilityModifiers": { "str": 4, "dex": 4, "con": 2, "int": 4, "wis": 0, "cha": 2 }
```

### Natural Attacks
Specify damage for Medium size:
```json
"naturalAttacks": [
  { "name": "Bite", "damage": "1d6", "count": 1, "isPrimary": true },
  { "name": "Claw", "damage": "1d4", "count": 2, "isPrimary": true }
]
```

### SLA Naming
Prefix IDs with a template abbreviation to avoid collisions:
- Half-Fiend: `hf_sla_darkness`, `hf_sla_desecrate`
- Half-Dragon: `hd_sla_sleep`
- Vampire: `vamp_sla_dominate`

### Resistances and Immunities
- Resistances: use `ModifyAttribute` with `target: "resistance"` and `resistanceElement`
- Immunities: use `GrantAbility` with a descriptive ability (the engine tracks these via the Abilities list)
- DR: use `GrantAbility` (damage reduction is displayed, not mechanically tracked by the engine)

## Example: Half-Fiend (comprehensive)

```json
[
{
  "id": "template:half_fiend",
  "name": "Half-Fiend",
  "typeOverride": "outsider",
  "subtypeAdditions": ["native"],
  "abilityModifiers": { "str": 4, "dex": 4, "con": 2, "int": 4, "wis": 0, "cha": 2 },
  "naturalArmor": 1,
  "speedModifiers": { "fly": 60 },
  "levelAdjustment": 4,
  "naturalAttacks": [
    { "name": "Bite", "damage": "1d6", "count": 1, "isPrimary": true },
    { "name": "Claw", "damage": "1d4", "count": 2, "isPrimary": true }
  ],
  "creationPermabuffs": [
    {
      "$type": "GrantAbility",
      "ability": { "id": "hf_darkvision_60", "name": "Darkvision 60 ft.", "description": "Can see in the dark up to 60 feet." }
    },
    { "$type": "ModifyAttribute", "target": "resistance", "resistanceElement": "acid", "value": 10 },
    { "$type": "ModifyAttribute", "target": "resistance", "resistanceElement": "cold", "value": 10 },
    { "$type": "ModifyAttribute", "target": "resistance", "resistanceElement": "electricity", "value": 10 },
    { "$type": "ModifyAttribute", "target": "resistance", "resistanceElement": "fire", "value": 10 },
    {
      "$type": "GrantAbility",
      "ability": { "id": "hf_immunity_poison", "name": "Immunity to Poison", "description": "Immune to all poisons." }
    },
    {
      "$type": "GrantAbility",
      "ability": { "id": "hf_smite_good", "name": "Smite Good", "description": "Once per day, +CHA to attack, +HD to damage (max 20) vs good creature." }
    }
  ],
  "scalingPermabuffs": {
    "1":  [{ "$type": "GrantSLA", "sla": { "id": "hf_sla_darkness", "name": "Darkness", "usesPerDay": "3/day" } }],
    "3":  [{ "$type": "GrantSLA", "sla": { "id": "hf_sla_desecrate", "name": "Desecrate", "usesPerDay": "1/day" } }],
    "5":  [{ "$type": "GrantSLA", "sla": { "id": "hf_sla_unholy_blight", "name": "Unholy Blight", "usesPerDay": "1/day" } }],
    "7":  [{ "$type": "GrantSLA", "sla": { "id": "hf_sla_poison", "name": "Poison", "usesPerDay": "3/day" } }],
    "9":  [{ "$type": "GrantSLA", "sla": { "id": "hf_sla_contagion", "name": "Contagion", "usesPerDay": "1/day" } }],
    "11": [{ "$type": "GrantSLA", "sla": { "id": "hf_sla_blasphemy", "name": "Blasphemy", "usesPerDay": "1/day" } }],
    "13": [{ "$type": "GrantSLA", "sla": { "id": "hf_sla_unholy_aura", "name": "Unholy Aura", "usesPerDay": "3/day" } }],
    "15": [{ "$type": "GrantSLA", "sla": { "id": "hf_sla_haste", "name": "Haste", "usesPerDay": "3/day" } }],
    "17": [{ "$type": "GrantSLA", "sla": { "id": "hf_sla_horrid_wilting", "name": "Horrid Wilting", "usesPerDay": "1/day" } }],
    "19": [{ "$type": "GrantSLA", "sla": { "id": "hf_sla_destruction", "name": "Destruction", "usesPerDay": "1/day" } }]
  },
  "scalingFormulas": [
    { "target": "spellResistance", "formula": { "expression": "TotalHD + 10" } }
  ]
}
]
```

## Source Text

[Paste D&D 3.5e template description here]
