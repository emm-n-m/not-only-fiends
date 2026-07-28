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
| 3 — builder offers non-playable races | **done** | 523 passed / 11 skipped / 0 failed | |
| 4 — surface write-only state on the sheet | **done** | 530 passed / 11 skipped / 0 failed | |
| 5 — languages (5a + 5c done, 5b skipped) | **partial by design** | 547 passed / 11 skipped / 0 failed | |

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

---

## Task 3 — the builder offered every race, playable or not — DONE

### What changed

- **`Studio/RaceCatalog.cs`** (new) — `IsSanctionedPcRace`, `ForPicker`, `NonPcMarker`,
  `DescribeLevelAdjustment`. Put in the Studio project, not the Blazor layer, so the rule is
  unit-testable (the test project has no bUnit, so nothing in a `.razor` file can be asserted) and
  so the API can apply the same rule later.
- **`BuilderView.razor`** — the picker now feeds through `ForPicker`; non-PC races are hidden by
  default behind a "Show non-PC races (N)" checkbox, are suffixed "— non-PC" when shown, and carry a
  tooltip explaining that the source printed no Level Adjustment for them.
- **`SheetView.razor`** — a null-LA race now shows a "no sanctioned LA" badge. Previously the sheet
  printed `(LA +N)` only when `N > 0`, so null and `LA +0` rendered identically.
- **`RaceCatalogTests.cs`** (new) — 9 tests.

### Judgment calls

- **Badge plus toggle, not a hard filter**, as the brief prefers — the builder is also used to build
  companions and monsters, and removing those races outright would break that workflow.
- **`ForPicker` takes an `alwaysIncludeId`.** Not in the brief, but necessary: `SearchSelect`
  reverts its text to a matching item on blur (`SearchSelect.razor:115-128`), so opening an existing
  companion character whose own race was filtered out would have silently changed that character's
  race. The currently-selected race is always in the list.
- **The sheet reads the `RaceDefinition`, not `state.LevelAdjustment`.** The latter coalesces null
  to 0 at `ReplayEngine.ApplyRace` (correctly — null contributes 0 to ECL), so by the time it
  reaches the sheet the distinction is gone. No engine change was needed; the sheet already holds
  the registry.
- **The toggle does not mark the character dirty.** It changes what the list shows, not the
  character, so it deliberately does not call `OnCharacterChanged`.

### Counts, as asked

**0 of 214 bundled-pack races are null-LA** — every public race states a real Level Adjustment, as
the brief expected. So on a machine with no private packs the filter is a visible no-op and the
checkbox reads "Show non-PC races (0)". `EveryBundledRace_StatesALevelAdjustment` pins that, so a
future public pack that adds an unpriced race is noticed rather than silently hidden.

The five real null-LA races (`race:ekolid`, `race:juvenile_nabassu`, `race:armanite`,
`race:yochlol`, `race:lilitu`) are all in the private `fiendish_codex_1` pack, which is present on
this machine, so they are asserted directly behind `[RequiresPrivatePacksFact]` **and** the
synthetic-fixture tests cover the filter on machines without it.

### Not done — worth knowing

`audit-agent-api` should be re-run now that a filter exists, as TODO §8 says: the API's `/races`
endpoint (`AgentApiService.GetRaces`) still lists every race unfiltered, so an agent building a
character through the API meets the original trap even though a human in the builder no longer does.
`RaceCatalog` is deliberately placed where that endpoint can use it. Not done here because the
brief scopes Task 3 to the builder and the sheet.

---

## Task 4 — write-only state now on the sheet — DONE

No engine changes were needed, as the brief predicted. Both items were display-only.

### 1. `Capabilities`

`GrantCapability` wrote it, `CharacterState` and `CharacterSheet` carried it, and **nothing read it
anywhere** — no engine logic, no prerequisite, no UI, no API, no test. Bundled content grants 17:
the druid's 14-entry wild shape matrix plus three `blood_witch:*` entries in a private pack.

A new Capabilities card on the sheet groups the `wild_shape:<kind>:<size>` family by kind with the
sizes in size order, so a 20th-level druid reads:

```
Wild Shape — Animal      Tiny, Small, Medium, Large, Huge
Wild Shape — Elemental   Small, Medium, Large, Huge
Wild Shape — Plant       Tiny, Small, Medium, Large, Huge
```

