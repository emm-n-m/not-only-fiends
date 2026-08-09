# Audit: mechanics that exist only in a description string

You are auditing a D&D 3.5e character engine (C#/.NET, repo root = cwd). Content lives in JSON
files that are deserialized into `Permabuff` objects. Your job is to find content whose rules are
written **only in a human-readable description** and never encoded as a permabuff, so the engine
silently does nothing.

This is a **read-and-report** task. Do not edit any file. Do not run `dotnet test`. Produce one
report at the end.

---

## Background you need

`GrantAbility` is inert. It appends `{ id, name, description }` to a list that the character sheet
prints. It changes no number. So this:

```json
{ "$type": "GrantAbility",
  "ability": { "id": "dark_blessing", "name": "Dark Blessing",
               "description": "Add Cha bonus to all saving throws." } }
```

...means the engine adds **nothing** to any saving throw. That was a real bug: one character's
saves were 16 points low. Three more of the same kind were found in one afternoon, including a
ring described as granting immunity to five energy types whose `grantedPermabuffs` array was empty.

Your question for every candidate is exactly one thing:

> **Does the description claim a permanent mechanical effect that no permabuff beside it delivers?**

You are **not** checking the description against the D&D rules. You are not checking it against any
book. You only compare the description to the JSON sitting next to it in the same file.

---

## Step 1 — collect candidates

Run this exactly. It prints one tab-separated candidate per line.

```bash
python3 - <<'PY'
import json,glob,os,re
# EXTRA_PACKS_PATH lives in .env, not the environment — every tool in this repo reads it there.
env=dict(l.strip().split('=',1) for l in open('.env') if '=' in l and not l.startswith('#'))
ROOTS=[('public','NotOnlyFiendsStudio/Content')]
if env.get('EXTRA_PACKS_PATH'): ROOTS.append(('private',env['EXTRA_PACKS_PATH']))
mech=re.compile(r'(immun|resist|damage reduction|bonus (on|to)|\+\d|spell resistance|'
                r'fast healing|regenerat|natural armor|speed|darkvision)',re.I)
for tag,root in ROOTS:
    for f in sorted(glob.glob(root+'/**/*.json',recursive=True)):
        try: d=json.load(open(f,encoding='utf-8-sig'))
        except Exception: continue
        stack=[d]
        while stack:
            o=stack.pop()
            if isinstance(o,dict):
                if o.get('$type')=='GrantAbility':
                    a=o.get('ability') or {}
                    if mech.search(a.get('description') or ''):
                        print(f"{tag}\t{os.path.relpath(f,root)}\t{a.get('id')}\t{a.get('description')}")
                stack.extend(o.values())
            elif isinstance(o,list): stack.extend(o)
PY
```

Expect 310 lines: 251 tagged `public`, 59 tagged `private`. If you get 251, `.env` was not read
and you are missing the private packs — stop and fix that before continuing.

Then run this second sweep, for anything that describes a benefit but grants nothing:

```bash
python3 - <<'PY'
import json,glob,os,re
mech=re.compile(r'(immun|resist|damage reduction|bonus (on|to)|\+\d|spell resistance)',re.I)
for f in sorted(glob.glob('NotOnlyFiendsStudio/Content/**/*.json',recursive=True)):
    try: d=json.load(open(f,encoding='utf-8-sig'))
    except Exception: continue
    for item in (d if isinstance(d,list) else [d]):
        if not isinstance(item,dict): continue
        if 'grantedPermabuffs' in item and not item['grantedPermabuffs']:
            if mech.search(item.get('description') or ''):
                print(f"{os.path.basename(f)}\t{item.get('id')}\t{(item.get('description') or '')[:160]}")
PY
```

Finally, a third sweep. The two above only see things that *say* something; content with an
empty `grantedPermabuffs` and **no description at all** is invisible to both, and 284 objects are
in that state — mostly epic feats. `feat:epic_leadership` is the worked example: it grants nothing
and says nothing, yet the SRD gives it "Multiply the number of followers of each level that the
character can lead by 10."

```bash
python3 - <<'PY'
import json,glob,os
env=dict(l.strip().split('=',1) for l in open('.env') if '=' in l and not l.startswith('#'))
for tag,root in [('public','NotOnlyFiendsStudio/Content'),('private',env.get('EXTRA_PACKS_PATH'))]:
    if not root: continue
    for f in sorted(glob.glob(root+'/**/*.json',recursive=True)):
        try: d=json.load(open(f,encoding='utf-8-sig'))
        except Exception: continue
        stack=[d]
        while stack:
            o=stack.pop()
            if isinstance(o,dict):
                if ('grantedPermabuffs' in o and not o['grantedPermabuffs']
                        and not (o.get('description') or '').strip() and o.get('id')):
                    print(f"{tag}\t{os.path.relpath(f,root)}\t{o['id']}\t{o.get('name','')}")
                stack.extend(o.values())
            elif isinstance(o,list): stack.extend(o)
PY
```

These have no description to compare against, so "compare only with adjacent JSON" cannot decide
them. Do **not** guess from the name. List them under a separate **NO-DESCRIPTION** heading,
grouped by file, with a count — they are a work-list for a human or for `verify-content`, which
does have an authoritative source. Nothing else in this audit changes.

## Step 2 — process candidates in batches of 20

For each candidate, open the file and **read the entire permabuff array that contains it** — the
whole `creationPermabuffs`, `levelPermabuffs["N"]`, or `grantedPermabuffs` list. Never judge a
`GrantAbility` in isolation. The single most common false positive is a description whose mechanic
*is* encoded, ten entries further down the same array.

Then assign exactly one verdict.

### Verdict: BY-DESIGN — skip it, one line in the report

Any of these four:

1. **Already encoded nearby.** A `GrantImmunity`, `ModifyAttribute`, `GrantDR`, `GrantTypedBonus`
   etc. in the same array already delivers what the description says.
2. **Not permanent.** The effect is activated or timed: "once per day", "for 1 minute", "as a
   standard action", "while raging", "when hosting an event". Permabuffs are permanent and
   irreversible, so an activated ability correctly has none.
3. **Not this character.** It affects other creatures: "foes within 60 ft.", "enemies must save",
   "targets are shaken".
4. **Not modellable.** It needs a subsystem the engine has no representation for — initiative,
   action economy, conditional or situational AC, attacks of opportunity, combat maneuvers.
   (Report those as ENGINE-GAP instead if you are confident; when unsure, BY-DESIGN.)

### Verdict: CONTENT-BUG — this is the valuable output

The description states a **permanent, static, self-affecting** mechanic, and the table below has a
permabuff for it, and it is absent from the array. Write the exact JSON to insert.

| Description says | Insert |
|---|---|
| "immune to X" | `{ "$type": "GrantImmunity", "immunity": "fire" }` — one per type. Vocabulary already in use: `fire cold acid electricity sonic poison sleep disease paralysis petrification phantasm energy drain critical hits fear charm compulsion` |
| "resist X N" | `{ "$type": "ModifyAttribute", "target": "resistance", "resistanceElement": "acid", "value": 10 }` |
| "DR N/X" | `{ "$type": "GrantDR", "value": 15, "bypassedBy": "good and epic and silver" }` |
| "SR N" | `{ "$type": "ModifyAttribute", "target": "spellResistance", "value": 20 }` |
| "+N natural armor" | `{ "$type": "ModifyAttribute", "target": "naturalArmor", "value": 9 }` |
| "+N to \<ability score\>" | `{ "$type": "ModifyAttribute", "target": "abilityScore", "abilityScore": "CHA", "value": 4 }` |
| "+N on all saves" (a fixed number) | `{ "$type": "ModifyAttribute", "target": "allSaves", "value": 2 }` |
| "+N on a skill" | `{ "$type": "GrantSkillBonus", ... }` — read `GrantSkillBonus` in `Permabuff.cs` for the field names |
| a speed or flight | `{ "$type": "GrantMovement", ... }` |
| a language | `{ "$type": "GrantLanguage", ... }` |
| a bonus feat | `{ "$type": "GrantBonusFeat", "featId": "feat:leadership" }` |
| "add your \<ability\> modifier to all saves" | `{ "$type": "GrantAbilityModifierToSaves", "sourceId": "<ability id>", "name": "<display name>", "ability": "CHA" }` |

**Never invent a `$type`.** If what you need is not in that table, open
`NotOnlyFiendsStudio/Models/Permabuff.cs` and read the `[JsonDerivedType]` list at the top — that
is the complete set. If it is still not there, the verdict is ENGINE-GAP.

### Verdict: ENGINE-GAP — report, propose nothing

The description is a permanent self-affecting mechanic, but no permabuff can express it.

**Name the specific missing capability, in the form "needs X on Y".** Required level of detail:

- "needs a multiplier on `CharacterState.FollowerCounts`; `ModifyLeadershipScore` changes the
  score, not the counts it produces"
- "needs a conditional bonus applying only in shadowy illumination; the engine has no situational
  modifiers at all"

A generic rationale is a failed finding. **Do not reuse the same sentence across entries** — if
more than three of your ENGINE-GAP entries share a rationale, you have not looked at them
individually and should go back and do so. Naming what is missing is the entire value of this
verdict; without it the reader has to redo the analysis.

Before filing one, **check the capability is really absent**: search the `[JsonDerivedType]` list
in `Permabuff.cs` *and* grep `CharacterState.cs` for a field that already models it. Wizard
specialisation looks like a gap and is not — `SpecialtyBonusSlots` and `WizardSchools` already
implement it, so it is BY-DESIGN. Anything already implemented elsewhere is BY-DESIGN.

One specific case to watch for. `GrantTypedBonus` evaluates its formula **once**, at the moment it
is applied. So a bonus keyed to an ability score that keeps changing (level-up increases, tomes,
worn items land later) **cannot** be a `GrantTypedBonus`. It needs a rule the engine re-evaluates
against the finished character. Only one such rule exists today:
`GrantAbilityModifierToSaves`. If you find "add your Strength modifier to your AC" or similar for
any target other than saves, that is ENGINE-GAP — say which target needs the treatment.

## Step 3 — write the report

Write to `test-reports/cosmetic_permabuff_audit_<YYYY-MM-DD>.md` (create the directory if needed).
That directory is gitignored, which is deliberate: the report will quote third-party content and
must not be committed to this repository.

Group by verdict, then by pack. Use this shape per finding:

```markdown
### CONTENT-BUG — `template:archfiend_lawful` / `archfiend_devil_traits`
- **File:** `deceit_homebrew/templates/deceit.json` (private pack)
- **Description:** "Immune to fire and poison. Resist acid 10, cold 10."
- **Present:** `ModifyAttribute resistance acid 10`, `ModifyAttribute resistance cold 10`, `GrantDR 15/good and epic and silver`
- **Missing:** immunity to fire, immunity to poison
- **Insert into `creationPermabuffs`:**
  ```json
  { "$type": "GrantImmunity", "immunity": "fire" },
  { "$type": "GrantImmunity", "immunity": "poison" }
  ```
```

End the report with a count table: CONTENT-BUG / ENGINE-GAP / BY-DESIGN / NO-DESCRIPTION, split
public vs private.

## Rules for the report

- Quote every description **verbatim**. Never paraphrase — a paraphrase is unreviewable.
- Keep public and private pack findings in separate sections. Content from the private packs is
  third-party material; do not copy its text into any file in the main repository.
- If you are unsure, say so and use BY-DESIGN. A false CONTENT-BUG costs a reviewer more than a
  missed one, because applying a wrong fix silently changes every saved character using that
  content.
- Do not fix anything. Do not run the test suite. Do not modify any JSON.
