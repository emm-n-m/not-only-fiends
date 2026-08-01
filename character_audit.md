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
- 528 persistent spell selections retained on 20 characters, including developed epic spells.
- Archfiend Lilly's PCGen custom-item bonuses, 11 source languages, Shadowdancer shadow slot,
  and Leadership followers represented as durable character inputs and Builder actions.
- familiar master level, HP, BAB, and base saves derived from the master instead of the
  familiar's caster level or racial Hit Dice.
- PCGen's CHA/INT/WIS epic-spell pseudo classes represented as developed-spell lists, with
  level-10 slots computed from the appropriate Knowledge ranks.

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
| Archfiend Lilly custom items inert | Fixed for source-declared numeric modifiers | PCGen `CUSTOMIZATION`/`EQMOD` data is preserved. Belly Chain grants +12 enhancement Charisma and +5 competence Bluff. Emerald's Best Creation grants +10 natural-armor enhancement, +20 competence Perform (Dance), and +5 competence Bluff, Diplomacy, Disguise, and Gather Information. Same-type competence bonuses do not stack. Infernal Sting retains the Whip profile and +10 enhancement; Leather, Dancing, Anarchic, Dread, and its spell activation are disclosed as retained but not yet modeled. |
| Imported languages absent from Builder | Fixed | Imported PCGen languages are stored as explicit source-language inputs rather than hidden permanent events or invented starting-Intelligence picks. Lilly's 11 languages replay and appear in the Languages card. |
| Shadowdancer shadow could not be created | Fixed | Summon Shadow grants a fixed `race:companion_shadow` slot at Shadowdancer 3, scaling to 3/5/7 HD at levels 3/6/9. Builder creation seeds the corresponding Undead racial-HD ticks. |
| Leadership followers could not be managed | Fixed | PCGen followers use a dedicated `leadership_follower` link. The Builder shows level 1–6 capacities from Leadership score and supports creating, linking, opening, and unlinking followers; source links with unknown level remain visible. |
| Archfiend domain appeared missing | Fixed | PCGen records Lilly's Charm selection on Blackguard 3, but the domain slot is granted by `Archfiend (Ascended)`. Replay now consumes the existing race/template slot before falling back to the recorded class, so Charm is owned by the Archfiend template, grants its power and nine tiered domain SLAs, and does not create Blackguard domain spell slots. Builder and sheet explicitly display imported/source-granted domains. |
| Familiar composite statistics wrong | Fixed | Familiar power now uses levels in familiar-granting Sorcerer/Wizard classes, not caster level. Composite replay substitutes half the master's HP (minimum 1), the master's pre-epic BAB, and the master's HD-progression base saves while retaining the familiar's own ability modifiers and non-progression bonuses. Epic attack/save bonuses belong to the master and are not copied. The former caster-level formula is migrated during replay for existing imported saves. |
| Fly speed and maneuverability hidden | Fixed | Builder and sheet list every replayed movement mode rather than only land speed. Dark Temptress 10 and its imported transformation template grant Fly 50 ft. (Average) without adding speeds together or stacking with each other; a better existing fly speed or maneuverability is preserved. Rose displays Fly 50 ft. (Average), and Archfiend Lilly displays Fly 90 ft. (Good). |
| Epic spells and epic slots omitted | Fixed for extracted spells | PCGen's `Epic Spells (CHA/INT/WIS)` pseudo classes map to non-HD developed-spell lists. Open level-10 slots are computed from Knowledge (arcana), or the better qualifying divine Knowledge skill, with arcane and divine qualification cumulative. Mass Frog was extracted from the RSRD pack and Mind Rape from the DECEIT pack; both now import for Duchess Rose. Other epic spell names still need individual content extraction. |
| Specialist Wizard choices discarded | Fixed | Abjurer, Necromancer, and Conjurer specialties and their prohibited schools are imported at Wizard 1 for the three affected characters. |
| Prestige spell advancement choice missing | Fixed | Every PCGen `ADD:[SPELLCASTER:...|CHOICE:...]` choice is retained. Loremaster ambiguity on Drow Abjurer is resolved. Red Dragon racial casting is now a modeled arcane source, so all five Archmage levels advance it without `AdvanceSpellcasting` warnings. |
| Domain source class/level ignored | Fixed | Domains are attached to their exact source driver and class level. Source-authorized variant domains are supported even when the base class has no ordinary domain slot; Nymph Archdruid's Plant domain is owned by Druid without a false pending-domain warning. |
| Seven skills absent or mis-mapped | Fixed | Added Ancient History, Demonology, Fey, History/Abyss, Monster Lore, and Craft (Tattoo) from their PCGen LST packs. Corrected core Architecture and Engineering's misspelled name, mapped both slash/name exceptions, and restored its Search synergy. No skill drops remain. |
| Active temporary modifiers silently ignored | Fixed as an import disclosure | Six `TEMPBONUS` rows on four characters are parsed, returned by the REST import response, and warned as ignored. They remain temporary sheet state rather than permanent replay inputs. |
| Regression report hid replay gaps | Fixed | The report now records replay warnings, HP roll sequences, companion links/origins, ignored temporary modifiers, specialist choices, and prestige spellcasting choices. Status includes replay warnings. |
| Feat/skill attribution to original tick | Deliberately dropped | The original PCG data lacks trustworthy acquisition ticks. Per the user decision, final-step attribution is retained because it reconstructs the final build and avoids inventing chronology. |
| Feats checked before same-HD skill purchases | Fixed | Replay now follows level-advancement order and applies skill purchases before feat selection. Feats can satisfy prerequisites with ranks gained at that HD; this removes eight false corpus warnings, including Rose's Ignore Material Components warning at HD 29. |
| Prestige entry checked before same-HD/undated feats | Fixed | Feat-based driver prerequisites are validated after the evaluated feat timeline and only for class entry. This permits a feat gained at the entry HD to qualify and respects the deliberate final-tick placement of PCGen feats whose acquisition level is absent. Builder/REST prospective class lists still require the feat in the current state. Rose's Spell Focus (Enchantment) now satisfies Dark Temptress without ten repeated warnings; 293 false/repeated driver-feat warnings disappear corpus-wide. |

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

