# ENGINE_UI_TASKS — run report

Execution of [ENGINE_UI_TASKS.md](ENGINE_UI_TASKS.md) (the executable form of TODO §8 and §1).
Branch: `engine-ui-tasks`, one commit per task, never pushed.

Assume the reader has none of the context of the run.

---

## Environment as detected

Recorded before starting, because every count below depends on it.

| resource | state | consequence |
|---|---|---|
| `EXTRA_PACKS_PATH` → `/home/imp/source/not-only-fiends-deceit` | **present**, at `2e1329e` (the commit the brief requires), working tree clean | the `[RequiresPrivatePacks*]` methods **run** |
| `PCGEN_CHARACTERS_PATH` | **absent** (commented out in `.env`) | `PcgImportRegression` + `PcgReconstructionTests` **skip** — no golden-baseline safety net for this run |
| `NotOnlyFiendsStudio/Content/srd_html/` (SRD mirror) | **absent** | **Task 5b skipped entirely** — see Task 5 |
| `PCGEN_DATA_PATH` / `SOURCE_PDFS_PATH` | absent | not needed by any task here |

`UPDATE_PCG_BASELINE=1` was never run and nothing under `test-reports/` was touched.

Before starting, the golden import baseline
(`not-only-fiends-deceit/test-reports/pcg_import_report.json`) was inspected to see whether any of
this work could move it. It records per character: `hp`, `bab`, `saves`, `skillRanks`, `feats`,
`classLevels`, `casterLevels`, and the dropped/warning lists. It records **no** skill totals, no
languages, no capabilities and no domain spells — so none of the five tasks can shift it even on a
machine that has the corpus.

## Step 0 — baseline

```
dotnet build   →  0 Error(s), 4 Warning(s)
dotnet test    →  Failed: 0, Passed: 495, Skipped: 11, Total: 506
```

These are the only numbers anything below is compared against.

---

## Task status

| task | status | tests after | commit |
|---|---|---|---|
| 1 — skill totals, synergies, skill bonuses | **done** | 509 passed / 11 skipped / 0 failed | |
| 2 — dangling domain spell refs + integrity guard | **done** | 512 passed / 11 skipped / 0 failed | |
| 3 — builder offers non-playable races | not started | | |
| 4 — surface write-only state on the sheet | not started | | |
| 5 — languages | not started | | |

---

## Task 1 — skill totals, synergies, skill bonuses — DONE

### What was wrong

No skill total existed anywhere in the solution. The sheet rendered a two-column table of skill
name and rank count, so the number a player actually rolls could not be stated. Two pieces of
already-modelled data hung off that absence: `SkillDefinition.Synergies` (9 skills, 12 entries) had
no reader anywhere, and `CharacterState.SkillBonuses` was write-only — every racial and class skill
bonus in every pack affected nothing observable.

### What changed

- **`Studio/ReplayEngine.cs`** — new `FinalizeSkills` tail pass, called as step 8 of `Evaluate`,
  after the equipment pass and the two existing tail passes. Computes synergies then totals.
- **`Models/CharacterState.cs`** — `SkillSynergyBonuses` and `SkillTotals`.
- **`Models/Character.cs`** — both mirrored onto `CharacterSheet`, which is also what the REST API
  returns (`AgentApiService` builds every response from `CharacterSheet.FromState`), so the API
  gained skill totals for free.
- **`Components/Pages/SheetView.razor`** — the skill table is now **Skill | Ranks | Ability | Misc |
  Total**, keeping the `(class)` marker. The Misc cell carries a tooltip splitting granted bonuses
  from synergy bonuses.
- **`SkillTotalTests.cs`** — 14 tests.

Total per skill = whole ranks + key-ability modifier + `SkillBonuses` + `SkillSynergyBonuses`.

### Judgment calls

- **Synergy results are kept in a separate dictionary, not merged into `SkillBonuses`.** Merging
  would have been less code but destroys provenance: `SkillBonuses` means "what content granted",
  and an existing private-pack assertion (`Hellbred_InfernalMienIsStructured`) reads it that way. A
  Diplomacy synergy landing in it would have silently broken that meaning. The sheet re-adds the
  two for the Misc column, and the tooltip shows the split.
