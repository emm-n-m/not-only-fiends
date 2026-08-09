# Known issues

## TODO: animal companions have their class skills but not their racial skill bonuses

Class skills were the part that mattered and are **done** — the Animal type grants no list of its
own, so a creature's class skills are the ones named in its statblock, and an animal without them
is charged cross-class for every rank. All 41 companion and familiar races now carry theirs, which
cleared the phantom overspend on eight roster characters.

The racial *bonuses* below are extracted but **not applied**. They change a printed total and
nothing else — no budget, no warning — so they are the lower-value half, and they are also where
all the extraction risk sits.

| race | SRD anchor | class skills | racial bonuses |
|:--|:--|:--|:--|
| `ape` | `ape` | climb, listen, spot | climb +8 |
| `badger` | `badger` | escape_artist, listen, spot | escape_artist +4 |
| `bear_black` | `black-bear` | climb, listen, spot, swim | swim +4 |
| `bear_brown` | `brown-bear` | listen, spot, swim | swim +4 |
| `bear_polar` | `polar-bear` | listen, spot, swim | hide +12* |
| `bear_dire` | `dire-bear` | listen, spot, swim | — |
| `boar` | `boar` | listen, spot | — |
| `camel` | `camel` | listen, spot | — |
| `crocodile` | `crocodile` | hide, listen, spot, swim | hide +4* |
| `dire_bat` | `dire-bat` | hide, listen, move_silently, spot | listen +4, spot +4 |
| `dire_lion` | `dire-lion` | hide, listen, move_silently, spot | hide +4, move_silently +4 |
| `dire_wolf` | `dire-wolf` | hide, listen, move_silently, spot, survival | hide +2*, listen +2*, move_silently +2*, spot +2*, survival +4* |
| `dog` | `dog` | jump, listen, spot, survival | jump +4*, survival +4* |
| `riding_dog` | `dog` | jump, listen, spot, survival | jump +4*, survival +4* |
| `eagle` | `eagle` | listen, spot | spot +8 |
| `elephant` | `elephant` | listen, spot | — |
| `heavy_warhorse` | `horse` | listen, spot | — |
| `horse_heavy` | `horse` | listen, spot | — |
| `horse_light` | `horse` | listen, spot | — |
| `leopard` | `leopard` | balance, climb, hide, jump, listen, move_silently, spot | balance +8, climb +8, hide +4, jump +8, move_silently +4 |
| `lion` | `lion` | balance, hide, listen, move_silently, spot | balance +4, hide +4, move_silently +4 |
| `monkey` | `monkey` | climb, hide, listen, spot | balance +8, climb +8 |
| `pony` | `pony` | listen, spot | — |
| `warpony` | `pony` | listen, spot | — |
| `tiger` | `tiger` | balance, hide, listen, move_silently, spot, swim | balance +4, hide +4, move_silently +4 |
| `tiger_dire` | `dire-tiger` | hide, jump, listen, move_silently, spot, swim | hide +4, move_silently +4 |
| `wolf` | `wolf` | hide, listen, move_silently, spot, survival | survival +4* |
| `wolverine` | `wolverine` | climb, listen, spot | climb +8 |

Verify each row against the source before applying:

- **`*` marks a bonus my heuristic thinks is conditional, and it is unreliable.** The dog's Jump +4
  is flat while its Survival +4 is "when tracking by scent"; both are starred. The polar bear's
  Hide +12 is genuinely conditional ("in snowy areas"), as are the owl's Spot in shadowy
  illumination and the cat's Hide in tall grass. Conditional bonuses have no representation in the
  engine and must be dropped rather than applied flat.
- **`dog`/`riding_dog`, the three horses and the two ponies share one statblock anchor**, so they
  share a row; the SRD entries may differ by column.
- `bear_dire` and `dire_bat` show no racial-bonus prose in their slice — possibly correct,
  possibly another slicing artifact.

Entries must be bounded by the *next anchor*, not the next `<h3>`: slicing to `<h3>` bleeds across
the interleaved dire-animal table and gave a dire bat Swim and Climb +8 belonging to its
neighbours. Watch for stray spaces after the sign too — the monkey's `Balance + 12` defeated a
`[+\-]\d` strip and silently dropped Balance from its skill list.

Three companion races are not animals and have no statblock in either file, so they are untouched
and unaudited: `companion_elemental_air_small`, `companion_elemental_water_small`,
`companion_shadow`.
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