The current corpus imports 528 distinct persisted selections on 20 characters. It explicitly drops 126 occurrences covering 109 distinct spell names on 17 characters.

Those remaining drops are no longer an importer omission. They fall into two content/model groups:

1. third-party or homebrew spells with no `SpellDefinition` yet;
2. the custom `Sorcerer/Cleric (Arcane)` source, which has no mapped driver.

Epic-spell sources are now modeled independently of ordinary class levels. Unextracted epic
spell names remain explicit content drops, but they are no longer dropped merely because their
PCGen source is `Epic Spells (CHA)` or `Epic Spells (INT)`.

Replay also reports preserved selections that are absent from currently modeled class spell lists. Fresh warning categories are:

- 106 prerequisite warnings;
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
- familiar: Wizard plus Sorcerer class levels (an explicitly authored custom formula may add
  prestige-class advancement when a class actually says that it improves familiar abilities);
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
| Replay warnings | 300 across 36 characters |
| HP roll mismatches | 0 |
| Companion links / origins | 32 / 32 |
| Specialist Wizards | 3 |
| Explicit spell-advancement choices | 181 |
| Imported selected spells | 528 on 20 characters |
| Dropped spell occurrences | 126 (109 distinct) |
| Non-spell mapping drops | 0 |
| Ignored temporary modifier rows | 6 |

## Verification

- Focused racial ability, domain/source, Wizard specialist, prestige casting, epic spell, equipment, HP, companion, temporary-state, Bard, skill, and Red Dragon tests pass.
- Strict bundled/private JSON deserialization and content-integrity tests pass.
- Full non-baseline suite: **1,021/1,021 passed**.
- Fresh `PcgImportRegression` completed conversion/evaluation of all 55 files and produced the expected review diff against the untouched golden baseline.
- Existing character destination: 55 preserved, 0 overwritten, 0 newly written.

