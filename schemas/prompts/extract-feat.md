# Extract Feats from D&D 3.5e Source Material

## Task

Extract feat data from the provided source text and output valid JSON matching the FeatDefinition schema. Output a JSON array (even for a single feat).

## Output Format

- Root is a JSON array: `[{ ... }, { ... }]`
- Use **camelCase** for all property names and enum values
- Omit null/empty optional fields
- The `$type` discriminator field is **required** on all Permabuff and Prerequisite objects

## Schema

Use `schemas/feat.schema.json` (self-contained, paste it into your context alongside this prompt).

## Critical Rules

### ID Conventions
- Use `snake_case`: `power_attack`, `weapon_focus`, `improved_initiative`, `greater_weapon_specialization`
- Keep IDs concise but unambiguous

### Feat Types
- `"general"` — available to any character (Power Attack, Dodge, Improved Initiative)
- `"fighterBonus"` — selectable as fighter bonus feats (Weapon Specialization, Greater Weapon Focus)
- `"metamagic"` — metamagic feats (Empower Spell, Maximize Spell)
- `"itemCreation"` — item creation feats (Craft Wondrous Item, Scribe Scroll)
- `"epic"` — requires 21+ HD (Epic Toughness, Epic Prowess)
- `"divine"` / `"vile"` / `"exalted"` / `"tactical"` / `"other"` — specialized categories

### Prerequisites
Use the matching `$type` for each requirement:
- `MinBAB` — "Base attack bonus +X"
- `MinAbility` — "Strength 13" → `{ "$type": "MinAbility", "ability": "str", "value": 13 }`
- `MinSkillRanks` — "Knowledge (arcana) 5 ranks" → `{ "$type": "MinSkillRanks", "skillId": "knowledge_arcana", "value": 5 }`. **`value` is in whole ranks** — use the number printed in the source as-is. The engine stores ranks as half-ranks internally and doubles at comparison time; do NOT pre-double.
- `MinClassLevel` — "Fighter level 4th" → `{ "$type": "MinClassLevel", "classId": "class:fighter", "value": 4 }`
- `HasFeat` — must already have the feat. For selectable feats (those with `selectionRequired` like `spell_focus`, `weapon_focus`), use the base ID — the prereq matches any selection variant (e.g., `HasFeat spell_focus` is satisfied by `spell_focus_evocation`). Use `HasFeatSelections` only when `minCount ≥ 2` is required.
- `AlignmentReq` — alignment restriction
- `MinHD` — "21st level" (for epic feats) → `{ "$type": "MinHD", "value": 21 }`
- `MinCasterLevel` — "Caster level 5th"
- `CanCastSpellLevel` — "Able to cast 3rd-level spells" → `{ "$type": "CanCastSpellLevel", "spellLevel": 3 }`
- `HasRace` — racial requirement
- `MinSave` — "Base Will save +5" → `{ "$type": "MinSave", "save": "will", "value": 5 }`
- `HasAbility` — "Sneak attack class feature" → `{ "$type": "HasAbility", "abilityId": "sneak_attack" }`
- `HasSpellcasting` — "Ability to cast spells" → `{ "$type": "HasSpellcasting" }` or with type filter: `{ "$type": "HasSpellcasting", "castingType": "arcane" }`
- `HasFeatOfType` — "Any two item creation feats" → `{ "$type": "HasFeatOfType", "featType": "itemCreation", "minCount": 2 }`
- `HasFeatWithTag` — "Any one Abyssal heritor feat" → `{ "$type": "HasFeatWithTag", "tag": "abyssal_heritor", "minCount": 1 }`

### GrantedPermabuffs — Usually Empty
Most feats have `"grantedPermabuffs": []`. Only populate this when the feat grants a **persistent mechanical effect the engine needs to track**:
- Use `ModifyAttribute` for permanent stat bonuses
- Use `GrantAbility` for trackable special abilities
- Do **not** model combat options (Power Attack trade-off, Combat Expertise trade-off) as permabuffs — these are tactical choices, not permanent modifications

### Repeatable Feats
Set `"repeatable": true` for feats that can be taken multiple times (Toughness, Weapon Focus). Most feats are `false`.

### Tags
Use `"tags"` for feat subcategories beyond the `type` enum. Tags enable prerequisites like "any one Abyssal heritor feat":
- `"abyssal_heritor"` — Abyssal Heritor feats (Fiendish Codex I)
- Add new tags as needed for other feat groups
- Omit the field entirely if no tags apply

### Selection Required
Set `"selectionRequired"` when the feat requires choosing something specific:
- `"weapon"` — Weapon Focus, Weapon Specialization, Improved Critical
- `"skill"` — Skill Focus
- `"school"` — Spell Focus, Greater Spell Focus
- Omit the field entirely if no selection is needed

## Examples

```json
[
  {
    "id": "improved_initiative",
    "name": "Improved Initiative",
    "type": "general",
    "prerequisites": [],
    "grantedPermabuffs": [],
    "repeatable": false
  },
  {
    "id": "power_attack",
    "name": "Power Attack",
    "type": "general",
    "prerequisites": [
      { "$type": "MinAbility", "ability": "str", "value": 13 }
    ],
    "grantedPermabuffs": [],
    "repeatable": false
  },
  {
    "id": "cleave",
    "name": "Cleave",
    "type": "general",
    "prerequisites": [
      { "$type": "MinAbility", "ability": "str", "value": 13 },
      { "$type": "HasFeat", "featId": "power_attack" }
    ],
    "grantedPermabuffs": [],
    "repeatable": false
  },
  {
    "id": "weapon_focus",
    "name": "Weapon Focus",
    "type": "general",
    "prerequisites": [
      { "$type": "MinBAB", "value": 1 }
    ],
    "grantedPermabuffs": [],
    "repeatable": true,
    "selectionRequired": "weapon"
  },
  {
    "id": "weapon_specialization",
    "name": "Weapon Specialization",
    "type": "fighterBonus",
    "prerequisites": [
      { "$type": "HasFeat", "featId": "weapon_focus" },
      { "$type": "MinClassLevel", "classId": "class:fighter", "value": 4 }
    ],
    "grantedPermabuffs": [],
    "repeatable": true,
    "selectionRequired": "weapon"
  },
  {
    "id": "epic_toughness",
    "name": "Epic Toughness",
    "type": "epic",
    "prerequisites": [
      { "$type": "MinHD", "value": 21 }
    ],
    "grantedPermabuffs": [],
    "repeatable": true
  }
]
```

## Source Text

[Paste D&D 3.5e feat descriptions here]
