# Known issues

## Conditional animal companion racial skill bonuses are unmodelled

Class skills were the part that mattered and are **done** — the Animal type grants no list of its
own, so a creature's class skills are the ones named in its statblock, and an animal without them
is charged cross-class for every rank. All 41 companion and familiar races now carry theirs, which
cleared the phantom overspend on eight roster characters.

The racial *bonuses* below change a printed total and nothing else — no budget, no warning — so
they are the lower-value half, and they are also where all the extraction risk sits. Every flat
bonus identified in these statblocks is now applied and covered by a regression. The remaining
entries are either conditional bonuses or statblocks with no racial-bonus prose; conditional
bonuses remain excluded because the character model has no terrain, illumination, or tracking
context.

| race | SRD anchor | class skills | racial bonuses |
|:--|:--|:--|:--|
| `ape` | `ape` | climb, listen, spot | climb +8 (**applied**) |
| `badger` | `badger` | escape_artist, listen, spot | escape_artist +4 (**applied**) |
| `bear_black` | `black-bear` | climb, listen, spot, swim | swim +4 (**applied**) |
| `bear_brown` | `brown-bear` | listen, spot, swim | swim +4 (**applied**) |
| `bear_polar` | `polar-bear` | listen, spot, swim | hide +12 (snowy areas; conditional) |
| `bear_dire` | `dire-bear` | listen, spot, swim | — |
| `boar` | `boar` | listen, spot | — |
| `camel` | `camel` | listen, spot | — |
| `crocodile` | `crocodile` | hide, listen, spot, swim | hide +4 (tall grass; conditional) |
| `dire_bat` | `dire-bat` | hide, listen, move_silently, spot | listen +4, spot +4 (blindsense; conditional) |
| `dire_lion` | `dire-lion` | hide, listen, move_silently, spot | hide +4, move_silently +4 (**applied**) |
| `dire_wolf` | `dire-wolf` | hide, listen, move_silently, spot, survival | hide +2, listen +2, move_silently +2, spot +2, survival +4* (**flat bonuses applied**) |
| `dog` | `dog` | jump, listen, spot, survival | jump +4 (**applied**), survival +4 (scent tracking; conditional) |
| `riding_dog` | `dog` | jump, listen, spot, survival | jump +4 (**applied**), survival +4 (scent tracking; conditional) |
| `eagle` | `eagle` | listen, spot | spot +8 (**applied**) |
| `elephant` | `elephant` | listen, spot | — |
| `heavy_warhorse` | `horse` | listen, spot | — |
| `horse_heavy` | `horse` | listen, spot | — |
| `horse_light` | `horse` | listen, spot | — |
| `leopard` | `leopard` | balance, climb, hide, jump, listen, move_silently, spot | balance +8, climb +8, hide +4, jump +8, move_silently +4 (**applied**) |
| `lion` | `lion` | balance, hide, listen, move_silently, spot | balance +4, hide +4, move_silently +4 (**applied**) |
| `monkey` | `monkey` | climb, hide, listen, spot | balance +8, climb +8 (**applied**) |
| `pony` | `pony` | listen, spot | — |
| `warpony` | `pony` | listen, spot | — |
| `tiger` | `tiger` | balance, hide, listen, move_silently, spot, swim | balance +4, hide +4, move_silently +4 (**applied**) |
| `tiger_dire` | `dire-tiger` | hide, jump, listen, move_silently, spot, swim | hide +4, move_silently +4 (**applied**) |
| `wolf` | `wolf` | hide, listen, move_silently, spot, survival | survival +4 (scent tracking; conditional) |
| `wolverine` | `wolverine` | climb, listen, spot | climb +8 (**applied**) |

Verify each row against the source before applying:

- Conditional rows are intentionally not applied flat: the dog's Survival +4 is "when tracking by
  scent", the polar bear's Hide +12 applies "in snowy areas", and the crocodile's Hide +4 applies
  in tall grass. The dire bat's Listen/Spot bonuses are lost if its blindsense is negated. The
  engine has no terrain, illumination, blindsense, or tracking state in which to evaluate these.
- **`dog`/`riding_dog`, the three horses and the two ponies share one statblock anchor**, so they
  share a row; the SRD entries may differ by column.
