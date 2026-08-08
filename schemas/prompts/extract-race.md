# Extract Races from D&D 3.5e Source Material

## Task

Extract race data from the provided source text and output valid JSON matching the RaceDefinition schema. Output a JSON array (even for a single race).

**Important**: If the race has racial Hit Dice, you also need to create a separate HDDriver file for the racial HD progression. This prompt covers the RaceDefinition only — use `extract-class.md` with `"kind": "racialHD"` for the HD driver.

## Output Format

- Root is a JSON array: `[{ ... }]`
- Use **camelCase** for all property names and enum values
- Omit null/empty optional fields
- The `$type` discriminator field is **required** on all Permabuff objects

## Schema

Use `schemas/race.schema.json` (self-contained, paste it into your context alongside this prompt).

## Critical Rules

### ID Conventions
- Use `snake_case`: `human`, `half_elf`, `aasimar`, `outsider`

### Creature Types
Use the camelCase enum values: `humanoid`, `outsider`, `magicalBeast`, `monstrousHumanoid`, `dragon`, `fey`, `giant`, `undead`, `aberration`, `construct`, `elemental`, `animal`, `ooze`, `plant`, `vermin`

### Size
Use lowercase: `fine`, `diminutive`, `tiny`, `small`, `medium`, `large`, `huge`, `gargantuan`, `colossal`

### Ability Modifiers
- Use `null` for races with no modifiers (Human)
- For races with modifiers, include ALL six scores (set unmodified ones to 0):
  ```json
  "abilityModifiers": { "str": 0, "dex": 2, "con": -2, "int": 0, "wis": 0, "cha": 0 }
  ```

### Speeds
Only include movement modes the race actually has:
```json
"speeds": { "land": 30 }
"speeds": { "land": 30, "swim": 30 }
"speeds": { "land": 20, "fly": 60 }
```
When a fly speed has a maneuverability grade, also author `flyManeuverability` using one of
`clumsy`, `poor`, `average`, `good`, or `perfect`. Do not infer a grade when the source omits it.

### Racial HD
- If the race has racial Hit Dice (e.g., Outsider with 2 HD, Dragon with 12 HD):
  - Set `"racialHDDriverId": "racial_hd:type_name"`
  - Create a separate HDDriver JSON file with `"kind": "racialHD"` and a `HasRace` prerequisite
- If the race has no racial HD (Human, Elf, Dwarf): omit `racialHDDriverId` or set to `null`

### Racial Permabuffs
Use for flat abilities applied at creation:
- `GrantAbility` for darkvision, weapon familiarity, stonecunning, etc.
- `ModifyAttribute` for resistance bonuses (e.g., +2 save vs enchantments)
- `GrantSLA` for racial spell-like abilities (e.g., drow's dancing lights)

### Natural Attacks

When the source grants natural weapons, add `naturalAttacks` entries with the source damage die,
the number of attacks in a full attack, and `isPrimary: false` for secondary attacks such as a
bite accompanying claws or talons. Keep conditional attacks (such as a sahuagin's swimming
rakes) in the descriptive ability unless the model can represent their condition.

### Scaling Formulas
Use for abilities that scale with total HD:
- Formula DSL: `"expression": "TotalHD + 11"` for SR, `"expression": "max(10, TotalHD)"` for scaling values
- These recalculate every tick and use SetAttribute semantics

### Bonus Feats and Skill Points
- Humans: `"bonusFeats": 1, "bonusSkillPointsPerHD": 1`
- Most other races: `"bonusFeats": 0, "bonusSkillPointsPerHD": 0`

## Examples

### Simple Race (Human)

```json
[
{
  "id": "human",
  "name": "Human",
  "type": "humanoid",
  "subtypes": [],
  "size": "medium",
  "speeds": { "land": 30 },
  "levelAdjustment": 0,
  "bonusFeats": 1,
  "bonusSkillPointsPerHD": 1,
  "racialPermabuffs": [],
  "scalingFormulas": []
}
]
```

### Race with Racial HD (Outsider)

```json
[
{
  "id": "outsider",
  "name": "Outsider",
  "type": "outsider",
  "subtypes": ["native"],
  "size": "medium",
  "speeds": { "land": 30 },
  "abilityModifiers": null,
  "levelAdjustment": 0,
  "bonusFeats": 0,
  "racialHDDriverId": "racial_hd:outsider",
  "racialPermabuffs": [],
  "scalingFormulas": []
}
]
```

The corresponding racial HD driver (separate file, use extract-class.md):

```json
[
{
  "$type": "HDDriver",
  "kind": "racialHD",
  "id": "racial_hd:outsider",
  "name": "Outsider",
  "hitDie": 8,
  "skillPointsPerLevel": 8,
  "classSkills": ["bluff", "craft", "knowledge_planes", "listen", "search", "sense_motive", "spot", "survival"],
  "babProgression": "good",
  "saveProgression": { "fort": "good", "ref": "good", "will": "good" },
  "prerequisites": [{ "$type": "HasRace", "raceId": "outsider" }],
  "levelPermabuffs": {}
}
]
```

## Source Text

[Paste D&D 3.5e race description here]
