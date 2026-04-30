# Extract Domains from D&D 3.5e Source Material

## Task

Extract domain data from the provided source text and output valid JSON matching the DomainDefinition schema. Output a JSON array (even for a single domain).

## Output Format

- Root is a JSON array: `[{ ... }, { ... }]`
- Use **camelCase** for all property names
- Omit null/empty optional fields

## Schema

Use `schemas/domain.schema.json` (self-contained, paste it into your context alongside this prompt).

## Critical Rules

### ID Conventions
- Format: `domain:snake_case_name` (e.g., `domain:knowledge`, `domain:corruption`, `domain:war`)
- Multi-word domains use underscores: `domain:animal`, `domain:dark_speech`

### Granted Powers
- Model the granted power as a `GrantAbility` permabuff with descriptive text
- The ability `id` should follow the pattern `domain_{domain_name}_power` (e.g., `domain_knowledge_power`)
- The `description` field on the DomainDefinition should contain the full granted power text from the source
- The `description` field on the GrantedAbility should be a brief mechanical summary

### Bonus Spells
- Map spell levels 1-9 to spell IDs in snake_case
- Use the standard spell ID conventions: `cure_light_wounds`, `detect_thoughts`, etc.
- For "Mass" / "Greater" / "Lesser" variants: `cure_light_wounds_mass`, `dispel_magic_greater`
- Keys are spell level numbers as strings: `"1"`, `"2"`, ..., `"9"`

### Special Cases
- If a granted power grants turning/rebuking: use `GrantAbility` with descriptive text
- If a granted power grants bonus class skills: use `GrantAbility` (engine doesn't auto-add class skills from domains yet)
- If a granted power grants a free feat: use `GrantBonusFeat` with the feat ID
- If a granted power modifies a stat permanently: use `ModifyAttribute`

## Examples

```json
[
  {
    "id": "domain:knowledge",
    "name": "Knowledge",
    "description": "Add all Knowledge skills to your list of cleric class skills. You cast divination spells at +1 caster level.",
    "grantedPermabuffs": [
      {
        "$type": "GrantAbility",
        "ability": {
          "id": "domain_knowledge_power",
          "name": "Knowledge Domain Power",
          "description": "All Knowledge skills are class skills. Cast divination spells at +1 caster level."
        }
      }
    ],
    "bonusSpells": {
      "1": "detect_secret_doors",
      "2": "detect_thoughts",
      "3": "clairaudience_clairvoyance",
      "4": "divination",
      "5": "true_seeing",
      "6": "find_the_path",
      "7": "legend_lore",
      "8": "discern_location",
      "9": "foresight"
    }
  },
  {
    "id": "domain:war",
    "name": "War",
    "description": "Free Martial Weapon Proficiency with deity's favored weapon (if necessary) and Weapon Focus with deity's favored weapon.",
    "grantedPermabuffs": [
      {
        "$type": "GrantAbility",
        "ability": {
          "id": "domain_war_power",
          "name": "War Domain Power",
          "description": "Free Martial Weapon Proficiency and Weapon Focus with deity's favored weapon."
        }
      }
    ],
    "bonusSpells": {
      "1": "magic_weapon",
      "2": "spiritual_weapon",
      "3": "magic_vestment",
      "4": "divine_power",
      "5": "flame_strike",
      "6": "blade_barrier",
      "7": "power_word_blind",
      "8": "power_word_stun",
      "9": "power_word_kill"
    }
  }
]
```

## Source Text

[Paste D&D 3.5e domain descriptions here]