- `bear_dire` and `dire_bat` show no racial-bonus prose in their slice — possibly correct,
  possibly another slicing artifact.

Entries must be bounded by the *next anchor*, not the next `<h3>`: slicing to `<h3>` bleeds across
the interleaved dire-animal table and gave a dire bat Swim and Climb +8 belonging to its
neighbours. Watch for stray spaces after the sign too — the monkey's `Balance + 12` defeated a
`[+\-]\d` strip and silently dropped Balance from its skill list.

Three companion races are not animals and were previously untouched by the animal audit. Their
printed movement, natural attacks, shadow deflection bonus, and flat Listen/Spot/Search bonuses
are now encoded and regression-covered: `companion_elemental_air_small`,
`companion_elemental_water_small`, and `companion_shadow`. Situational mastery, whirlwind/vortex,
turn resistance, and incorporeal-touch resolution remain outside the current combat model.
## TODO: animal tricks are unmodelled, and import as unmatched class abilities

A `.pcg` records trained tricks as class abilities — "Animal Trick ~ Attack", "~ Defend", "~ Fetch"
— so the converter reports each as `has no matching class-feature option`. Eight warnings of pure
noise on any trained companion, and the tricks themselves are dropped.

They are not class features. A trick is a property of the **animal**, and its budget is a capacity
the creature has rather than anything a class grants:

- **Handle Animal, Teach an Animal a Trick:** "An animal with an Intelligence score of 1 can learn
  a maximum of three tricks, while an animal with an Intelligence score of 2 can learn a maximum
  of six tricks." Those are the only two values the SRD gives, because that is the animal range.
- **Bonus tricks** from the companion progression, which "don't require any training time or Handle
  Animal checks, and they don't count against the normal limit": 1 at effective druid level 1–2,
  then 2/3/4/5/6/7 at 3–5, 6–8, 9–11, 12–14, 15–17, 18–20. Epic adds one more every three levels
  past 20th.
- Teaching an animal to attack *all* creatures "counts as two tricks" — which is what PCGen's
  "Animal Trick ~ Attack II" alongside "~ Attack" is recording.

Modelling it needs a tricks list on `CharacterState`, the capacity rule, import mapping including
the double-cost variants, and somewhere to spend the budget. **Parked deliberately: the area is
badly defined.** The SRD table stops at Intelligence 2, and a boosted companion goes past it — the
roster's fiendish viper carries `template:int_bonus_+2` and sits at Intelligence 3, where the rules
say nothing. Its eight tricks happen to equal 6 (the Int-2 maximum) plus 2 (its bonus tricks at
effective druid level 3), which may be the right budget or may be coincidence; there is no way to
tell from the text.

Until it is modelled, the cheap win is to stop the warnings: recognise "Animal Trick ~ *" on import
and drop it deliberately rather than reporting it as an unmatched class feature.

**The warning suppression is now implemented.** The converter deliberately ignores these entries;
the trick budget and selected-trick state remain unmodelled.

## The Celestial and Fiendish Creature templates are derived, not transcribed

