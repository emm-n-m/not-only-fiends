---
name: verify-content
description: Verify existing content JSON against the authoritative SRD text and report mismatches. Use to audit rules accuracy (class saves/BAB/proficiencies/prerequisites, racial HD, races, feats, spells) rather than to add new content.
---

# Verify Content Against the SRD

The `extract-*` skills **add** content. This skill **audits** content that already exists,
by diffing it against the SRD mirror. Extraction often captures level tables while missing
prose sections such as prestige-class requirements, so both must be checked.

## The one rule that matters

**Ground truth is the SRD HTML mirror. Never your own knowledge of D&D.**

A model verifying 3.5e content from recall will confidently invent errors and miss real ones,
and the output is worse than useless because it *looks* authoritative. Concretely:

- Every finding must carry a **verbatim quote** from the SRD page that proves it.
- If the mirror has no authoritative text for something, output `UNVERIFIABLE`. Never guess.
- Content with **no SRD page at all** (the private non-SRD packs) is out of scope entirely.

## Verify description strings, not just fields

Extraction invents rules, and a description is where it hides — nothing else in the pipeline
reads prose, so a false sentence survives indefinitely and the sheet quietly teaches it to the
user. Known examples:

| Content | Description claimed | SRD says |
|---|---|---|
| `template:lich` Undead Traits | "Uses CHA for HP and Fort saves" | "No Constitution score" — nothing about Charisma |
| `template:undead` Undead Traits | "Uses CHA for HP, Fort saves, and CON-based abilities" | as above |

That is a **Pathfinder** rule, in two templates, from one extraction batch. Its neighbours were
all correct, so `audit-cosmetic-permabuffs` could not see it: that skill compares a description
to the permabuffs beside it, and here the permabuffs faithfully implemented a sentence that
should never have been written. Only a source diff catches this class of error.

So: read every `description` in scope against the SRD paragraph it claims to summarise, and
check both directions —

- **Invented** — a rule with no counterpart in the source. Highest severity: it is indistinguishable
  from real content to every later reader.
- **Dropped** — the same lich entry had lost immunity to mind-affecting effects, immunity to
  damage to physical ability scores, and healing from negative energy.
- **Edition drift** — a rule that is real but from another edition. Cha-to-hit-points, Cha-to-saves
  for undead, and "spell descriptor" reasoning about domains are the ones seen so far.

A description is also the only home a rule gets when the engine cannot express it. That is
legitimate — but say so in the finding, so the difference between "not encoded yet" and "not
encodable" stays visible.

## Report, never edit

Content changes silently rewrite every saved character that touches them — one save-progression
fix moved an existing character's Fortitude from +5 to +8 with no warning. So this skill
produces findings and proposed diffs; applying them is a separate, reviewed step.

## Ground truth

`NotOnlyFiendsStudio/Content/srd_html/` — 274 files, gitignored, local only.

| content | SRD page |
|---|---|
| base classes | `<classname>.html` — `fighter.html`, `barbarian.html` … |
| prestige classes | camelCase — `eldritchKnight.html`, `dwarvenDefender.html`, `mysticTheurge.html` |
| NPC classes | `npcClasses.html` (adept, aristocrat, commoner, expert, warrior — all on one page) |
| UA variants | `unearthedCoreClass.html`; it defers to the base class page for anything not listed as an exception |
| racial HD / creature types | `monsterTypes.html` — also the **Nonabilities** paragraph |
| races | `monstersAtoZ.html` and the `monsters*.html` set |
| templates | the `monsters*.html` set, section `CREATING A <NAME>` |
| spells | `spellsAtoZ.html` and the per-letter spell pages |

Strip tags before matching:

```bash
python3 -c "import re,html;t=open(P,encoding='utf-8',errors='replace').read();\
t=re.sub(r'<[^>]+>',' ',t);t=html.unescape(t);print(re.sub(r'\s+',' ',t))"
```

## Scope first, always

Verify only what ships in the public packs. Derive the list rather than assuming:

```bash
python3 -c "
import json,glob
pub=set(json.load(open('content-public.json'))['publicPackIds'])
for p in glob.glob('NotOnlyFiendsStudio/Content/packs/**/*.json',recursive=True):
    if p.split('packs/')[1].split('/')[0] in pub: print(p)"
```

Anything outside that set has no SRD ground truth. Say so and stop; do not substitute recall.

## Tiers — prioritise by blast radius, not item count

