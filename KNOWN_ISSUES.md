# Known issues

## War-domain favored weapons

The War domain currently asks the player to choose a weapon from the equipment catalog. It grants
Martial Weapon Proficiency when the character lacks the class-wide martial proficiency and always
grants Weapon Focus for that chosen weapon. This is a temporary creation-time input: the content
model does not yet contain authoritative deity-to-favored-weapon mappings, so the engine cannot
derive the choice from `Character.Deity`.

## The vampire template does not change creature type

`template:vampire` sets no `typeOverride`, so applying it alone leaves a humanoid humanoid. The
SRD vampire template states "the creature's type changes to undead", and every other type-changing
template in the corpus encodes that directly. Imported characters come out right only because
PCGen applies `template:undead` alongside it, which is what actually moves the type — so the gap
is invisible in the corpus and would bite the first character built in the UI.

`TemplateTests.TypeChangingTemplate_SetsLifeStateFromTheResultingType` covers `template:lich` and
`template:undead` and notes the vampire exclusion.

## Imported companions carry no progression template

`PcgConverter` links companions but attaches no `template:animal_companion_standard`, so an
imported animal companion gets none of the link/share-spells/evasion/devotion abilities, natural
armour, ability increases or bonus tricks — the scaling exists and simply has nothing to fire on.
Visible on every companion in the corpus: `ac_*` abilities empty, `ac_bonus_tricks` zero, even for
masters whose effective level is well past the tiers.

Familiars (`template:familiar_standard`) and special mounts have the same shape and should be
checked alongside it.

## Nymph Archdruid's tiger is above her tier

A tiger is on the 7th-level alternative list at –6, and the Archdruid is druid 6, so the companion
resolves to effective level 0 — she is one level short of being able to field it at all.
`CompanionResolver` warns about this now. Either the pet is a different species or she needs a
7th druid level.

## Racial spellcasting grants still advance class features

`GrantRacialSpellcasting` ("a nymph casts as a 7th-level druid") registers an
`EffectiveLevelRule` so class levels stack onto the racial caster level. That rule is now scoped
`SpellcastingOnly` and `Formula`'s `EffectiveClassLevel()` honours the scope, but
`ReplayEngine`'s `LevelPermabuffs` paths still read every rule regardless of scope. A nymph druid 6
therefore fires druid class-feature permabuffs up to level 13 — wild shape and the rest — off a
grant that only ever meant caster level.

The fix is to filter both loops to `EffectiveLevelScope.ClassFeatures`. It is left out here
because it changes class features for every racial caster in the corpus (nymph, aranea, ghaele,
lillend, red dragon) and wants its own baseline review.

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

## Backlog: paladin of slaughter and paladin of freedom are not implemented

Unearthed Arcana defines three alignment variants of the paladin; only `class:paladin_of_tyranny`
exists in content. Both missing classes are on the same UA page as tyranny, share the paladin's
hit die, skill points, proficiencies and spells per day, and differ in class features and spell
list. Their lists are already transcribable from that page:

- **Paladin of slaughter** (chaotic evil) — a full replacement, like tyranny's. 1st: bane, corrupt
  weapon, create water, curse water, detect poison, detect undead, divine favor, endure elements,
  inflict light wounds, magic weapon, protection from good, protection from law, read magic,
  resistance, virtue. 2nd–4th likewise on the page.
- **Paladin of freedom** (chaotic good) — expressed as a delta against the paladin list rather
  than a replacement: *remove* death ward, discern lies, dispel chaos, magic circle against chaos,
  protection from chaos; *add* 1st protection from law, 3rd magic circle against law and remove
  curse, 4th dispel law and freedom of movement. The content model has no "remove from borrowed
  list" mechanism, so this one is either a full replacement or needs one.

Needs `extract-class` for the class definitions first; the spell tagging is mechanical after that.

## PCGen spell sources with composite labels are unmapped

PCGen writes a couatl's innate casting under the source label `Sorcerer/Cleric (Arcane)`.
`PcgIdMapper.MapClass` does not recognise it, so `PcgConverter` skips every spell under that
label — 29 spells that *do* exist in content are dropped from `Fey High Arcanist.pcg`, even
though the reconstructed character ends up with a `class:sorcerer` caster that could hold them.

Pinned by `PcgReconstructionFidelityTests.Corpus_HasExactlyOneUnmappedSpellSourceLabel`, which
fails once the label is mapped so the workaround gets removed with the fix.