The SRD mirror carries neither page — `siteMap.html` lists both, but no file holds the text, the
same gap that already existed for `template:fiendish`. `template:celestial` was authored on
2026-08-09 by mirroring `template:fiendish` exactly (alignment prerequisite inverted, base-type
list, `animal`/`vermin` → magical beast, LA +2, darkvision 60, DR 5/magic at 4 HD and 10/magic at
12, SR = HD + 5) and substituting the two things that genuinely differ: resistance to acid, cold
and electricity in place of cold and fire, and Smite Evil in place of Smite Good. The element set
is corroborated by the Half-Celestial entry in `monstersHtoI.html` ("Resistance to acid 10, cold
10, and electricity 10") against Half-Fiend's four, though those are a different template with
different numbers.

If either page ever appears in a real source, replace both rather than editing around them —
the same standing instruction as the two derived spells below.

## A half-created companion made its master unreadable — fixed

Clicking "create follower" (and "create blank cohort") saved a stub character with an empty
`RaceId`, which `ContentRegistry.GetRace` throws on. `CompanionResolver` evaluated companions
inside the master's build with nothing between them, so that one stub took the master's whole
evaluation down — and the builder replaced its entire markup with the exception text, leaving no
way to repair either character in the UI. Two roster followers were created this way and deleted
on 2026-08-14, with their master links removed.

Three changes, in order of how much they matter:

- `CompanionResolver.AddCompanion` isolates each companion; a failure there is a warning naming
  the companion, the companion is left out of the build, and the master builds in full. Covered by
  `CompanionTests.CompanionResolver_CompanionThatCannotEvaluate_WarnsAndBuildsMaster`.
- The builder separates a fatal content-load failure (still takes the page) from an evaluation
  failure (a banner above a live, editable builder), so a character that fails to evaluate is
  repairable where it is displayed. The read-only sheet offers "Open in builder to fix" instead.
- Every character the builder creates without a species pick now gets `race:human` rather than
  `""`, so the stub cannot be written in the first place.

The general rule this encodes: **a failure while creating or resolving one character must not make
another character unreadable.**

## A celestial or fiendish animal companion cannot be chosen in the builder — fixed

The builder and API now expose optional celestial/fiendish template choices for a planar ranger’s
animal-companion slot, gated by the master’s alignment. The choice is stored in
`TickChoices.CompanionTemplateChoices`, carried onto the evaluated slot, and applied when the
builder creates the linked companion character. True neutral exposes both options; imported
characters continue to work through their existing template IDs.

## Cohort slot level is arithmetic while the cap is a table — fixed

`MaxCohortLevel` now comes from the SRD Leadership / Epic Leadership tables, whose progression is
irregular (score 20 → 14th, 21 → 15th, 22 → 15th) and cannot be derived. But the cohort
*companion slot* now uses the formula DSL's `CohortLevel()` function, so its effective level
matches the table-derived `MaxCohortLevel` used by warnings and companion validation.

## Leadership modifiers are a manual input

The SRD modifier table is mostly campaign judgement — renown, cruelty, whether the leader keeps a
stronghold — so it lives in `Character.LeadershipModifiers` and must be ticked per character in the
builder. An imported character therefore starts with every modifier unset and a Leadership score
equal to the bare base.

**This is not an import gap, and there is nothing to fix in `PcgConverter`.** PCGen models no
follower counts and enforces no leadership rules at all — it will happily file a deity as the
follower of a toad — so a `.pcg` has no modifier field to carry, and no cohort or follower limit
to compare against. This engine is the stricter of the two here. The two derivable modifiers
(familiar/mount/companion, differently aligned cohort) are computed and are not settable.

## The PCG baseline did not record leadership outputs — fixed

`PcgImportRegression`'s `CharacterReport` captures no `Followers`, `LeadershipScore` or
`MaxCohortLevel`, so the 2026-08-09 Epic Leadership work — which changed follower counts from
135/13/7/4/2/2 to 740/74/37/19/10/5/3/2/1 on a roster character — produced no baseline diff at
all. The report now stores and diffs the leadership score, cohort cap, and complete follower
level table. The next run against the live corpus should be accepted deliberately with
`UPDATE_PCG_BASELINE=1`.

## Mechanics described in prose but never encoded

`GrantAbility` carries a description string only, so any rule stated there and nowhere else is
flavour text as far as the engine is concerned. Three instances were found and fixed on
2026-08-09 (blackguard Dark Blessing / paladin Divine Grace, the Archfiend devil-and-demon-trait
immunities, and the Ring of Universal Energy Immunity's empty `grantedPermabuffs`), but the
pattern is corpus-wide and unaudited: a sweep for `"description"` strings containing "immune",
"resistance", "bonus to" and similar wording finds many more candidates, mostly in the private
packs' spells and class features.

Enumerated on 2026-08-09 by the `audit-cosmetic-permabuffs` skill: **138 public-pack
CONTENT-BUG candidates**, plus 12 in the private packs. The dated queue from that run has been
retired: it was a historical snapshot and several candidates were fixed after it ran. Its
unresolved categories are preserved in [CONTENT_MECHANICS_BACKLOG.md](CONTENT_MECHANICS_BACKLOG.md).
Re-audit before treating a description as an active defect. Until the remaining candidates are
worked through, a stat that looks wrong on a character sheet may still be an un-encoded description.

The public Unearthed Arcana `feat:bladeproof_skin` now grants its printed DR 3/bludgeoning; its
armor check penalty still has no corresponding state field. Dragon Disciple's printed ability
boosts, Medium-size claws and bite, and the final +4 natural
armor total are now encoded and regression-covered. The remaining variety-dependent breath and
energy immunity, the structured 30/60-foot blindsense range, and the apotheosis type/vision
metadata still need model support or a player choice for dragon ancestry.

Monk Diamond Soul now contributes spell resistance equal to the current monk level + 10. The
state also exposes fast healing and turn resistance, and the corresponding static values are
encoded for epic Fast Healing, imps, quasits, vampires, liches, and shadows. A turning resolver,
activation timing for magic-item healing, and other encounter-only consequences remain outside
the model.

The same pass also made Monk AC Bonus conditional on the final armor and load state, encoded the
Nymph's Charisma-based Unearthly Grace deflection bonus, and added Shambling Mound fire resistance
and Grimlock's gaze/visual/illusion immunities. Conditional terrain, opponent, and sensory effects
remain intentionally unmodelled.

Several static equipment effects from the same historical queue are now encoded: Lens of Detection
Search, Necklace of Adaptation vapor/gas immunity, both Periapts' immunities, and Robe of Eyes'
Search/Spot bonuses, and Shield of the Sun's SR 15 and five energy resistances. Their remaining
vision, conditional, spellcasting, flat-footed, and anti-flanking rules remain unrepresented.

## Weapon-specific combat bonuses are partially implemented

The non-equipment typed-bonus track is now present on `CharacterState`, so general AC, attack and
damage bonuses apply during final combat calculation. `feat:dodge`, `feat:epic_prowess`, and the
loremaster Weapon Trick/Dodge Trick now use it. Dodge is intentionally represented as a flat
bonus because the character model has no designated-opponent state.

`feat:weapon_focus`, `feat:weapon_specialization`, their greater versions, and the epic weapon
focus/specialization feats now carry a selected weapon ID through to the matching attack line.
Remaining gaps are conditional benefits such as Epic Weapon Specialization's 30-foot ranged
restriction, critical-hit-only bonuses, and complete identity handling for unarmed and natural
weapons.

## Loremaster's Secret Health grants Toughness instead of +3 hit points — fixed

The content now grants the actual +3 hit points, and the Toughness feat itself uses the same
flat-hit-point path. The bonus survives the Constitution tail pass and still works for grants
applied to an already-finished companion state.

## The undead templates were extracted with invented rules

Found 2026-08-10 while chasing a hit-die bug, and the reason to re-audit the rest of that batch
rather than trust it. Corrected:

- **`template:lich` and `template:undead` both claimed undead "use CHA for HP and Fort saves"**
  (the generic one added "and CON-based abilities"). That is a Pathfinder rule; the 3.5 SRD undead
  entry says only "No Constitution score", with the d12 Hit Die as the compensation. Rewriting the
  descriptions also restored three traits the extraction had dropped — immunity to mind-affecting
  effects, immunity to damage to physical ability scores, and healing from negative energy.
- **The lich's natural armor was additive.** The SRD gives it "+5 natural armor bonus *or the base
  creature's, whichever is better*", while the vampire's really does say "improves by +6" — the
  same-looking number with opposite arithmetic. Hence `naturalArmorFloor` beside `naturalArmor`.
- **The lich's Fear Aura, Touch Attack and Paralyzing Touch were plain abilities.** They are
  Special Attacks in the SRD and are now `GrantSpecialAttack`. The touch is supernatural and taken
  once per round: it is deliberately *not* a natural attack, which would wrongly earn it iteratives.

`template:vampire` is now corrected: Children of the Night is present, and blood drain, children
of the night, dominate, create spawn, and energy drain are represented as special attacks rather
than inert abilities. Turn resistance and the other explicitly unmodelled template fields remain
outstanding below.

Unmodelled for every template, not a mis-extraction: Challenge Rating, Treasure, Organization and
Advancement are not represented at all. Turn resistance is now retained in state, but there is no
turning system behind it. A
type-changing template adds the `augmented` subtype but not the original type alongside it, so a
lich reads as "undead (augmented)" rather than "undead (augmented humanoid)".

Monk Perfect Self is now represented as a type change to Outsider at monk 20, alongside its
existing DR 10/magic. Resurrection-specific treatment and other type-change edge cases remain
outside the current model.

## An acquired template applies from creation, so it pays for levels taken before it

> **Done 2026-08-11.** `Character.TemplateAcquisitionHD` records the 1-based HD a template was
> acquired at (absent = creation, so every stored character keeps working); the engine applies it
> at that tick, forward only, restating banked hit-die sizes per the SRD's "current and future"
> rule while never re-opening accrued skill points. An `ApplyTemplate` permabuff covers the class
> capstone and consequence chains (lich → undead → augmented humanoid), the importer strips
> consequence rows and stamps prerequisite-bearing templates at the earliest legal HD, and the
> builder surfaces the acquisition HD as an owed decision. Lich Recruiter grants 162 with 0
> unspent and is a living human at HD 8; Duchess Rose answers as both her sheets from one record
> (`Golden_CapstoneTransformation_*`). The ascension case — a transformation that CONSUMES
> another template — is covered by `RevokeTemplate` (delta-inverse, type rebuilt from the base
> race up, scaling-formula targets reset; floors and max-semantics buffs stay and warn) plus
> `EndRacialBonusSkillPoints` (racial identity ends going forward, the acquisition tick still
> pays, the race remains on the sheet as recorded origin): Infernal Countess Lilly grants
> exactly PCGen's 253 with 0 unspent (`Golden_Ascension_InfernalCountessLilly`). Residuals: the
> vampire's own chain is not yet authored (her saved character still carries explicit
> Undead/Augmented rows at creation), and the roster and header owed-rollups do not yet count
> an unanswered acquisition HD. The analysis below is kept as the rationale.

A template is applied before the first HD tick, whatever it is. That is right for an inherited
template — a half-fiend was always a half-fiend — and wrong for an acquired one. Lichdom needs
caster level 11 to make the phylactery, so a character reaches 11th level as a living creature
and *then* becomes a lich; the template's +2 Int must feed forward from that HD, exactly as a
tome of intellect read at 11th level would.

Because it applies from HD 1 instead, it retroactively buys skill points the character never had.
Lich Recruiter (human bard 13, Int 17, all three level-up increases spent on Cha) shows it
cleanly — the builder reports "14 unspent":

| Int used per level | 1st | 2nd–11th | 12th–13th | total |
|:--|--:|--:|--:|--:|
| applied at creation (pre-fix) | 19 → +4 → 44 | 12 each | 12 each | **176** |
| acquired at 12th | 17 → +3 → 40 | 10 each | 19 → +4 → 11 each | **162** |
| PCGen's own per-level record | 40 | 10 each | 11 each | **162** |

The SRD compensates for this in the template text itself — "Do not recalculate base attack bonus,
saves, or skill points" — which is what PCGen implements.

**Nothing about an acquired template reaches backwards in time.** At 8th level this character is a
human bard working towards lichdom, and evaluating the timeline at HD 8 must say so: living,
Int 17, d6 Hit Dice. That is the whole point of replaying a timeline rather than storing a
snapshot — one authoritative sheet answers "what was she at 8th?" and "what is she now?", where
PCGen needs a separate sheet per level.

The one thing to be careful of is that a template firing at HD 12 may still *restate* quantities
already laid down by that same evaluation. "Increase **all current and future** Hit Dice to d12s"
means that at the moment she becomes a lich, dice she rolled at 1st through 11th become d12 —
which is not the template applying at HD 1, it is the template applying at HD 12 and rewriting
what is on the sheet then. Evaluated at HD 8 those dice are still d6.

So the split the implementation needs is between quantities that are *re-derived* from the
finished character and quantities *accrued* per tick:

- **Accrued per tick, and never re-opened** — skill points, and anything else banked at the level
  it was earned. These read the ability scores as they were at that tick. BAB and saves are
  progression-derived and already immune.
- **Re-derived from the state at the evaluated HD** — hit die size and hit points, creature type
  and everything following from it (life state, corporeality, nonabilities), natural armor, DR,
  immunities, level adjustment. These are correct automatically once the template fires at the
  right tick, because they are computed rather than accumulated.

Nothing records *when* it was acquired. The `.pcg` has no acquisition level and PCGen does not
model one, so this is new character input — a decision owed, placed the way a tome is placed, and
an import cannot derive it. Lich Recruiter cannot even disambiguate her own: 11th, 12th and 13th
all yield 162, because the +2 only moves the modifier across the same boundary the character
would have crossed anyway. Import fidelity is not the goal here; modelling the character
truthfully is, and where the two disagree the timeline wins.

The same mechanism is owed to mid-career changes that are *not* templates the player places. A
prestige-class capstone that changes creature type is the same kind of event, but pinned to a
class level, so it needs no decision and can be authored on the class. Whatever shape the
acquisition support takes should cover both, since a type change at 10th has exactly the same
"what was she at 8th?" question behind it.

**There is a ready-made acceptance test.** The corpus holds one character as *two* PCGen sheets —
before and after a capstone that changes her type and adds several templates — because PCGen
cannot express both from one record. That is the case this feature exists to collapse: importing
the later sheet and evaluating the timeline at the earlier HD should reproduce the earlier sheet,
with no second character stored. The pairing is named in the private materials repo's
`CONTENT_GAPS.md` (the classes and templates involved are not OGC).

## Nonabilities are modelled for modifiers, not for their other consequences

Undead and constructs have no Constitution and incorporeal creatures no Strength, and as of
2026-08-10 every modifier read goes through `CharacterState.AbilityModifier`, which returns +0 for
an absent ability — so hit points, saves, skills and attacks are right, and an incorporeal
creature attacks with Dexterity. Undead templates now also expose their physical-ability-damage
and Fortitude-effect immunities in state. The rest of the SRD's Nonabilities paragraph is not
modelled: automatically failing checks keyed to the missing ability, immunity to ability
damage/drain beyond the typed undead entries, and being unable to tire. Carrying capacity now
reports zero for a creature without Strength; the remaining items need rule-state and check-system
support.

The score itself is left as the source recorded it and rendered as `—`; it is not zeroed, because
the SRD is explicit that these creatures "do not have an ability score of 0 — they lack the
ability altogether", and a stored 0 would read as a real score to anything that missed the flag.

Related: the lich template's own description claims undead "use CHA for HP and Fort saves". That
is a Pathfinder rule, not 3.5 — the SRD undead entry says only "No Constitution score", and the
d12 Hit Die is the compensation. This description remains a source-verification correction, not
an engine rule.

## Choice-dependent spell exclusions are only partially modeled

`SpellcastingProgression.SpellListExclusions` is a fixed list authored on the driver, which suits
the paladin of freedom (it always loses the same five spells) and nothing else. Secrets of
Theurgy's elemental druid is now handled: `GrantDomainSelection.OpposedDomainIds` records the
character-dependent mapping, and replay removes the opposed domain's bonus spell IDs from the
chosen class list; the API and builder hide those spells and replay rejects attempts to select
them. Other choice-dependent exclusions still need declarations.

## Epic class progression is unmodelled except for Arcane Trickster

`LevelPermabuffs` is keyed by class level and every class stops at its non-epic maximum, so no
class grants anything above 20th (or above 10th for a prestige class). Epic bonus feats, epic
class features and the rest are simply absent. Arcane Trickster was given its epic bonus feats
(levels 14/18/22/26/30, matching `14:REPEATLEVEL:4` in PCGen's `rsrd_classes_prestige_epic.lst`)
on 2026-08-09 because an imported character needed them; every other epic progression is still
missing, and the 30 cap there is arbitrary rather than a rule.

## IMP.pcg spends one more feat than its own sheet allows

`IMP.pcg` lists 11 feats — 9 general plus 2 from the Epic Arcane Trickster pool — but at 26 HD
and Arcane Trickster 14 the budget is 9 general plus 1 pool feat. The .pcg's own
`USERPOOL:Epic Arcane Trickster Feat|POOLPOINTS:0.0` agrees there is nothing left to spend, so
the source sheet is over budget, not the engine. The import drops one feat and reports it; which
one it drops follows list order and is arbitrary. Fix the character in PCGen, not the importer.

## Permanent events scheduled past the last tick are silently dropped — fixed

Events at or after the final tick boundary are now applied before post-tick finalization, so a
tome or wish read after the final level is visible in the final state.

## PRC class abilities not converted — fixed for mapped selections

The converter now maps the selected options for Archmage High Arcana and Loremaster Secrets to
their granting ticks. Unsupported or unmapped private-pack selections remain reported as drops.

## War-domain favored weapon mapping remains deity-dependent

The War domain asks the player to choose a weapon from the equipment catalog. It grants Martial
Weapon Proficiency when the character lacks the class-wide martial proficiency and always grants
Weapon Focus for that chosen weapon; this choice is persisted and replayed. The content model does
not yet contain authoritative deity-to-favored-weapon mappings, so the engine cannot derive the
choice from `Character.Deity`.

## Nymph Archdruid's tiger is above her tier

A tiger is on the 7th-level alternative list at –6, and the Archdruid is druid 6, so the companion
resolves to effective level 0 — she is one level short of being able to field it at all.
`CompanionResolver` warns about this now. Either the pet is a different species or she needs a
7th druid level.

## Two spells are derived from their good-aligned originals, not quoted from the SRD

*Corrupt weapon* (blackguard 1st, paladin of tyranny 1st) and *unholy sword* (paladin of tyranny
4th) are named on those class pages but have no stat block anywhere in the mirror. The SRD
describes the first only as "the opposing counterpart of the paladin spell bless weapon", and the
UA paladin variants are explicitly alignment inversions of the paladin, so both are authored by
mirroring their originals — `bless_weapon.json` and `holy_sword.json` — with the alignment flipped:
same school, components, casting time, range, duration, save and SR, with the target alignment and
`unholy_sword`'s descriptor reversed.

Every other spell in the corpus is transcribed from the mirror. If these two ever appear in a
source with real text, replace them rather than editing around them.

Separately, the blackguard page lists *protection from elements*, the 3.0 name of *protection
from energy*; the latter is what is tagged.

Every spellcasting class now reaches its list. `SpellContentTests` pins the assassin, blackguard
and paladin-of-tyranny lists spell by spell, and pins cloistered cleric and planar ranger to the
lists they borrow via `spellListSources`.

## Archfiend spell list is dynamic — implemented for the configured pack

The engine supports the selected arcane/cleric/druid list through the archfiend list templates,
and `GrantDomainSelection` can merge the two selected domain lists without creating prepared
domain slots. The behavior is covered by `ArchfiendSpellListTests`; the tests are gated when the
private archfiend pack is unavailable.

# Agent-facing API issues

Found by the 2026-08 corpus rebuild test: 55 agents each rebuilt a saved character through
`/api/*` alone, guided only by its `.pcg` file, and the results were diffed against the PCG
import golden baseline. 13 of 55 rebuilds matched the baseline exactly (including multiclass
prestige builds); most incomplete builds were the engine *correctly* refusing prestige classes
whose prerequisites the source characters never actually met — the import replay applies those
ticks permissively and logs the same "prerequisite not met" warnings. The issues below are what
the test surfaced in the API itself, ranked by how badly each misleads an agent.
Content gaps found by the same test are tracked in [CONTENT_GAPS.md](CONTENT_GAPS.md) (SRD
packs) and the private materials repo's `CONTENT_GAPS.md` (extra packs).

## Unknown choice keys are rejected or warned explicitly

`/simulate` and `/ticks` used to accept unrecognized `TickChoices` keys with HTTP 200, no warning,
and no effect — `domainSelections` and `domainIds` (both plausible names for the domain pick) no-op
silently, and an empty `choices:{}` with outstanding feat and domain slots is likewise accepted
clean. A wrong `classFeatureChoices` key got only a soft "unknown class feature type" warning
that didn't name the valid keys. `PUT /api/characters/{id}` accepted a malformed
`companionLinks` entry, discarded the meaningful fields, and serialized back an all-empty link.
**Partially fixed:** unknown top-level JSON fields now fail model binding, unknown choice fields
are warned about by exact key, unknown class-feature keys list valid pending keys, malformed
companion links are rejected, and restricted domain grants now expose only their legal options.
An empty choices object is still legal input and produces pending-choice metadata; archfiend-specific
domain presentation semantics remain follow-up work.

## An ineligible class is indistinguishable from a nonexistent one — fixed

`next-step` now returns `excludedDrivers` with the failed prerequisite or max-level reason, and
`unknownDriverIds` for requested IDs absent from the catalog.

## Character creation fails with an empty 400 — fixed

API model-binding failures are now translated into the standard `ErrorResponse` shape with a
`malformed_request` code and actionable JSON error message.

## Familiar selection — fixed

`next-step` exposes the familiar option group with the exact
`classFeatureChoices["class_feature:familiar_options"]` key and legal options. The builder shows
the same selection in the Companions facet on the tick that grants the slot; a full-character PUT
or the builder’s normal save path submits the choice.

## Wizard spellbook contents cannot be declared — fixed

`next-step` now exposes spellbook `spellSelections` groups with legal options, existing picks,
and remaining capacity for each wizard spell level.

## An invalid skill id is partially applied — fixed

`simulate` still answers an unknown `skillId` with a warning, but the allocation is skipped and
the committed sheet no longer records ranks under the bogus id.

## The skill-point pool was opaque — fixed

`next-step` now exposes `skillPointAccruals` (source, base points, Intelligence modifier, first-HD
multiplier, and awarded points), alongside the remaining pool, so callers can reconcile the total.

## Leadership outputs are computed but not exposed by the API — fixed

`CharacterSheet` now carries a nullable `leadership` block (base/cohort/follower scores, cohort
cap, follower capacity table, occupancy, modifier notes), mapped in `CharacterSheet.FromState`,
so `/sheet`, evaluate responses and `next-step`'s `currentSheet` all serve it. Null — omitted
from JSON — when the character has no Leadership, so a consumer can tell "no Leadership" from a
score of 0. Found 2026-08-27 while building Ember's followers through the API: the only way to
learn her follower capacity (135/13/7/4/2/2) was to read the baseline report in the private
materials repo.

**Residual: `followerOccupancy` is always empty on API paths.** `CompanionResolver` runs only in
`BuilderView`; no API evaluation resolves companions, so slots-taken counts (and over-capacity
warnings) exist only in the builder. Wiring the resolver into `AgentApiService` evaluation is a
deliberate follow-up — it multiplies per-request evaluation cost by the companion count and
changes the API's warning surface.

## An ability increase on a tick that grants none is dropped silently — fixed

Racial HD deliberately grant no every-4-HD ability increase (`GameRules.GrantsAbilityIncrease`
requires `DriverKind.Class`; a monster's printed scores already reflect its innate HD). But a
tick that carries `abilityIncrease` when none is due was accepted with HTTP 200, no warning, and
no effect — found 2026-08-27 when a succubus's HD-4 racial tick silently ate a DEX increase that
by the engine's own rule belongs at total HD 8 (her second class level). The engine now warns
("ability increase 'X' ignored — no increase is due on this tick"). The new warning exposed two
corpus imports carrying dead increases from PCGen PRESTAT stat *edits* on unscheduled levels;
`PcgConverter` now writes `AbilityIncrease` only on scheduled ticks (mirroring its racial-HD
guard), so the baseline diff is empty rather than accepted-with-noise.

## Familiar link bonuses are unmodelled — and belong to the relationship, not the creature

The SRD's familiar-side improvements — Intelligence rising with master class level, the
improved natural armor bonus, hit points as half the master's, the share-spells/empathic-link
family — are not represented anywhere: familiar races carry no scaling formulas, and
`CompanionOrigin.EffectiveMasterLevel` reaches the familiar's evaluation but nothing consumes
it for stats. A standalone familiar therefore reads as the base animal (verified 2026-08-27:
the default tiny viper reproduces the SRD statblock exactly).

**Design decision: keep it that way on the character's own sheet.** These bonuses are courtesy
of the relationship, not real stats of the creature — so when modelled, they apply only in the
companion-resolution context (`CompanionResolver` / the builder's Companions facet, where the
master's level is in view), the same host-side boundary that owns follower occupancy. The
standalone record stays the base animal; breaking the link must cost the bonuses without
touching the character file.

## Known-caster spell selections have no discoverable option groups

The wizard spellbook fix exposed `spellSelections` groups in `next-step`, but a spells-known
caster (bard at least; likely sorcerer and assassin) gets no `spellChoices` group at any tick —
building a bard's known list through the API on 2026-08-27 required reading the class table and
submitting selections blind (they are accepted, validated, and persisted correctly; only the
discovery surface is missing). Re-verify against `next-step` before relying on this, then extend
the wizard group mechanism to spells-known progressions with per-level capacity.