## A celestial or fiendish animal companion cannot be chosen in the builder

A planar ranger "may have a celestial version of a normal animal as his animal companion" if
nonevil, or a fiendish version if nongood. `CompanionResolver` validates this — a non-animal
companion is rejected unless the master is a planar ranger, and then only in the direction its
alignment allows — but nothing *offers* it. The species picker lists plain animals, so building a
planar ranger in the app produces an ordinary companion and the template has to be added to the
companion character by hand. Imported characters are unaffected: PCGen records the templates and
the converter carries them.

The right shape is a selection gated by alignment, not an automatic grant: the SRD says "may
have", and a true-neutral planar ranger is both nonevil and nongood, so may choose either.

## Cohort slot level is arithmetic while the cap is a table

`MaxCohortLevel` now comes from the SRD Leadership / Epic Leadership tables, whose progression is
irregular (score 20 → 14th, 21 → 15th, 22 → 15th) and cannot be derived. But the cohort
*companion slot* granted by `feat:leadership` carries an authored `EffectiveLevelFormula` —
`min(TotalHD - 2, LeadershipScore - 2)` — and the formula DSL has no table lookup, so the two
disagree. The warning a player sees uses `MaxCohortLevel` and is correct; the slot's effective
level, which drives companion scaling, is not. Either the DSL needs a `CohortLevel()` function or
the slot should read `MaxCohortLevel` directly.

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

## The PCG baseline does not record leadership outputs

`PcgImportRegression`'s `CharacterReport` captures no `Followers`, `LeadershipScore` or
`MaxCohortLevel`, so the 2026-08-09 Epic Leadership work — which changed follower counts from
135/13/7/4/2/2 to 740/74/37/19/10/5/3/2/1 on a roster character — produced no baseline diff at
all. Any future leadership change is invisible to the harness.

## Mechanics described in prose but never encoded