rather than 14 raw colon-delimited strings. Anything outside that family falls back to a title-cased
reading of its segments (`blood_witch:minor_sacrifice` → "Blood Witch — Minor Sacrifice"). Sizes are
ordered smallest-first from an explicit list, because alphabetical would read "Huge, Large, Medium,
Small, Tiny", which is not the order a druid gains them.

This was the missing half of the feature: Wild Shape already appeared as a granted ability with a
uses/day counter, so what a player could not find out was **which forms** they could assume.

### 2. `SLA.SaveDC`

The sheet rendered spell-like abilities as name plus uses/day only. Now appends `— DC N` when a save
DC is stored. Eight SLAs across six bundled races carry one (`race:grig` ×3, `race:couatl` ×2,
`race:svirfneblin`, `race:gynosphinx`, `race:nixie`).

### Tests

`CapabilityTests.cs` (new) — 7 tests. The grouping and formatting live in `SheetView.razor`, which
has no unit-test harness in this project (no bUnit), so the tests pin the *data* the display depends
on rather than the markup: a druid below 5th has no forms, a 5th-level druid has exactly Small and
Medium animal, a 20th has all 14, capabilities reach `CharacterSheet` (and therefore the API), every
bundled capability is colon-delimited as the grouping assumes, and the Grig/Couatl save DCs survive
evaluation. The markup itself is verified by running the app.

---

## Task 5 — languages — 5a and 5c DONE, 5b SKIPPED (no SRD mirror)

### 5a — PCG importer — done

`PcgParser` did not handle `LANGUAGE:` lines at all, though every `.pcg` carries one. Added:

- **`PcgCharacterData.Languages`** and a `LANGUAGE:` branch in `PcgParser.ParseLines`. PCGen writes
  the whole set on one line with the tag repeated
  (`LANGUAGE:Abyssal|LANGUAGE:Auran|LANGUAGE:Common|…`), so the parser splits on the pipe and strips
  each tag; that also handles the single-language form. Repeats are ignored case-insensitively.
- **`PcgIdMapper.MapLanguage`** — `DefaultIdTransform` with **no prefix**. Language ids are bare in
  content (`race:hellbred` grants `infernal`, `class:dragon_disciple` requires `draconic`), unlike
  every other id the mapper produces. There is no language registry to validate against, so this is
  a pure name transform and unknown languages are carried rather than dropped.
- **`PcgConverter.Convert`** — attaches the grants as a
  `PermanentEvent { BeforeTick = 0, Permabuffs = [GrantLanguage …] }`.

**Judgment call — why a permanent event.** `GrantLanguage` is the only writer to
`CharacterState.Languages`, and `Character` has no language field. The options were to add one (a
model and serialization change, and a second way for languages to enter state) or to use the
existing extension point. Permanent events are applied by the tick loop for `BeforeTick == 0` before
anything else runs, so a class taken at 1st level already sees the languages — which is the case
that matters, since Dragon Disciple's prerequisite is checked on the tick that enters the class.
`GrantLanguage` is already a registered `[JsonDerivedType]`, so it round-trips through saved
character JSON; there is a test for that, because a discriminator gap would lose an imported
character's languages the first time it was written to disk.

**What this unblocks.** `class:dragon_disciple`'s `HasLanguage{draconic}` was satisfiable by nothing
at all — core SRD content gating a class no character could enter. Importing a `.pcg` is now a route
that satisfies it, asserted against the prerequisite instance taken from real content rather than a
constructed one.

### 5c — display — done

`CharacterSheet.Languages` (so the REST API carries them too) and a Languages line in the sheet
header, title-cased and sorted.

### 5b — race automatic languages — SKIPPED, and why

`NotOnlyFiendsStudio/Content/srd_html/` **does not exist on this machine** (verified, not assumed).
Ground rule 3 forbids inventing rules values, and a race's automatic and bonus language lists are
exactly that — recalling "dwarves speak Common and Dwarven" from memory is the failure mode the rule
exists to prevent, and there is a live example of it in this repo's history (five Fiendish Codex
races carried invented level adjustments for months).

So: **no race schema field was added, no languages were backfilled, and
`.claude/skills/extract-race/SKILL.md:40` still says to treat "Automatic Languages / Bonus
Languages" as flavor only.** That line is correct as long as there is no schema field; it should be
updated in the same change that adds one, on a machine with the mirror.

Consequence: authored characters still have no languages unless their race grants one
(`race:hellbred` is still the only content that does). Only imported characters get them.

### Explicitly not in scope, per the brief — not attempted

- **Int-based bonus-language selection.** Needs a race bonus-language list, a `TickChoices`
  mechanism and builder UI. Design work for a human.
