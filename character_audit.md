# PCGen Character Import Audit

Initial audit: 2026-07-31 (Europe/Athens)  
Completed repair pass: 2026-08-01 (Europe/Athens)

Corpus: 55 `.pcg` files from the configured `PCGEN_CHARACTERS_PATH`.

## Outcome

The importer omissions identified by this audit have been repaired. Fresh conversion now has:

- 55/55 files parsed and converted, with no race fallback or parse failure;
- no dropped races, classes, feats, skills, templates, domains, or equipment;
- all 652 PCGen level HP rolls preserved, with zero source/import roll-sequence mismatches;
- all 64 companion relationship records represented as 32 master-side links and 32 companion origins;
- all three specialist Wizards retaining specialty and prohibited schools;
- all seven previously missing skill names resolved, restoring 369 ranks;
- correct primary/off-hand and double-weapon representation, bracer slots, carried-item behavior, and quantities;
- 519 persistent spell selections retained on 19 characters.

The enhanced regression report calls 13 characters clean and 42 warning-bearing because it now includes replay validation warnings as well as converter warnings. That is deliberately stricter than the old report. Most remaining replay warnings are the prerequisite/skill-budget reconstruction problem that was discussed and explicitly dropped: the original files do not contain reliable feat/skill acquisition ticks, and final-step attribution preserves the final build.

The golden PCG baseline has not been accepted, and no existing converted character save was overwritten.

## Finding-by-finding status

| Finding | Status | Result |
| --- | --- | --- |
| Persistent spell choices omitted | Fixed | `Known Spells` and spellbook choices are imported by acquisition mode. Prepared rows, full-list availability, and synthetic Wizard known-list rows are intentionally not persisted. Unresolved names are explicit drops. |
| Harp Bow absent | Fixed | Added from the Malhavoc Press PCGen LST in private pack `malhavoc_complete_eldritch_might`; Small and Medium generated names map to it. |
| Racial-HD ticks request ability increases | Fixed | Eligibility now requires a class driver in replay, Builder, and REST previews. PCGen racial `PRESTAT` rows are neither imported as choices nor subtracted from base scores. The five affected source rows on three corpus characters no longer double-count racial adjustments. |
| Generic Bardic Music only | Fixed | Bard now grants Countersong, Fascinate, Inspire Courage and its +1/+2/+3/+4 replacements, Inspire Competence, Suggestion, Inspire Greatness, Song of Freedom, Inspire Heroics, and Mass Suggestion at their SRD levels. Rank requirements remain in descriptions because conditional ability grants by Perform ranks are not yet modeled. |
| Bracers occupy glove slot | Fixed | PCGen `Arms` and all Bracers of Armor variants normalize to `wrists`; `hands` remains available for gloves and gauntlets. |
| Dual-wield weapons become two main-hand weapons | Fixed | `Primary Hand`, `Secondary Hand`, and `Double Weapon` are recognized. Identical weapon entries are no longer collapsed, and a double weapon produces main- and off-hand attack contributions. |
| Equipment quantity discarded | Fixed | Quantity and source-adjusted weight/price are retained. Weight and carried load use quantity. Explicitly carried items do not apply equipped bonuses or attacks. All seven quantity-bearing corpus rows are preserved. |
| HP rolls discarded | Fixed | `TickChoices.HitPointsRolled` stores every PCGen `HITPOINTS` result. Replay uses the saved roll and reapplies final Constitution consistently, including after equipment changes. All 652 source/import sequences match. |
| `MASTER`/`FOLLOWER` ignored | Fixed | Both tags are parsed into `CompanionLinks`/`CompanionOrigin`. Animal companions, familiars, shadow companions, and Leadership cohorts receive appropriate effective-level formulas. Broken/external relative references are retained by stable character ID and warned. |
| Specialist Wizard choices discarded | Fixed | Abjurer, Necromancer, and Conjurer specialties and their prohibited schools are imported at Wizard 1 for the three affected characters. |
| Prestige spell advancement choice missing | Fixed | Every PCGen `ADD:[SPELLCASTER:...|CHOICE:...]` choice is retained. Loremaster ambiguity on Drow Abjurer is resolved. Red Dragon racial casting is now a modeled arcane source, so all five Archmage levels advance it without `AdvanceSpellcasting` warnings. |
| Domain source class/level ignored | Fixed | Domains are attached to their exact source driver and class level. Source-authorized variant domains are supported even when the base class has no ordinary domain slot; Nymph Archdruid's Plant domain is owned by Druid without a false pending-domain warning. |
| Seven skills absent or mis-mapped | Fixed | Added Ancient History, Demonology, Fey, History/Abyss, Monster Lore, and Craft (Tattoo) from their PCGen LST packs. Corrected core Architecture and Engineering's misspelled name, mapped both slash/name exceptions, and restored its Search synergy. No skill drops remain. |
| Active temporary modifiers silently ignored | Fixed as an import disclosure | Six `TEMPBONUS` rows on four characters are parsed, returned by the REST import response, and warned as ignored. They remain temporary sheet state rather than permanent replay inputs. |
| Regression report hid replay gaps | Fixed | The report now records replay warnings, HP roll sequences, companion links/origins, ignored temporary modifiers, specialist choices, and prestige spellcasting choices. Status includes replay warnings. |
| Feat/skill attribution to original tick | Deliberately dropped | The original PCG data lacks trustworthy acquisition ticks. Per the user decision, final-step attribution is retained because it reconstructs the final build and avoids inventing chronology. |