`GrantAbility` carries a description string only, so any rule stated there and nowhere else is
flavour text as far as the engine is concerned. Three instances were found and fixed on
2026-08-09 (blackguard Dark Blessing / paladin Divine Grace, the Archfiend devil-and-demon-trait
immunities, and the Ring of Universal Energy Immunity's empty `grantedPermabuffs`), but the
pattern is corpus-wide and unaudited: a sweep for `"description"` strings containing "immune",
"resistance", "bonus to" and similar wording finds many more candidates, mostly in the private
packs' spells and class features.

Enumerated on 2026-08-09 by the `audit-cosmetic-permabuffs` skill: **138 public-pack
CONTENT-BUGs**, queued in priority order in [PERMABUFF_FIX_QUEUE.md](PERMABUFF_FIX_QUEUE.md), plus
12 in the private packs. Until they are worked through, treat a stat that looks wrong on a
character sheet as a likely un-encoded description.

## There is no attack or AC track outside the equipment pass

`GrantTypedBonus` only reaches AC, attack and damage when it runs inside the equipment pass, so a
class feature or feat that grants one of those has nowhere to put it. That is why `feat:dodge`,
`feat:weapon_focus` and `feat:epic_prowess` all carry empty `grantedPermabuffs`, and why the
loremaster's Weapon Trick (+1 attack) and Dodge Trick (+1 dodge AC) secrets, added 2026-08-09 so
their selections stop being dropped on import, are encoded as `GrantAbility` descriptions. The
printed AC and attack lines are short by whatever these should contribute. Fixing this needs a
non-equipment bonus track on `CharacterState`, not more content.

## Loremaster's Secret Health grants Toughness instead of +3 hit points

The SRD secret is "+3 hit points"; `loremaster_secret.json` grants `feat:toughness` instead. The
character ends up owning a feat it never took, which any `HasFeat` prerequisite will see. It is
currently invisible because `feat:toughness` is itself a no-op (`ModifyAttribute` with `value: 0`),
so the +3 is missing either way. `AttributeTarget.HitPoints` exists but is documented as
post-evaluation only, so a correct fix needs a hit-point grant that survives the CON tail pass.

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

## Permanent events scheduled past the last tick are silently dropped

`ReplayEngine` applies a `PermanentEvent` only when `BeforeTick` matches a tick index that
actually exists, so a tome or wish read *after* the final level (`BeforeTick == Ticks.Count`)
never fires and produces no warning. Storing it is accepted, which makes the loss invisible.

##PRC Class abilities not converted

When converting characters with PRCS that grant selectable abilities (like Archmage High Arcana or Loremaster sercret), the selected values are ignored and not converted

## War-domain favored weapons

The War domain currently asks the player to choose a weapon from the equipment catalog. It grants
Martial Weapon Proficiency when the character lacks the class-wide martial proficiency and always
grants Weapon Focus for that chosen weapon. This is a temporary creation-time input: the content
model does not yet contain authoritative deity-to-favored-weapon mappings, so the engine cannot
derive the choice from `Character.Deity`.

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

## Archfiend spell list is dynamic and unmodeled

The archfiend does not have an authored spell list: the class selects its list (arcane, cleric
or druid) and folds its two chosen domains' spells into it, casting everything as arcane. The
engine has no mechanism for a player-selected spell-list source combined with a domain-spell
merge — `spellListSources` handles fixed borrowing (cloistered cleric, planar ranger) but not a
choice, and domain spells never join a class list. Until that exists, `class:archfiend` has no
legal spells: nothing in the catalog is (or can be) tagged to it, and an API build of an
archfiend caster cannot acquire any of its known spells. This is an engine gap, not a content
gap — do not author a static archfiend spell list to paper over it.

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

## Unknown choice keys are silently ignored

`/simulate` and `/ticks` accept unrecognized `TickChoices` keys with HTTP 200, no warning, and
no effect — `domainSelections` and `domainIds` (both plausible names for the domain pick) no-op
silently, and an empty `choices:{}` with outstanding feat and domain slots is likewise accepted
clean. A wrong `classFeatureChoices` key gets only a soft "unknown class feature type" warning
that doesn't name the valid keys. `PUT /api/characters/{id}` accepted a malformed
`companionLinks` entry, discarded the meaningful fields, and serialized back an all-empty link.
Worst case observed: a correctly-shaped `classFeatureChoices` `domains` selection on the
archfiend class was accepted without warning and never applied. Unknown keys should be rejected
(or at minimum warned about by name), and the response should make "this choice did nothing"
impossible to miss.

## An ineligible class is indistinguishable from a nonexistent one

`next-step` omits drivers whose prerequisites aren't met, and `?driverIds=` on an ineligible
driver returns a generic payload that simply doesn't include it — identical to asking for a
made-up id. Several test agents concluded Shadowdancer, Blackguard, Archmage and Blood Hexer
"don't exist in the content set"; all of them exist, and a verification probe confirmed
`next-step` offers Archmage the moment a character genuinely qualifies. The prerequisite data is
already authored and served by `/api/content/drivers/{id}` — `next-step` should list gated
drivers with their failed prerequisites (or say "excluded: prerequisite X unmet") instead of
hiding them.

## Character creation fails with an empty 400

`POST /api/characters` returns 400 with a zero-length body on validation failure. One agent had
to bisect field by field to discover that True Neutral is alignment `"n"`, not `"tn"`. Every
validation failure should return the standard `ErrorResponse` with a code and message.

## Familiar selection has no discoverable path

The familiar choice never appears in the level-up loop; it surfaces only in
`currentPendingChoices` after all HD are committed, with no documented resolution mechanism.
The working path — `classFeatureChoices` keyed `"class_feature:familiar_options"` on an existing
tick, submitted via a full-character PUT — had to be reverse-engineered from the `featureType`
field. The pending choice should say how to answer it, or `next-step` should offer it as a step.

## Wizard spellbook contents cannot be declared

No wizard level ever offers a `spellSelections` choice, so an agent cannot populate specific
spellbook contents through the API; casters that prepare from a book end up with whatever the
default grants. (Spontaneous casters' known-spell picks work fine.)

## An invalid skill id is partially applied

`simulate` answers an unknown `skillId` with a soft "unknown skill" warning, but the committed
sheet still records ranks under the bogus id. Unknown skill ids should reject the allocation
rather than half-apply it.

## The skill-point pool is opaque

`unspentSkillPoints` did not match a hand computation from `skillPointsPerLevel` + Int modifier
in at least one build (26 reported vs 21 computed for a sorcerer block), and no response
explains the difference. Underspending never warns, so a caller who mistrusts the number has no
way to reconcile it. Expose the accrual breakdown (per-driver pool, racial ×4 first-HD rule,
whatever applies) in `next-step`.
