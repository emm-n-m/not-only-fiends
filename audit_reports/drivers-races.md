# Public SRD audit — drivers, racial HD, races, and templates

Audited 2026-07-31 against the local `NotOnlyFiendsStudio/Content/srd_html/` mirror only. Scope was the four IDs in `content-public.json` (`srd_core`, `srd_epic`, `srd_monsters`, and `srd_unearthed_arcana`) and the 186 definitions in their `classes/`, `racial_hd/`, `races/`, and `templates/` directories. Content was not modified.

The checks included driver progression primitives (HD, BAB, saves, and skill points), standard race identity/size/LA, and template mechanics. I also inspected the current model before classifying a missing prerequisite: `MinAbility`, `AlignmentReq`, and `HasCreatureType` are available, so those omissions are representable rather than model limitations.

| Item | Field | JSON value | SRD value | SRD quote | Severity |
|---|---|---|---|---|---|
| `class:barbarian` | Level-7 class feature | `ModifyAttribute NaturalArmor 0`; no damage-reduction mechanic or ability | Damage reduction 1/— at level 7, then increases at levels 10, 13, 16, and 19 | `barbarian.html`, Table: The Barbarian: “7th … Damage reduction 1/—”; later rows state “Damage reduction 2/—”, “3/—”, “4/—”, and “5/—”. | HIGH |
| `template:half_dragon` | Racial HD | No HD adjustment | Increase *racial* HD by one die size (maximum d12); class HD are unchanged | `monstersHtoI.html`, Creating a Half-Dragon: “Increase base creature’s racial HD by one die size, to a maximum of d12. Do not increase class HD.” | HIGH |
| `template:half_dragon` | Flight | No speed/flight rule | Large-or-larger base creatures fly at twice base land speed (maximum 120 ft.); Medium-or-smaller do not gain wings | `monstersHtoI.html`, Creating a Half-Dragon: “A half-dragon that is Large or larger has wings and can fly at twice its base land speed (maximum 120 ft.) … A half-dragon that is Medium or smaller does not have wings.” | HIGH |
| `template:half_fiend` | Eligibility | No prerequisites | Must be living, corporeal, Int 4+, and nongood | `monstersHtoI.html`, Creating a Half-Fiend: “can be added to any living, corporeal creature with an Intelligence score of 4 or more and nongood alignment”. | HIGH |
| `template:half_fiend` | Flight speed | Fixed `fly: 60` | Base land speed, unless the base creature already has a better fly speed | `monstersHtoI.html`, Creating a Half-Fiend: “Unless the base creature has a better fly speed, the creature can fly at the base creature’s base land speed”. | HIGH |
| `template:fiendish` | Eligibility | No prerequisites | Corporeal aberration, animal, dragon, fey, giant, humanoid, magical beast, monstrous humanoid, ooze, plant, or vermin; nongood alignment | `monstersEtoF.html`, Creating a Fiendish Creature: “can be added to any corporeal aberration, animal, dragon, fey, giant, humanoid, magical beast, monstrous humanoid, ooze, plant, or vermin of nongood alignment”. | HIGH |
| `template:fiendish` | Creature type/subtype | Always adds `extraplanar`; leaves animals and vermin unchanged unless a second helper template is manually chosen | Animals and vermin become magical beasts; extraplanar is conditional on being encountered on the Material Plane | `monstersEtoF.html`, Creating a Fiendish Creature: “Animals or vermin with this template become magical beasts, but otherwise the creature type is unchanged.” Also: “Fiendish creatures encountered on the Material Plane have the extraplanar subtype.” | HIGH |
| `template:fiendish` | Granted qualities | Missing darkvision and Smite Good | Darkvision 60 ft. and a once-per-day Smite Good equal to HD (maximum +20) | `monstersEtoF.html`: “Smite Good (Su): Once per day the creature can make a normal melee attack to deal extra damage equal to its HD total (maximum of +20) against a good foe.” And: “Darkvision out to 60 feet.” | HIGH |

## VERIFIED CLEAN

- `racial_hd:animal`, `racial_hd:dragon`, and `racial_hd:outsider` have the correct d8/d12/d8 HD, 3/4/full/full BAB, good-save patterns, and 2/6/8 skill points. For example, `monsterTypes.html` states: “An animal has … d8 Hit Dice” and “Base attack bonus equal to 3/4 total Hit Dice”; dragon: “12-sided Hit Dice” and “Good Fortitude, Reflex, and Will saves”; outsider: “8-sided Hit Dice”, “Base attack bonus equal to total Hit Dice”, and “Skill points equal to (8 + Int modifier …) per Hit Die.”
- The core playable race files (`dwarf`, `elf`, `gnome`, `half_elf`, `half_orc`, `halfling`, and `human`) match the SRD race headings for creature type, size, base land speed, ability modifiers, and LA 0 where stated. The audit did not find a discrepancy in those identity/progression fields.
- `template:half_dragon` correctly uses dragon type, +4 natural armor, ability modifiers (+8 Str, +2 Con, +2 Int, +2 Cha), and LA +3. `monstersHtoI.html` states: “Natural armor improves by +4”; “Str +8, Con +2, Int +2, Cha +2”; and “Level Adjustment: Same as base creature +3.”
- `template:half_fiend` correctly uses outsider/native, +1 natural armor, the listed ability modifiers, SLAs by HD threshold, and LA +4. Its spell resistance formula is also correct: the SRD gives “Spell resistance equal to the creature’s HD + 10 (maximum 35).”
- `template:fiendish` correctly uses LA +2, SR = HD + 5 (maximum 25), and the 1–3/4–7/8–11/12+ resistance and DR threshold pattern. The two +5 resistance modifications at HD 8 total resistance 10 under the current additive `ModifyAttribute` semantics.

## UNVERIFIABLE

- The mirror has no authoritative SRD source for the app-specific helper templates (companion/familiar/special-mount progressions, choice/permission templates, subtype-assignment helpers, `int_bonus_+2`, and `slam_medium`). They are not treated as SRD rules claims in this report.
- The mirror does not state level adjustment for the companion/familiar catalogue entries used as application choices. The `0` values on those non-PC helper races are therefore not source-verifiable as playable-race LAs.
- Several monster-race files are curated reproductions of full monster stat blocks. The local mirror does contain the monsters, but this pass did not claim clean status for every ability prose, natural-attack damage value, SLA, or scaling detail; those need entry-by-entry source-block audits in a follow-up pass.

## Proposed focused regression assertions

- A level-7 barbarian has a `damage_reduction` ability/value of 1, which becomes 2/3/4/5 at levels 10/13/16/19, without changing natural armor.
- Applying Half-Dragon to a creature with racial HD upgrades only its racial hit die one size (capped at d12); a class-only character’s class HD remain unchanged.
- Half-Dragon grants flight only at Large size or larger and calculates it from base land speed, capped at 120 ft.
- Half-Fiend is rejected for good-aligned or Int-3 creatures and derives flight from the base creature’s land speed instead of a fixed 60 ft.
- Fiendish rejects good or unsupported creature types; animal/vermin applications become magical beasts; extraplanar is conditional rather than unconditional; and the resulting state includes Darkvision 60 ft. and once-per-day Smite Good.
