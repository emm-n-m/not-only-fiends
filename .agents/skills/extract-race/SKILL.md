---
name: extract-race
description: Extract D&D 3.5e playable races and associated racial HD drivers from SRD HTML or supplement PDFs into validated content JSON. Use when adding racial traits, ability adjustments, level adjustment, speeds, or racial HD.
---

# Extract Races

Extract race content for the NotOnlyFiendsStudio pipeline. Use source text for all traits and
never price a race's level adjustment from memory.

## Workflow

1. Read `schemas/race.schema.json` and `schemas/prompts/extract-race.md` completely. If racial
   HD is needed, also read `schemas/hddriver.schema.json`.
2. Prefer local SRD HTML:
   - `races.html`
   - `monstersAsRaces.html`
   - `psionicRaces.html`
   - `unearthedRaces.html`
   - the relevant `monsters*.html` entry
   Use PDF only for supplements without an HTML source.
3. Resolve the requested race IDs. If the source contains several races and scope is omitted,
   enumerate the race anchors and confirm the batch.
4. Extract size, type/subtypes, speed, ability modifiers, traits, languages, level adjustment,
   and racial Hit Dice.
5. Transcribe level adjustment exactly. Use `null` when the source prints none; never infer or
   estimate LA from Challenge Rating, advancement, favored class, ability scores, or powers.
6. Represent each persistent racial mechanic with the most specific available permabuff.
   Preserve unsupported or choice-bearing behavior accurately in a descriptive ability and
   call out the model gap.
7. For racial HD, derive hit die/BAB/saves/skills from the source creature entry or
   `monsterTypes.html`, and gate the driver with `HasRace`.
8. Write races to the appropriate pack's `races/` directory and racial HD to its sibling
   `racial_hd/` directory.
9. Add focused assertions for LA, ability adjustments, traits, and racial-HD linkage. Run
   focused tests, strict validation, then `dotnet test`.

## Conventions

- Race IDs: `race:<snake_case>`.
- Racial HD IDs: `racial_hd:<snake_case>`.
- Include alignment, extraplanar, and creature subtypes when the source supplies them.
- Include all six ability adjustments when any are present; use zero for unmodified scores.
- Use `null` ability adjustments only when the race has none.
- Omit `racialHDDriverId` when the race has no racial HD.
- Preserve language and favored-class source text even where the engine does not yet model it.

## References

- `schemas/race.schema.json`
- `schemas/prompts/extract-race.md`
- `schemas/hddriver.schema.json`
- `NotOnlyFiendsStudio/Content/packs/srd_core/races/`
- `NotOnlyFiendsStudio/Content/packs/srd_monsters/races/`
