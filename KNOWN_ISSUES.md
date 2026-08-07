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