- **Restoring the three Fiendish Codex II prestige classes' "Language: Infernal" prerequisites.**
  Deliberately left dropped: until a general language-selection mechanism exists, that prerequisite
  would make those classes hellbred-only, which the book does not intend.

### Related finding, from Task 2's sweep

`skill:speak_language` is listed as a class skill by 9 classes and **no pack defines it**. That is
the authoring-side counterpart to this task: even once a race language schema exists, the skill a
character would spend ranks on to learn additional languages is missing. Not fixed here — adding it
means choosing its key ability, which nothing in the repository states.

---

## End-to-end verification in the running app

A second instance was run on port 5099 (never touching port 5000) and driven through the REST API,
because the sheet loads its character from `sessionStorage` and so cannot be reached by `curl`. The
API returns the same `CharacterSheet` the sheet renders, so this exercises the real code path, not
just the unit tests.

**16th-level druid** — `POST /api/characters/evaluate`:

```
languages: ["druidic", "sylvan"]
skillTotals: { knowledge_nature: 22, survival: 23, spot: 14 }
skillSynergyBonuses: { knowledge_nature: 2 }
capabilities: 13 entries (wild_shape animal ×5, plant ×5, elemental ×3)
```

Survival 19 ranks + 4 (WIS 18) = 23. Knowledge (nature) 19 ranks + 1 (INT 12) + **2 synergy from
Survival** = 22 — real shipped SRD synergy data reaching a real total for the first time. 13 forms,
not 14, is correct at 16th: `wild_shape:elemental:huge` arrives at 20th.

**Grig** (racial HD only, zero skill ranks) — the clearest demonstration of what was broken:

```
skills (ranks): {}
skillBonuses:   { search: 2, spot: 2, listen: 2, jump: 8 }
skillTotals:    { search: 2, spot: 4, listen: 4, jump: 3 }
slAs: Entangle 3/day DC 13 | Pyrotechnics 3/day DC 14 | Ventriloquism 3/day DC 13
      Disguise Self 3/day (no DC) | Invisibility (Self Only) 3/day (no DC)
```

Jump +3 is +8 racial −5 (STR 6). Before this run those four racial bonuses affected nothing a user
could see, and the three save DCs were stored and never shown.

