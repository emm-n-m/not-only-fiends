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
| 2 — dangling domain spell refs + integrity guard | not started | | |
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