- **The pass runs last, after equipment.** Equipment can move ability scores (`FinalizeEquipment`
  applies `BonusTarget.AbilityStr…Cha` via `AddAbility`), so computing totals any earlier would use
  pre-equipment modifiers. This is a stronger ordering requirement than the brief called out.
- **The pass clears its two dictionaries before writing.** The sheet's HD slider re-evaluates the
  same `Character` repeatedly; a pass that accumulated would inflate on every look. Covered by
  `Totals_AreRecomputedNotAccumulated_AcrossRepeatedEvaluation`.
- **Totals are emitted only for skills with ranks, a granted bonus, or a synergy** — not for all 74
  skills. This matches what the sheet lists today and keeps the saved-character payload from
  growing by ~70 entries per character. The cost is that a 0-rank untrained skill shows no line;
  since untrained-use rules are explicitly out of scope, adding those lines would have meant
  printing numbers without saying whether the skill is even usable untrained.
- **Whole ranks truncate** (`SkillHalfRanks / 2`, integer division). 2.5 cross-class ranks give +2,
  which is the 3.5 rule. Asserted by `HalfRanks_AreTruncatedNotRounded`.
- **`keyAbility` is parsed case-insensitively** — content stores `"int"`, `"dex"` etc. A skill whose
  key ability fails to parse, or that is not in the registry, contributes +0 rather than throwing;
  no such skill exists in any loaded pack today.

### Out of scope, as instructed — not attempted

Armor check penalty, untrained-use restrictions, and size/situational modifiers. The sheet
therefore states a total that is correct for an unarmoured, non-situational check. Armor check
penalty is the most visible of the three: `SkillDefinition.ArmorCheckPenalty` is already populated
per skill and `ArmorProfile` already carries the penalty value, so the data for it exists — it is a
follow-up, not a research problem.

---

## Task 2 — dangling domain spell references + integrity guard — DONE

### Count correction

The brief says "**11** domain bonus-spell slots across **11** public-pack domains". It is 11
**domains** but **14 slots** — the table in TODO §1 collapses `air`/`earth`/`fire`/`water` into one
row and `chaos`/`evil`/`good`/`law` into another, so eight slots appear as two rows. All 14 were
fixed; no other reading of the table changes which ids are wrong.

### What changed

- **`packs/srd_core/domains/srd.json`** — 14 bonus-spell ids repointed, exactly as TODO §1
  specifies: `bull_s_strength`→`bulls_strength`; `bigby_s_{grasping_hand,clenched_fist,
  crushing_hand}`→ the un-prefixed forms; `mordenkainen_s_disjunction`→`mages_disjunction`;
  `greater_teleport`→`teleport_greater`; the four `elemental_swarm_<element>`→`elemental_swarm`;
  the four `summon_monster_ix_<alignment>`→`summon_monster_ix`.
- **`packs/srd_core/spells/elemental_swarm.json`** — added `domain:air/earth/fire/water: 9`.
- **`packs/srd_core/spells/summon_monster_ix.json`** — added `domain:chaos/evil/good/law: 9`.
- **`Studio/ContentRegistry.cs`** — `Validate()` now checks every domain bonus spell resolves, so
  this class of breakage fails at load rather than being invisible.
- **`ContentIntegrityTests.cs`** (new) — the general cross-reference sweep.
- **`PrivatePackRulesAccuracyTests.cs`** — the seven-domain
  `FiendishCodexDomains_ReferenceOnlyRealSpells` is gone, replaced by the ungated
  `ContentIntegrityTests.EveryDomainBonusSpell_Resolves` over every domain in every loaded pack.

Guard verified negatively: repointing `domain:air[9]` back at `spell:elemental_swarm_air` failed
three tests (`SRDContent_PassesValidation`, `EveryReference_Resolves_OrIsAKnownGap`,
`EveryDomainBonusSpell_Resolves`), each naming the domain, level and id. Reverted.

### The sweep found one more real bug — fixed

`class:dragon_disciple` carried `LacksTemplate { templateId: "half_dragon" }`, **unprefixed**, while
every template in the registry is `template:<id>`. The SRD's "cannot already be a half-dragon"
restriction would therefore not have fired even once a half-dragon template existed. Corrected to
`template:half_dragon`; behaviour today is unchanged because neither id resolves (no half-dragon
template has ever been extracted — that remains a content gap, recorded in `KnownGaps`).
`RulesAccuracyTests.DragonDisciple_RequiresDraconicAndExcludesHalfDragon` pinned the old id and was
updated with the reasoning.

