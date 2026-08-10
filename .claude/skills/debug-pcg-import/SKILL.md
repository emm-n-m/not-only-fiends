---
name: debug-pcg-import
description: Diagnose an imported PCGen character that came out wrong — missing feats or class-feature picks, unfamiliar warnings, values that disagree with the .pcg. Reference for the .pcg tags the importer reads and the ones that have bitten it. Use when the user says an imported character looks wrong.
argument-hint: [character-name-or-pcg-file]
---

# Debug a PCGen Import

The user reads a character in the app, spots something wrong, and reports it. Almost every such
report resolves to one of three things, and telling them apart early saves the whole
investigation:

1. **The importer never read the tag.** The information is in the `.pcg` and got dropped.
2. **The content is missing or wrong**, so a correctly-read id resolves to nothing.
3. **The source sheet is wrong** — PCGen let the user build something illegal. Then the engine
   is right and the finding belongs in the private repo's `CONTENT_GAPS.md`, not in a fix.

Decide which by reading the `.pcg` first. It is a plain-text, pipe-delimited, one-record-per-line
format; `grep` answers most questions in one command.

## Orient

```bash
# .env holds the paths; the corpus is PCGEN_CHARACTERS_PATH, saved sheets CHARACTERS_PATH.
grep -E "^(CLASS|RACE|TEMPLATESAPPLIED|STAT|DOMAIN):" "$PCGEN_CHARACTERS_PATH/Name.pcg"
```

The committed baseline report is the fastest way to see what the importer *thinks* it did —
per-character warnings, dropped items and replay warnings, without running anything:

```bash
python3 -c "
import json,io
d=json.load(io.open('$EXTRA_PACKS_PATH/test-reports/pcg_import_report.json',encoding='utf-8-sig'))
for c in d['characters']:
    if 'NAME' in c['name']:
        print(json.dumps(c,indent=1))"
```

Note the BOM: always `utf-8-sig`.

## Tags that have bitten this importer

Every one of these was a live bug found by a user reading a sheet. When a pick is missing, check
whether its tag is on this list before assuming the content is absent.

### `~` is "parent ~ choice", not part of the name

```
ABILITY:High Arcana Ability|CATEGORY:Special Ability|KEY:High Arcana ~ Arcane Fire|APPLIEDTO:Wizard
ABILITY:Bard Variant|CATEGORY:ACF|KEY:Bard Variant ~ Druid-like Bard
RACE:Companion ~ Snake (Viper/Medium)
```

The half after the tilde is the selection. `APPLIEDTO` is **not** it — on the archmage rows it
holds the advanced spellcasting class ("Wizard"), so reading `APPLIEDTO` gives you a class name
where an arcana was wanted. But some content genuinely has a tilde *in its name*
(`Favored Soul ~ Energy Resistance` is one feature), so match the whole key first and only split
as a fallback.

### `CATEGORY:` decides what a row is; the opening tag does not

```
ABILITY:FEAT|TYPE:NORMAL|CATEGORY:FEAT|KEY:Heighten Spell          <- a feat
ABILITY:Wizard Feat|TYPE:NORMAL|CATEGORY:FEAT|KEY:Extend Spell     <- also a feat
ABILITY:Fighter Feat|TYPE:NORMAL|CATEGORY:FEAT|KEY:Power Attack    <- also a feat
```

A feat handed out by a class keeps the granting pool in the first field. Matching on
`ABILITY:FEAT|` silently drops every class bonus feat — and with them any later feat that named
one as a prerequisite, which surfaces as a bogus "prerequisite not met". Route on `CATEGORY`.

The corollary: **a recovered bonus feat needs a slot to land in.** Check that the granting class
actually grants one (`GrantFeatSlot`) before assuming a "no available feat slot" warning is a new
bug. Several classes had the pool as a `GrantAbility` description only.

### `SUBSTITUTIONLEVEL:` rides on the level row

```
CLASSABILITIESLEVEL:Druid=1|SUBSTITUTIONLEVEL:Elemental Druid Option|HITPOINTS:8|SKILLSGAINED:7
```

The `CLASS:` row still says `Druid`. Substitution is *per level* in PCGen; the engine models a
variant as a whole class, which is only equivalent while the substitution differs from its base
at one level.

### An ACF can replace the class while `CLASS:` keeps the base name

`Bard Variant ~ Druid-like Bard` means the character's bard levels are a different class.
Everything filed under the class name has to follow the swap — ticks, skill purchases **and
spell rows**, since the spells are still recorded as `CLASS:Bard`.

### `USERPOOL` / `POOLPOINTS` is PCGen's own budget

```
USERPOOL:Epic Arcane Trickster Feat|POOLPOINTS:0.0
```

`POOLPOINTS:0.0` means nothing left to spend. If the sheet lists more picks from that pool than
the class grants, **the source sheet is over budget** and the engine's dropped-item warning is
correct. This is finding type 3 — record it, do not "fix" it.

### `STAT:` carries placeholders for abilities the creature does not have

An undead's `STAT:CON|SCORE:3` is not a Constitution score — undead have none. Anything reading
ability modifiers must go through the nonability rule (`CharacterState.AbilityModifier`), which
returns +0. A placeholder that leaks in reads as a −4 penalty.

### `HITPOINTS:` is the die roll, and the die may not be the class's

PCGen re-rolls when a template changes the die size, so a lich bard's rolls are d12 even though
bard is d6. A run of "saved hit-point roll N is outside dX" warnings means the *engine's* die is
wrong, not the source's.

## The mapper is shared across the corpus

`PcgImportRegression` converts all 56 characters through **one** `PcgIdMapper`. Any per-character
state stored on it leaks onto every later character — one character's class variant silently
rebuilt three unrelated ones, and only the baseline diff caught it. Keep resolution local to the
`Convert` call; the mapper's tables are static and read-only for a reason.

## Always finish on the baseline

A one-character fix is a corpus-wide change. Run `pcg-baseline` and read the per-character diff
before believing the fix — it is the only thing that reports collateral damage, and in this
session it caught two bugs that all 1,200 unit tests missed. See that skill for the workflow, and
for how to handle a baseline another session has moved underneath you.

## Where findings go

- Importer or engine bug → fix, plus a test that reads the fixture rather than restating values.
- Missing content → `CONTENT_GAPS.md` (public repo for SRD, private repo for third-party).
- Engine cannot express the rule → `KNOWN_ISSUES.md`.
- Source sheet is wrong → the private repo's `CONTENT_GAPS.md`, under "Source-data findings
  (not gaps — the engine is right)", so nobody later "fixes" content to make it build.
