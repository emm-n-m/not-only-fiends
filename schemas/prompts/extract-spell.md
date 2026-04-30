# Extract Spells from D&D 3.5e Source Material

## Task

Extract spell data from the provided source text and output valid JSON matching the SpellDefinition schema. Output a JSON array (even for a single spell).

**Note**: The SpellDefinition type is not yet implemented in the C# engine. This schema defines the target format for future integration.

## Output Format

- Root is a JSON array: `[{ ... }, { ... }]`
- Use **camelCase** for all property names and enum values
- Omit null/empty optional fields
- Process spells in batches (by school, class, level, or source page) for best results

## Schema

Use `schemas/spell.schema.json` (self-contained, paste it into your context alongside this prompt).

## Critical Rules

### ID Conventions
- Use `snake_case`: `fireball`, `cure_light_wounds`, `detect_magic`, `tashas_hideous_laughter`
- For spells with possessives, use the possessive name: `bigbys_interposing_hand`, `mordenkainens_disjunction`
- For "Mass" / "Greater" / "Lesser" variants: `cure_light_wounds_mass`, `dispel_magic_greater`, `restoration_lesser`

### Class Levels
Map source abbreviations to driver IDs:
| Source | Driver ID |
|--------|-----------|
| Sor/Wiz | `class:sorcerer` AND `class:wizard` (both entries) |
| Clr | `class:cleric` |
| Drd | `class:druid` |
| Brd | `class:bard` |
| Pal | `class:paladin` |
| Rgr | `class:ranger` |
| Wiz | `class:wizard` |

**Important**: When a spell appears on the "Sor/Wiz" list, create entries for BOTH `class:sorcerer` AND `class:wizard` at the same level.

```json
"classLevels": {
  "class:sorcerer": 3,
  "class:wizard": 3
}
```

### Schools
Use lowercase: `abjuration`, `conjuration`, `divination`, `enchantment`, `evocation`, `illusion`, `necromancy`, `transmutation`, `universal`

### Subschools
Common subschools (use null if the spell has no subschool):
- Conjuration: `"healing"`, `"creation"`, `"calling"`, `"summoning"`, `"teleportation"`
- Enchantment: `"charm"`, `"compulsion"`
- Illusion: `"figment"`, `"glamer"`, `"pattern"`, `"phantasm"`, `"shadow"`

### Descriptors
Common descriptors as array elements: `"acid"`, `"air"`, `"chaotic"`, `"cold"`, `"darkness"`, `"death"`, `"earth"`, `"electricity"`, `"evil"`, `"fear"`, `"fire"`, `"force"`, `"good"`, `"language-dependent"`, `"lawful"`, `"light"`, `"mind-affecting"`, `"sonic"`, `"water"`

### Components
```json
"components": {
  "verbal": true,
  "somatic": true,
  "material": "bat guano and sulfur",
  "focus": null,
  "divineFocus": false,
  "xpCost": null
}
```
- `material`: string description if M component, null otherwise
- `focus`: string description if F component, null otherwise
- `divineFocus`: true if DF component (divine casters use holy symbol)
- `xpCost`: string if XP component (e.g., `"250 XP"`), null otherwise

### Range Categories
Use the exact SRD wording:
- `"personal"`, `"touch"`
- `"close (25 ft. + 5 ft./2 levels)"`
- `"medium (100 ft. + 10 ft./level)"`
- `"long (400 ft. + 40 ft./level)"`
- `"unlimited"`, or a specific distance like `"60 ft."`

### Area / Effect / Target
A spell typically has ONE of these (sometimes two). Set the others to null:
- `area` — for area-of-effect spells: `"20-ft.-radius spread"`, `"cone-shaped burst"`
- `effect` — for spells that create something: `"ray"`, `"one or more creatures"`
- `target` — for targeted spells: `"you"`, `"one creature"`, `"one creature/level"`

### Duration
Use SRD format. Append `(D)` for dismissable:
- `"instantaneous"`, `"1 round/level"`, `"1 min./level (D)"`, `"permanent"`, `"concentration + 1 round/level"`

## Examples

```json
[
  {
    "id": "fireball",
    "name": "Fireball",
    "school": "evocation",
    "descriptors": ["fire"],
    "classLevels": { "class:sorcerer": 3, "class:wizard": 3 },
    "components": {
      "verbal": true,
      "somatic": true,
      "material": "a tiny ball of bat guano and sulfur",
      "divineFocus": false
    },
    "castingTime": "1 standard action",
    "range": "long (400 ft. + 40 ft./level)",
    "area": "20-ft.-radius spread",
    "duration": "instantaneous",
    "savingThrow": "Reflex half",
    "spellResistance": "yes",
    "description": "Deals 1d6 fire damage per caster level (max 10d6) to all creatures in the area."
  },
  {
    "id": "cure_light_wounds",
    "name": "Cure Light Wounds",
    "school": "conjuration",
    "subschool": "healing",
    "descriptors": [],
    "classLevels": { "class:cleric": 1, "class:druid": 1, "class:bard": 1, "class:paladin": 1, "class:ranger": 2 },
    "components": {
      "verbal": true,
      "somatic": true,
      "divineFocus": false
    },
    "castingTime": "1 standard action",
    "range": "touch",
    "target": "creature touched",
    "duration": "instantaneous",
    "savingThrow": "Will half (harmless)",
    "spellResistance": "yes (harmless)",
    "description": "Cures 1d8 damage + 1/caster level (max +5)."
  },
  {
    "id": "detect_magic",
    "name": "Detect Magic",
    "school": "divination",
    "descriptors": [],
    "classLevels": { "class:sorcerer": 0, "class:wizard": 0, "class:cleric": 0, "class:druid": 0, "class:bard": 0 },
    "components": {
      "verbal": true,
      "somatic": true,
      "divineFocus": false
    },
    "castingTime": "1 standard action",
    "range": "60 ft.",
    "area": "cone-shaped emanation",
    "duration": "concentration, up to 1 min./level (D)",
    "savingThrow": "none",
    "spellResistance": "no",
    "description": "Detects spells and magic items within 60 ft. cone. Reveals number of auras, then strength, then school over 3 rounds of concentration."
  },
  {
    "id": "haste",
    "name": "Haste",
    "school": "transmutation",
    "descriptors": [],
    "classLevels": { "class:sorcerer": 3, "class:wizard": 3, "class:bard": 3 },
    "components": {
      "verbal": true,
      "somatic": true,
      "material": "a shaving of licorice root",
      "divineFocus": false
    },
    "castingTime": "1 standard action",
    "range": "close (25 ft. + 5 ft./2 levels)",
    "target": "one creature/level, no two of which can be more than 30 ft. apart",
    "duration": "1 round/level",
    "savingThrow": "Fortitude negates (harmless)",
    "spellResistance": "yes (harmless)",
    "description": "One extra attack at full BAB when making a full attack, +1 attack rolls, +1 AC, +1 Reflex saves, +30 ft. speed. Counters slow."
  }
]
```

## Source Text

[Paste D&D 3.5e spell descriptions here]
