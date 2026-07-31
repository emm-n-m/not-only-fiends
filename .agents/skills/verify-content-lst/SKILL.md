---
name: verify-content-lst
description: Audit private-pack content JSON against configured PCGen LST sources and report quoted, classified mismatches without editing content. Use for third-party or homebrew packs that have no authoritative SRD page.
---

# Verify Private Content Against PCGen LSTs

Audit definitions under `EXTRA_PACKS_PATH` against structured LST files under
`PCGEN_DATA_PATH`. Use the LST source—not rules knowledge from memory—as evidence.

## Non-negotiable rules

- Include a verbatim LST fragment for every finding.
- Use `UNVERIFIABLE` when no matching LST source exists.
- Report findings and proposed diffs only; do not edit content without a separate request.
- Treat community LSTs as fallible witnesses:
  - `JSON-BUG`: coherent structured LST data plainly differs from JSON;
  - `LST-SUSPECT`: the LST is internally inconsistent or uses a known faulty idiom;
  - `UNRESOLVABLE`: neither side can be established as correct.
- Treat the user's `deceit/` transcription as ground truth of intent unless the user says
  otherwise.

## Preconditions and scope

1. Read `.env` and verify `EXTRA_PACKS_PATH` and `PCGEN_DATA_PATH` exist.
2. Derive the requested item list from private pack JSON, excluding `pack.json` and
   `test-reports/`.
3. Locate sources using this starting map, then search the publisher directory before
   declaring an item unverifiable:

| Pack | LST source |
|---|---|
| `12_to_midnight` | `12_to_midnight/curses/curses_classes.lst` |
| `12_to_midnight_curses` | `12_to_midnight/curses/curses_{feats,spells}.lst` |
| `aeg_evil` | `alderac_entertainment_group/evil/` plus `gods/aeg_gods_spells.lst` |
| `aeg_feats` | `alderac_entertainment_group/feats/35e/` |
| `en_elements_of_magic` | `en_publishing/elements_of_magic_revised/` |
| `mongoose_enchantment` | `mongoose_publishing/encyclopaedia_arcane/enchantment/` |
| `mongoose_publishing` | the same publisher tree, including `enchantment_35e/` |
| `necromancer_eldritch_sorcery` | `necromancer_games/eldritch_sorcery/` |
| `sword_and_sorcery` | `sword_and_sorcery_studios/scarred_lands/relics_and_rituals/` |
| `deceit_homebrew` | `deceit/` and companion ability files |

`fiendish_codex_1` and `fiendish_codex_2` have no LST ground truth. Audit them against owned
PDFs under `SOURCE_PDFS_PATH` with page-number quotes.

## Decode LST data

- Merge continuation/header lines belonging to the same class.
- Classes: compare `HD`, `MAXLEVEL`, `STARTSKILLPTS`, `CSKILL`, BAB/save formulas,
  prerequisites, proficiency abilities, and `ADD:SPELLCASTER`.
- Feats: compare `TYPE`, `PRE*`, `MULT`, and `STACK`; ignore display-only `OUTPUTNAME`.
- Spells: compare `CLASSES`, `DOMAINS`, school, subschool, descriptors, components, casting
  time, range, target/area, duration, save, and spell resistance.
- Domains: assemble domain spell lists from publisher-wide `DOMAINS:` tags.
- Templates: pair template files with their companion ability definitions.

Progression idioms:

- `BONUS:COMBAT|BASEAB|CL` → good BAB;
- `(CL*3)/4` → average BAB;
- `CL/2` → poor BAB;
- `BONUS:SAVE|BASE.X|CL/2+2` → good save;
- `CL/3` → poor save.

Some third-party sources use `CL/5+1+((CL+3)/5)` for a good save. Treat this known idiom as
`LST-SUSPECT`/good progression rather than blindly importing it.

## Prerequisite semantics

- `PREABILITY:N,CATEGORY=FEAT,A,B` means N of the listed feats; it is not always an AND.
- `PRESKILL` ranks are whole ranks.
- `PRESPELLTYPE` is spell-level capability.
- `PRESPELLCAST:MEMORIZE=N` maps to spontaneous casting.
- Use `NotOnlyFiendsStudio/PcGen/PcgIdMapper.cs` for name-to-ID matching. A failure to map is a
  finding; do not invent a parallel normalization rule.

## Character corpus as a second witness

Existing `.pcg` builds can corroborate prerequisites because PCGen enforced them when the
characters were built. After a proposed prerequisite correction, identify affected characters
and check whether they satisfy it. A conflict may indicate an LST error, an import error, or a
historical house-rule override; report the ambiguity.

## False-positive guards

- Parent skill umbrellas are valid.
- Selectable base-feat IDs can match their suffixed selections.
- Compare structured LST fields, not typo-prone `DESC` wording.
- Ignore PCGen UI/plumbing tags unless the underlying game rule is missing.
- Report one-sided definitions as `JSON-ONLY` or `LST-ONLY`, not automatically as bugs.

## Output

Return:

`Item | Field | JSON value | LST value | LST quote | Verdict | Severity`

Then list `VERIFIED CLEAN`, `UNVERIFIABLE`, `JSON-ONLY`, and `LST-ONLY`. Use `HIGH` for
computed-value or build-legality effects and `LOW` for cosmetic metadata.

If the user later authorizes fixes, gate regression assertions behind private-pack attributes.
After any applied fix, invoke `pcg-baseline`.

## References

- `.env`
- `NotOnlyFiendsStudio/PcGen/PcgIdMapper.cs`
- `NotOnlyFiendsStudio.Tests/RequiresPrivatePacksAttributes.cs`
- `TODO.md`