### Found and deliberately NOT fixed

**1. The `domain:` key is missing from 345 of 387 domain bonus-spell slots — and it is the key that
actually matters.**

This is the biggest thing found in the run and it reframes Task 2. Tracing what reads what:

- `DomainDefinition.BonusSpells` (the domain side, which the brief's 14 fixes are about) is read by
  **no engine or UI code at all** — only by tests. It is inert.
- The domain spell picker filters on the **spell** side: `BuilderView.razor:1285` keeps a spell for
  a domain only if `spell.ClassLevels` contains that `domain:<id>` key. `ContentRegistry`'s
  `GetSpellsForList` / the `/spells` API endpoint work the same way.

So the user-visible behaviour — which spells a cleric can actually pick for a domain — depends
entirely on the spell-side key, and only **42 of 387** slots have it (the 42 are the Fiendish Codex
spells, which is why the convention was described as "established"). Adding the two the brief asked
for takes that to 50. A cleric who takes, say, the Healing or Knowledge domain still gets an empty
"Add domain spell..." list at every level.

Not fixed here because: the brief names exactly two spells, ground rule 5 restricts content edits
to what a task explicitly authorises, and its closing line says not to invent further content-pack
work. The fix is mechanical and needs no source — each domain's own `bonusSpells` table already
states the domain, the level and the spell, so the backfill is derivable from data in this
repository — but it touches ~300 public spell files and deserves its own task and review.

**Highest-value follow-up available.** Suggested shape: generate the 345 additions from the domain
tables, then extend `EveryDomainBonusSpell_Resolves` to assert both directions of the link so it
cannot drift apart again.

**2. References that cannot be resolved without inventing a value or a primitive.** These are in
`ContentIntegrityTests.KnownGaps`, listed individually so anything *new* still fails the sweep:

| id | where | why it is not a rename |
|---|---|---|
| `skill:speak_language` | class-skill list on 9 classes (bard, loremaster, aristocrat, thaumaturgist, dragon disciple, …) | Speak Language is a real SRD skill that no pack defines and that has no sub-skills. Adding it means choosing a key ability, which nothing in the repo states. Adjacent to Task 5. |
| `skill:type_perform` (11 refs) | `MinSkillRanks` on Disguise Spell and 10 epic feats | Means "12 ranks in **any** Perform skill". Expressing it needs a prerequisite that matches a skill *category*; `MinSkillRanksAcross` takes an explicit id list, not a category. Those feats are currently unenterable. |
| `skill:type_{strength,dexterity,constitution,intelligence,wisdom,charisma}` (6 refs) | `MinSkillRanks` in `srd_epic` | Same shape: "any Strength-based skill". Same missing primitive. |
| `skill:scry`, `skill:divination`, `skill:dispel_magic` | private `en_elements_of_magic` feats | Gate on skills from a different magic system this content set does not define. |
| `template:half_dragon` | `class:dragon_disciple` | Missing content, not a naming mismatch — see above. |

### Checked and concluded correct — recorded so nobody re-investigates

- **Parent-skill ids** (`skill:craft`, `skill:knowledge`, `skill:perform`, `skill:profession`) appear
  as class skills on ~87 drivers and match no skill definition. They are **correct**:
  `ReplayStudio.ExpandParentSkillsInPlace` expands them to every sub-skill declaring them as
  `ParentSkill`. The sweep recognises the convention rather than allowlisting them.
- **Selectable feat variant ids** (`feat:spell_focus_conjuration`, `feat:skill_focus_spellcraft`,
  `feat:energy_resistance`) match no feat definition and are **correct**: they resolve against a
  repeatable feat with `selectionRequired`, the rule `ContentRegistry.IsSelectableFeatVariant`
  already applies. The sweep applies the same rule plus the `HasFeatSelections` prefix match.
- **`Capabilities` and language ids are free-form strings, not registry ids** (`wild_shape:animal:huge`,
  `infernal`). There is no capability or language registry to check them against, so the sweep does
  not treat them as references. Task 4 surfaces capabilities; a language registry is not proposed.
- **`elemental_swarm` and `summon_monster_ix` are each genuinely one spell.** Re-confirmed rather
  than assumed: the element/alignment is a casting-time choice, no per-element or per-alignment
  spell exists in any pack, and none should.