**Builder prerender** — `Show non-PC races (5)`, correctly counting the five private-pack null-LA
races, and no initialization errors in the page or the app log. (The `blazor-error-ui` div in the
HTML is the framework's always-present hidden error slot, not an error.)

### What could not be verified this way

The sheet's own markup — the five-column skill table, the grouped Capabilities card, the `— DC N`
suffix, the Languages line and the "no sanctioned LA" badge — is behind the interactive Blazor
circuit. The **data** behind every one of them is asserted by unit tests and confirmed through the
API above, and the components compile, but the rendered markup itself is untested. Worth one manual
click-through: build a druid, view the sheet, and toggle "show non-PC races" in the builder.

---

## Summary

| task | status | commit |
|---|---|---|
| 1 — skill totals, synergies, skill bonuses | done | `e9a8a99` |
| 2 — dangling domain spell refs + integrity guard | done | `a8b200a` |
| 3 — builder offers non-playable races | done | `dde7a86` |
| 4 — surface write-only state on the sheet | done | `6fd16aa` |
| 5 — languages (5a + 5c) | done | `12a7412` |
| 5b — race automatic languages | **skipped** — SRD mirror absent | — |

```
step 0:  Failed: 0, Passed: 495, Skipped: 11, Total: 506
final:   Failed: 0, Passed: 547, Skipped: 11, Total: 558
```

52 tests added, none removed, nothing weakened, no gate loosened. Skipped count is unchanged at 11 —
the same `PcgImportRegression` / `PcgReconstructionTests` methods that skip for want of
`PCGEN_CHARACTERS_PATH`. `UPDATE_PCG_BASELINE=1` was never run; `test-reports/` was never touched.

### Ranked follow-ups this run found and did not do

1. **The builder offers spells-known selection to prepared casters** — see the correction below.
   Fixing that supersedes what this report originally ranked first (backfilling 345 `domain:`
   keys), which would have populated a control that should not be a dropdown at all.
2. **A prerequisite that can match a skill category** — `skill:type_perform`,
   `skill:type_<ability>`. Disguise Spell and ten epic feats are unenterable without it.
3. **Define `skill:speak_language`** — 9 classes list it as a class skill; nothing defines it. Needs
   its key ability from a source.
4. **Re-run `audit-agent-api`** (Task 3). `/api/content/races` still lists every race unfiltered and
   returns only `id`/`name`/`description`, so it cannot express the PC/non-PC distinction at all —
   an agent building through the API meets the original trap. `RaceCatalog` is placed where that
   endpoint can adopt it.
5. **Armor check penalty on skill totals** (Task 1). `SkillDefinition.ArmorCheckPenalty` and
   `ArmorProfile`'s penalty value both already exist; only the wiring is missing.
6. **Extract the SRD half-dragon template** (Task 2). Dragon Disciple's "cannot already be a
   half-dragon" restriction has nothing to test against.
7. **5b, on a machine with the SRD mirror** — race language schema field, backfill, and the
   `extract-race` skill update.

---

## Correction — the builder offers spells-known selection to prepared casters

Raised after the run by the user, and confirmed in code. **This supersedes what this report
originally ranked as follow-up #1.**

### The bug

`BuilderView.razor:400` shows the spell panel for any caster, and `:442-473` renders an
"Add spell..." dropdown per spell level gated only on whether spells exist at that level. It never
asks whether the class is prepared or spontaneous. So a cleric is offered a spells-known style
choice at every level, listing the entire cleric list — when a cleric knows their whole list and
prepares from it daily. There is no such choice in 3.5.

It affects **10 of the 13 spellcasting classes**: `cleric`, `cloistered_cleric`, `druid`, `adept`,
`paladin`, `paladin_of_tyranny`, `ranger`, `planar_ranger`, `blackguard`, `wizard`. Only `sorcerer`
and `bard` (and racial casters that stack onto them) legitimately have spells known.

Secondary effect: `alreadyKnown` (`:445`) is only computed when `SpellsKnown != null`, so for
prepared casters the filter at `:454` passes everything and the **same spell can be added
repeatedly**.

### The engine is not at fault

`SpellsKnown == null` is this codebase's definition of a prepared caster, and two places apply it
correctly:

- `HasSpontaneousCasting.IsMet` (`Prerequisite.cs:240`) tests exactly `s.SpellsKnown != null`.
- `CheckSpellsKnownLimits` (`ReplayEngine.cs:550`) skips those casters, with a comment saying why —
  "a wizard's spellbook is unbounded".

So the distinction is modelled and enforced; the builder simply never consults it. Note the nuance
the engine comment implies: for a **wizard** a selection is meaningful (it is a spellbook, just
unbounded), while for cleric, druid, paladin, ranger, adept and blackguard it is meaningless.

### Why this changes the domain-spell recommendation

This report originally ranked "backfill the 345 missing `domain:` keys" first, so that the
"Add domain spell..." dropdown would populate. **That was the wrong conclusion.** A domain's bonus
slot at level N has exactly one legal spell — whichever the domain lists — so a dropdown is the
wrong control regardless of how many keys exist. The spell is determined, not chosen.

The facts behind the original finding still hold: `DomainDefinition.BonusSpells` is read by no
engine or UI code, the picker filters on the spell-side `domain:` key, and only 42 of 387 slots
carry one. But the fix they point to is the opposite of a backfill: **read `BonusSpells` — the
dictionary that is currently inert — and display which spell fills each domain slot.** That needs
no content change at all, and it makes the domain side of the link the consumed one, which is where
the data already lives and where the 14 ids fixed in Task 2 already are.

### Suggested shape of the fix

1. Gate the class spell picker on `SpellsKnown != null`. For prepared casters show the available
   list read-only (or, for the wizard, label it "spellbook" and keep it unbounded and deduplicated).
2. Replace the domain spell dropdown with a read-only line per domain slot, sourced from
   `DomainDefinition.BonusSpells`, e.g. "Air, level 9 — Elemental Swarm".
3. Drop the 345-key backfill from the plan; the two spell-side additions made in Task 2 remain
   correct and harmless either way, since `classLevels` is also what `GetSpellsForList` and the
   `/api/content/spells?listId=` endpoint query.

---

## Follow-up work — spell acquisition and specialist wizards

Done after the five tasks, on the user's direction, following the correction above.

### 1. Three kinds of caster, not one

New `SpellAcquisition` enum (`Models/Enums.cs`) — `FullList` / `Spellbook` / `SpellsKnown` — with
`SpellcastingProgression.ResolvedAcquisition` inferring it: a class with a `spellsKnown`
progression knows a fixed number of spells, anything else has its whole list. That is the rule the
engine already applied implicitly, so **only the wizard needed an explicit
`"acquisition": "spellbook"`** in content; the other twelve casting classes are unchanged.
`OnlyTheWizard_NeedsAnExplicitAcquisitionInContent` pins that.

Builder behaviour by kind:

| kind | classes | picker |
|---|---|---|
| `FullList` | cleric, cloistered cleric, druid, paladin (+tyranny), ranger (+planar), adept, blackguard | **none** — replaced by "Knows the entire *N*-spell list, prepared daily, nothing to choose here" |
| `Spellbook` | wizard | budgeted picker, 0-level omitted (automatic), deduplicated |
| `SpellsKnown` | sorcerer, bard, assassin | unchanged |

Deduplication now applies to every kind. It used to be computed only when `SpellsKnown != null`, so
a prepared caster could add the same spell repeatedly.

### 2. Wizard spellbook budget

`ReplayStudio.SpellbookSpellsAllowed(wizardLevel, intModifier)` = `3 + max(0, intMod) + 2 x (level - 1)`,
checked by `CheckSpellbookLimits`, and shown live in the builder as "N / M spells of level 1+".

Judgment calls: counted against **actual wizard class levels**, not caster level — a prestige class
that advances spellcasting grants caster level and spells per day, not new spellbook spells. An
Intelligence *penalty* does not reduce the starting three ("for each point of Intelligence bonus").
Spells copied from scrolls or another wizard's book are not modelled and are not counted. Cantrips
and domain picks never count.

### 3. Domain bonus slots are no longer a dropdown

Replaced by a read-only list per domain, sourced from `DomainDefinition.BonusSpells` — which until
now no engine or UI code read at all. A domain grants one bonus slot per spell level and exactly one
spell is legal in it, so this was never a choice. **This is the fix that replaces the 345-key
backfill** this report originally ranked first: nothing needs backfilling, because the domain side
of the link already holds the answer and is the side the 14 Task 2 fixes corrected.

### 4. Specialist wizards

New `class_feature:wizard_specialization` and `class_feature:wizard_prohibited_schools`, each
offering the eight schools, granted once at 1st wizard level. Both ride the existing class-feature
selection machinery, so they need no new state and reach the sheet and API through
`ClassFeatureSelections` for free. `Studio/WizardSchools.cs` is the single place that knows the
rule, shared by the builder's filtering and the engine's validation so they cannot drift.

- **Spells of a given-up school are not offered** in the builder, at any level, and the engine warns
  if one arrives through the API or a hand-edited file.
- Specializing is optional — picking nothing makes a universalist who keeps every school.
- **Universal spells are never prohibited.** They belong to no school, and `universal` is not among
  the eight options. (Worth recording: the five bundled universal spells are `arcane_mark`,
  `prestidigitation`, `permanency`, `wish`, `limited_wish`. `read_magic`, despite the name, is
  divination — an assumption that failed a test during this work.)
- Warns on: wrong number of schools given up, specializing in a school also given up, and giving up
  schools with no specialty.

The school check is a **tail pass**. Within a tick, spell selections are applied before class
feature choices, so a per-tick check would let a 1st-level wizard write a barred spell into its book
before its own specialty was recorded. `SchoolsChosenOnTheSameTickAsSpells_AreStillEnforced` covers
it.

### Rules values that could not be verified here

The SRD mirror is absent on this machine, so two numbers rest on the user's instruction and the
class's own text rather than a quoted source. Both are worth checking when the mirror is available:

1. **A diviner gives up one school, every other specialist two.** Isolated in
   `WizardSchools.RequiredProhibitedCount` with a comment. It affects only a warning message, never
   which spells are available.
2. **The spellbook formula** (3 + Int bonus at 1st, +2 per level). Stated by the user and matching
   the wizard's own class-feature text, which was corrected in the same change — it previously said
   "all 0-level spells and three 1st-level spells", omitting the Intelligence bonus and the
   per-level additions entirely.

### Deliberately not done

- **The specialist's bonus spell slot per level** (one extra slot of each level, castable only from
  the specialty school). Real, and not asked for. `SpellsPerDay` comes from progression tables, so
  adding it means a permabuff that adjusts the computed slots — a separate change.
- **Prohibited schools blocking item use or existing characters.** Selection is never blocked
  anywhere in this engine; illegal input warns and the build continues. The builder simply stops
  offering the spells.
- **Specialist prestige classes / bonus specialist feats.** Out of scope.