## Red Dragon casting repair

The SRD/PCGen Red Dragon monster class uses racial-HD levels for a spell progression that does not equal caster level. The content model now supports separate driver-level-to-progression and driver-level-to-caster-level mappings.

`racial_hd:red_dragon` now models:

- spontaneous Charisma-based arcane casting;
- PCGen's Red Dragon spells-per-day and spells-known progression;
- its combined Sorcerer, Cleric, Chaos, Evil, and Fire spell lists;
- caster-level scaling through epic Red Dragon HD;
- prestige advancement as an existing arcane spellcasting source.

For `Dragon.pcg`, 40 Red Dragon spell selections now import and the five Archmage levels advance caster level 27 to 32. Only `Scribe Spell`, `The Good Cook`, and `Transcribe` remain missing definitions for that character.

## Spell import results and remaining content work

The current corpus imports 519 distinct persisted selections on 19 characters. It explicitly drops 135 occurrences covering 111 distinct spell names on 18 characters.

Those remaining drops are no longer an importer omission. They fall into three content/model groups:

1. third-party or homebrew spells with no `SpellDefinition` yet;
2. PCGen epic-spell pseudo-classes (`Epic Spells (CHA)` and `Epic Spells (INT)`), which have no engine acquisition model;
3. the custom `Sorcerer/Cleric (Arcane)` source, which has no mapped driver.

Replay also reports preserved selections that are absent from currently modeled class spell lists. Fresh warning categories are:

- 407 prerequisite warnings;
- 32 skill-budget warnings;
- 125 selected-spell/class-list warnings;
- 9 spells-known-budget warnings;
- 1 selected-spell level mismatch;
- 3 feat-slot warnings;
- 24 other warnings.

The prerequisite and skill-budget warnings are predominantly consequences of the intentionally non-chronological feat/skill reconstruction. The spell-list and spells-known warnings are content follow-up: keeping the selections means future class/spell content corrections will restore them without another import change.

## Equipment details

The Harp Bow entry preserves the LST's base Large size, 3,330 gp price, 5 lb. weight, 1d6 piercing damage, 60-foot range, ×3 critical, martial ranged proficiency, and two-handed use. Per-character source weight and price overrides preserve the generated Small/Medium profiles.

The LST grants +2 to attack only and applies Strength penalties, but not bonuses, to damage. The current weapon model has only one enhancement field for both attack and damage. Applying +2 through that field would be wrong, so these two rules remain in the description pending separate weapon attack/damage modifiers.

## Companion and temporary-state behavior

Companion references are not blindly resolved as filesystem paths. The importer normalizes character IDs, preserves source file/race notes, and warns when PCGen points outside the character directory. Effective-level formulas use the reconstructed master:

- animal companion: Druid plus Ranger −3 where applicable;
- familiar: Wizard plus Sorcerer caster levels;
- Leadership cohort: the lower of Total HD −2 and Leadership score −2;
- other links: Total HD.

Active temporary effects are intentionally not converted to permanent events. The six source rows—familiar-within-reach effects and Wizard's Fox's Cunning—are visible in import warnings and API results so the resulting sheet cannot appear silently identical.

## Fresh regression metrics

| Metric | Result |
| --- | ---: |
| Source files | 55 |
| Parsed/imported | 55 |
| Parse failures | 0 |
| Race fallbacks | 0 |
| Converter-clean characters | 37 |
| Converter-warning characters | 18 |
| Combined clean (including replay) | 13 |
| Combined warning-bearing | 42 |
| Replay warnings | 601 across 40 characters |
| HP roll mismatches | 0 |
| Companion links / origins | 32 / 32 |
| Specialist Wizards | 3 |
| Explicit spell-advancement choices | 181 |
| Imported selected spells | 519 on 19 characters |
| Dropped spell occurrences | 135 (111 distinct) |
| Non-spell mapping drops | 0 |
| Ignored temporary modifier rows | 6 |

## Verification

- Focused racial ability, domain/source, Wizard specialist, prestige casting, equipment, HP, companion, temporary-state, Bard, skill, and Red Dragon tests pass.
- Strict bundled/private JSON deserialization and content-integrity tests pass.
- Full non-baseline suite: **1,000/1,000 passed**.
- Fresh `PcgImportRegression` completed conversion/evaluation of all 55 files and produced the expected review diff against the untouched golden baseline.
- Existing character destination: 55 preserved, 0 overwritten, 0 newly written.

The latest and diff reports are in the configured private pack repository's `test-reports/` directory. Accepting the new golden baseline remains a separate explicit review action.
