---
name: audit-cosmetic-permabuffs
description: Find content whose mechanics are stated only in a description string and never encoded as a permabuff, so the engine treats them as flavour text. Use to audit whether granted abilities, template traits and magic items actually do anything.
argument-hint: [public|private|content-ids...]
---

# Audit Cosmetic-Only Permabuffs

`GrantAbility` appends a `{ id, name, description }` record to `state.Abilities` and **nothing
else**. The description is display text. Any rule that exists only in that string is invisible to
the engine: it will not change a save, a resistance, an immunity or a score, and the character
sheet will quietly print the wrong number forever.

This skill finds those. It is the counterpart to `verify-content`, which asks *"does the JSON
match the source?"*; this one asks *"does the JSON **do** what its own description says?"* — a
question answerable without any external source, because the description and the permabuffs sit
side by side in the same file.

There is a Codex-targeted version of this procedure at
`.codex/prompts/audit-cosmetic-permabuffs.md`. Keep the two in step when either changes.

## Why this exists

Found on 2026-08-09, by a player noticing wrong numbers on one character sheet:

| Content | Description said | Engine did |
|---|---|---|
| `class:blackguard` L2 Dark Blessing | "Add Cha bonus to all saving throws." | nothing — saves were 16 low |
| `class:paladin` L2 Divine Grace (+3 UA variants) | "Add Cha bonus to all saving throws." | nothing |
| `template:archfiend_lawful` Devil Traits | "Immune to fire and poison. Resist acid 10, cold 10." | resistances yes, immunities no |
| `ring:universal_energy_immunity` | "takes no damage from energy of any of these types" | `grantedPermabuffs: []` |

The Archfiend case is the tell: the acid/cold `ModifyAttribute` **and** the `GrantDR` beside it
were both encoded correctly, and only the two immunities were dropped. These are transcription
omissions inside otherwise-good content, so they cannot be found by spot-checking whatever looks
neglected.

## The one rule that matters

**A finding is a disagreement between a description and the permabuffs beside it. Nothing else.**

Do not rule on whether the description matches the SRD, the PDF, or your recollection of 3.5e —
that is `verify-content` / `verify-content-lst`, and mixing the two produces a report nobody can
act on. If a description looks wrong against the rules, that is out of scope: note it in one line
and move on.

## Report, never edit

Content changes silently rewrite every saved character that touches them. The Dark Blessing fix
moved three characters' saves and cleared warnings on twenty — all correct, all invisible until
the regression baseline diff was read. So: produce findings and proposed diffs. Applying them is a
separate, reviewed step.

## Scope

```bash
NotOnlyFiendsStudio/Content/**/*.json      # public (OGL) — findings and fixes belong in this repo
$EXTRA_PACKS_PATH/**/*.json                # private packs — findings belong in the materials repo
```

As of 2026-08-09 the sweep below returns **310 candidates** — 251 public across 46 files, 59
private across 15 files, out of 1,031 `GrantAbility` instances in total. Most are legitimate (see the traps). Take one pack,
or one content type, at a time.

## Finding candidates

```bash
python3 - <<'PY'
import json,glob,os,re
# For the private packs, read EXTRA_PACKS_PATH out of .env — it is not an environment variable.
root='NotOnlyFiendsStudio/Content'
mech=re.compile(r'(immun|resist|damage reduction|bonus (on|to)|\+\d|spell resistance|'
                r'fast healing|regenerat|natural armor|speed|darkvision)',re.I)
for f in glob.glob(root+'/**/*.json',recursive=True):
    try: d=json.load(open(f,encoding='utf-8-sig'))
    except Exception: continue
    stack=[d]
    while stack:
        o=stack.pop()
        if isinstance(o,dict):
            if o.get('$type')=='GrantAbility':
                a=o.get('ability') or {}
                if mech.search(a.get('description') or ''):
                    print(f"{os.path.relpath(f,root)}\t{a.get('id')}\t{a.get('description')}")
            stack.extend(o.values())
        elif isinstance(o,list): stack.extend(o)
PY
```

Also sweep three places `GrantAbility` never reaches:

- **Anything with `grantedPermabuffs: []` and a description that grants something.** This is how
  the Ring of Universal Energy Immunity was inert.
- **`GrantSLA` / `GrantSpecialAttack` descriptions carrying a static rider.** "…gains fire
  resistance 10 while active" is fine; "…the wearer is immune to fire" is not.
- **`grantedPermabuffs: []` with _no description at all_** — 284 objects, mostly epic feats, and
  invisible to every description-based sweep. `feat:epic_leadership` is the case that exposed it:
  it grants nothing, says nothing, and the SRD gives it "Multiply the number of followers of each
  level that the character can lead by 10." These cannot be judged by this skill's one rule (there
  is no description to compare against), so list them under **NO-DESCRIPTION** as a work-list for
  `verify-content`, which has an authoritative source. Never guess the mechanic from the name.

## What the engine can actually encode

Never invent a `$type`. The full set is the `[JsonDerivedType]` list at the top of
`NotOnlyFiendsStudio/Models/Permabuff.cs`; these are the ones a static trait usually needs:

| Description says | Encode as |
|---|---|
| "immune to X" | `GrantImmunity` — `{"immunity":"fire"}`. Free-form string; existing vocabulary is `fire cold acid electricity sonic poison sleep disease paralysis petrification phantasm energy drain critical hits`, plus `fear charm compulsion` added 2026-08-09 |
| "resist X N" | `ModifyAttribute` — `target:"resistance"`, `resistanceElement`, `value` |
| "DR N/X" | `GrantDR` — `{value, bypassedBy}`. Same `bypassedBy` does not stack; higher wins |
| "SR N" | `ModifyAttribute` — `target:"spellResistance"` |
| "+N natural armor" | `ModifyAttribute` — `target:"naturalArmor"` |
| "+N to an ability score" | `ModifyAttribute` — `target:"abilityScore"`, `abilityScore:"CHA"` |
| "+N on all saves" (fixed number) | `ModifyAttribute` — `target:"allSaves"` |
| "+N to X" where N is a formula | `GrantTypedBonus` — `target` ∈ `AC Attack Damage SaveFort SaveRef SaveWill AllSaves AbilityStr…AbilityCha SkillRanks SR NaturalArmor`, plus `bonusType` and a Formula `value` |
| "add your \<ability\> modifier to all saves" | `GrantAbilityModifierToSaves` — `{sourceId, name, ability, positiveOnly}` |
| "+N on a skill" | `GrantSkillBonus` |
| speed / flight | `GrantMovement` |
| a language | `GrantLanguage` |
| a bonus feat | `GrantBonusFeat` — cascades that feat's own `GrantedPermabuffs` |
| an at-will / N-per-day effect | `GrantSLA`, `GrantSpecialAttack` or `ModifyCounter` — **not** a static bonus |

### Static value vs derived value — the trap that produced the original bug

`GrantTypedBonus` evaluates its formula **once, at the tick that applies it**. Correct for "+4
natural armor"; wrong for anything keyed to a score that keeps moving. Dark Blessing could not be
`GrantTypedBonus(AllSaves, "Mod(CHA)")` — the character's Charisma reached 43 only after the
post-tick equipment pass, so a value banked at blackguard level 2 would have been silently low.

If the description says *"your \<ability\> modifier"*, it needs a **rule the engine re-evaluates
against final state**, not a number. `GrantAbilityModifierToSaves` is the existing example of that
shape. Anything needing the same treatment for a different target is an ENGINE-GAP, not a content
fix.

## False positives — check all four before writing a finding

1. **Already encoded next door.** Read the whole `creationPermabuffs` / `levelPermabuffs` array,
   not the `GrantAbility` in isolation. The Archfiend's "DR 15/good and epic and silver" has a
   `GrantDR` eight entries further down.
2. **Not permanent.** Permabuffs are permanent and irreversible. "Once per day, gain resist fire 30
   for 1 minute" is activated, correctly has no permabuff, and is not a finding.
3. **Not this character.** "Foes within 60 ft. must save or become shaken" modifies other
   creatures; the engine models one character's permanent state.
4. **Deliberately display-only.** If encoding it would need a subsystem that does not exist
   (initiative, action economy, conditional AC), that is ENGINE-GAP, not CONTENT-BUG.

## Verdicts

Every finding carries one, mirroring `verify-content-lst`:

- **CONTENT-BUG** — the engine has a permabuff for this and the content omits it. Ship a proposed
  diff. This is the useful output; all four fixes above were this.
- **ENGINE-GAP** — the description is correct but nothing can express it. Name the *specific*
  missing capability, as "needs X on Y": *"needs a multiplier on `CharacterState.FollowerCounts`;
  `ModifyLeadershipScore` changes the score, not the counts it produces."* A generic rationale is
  a failed finding, and a rationale repeated across entries means they were not looked at
  individually — the 2026-08-09 run filed all 150 with one boilerplate sentence and the section
  was unusable. Check the capability is genuinely absent first (the `[JsonDerivedType]` list *and*
  `CharacterState`'s fields): wizard specialisation reads like a gap but `SpecialtyBonusSlots`
  already implements it, so it is BY-DESIGN. Never invent a `$type`.
- **BY-DESIGN** — trips a trap above. One line, no diff. Include these, so a reviewer knows they
  were considered rather than missed.

## Output

`test-reports/cosmetic_permabuff_audit_<YYYY-MM-DD>.md` (gitignored — the report quotes
third-party content), grouped by verdict then pack. Every finding carries the file path, the ability id, the description **verbatim**, the
permabuffs actually present, and for CONTENT-BUG the exact JSON to insert.

Keep the repos separate in the report; private-pack findings must not quote non-OGC text into
anything destined for this repository. See `CONTENT_POLICY.md`.

## Verifying a fix

1. `dotnet build` — a bad `$type` fails at load, and `ContentRegistry.Validate()` catches dangling
   references.
2. `dotnet test` — expect `PcgImportRegression` to fail. That is the harness working.
3. Read `{EXTRA_PACKS_PATH}/test-reports/pcg_import_report.diff.md`. Every changed value must be
   explainable by the fix. When Dark Blessing landed, exactly three characters moved and each
   delta equalled that character's Charisma modifier — that arithmetic is the acceptance test.
4. Only then `UPDATE_PCG_BASELINE=1 dotnet test --filter "FullyQualifiedName~PcgImportRegression"`.
5. Add a rule assertion for anything fixed — see the Divine Grace / Dark Blessing tests in
   `NotOnlyFiendsStudio.Tests/RulesAccuracyTests.cs`, and the assertion discipline in `AGENTS.md`:
   never write the expected value by reading it off the new output.