The latest and diff reports are in the configured private pack repository's `test-reports/` directory. Accepting the new golden baseline remains a separate explicit review action.

## Archfiend Lilly targeted repair

The exact source file now has an integration assertion covering the reported issues. Fresh replay produces:

- final Charisma 43, including Belly Chain's +12 enhancement bonus;
- +10 natural-armor enhancement from Emerald's Best Creation;
- Bluff 45 and Perform (Dance) 60, with the two +5 competence Bluff sources correctly non-stacking;
- all 11 PCGen languages;
- a fixed Shadow companion slot at effective level 7 for Shadowdancer 11;
- six dedicated Leadership follower links plus the cohort and Shadow companion links.
- the Charm domain owned by the Archfiend race/template, including its granted power and nine
  Archfiend domain spell-like abilities (levels 1–3 at will, 4–6 three times/day, 7–9 once/day).

The generic custom-item parser also restores equivalent supported `EQMOD` numeric bonuses on other corpus characters. Unsupported named powers are returned separately by the import UI/API so they are visible without turning otherwise clean imports into mapping failures.

## Duchess Rose / Lilliette targeted repair

The standard PCGen familiar modifier explicitly declares `COPYMASTERBAB:MASTER`,
`COPYMASTERCHECK:MASTER`, and `COPYMASTERHP:max(1,MASTER/2)`. Composite replay now implements
those rules rather than presenting Lilliette's ordinary 2-HD elemental combat progression.

For `Duchess Rose, Elite Succubus.pcg`, fresh integration replay confirms:

- Rose has Sorcerer 6 and Sorcerer caster level 23; Arcane Trickster and Dark Temptress spell
  advancement no longer increases familiar power;
- Lilliette's effective master level is therefore 6 from the recorded source build;
- Lilliette receives half Rose's 199 HP (99), Rose's class-derived BAB +10, and Rose's
  HD-progression base saves (Fortitude +6, Reflex +12, Will +15);
- Rose's displayed attack total is +15 only after her five epic attack increments; Lilliette does
  not inherit those increments, nor Rose's four epic save increments;
- Lilliette continues to use her own saving-throw ability modifiers and any bonuses belonging
  to her, while bonuses belonging only to Rose are not copied.
- PCGen's internal familiar modifiers are converted into the universal familiar progression
  template for every imported familiar, including Improved Familiar creatures. At effective
  master level 6, Lilliette retains her Small Air Elemental traits while reaching natural armor
  +6 and Intelligence 8 and receiving the cumulative familiar abilities through Speak with
  Master. Improved Familiar applies no animal-companion-style master-level penalty.
- Rose's `Epic Spells (CHA)` source is a developed-spell list at caster level 23, not a racial or
  class HD level; her 32 Knowledge (arcana) and 32 Spellcraft ranks produce three open level-10
  epic slots per day;
- Mass Frog and Mind Rape import as her two developed epic spells. Mass Frog comes from the RSRD
  epic spell list; Mind Rape comes from the DECEIT epic spell list and retains its compulsion,
  mind-affecting, XP-component, save, duration, and Spellcraft DC data.
- Skill purchases now replay before feat selection at each HD, matching level-advancement order.
  Rose's 10 final Dark Temptress Spellcraft ranks are therefore present when Ignore Material
  Components checks its 25-rank prerequisite at HD 29; her actual 32 ranks satisfy it.
- Feat-based prestige-entry validation runs after the evaluated feat choices, so Rose's imported
  Spell Focus (Enchantment) satisfies Dark Temptress. PCGen does not record the feat's acquisition
  HD, and the importer continues to preserve that uncertainty instead of inventing one.

The reported expectation of familiar master level 7 does not match this PCG file: both the
`CLASS:Sorcerer|LEVEL:6` record and PCGen's generated text sheet say Sorcerer 6, and neither
Arcane Trickster nor Dark Temptress explicitly advances familiar abilities. No unexplained +1
has been invented in the imported character.
