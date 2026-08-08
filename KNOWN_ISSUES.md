# Known issues

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
