# Extract Skills from D&D 3.5e Source Material

## Task

Extract skill data from the provided source text and output valid JSON matching the SkillDefinition schema. Output a JSON array (even for a single skill).

**Note**: The SkillDefinition type is not yet implemented in the C# engine. Skills are currently referenced by string ID in `classSkills` arrays on HDDrivers. This schema defines the target format for future integration.

## Output Format

- Root is a JSON array: `[{ ... }, { ... }]`
- Use **camelCase** for all property names
- Omit null/empty optional fields
- Process skills alphabetically for consistency

## Schema

Use `schemas/skill.schema.json` (self-contained, paste it into your context alongside this prompt).

## Critical Rules

### ID Conventions
- Use `snake_case`: `balance`, `climb`, `hide`, `use_magic_device`
- Subspecialty skills: `knowledge_arcana`, `knowledge_religion`, `craft_alchemy`, `perform_sing`
- These IDs **must match** the string IDs used in `classSkills` on HDDriver content files

### Existing IDs in the Studio
These skill IDs are already in use across class/racial HD content. **Do not change them**:

| ID | Skill |
|----|-------|
| `balance` | Balance |
| `bluff` | Bluff |
| `climb` | Climb |
| `concentration` | Concentration |
| `craft` | Craft (generic — use `craft_alchemy`, `craft_weaponsmithing`, etc. for specifics) |
| `decipher_script` | Decipher Script |
| `diplomacy` | Diplomacy |
| `disguise` | Disguise |
| `forgery` | Forgery |
| `handle_animal` | Handle Animal |
| `heal` | Heal |
| `hide` | Hide |
| `intimidate` | Intimidate |
| `jump` | Jump |
| `knowledge_arcana` | Knowledge (Arcana) |
| `knowledge_history` | Knowledge (History) |
| `knowledge_local` | Knowledge (Local) |
| `knowledge_nobility` | Knowledge (Nobility) |
| `knowledge_planes` | Knowledge (The Planes) |
| `knowledge_religion` | Knowledge (Religion) |
| `listen` | Listen |
| `move_silently` | Move Silently |
| `perform` | Perform (generic — use `perform_sing`, `perform_dance`, etc. for specifics) |
| `profession` | Profession (generic — use `profession_sailor`, etc. for specifics) |
| `ride` | Ride |
| `search` | Search |
| `sense_motive` | Sense Motive |
| `spellcraft` | Spellcraft |
| `spot` | Spot |
| `survival` | Survival |
| `swim` | Swim |
| `tumble` | Tumble |

### Key Ability Mapping
| Ability | Skills |
|---------|--------|
| `str` | Climb, Jump, Swim |
| `dex` | Balance, Escape Artist, Hide, Move Silently, Open Lock, Ride, Sleight of Hand, Tumble, Use Rope |
| `con` | Concentration |
| `int` | Appraise, Craft, Decipher Script, Disable Device, Forgery, Knowledge (all), Search, Spellcraft |
| `wis` | Heal, Listen, Profession, Sense Motive, Spot, Survival |
| `cha` | Bluff, Diplomacy, Disguise, Gather Information, Handle Animal, Intimidate, Perform, Use Magic Device |

### Trained Only
These skills **require ranks** to use (set `trainedOnly: true`):
- Disable Device, Handle Animal, Knowledge (all), Open Lock, Profession, Sleight of Hand, Speak Language, Spellcraft, Tumble, Use Magic Device, Decipher Script

All other skills can be used untrained (`trainedOnly: false`).

### Armor Check Penalty
These skills apply the armor check penalty (set `armorCheckPenalty: true`):
- Balance, Climb, Escape Artist, Hide, Jump, Move Silently, Sleight of Hand, Swim, Tumble

All other skills do not (`armorCheckPenalty: false`).

### Subspecialty Skills
Some skills have subspecialties. For generic entries (used in `classSkills`), the parent entry acts as a catch-all:
- **Craft**: `craft` (generic), `craft_alchemy`, `craft_armorsmithing`, `craft_weaponsmithing`, etc.
- **Knowledge**: `knowledge_arcana`, `knowledge_architecture`, `knowledge_dungeoneering`, `knowledge_geography`, `knowledge_history`, `knowledge_local`, `knowledge_nature`, `knowledge_nobility`, `knowledge_planes`, `knowledge_religion`
- **Perform**: `perform` (generic), `perform_act`, `perform_comedy`, `perform_dance`, `perform_keyboard`, `perform_oratory`, `perform_percussion`, `perform_sing`, `perform_string`, `perform_wind`
- **Profession**: `profession` (generic), `profession_sailor`, etc.

Set `parentSkill` on subspecialties (e.g., `"parentSkill": "knowledge"` for `knowledge_arcana`).

### Synergies
5+ ranks in certain skills grant a +2 synergy bonus to related skills. Include these where documented:
- Bluff → Diplomacy, Disguise (acting), Intimidate, Sleight of Hand
- Knowledge (arcana) → Spellcraft
- Knowledge (architecture) → Search (secret doors/compartments)
- Knowledge (nobility) → Diplomacy
- Knowledge (the planes) → Survival (on other planes)
- Tumble → Balance, Jump
- Use Magic Device → Spellcraft (scrolls)
- etc.

Use `condition` when the synergy only applies in specific circumstances.

## Examples

```json
[
  {
    "id": "balance",
    "name": "Balance",
    "keyAbility": "dex",
    "trainedOnly": false,
    "armorCheckPenalty": true,
    "description": "Keep your balance while walking on narrow or treacherous surfaces.",
    "synergies": [
      { "targetSkillId": "tumble", "bonus": 2 }
    ]
  },
  {
    "id": "bluff",
    "name": "Bluff",
    "keyAbility": "cha",
    "trainedOnly": false,
    "armorCheckPenalty": false,
    "description": "Make the false appear true. Used for feinting in combat, creating diversions, and delivering secret messages.",
    "synergies": [
      { "targetSkillId": "diplomacy", "bonus": 2 },
      { "targetSkillId": "disguise", "bonus": 2, "condition": "when acting in character" },
      { "targetSkillId": "intimidate", "bonus": 2 },
      { "targetSkillId": "sleight_of_hand", "bonus": 2 }
    ]
  },
  {
    "id": "knowledge_arcana",
    "name": "Knowledge (Arcana)",
    "keyAbility": "int",
    "trainedOnly": true,
    "armorCheckPenalty": false,
    "parentSkill": "knowledge",
    "description": "Ancient mysteries, magic traditions, arcane symbols, cryptic phrases, constructs, dragons, magical beasts.",
    "synergies": [
      { "targetSkillId": "spellcraft", "bonus": 2 }
    ]
  },
  {
    "id": "use_magic_device",
    "name": "Use Magic Device",
    "keyAbility": "cha",
    "trainedOnly": true,
    "armorCheckPenalty": false,
    "description": "Activate magic items that you couldn't normally use, such as scrolls, wands, or items with class or alignment restrictions.",
    "synergies": [
      { "targetSkillId": "spellcraft", "bonus": 2, "condition": "when deciphering scrolls" }
    ]
  }
]
```

## Source Text

[Paste D&D 3.5e skill descriptions here]
