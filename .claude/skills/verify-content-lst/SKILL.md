---
name: verify-content-lst
description: Verify private-pack content JSON against the PCGen LST data files and report mismatches. The extra-packs sibling of verify-content — use to audit rules accuracy of third-party/homebrew content that has no SRD page.
argument-hint: [pack-ids...]
---

# Verify Private-Pack Content Against PCGen LSTs

`verify-content` audits the public packs against the SRD mirror and declares everything else
out of scope. This skill covers that remainder: the private packs in `EXTRA_PACKS_PATH`,
diffed against the PCGen LST data set. LSTs are *structured* — `HD:4`, `PRESKILL:1,Spellcraft=10`,
`CLASSES:Cleric=4` — so most comparisons are tag-to-field, easier than the SRD's HTML prose.

## The one rule that matters

**Ground truth is the LST files under `PCGEN_DATA_PATH` (in `.env`). Never your own knowledge
of these books.** Every finding carries a verbatim LST fragment; anything with no LST entry is
`UNVERIFIABLE`, never guessed.

One asymmetry the SRD audit didn't have: the publisher LSTs are community transcriptions and
the user does **not** own PDFs of these books, so a disagreement cannot be tie-broken against
the source. Findings therefore carry a verdict, not just a severity:

- `JSON-BUG` — the LST is structurally coherent and the JSON plainly diverges (wrong HD,
  missing prerequisite, wrong spell level).
- `LST-SUSPECT` — the LST looks like the error (internally inconsistent, contradicts its own
  DESC, known-buggy formula idioms).
- `UNRESOLVABLE` — genuine ambiguity; report both readings.

Exception: `deceit/` is the user's **own** transcription of the homebrew — treat it as ground
truth of intent, and JSON≠LST there defaults to `JSON-BUG` (or a question for the user).

## Report, never edit

Same reason as verify-content: content changes silently rewrite saved characters. Produce
findings and proposed diffs; applying them is a separate, reviewed step. This matters *more*
here — every private pack exists because a real `.pcg` character uses it, and the PCG import
golden baseline (`pcg-baseline` skill) will shift with any fix.

## Ground truth map

`PCGEN_DATA_PATH` (see `.env`) — per-publisher directories. The pack→LST mapping:

| pack | LST location |
|---|---|
| `12_to_midnight` (Blood Hexer) | `12_to_midnight/curses/curses_classes.lst` |
| `12_to_midnight_curses` | `12_to_midnight/curses/curses_{feats,spells}.lst` |
| `aeg_evil` | `alderac_entertainment_group/evil/` — `evil_domains.lst`, `evil_feats.lst`. The pack's spells are NOT from the Evil book: all of them live in `alderac_entertainment_group/gods/aeg_gods_spells.lst` (found 2026-07-27 audit) |
| `aeg_feats` | `alderac_entertainment_group/feats/35e/aeg_feats_35e_feats_*.lst` (split by feat type: general/background/infernal/magic/quest/appendix) |
| `en_elements_of_magic` | `en_publishing/elements_of_magic_revised/eomr_{feats,spells}.lst` |
| `mongoose_enchantment` | `mongoose_publishing/encyclopaedia_arcane/enchantment/enchantment_{feats,spells,templates}.lst` |
| `mongoose_publishing` (Dark Temptress) | same dir, classes in `enchantment_35e/` |
| `necromancer_eldritch_sorcery` | `necromancer_games/eldritch_sorcery/eldritchsorcery_{feats,spells}.lst` |
| `sword_and_sorcery` (Blood Witch) | `sword_and_sorcery_studios/scarred_lands/relics_and_rituals/` (ignore `relics_and_rituals_BAK/`) |
| `deceit_homebrew` | `deceit/` — per-template `*_templates.lst` + `*_abilities.lst`; Archfiend under `deceit/archfiend/` |
| `fiendish_codex_1`, `fiendish_codex_2` | **no LSTs — out of scope here.** Audit these against the PDFs at `SOURCE_PDFS_PATH`, verify-content-style with page-number quotes. |

The table is a starting point, not gospel — when an item isn't where expected, grep the
publisher directory for its name before declaring `UNVERIFIABLE`. Some homebrew JSON content
may predate or postdate the LSTs and exist only on one side; report one-sided items as
`LST-ONLY` / `JSON-ONLY` rather than as errors.

## Scope first, always

Derive the item list from the packs themselves:

```bash
source .env
find "$EXTRA_PACKS_PATH" -name '*.json' -not -path '*/test-reports/*' -not -name 'pack.json'
```

## Reading LST files

Tab-separated tag lines; `#` starts a comment; a class's numbered lines (`1`, `2`, …) are its
level-progression table. The same CLASS name may span several header lines — merge all of them.

**Classes** — `HD:n` (hit die), `MAXLEVEL:n`, `TYPE:PC.Prestige`, `STARTSKILLPTS:n`,
`CSKILL:` pipe-separated class skills (`TYPE=Craft` ⇒ the umbrella `craft` skill).
Progressions are formulas over class level, decode:

- `BONUS:COMBAT|BASEAB|CL` → `good` BAB; `(CL*3)/4` → `average`; `CL/2` → `poor`.
- `BONUS:SAVE|BASE.X|CL/2+2` → `good` save; `CL/3` → `poor`.
- Known LST error: some third-party class files (Mongoose Encyclopaedia Arcane/Divine,
  Bastion Press) used `CL/5+1+((CL+3)/5)` for their good saves. User ruling
  (2026-07-27): read it as `good`. The local LST copies were fixed the same day
  (19 occurrences → `/2+2`), but a fresh PCGen data install would reintroduce it.

Prerequisites: `PREABILITY:N,CATEGORY=FEAT,A,B` = *N of* the listed feats (N < list length is
a disjunction); `PREALIGN:LE,NE,CE`; `PRESKILL:N,Skill=ranks`; `PRESPELL:1,Spell Name` (knows
the spell); `PRESPELLTYPE:1,Arcane=3` (can cast 3rd-level arcane); `PRESPELLCAST:MEMORIZE=N`
(spontaneous caster — maps to `HasSpontaneousCasting`); `PREBAB`, `PREHD` as expected.
Proficiencies appear as level-1 `ABILITY:FEAT|AUTOMATIC|Armor Proficiency (Light)` lines —
same negative-space rules as the SRD audit: what's *absent* from the block matters.

**Feats** — first token is the name; then `CATEGORY:FEAT`, `TYPE:General.Fighter` (feat
types), `PRE*` tags as above, `MULT:YES`/`STACK:YES`, `DESC:`. `OUTPUTNAME:` is display-only.

**Spells** — `CLASSES:Sorcerer,Wizard=3|Cleric=4` is the class/level assignment,
`DOMAINS:Death=7` the domain assignment; then `SCHOOL:`, `SUBSCHOOL:`, `DESCRIPTOR:`,
`COMPS:`, `CASTTIME:`, `RANGE:`, `TARGETAREA:`, `DURATION:`, `SAVEINFO:`, `SPELLRES:`.

**Domains** — name + `DESC:` (the granted power); the domain's spell list is assembled from
`DOMAINS:` tags across the spells LSTs, so grep for `DOMAINS:.*<Name>` publisher-wide.

**Templates** — `deceit/*_templates.lst` plus companion `*_abilities.lst` for the granted
special abilities.

## Matching names to IDs

`NotOnlyFiendsStudio/PcGen/PcgIdMapper.cs` is the canonical LST-name → content-ID transform
(`DefaultIdTransform` plus the explicit override tables). Use it to pair items instead of
inventing a matching; a name that fails to map is itself a finding (the PCG importer would
miss it too).

## What to compare

Same priority as verify-content — drivers first, blast radius over item count:

1. **Classes** — `hitDie`, `maxLevel`, `babProgression`, `saveProgression`,
   `skillPointsPerLevel`, `classSkills`, prestige `prerequisites`, level-1 proficiency
   grants, which levels carry `AdvanceSpellcasting` (LST: `ADD:SPELLCASTER|ANY` lines).
2. **Feats** — prerequisites, `type`, stacking/multiple-take flags.
3. **Domains** — granted power, assembled spell list.
4. **Templates** — granted abilities against the `*_abilities.lst` companions.
5. **Spells** — class/domain level assignments (`CLASSES:`/`DOMAINS:` — highest-value field,
   it gates who can cast what and when), then school/components/range/duration/save/SR.

## False-positive traps

The two verify-content traps still apply — **umbrella skills** (`TYPE=Knowledge` in CSKILL ↔
bare `"knowledge"` in JSON is correct on both sides) and **selection-variant feat IDs**
(`HasFeat` on `x` matches any `x_*`). LST-specific additions:

- **Prose is not data.** LST `DESC:` text is abbreviated and full of typos ("Permanant").
  Never report description-wording diffs; compare structured fields only.
- **`PREABILITY` counts.** `PREABILITY:2,CATEGORY=FEAT,A,B` requires *both* only because
  N equals the list length. `PREABILITY:1,...,A,B` is an OR — don't report a missing
  conjunct.
- **Formula idioms.** `classlevel("APPLIEDAS=NONEPIC")` is just CL; `.REPLACE`/`TYPE=Base`
  suffixes are PCGen stacking plumbing, not rules content.
- **PCGen-only plumbing.** `BONUS:VAR|...Progression|CL`, `ABILITY:Special Ability|AUTOMATIC|...`,
  ability-category files, and `kits_races` files implement UI behavior; absence of a JSON
  counterpart is only a finding if the underlying *rule* (a class feature, a granted ability)
  is missing from the pack.

## Parallelising

Split by pack — each agent gets one pack's JSON file list and its LST locations from the
table above, plus the decoding rules. Scope the work yourself first. Spot-check "all clean"
reports and every `JSON-BUG` on a class driver before passing them on.

## Output format

`Item | Field | JSON value | LST value | LST quote | Verdict | Severity` — severity HIGH
(changes computed numbers or lets an illegal build through) / LOW (cosmetic). Follow with
`VERIFIED CLEAN:`, `UNVERIFIABLE:`, `JSON-ONLY:`, `LST-ONLY:` lists.

Confirmed `JSON-BUG` findings become assertions gated behind the private packs (see
`NotOnlyFiendsStudio.Tests/RequiresPrivatePacksAttributes.cs`) — not in the public
`RulesAccuracyTests`, since dev-VM runs have no private content. After any applied fix, run
the `pcg-baseline` skill: these packs exist to serve the 54 imported characters, and the
golden baseline is the regression net.