1. **Drivers** — classes and racial HD. Tabular ground truth, and an error here affects
   every character using that class. Re-audit when content or extraction logic changes.
2. **Races, feat prerequisites and feat `type`, domains.**
3. **Spells (~1,466)** — highest count, lowest per-item impact, mostly prose.

A 48-item tier done thoroughly beats a 1,500-item tier done shallowly.

## What to compare

**Classes** — `hitDie`, `skillPointsPerLevel`, `classSkills`, `maxLevel`, `babProgression`,
`saveProgression`, level-1 proficiency grants, prestige `prerequisites`, and which levels carry
`AdvanceSpellcasting`.

Deriving progressions from the level table:

- BAB: `+1` at 1st → `good`; `+3` at 4th → `average`; `+1` at 3rd → `poor`.
- Saves: a **good** save starts at `+2` at 1st level, a **poor** save at `+0`. Read the
  1st-level row — this is where the Eldritch Knight bug hid (it had the wizard's line).

**Creature types** (`monsterTypes.html`) — "8-sided Hit Dice" → `hitDie: 8`; "equal to total
Hit Dice (as fighter)" → `good`, "3/4 … (as cleric)" → `average`, "1/2 … (as wizard)" → `poor`;
"Good Will saves" → everything not listed is `poor`; "Skill points equal to (N + Int modifier)".

**Proficiencies** — level-1 `{"$type":"GrantBonusFeat","featId":…}` entries against the
"Weapon and Armor Proficiency" paragraph. Feat ids: `simple_weapon_proficiency`,
`weapon_proficiency_martial` (= *all* martial weapons, grant-only), `armor_proficiency_light`
/`_medium`/`_heavy`, `shield_proficiency`, `tower_shield_proficiency`.

Watch the negatives — they are where the errors are: a class whose SRD text enumerates
*specific* weapons (druid, monk, wizard, commoner) must **not** get a blanket weapon
proficiency; "except tower shields" means no `tower_shield_proficiency`; "but not with
shields" (rogue, expert) means no `shield_proficiency` at all.

## Before reporting a finding

Check these recurring false-positive traps before reporting a finding:

- **Umbrella skills.** `craft`, `knowledge`, `perform`, `profession` are not skill IDs but
  umbrellas resolved via `parentSkill`. `"knowledge"` in a `classSkills` list is the correct
  idiom, not a broken reference — the engine expands it to all ten `knowledge_*` skills.
- **Selection-variant feat IDs.** `HasFeat` matches `featId` exactly *or* any `featId_*`
  variant. So `skill_focus_knowledge` matches all ten `skill_focus_knowledge_*` selections and
  nothing else — a disjunction over ten skills, expressible with one entry. Check the ID
  hierarchy before declaring a requirement unexpressible.

## Available prerequisite types

`MinBAB` · `MinAbility` · `MinSkillRanks` (whole ranks; engine doubles) · `MinSkillRanksAcross`
(N of a set) · `MinClassLevel` · `HasFeat` · `HasFeatOfType` · `HasFeatOfAnyType` (combined
count across types) · `HasFeatWithTag` · `HasFeatSelections` · `AlignmentReq` · `MinHD` ·
`MinCasterLevel` · `CanCastSpellLevel` · `HasSpellcasting` · `HasSpontaneousCasting` ·
`HasRace` · `MinSave` · `HasAbility`

If an SRD requirement cannot be expressed, say so explicitly in the report rather than
silently dropping it or approximating. Record public content gaps in `CONTENT_GAPS.md` and
engine limitations in `KNOWN_ISSUES.md`.

## Parallelising

Split by content group (base classes / NPC+UA / prestige A–D / prestige E–T / racial HD) and
give each agent its exact file list and page mapping — scope the work yourself first rather
than letting agents rediscover it. Then **spot-check the results**: re-verify the highest-impact
claims and any "all clean" report against the source before passing them on.

## Output format

A markdown table — `Item | Field | JSON value | SRD value | SRD quote | Severity` — where
severity is HIGH (changes computed numbers, or lets an illegal build through) or LOW (cosmetic).
Follow it with `VERIFIED CLEAN: <items with zero findings>` and
`UNVERIFIABLE: <item:field pairs that could not be checked>`.

Findings become assertions in
[NotOnlyFiendsStudio.Tests/RulesAccuracyTests.cs](../../../NotOnlyFiendsStudio.Tests/RulesAccuracyTests.cs),
each carrying its SRD quote as a comment, so a fix is a permanent regression rather than a
document that goes stale.
