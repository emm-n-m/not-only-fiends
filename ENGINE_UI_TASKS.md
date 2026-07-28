# Engine & UI work order — unattended dev-machine run

A self-contained brief for an agent running on the **dev machine** without interactive
approval. Every task here is scoped so it can be completed and verified with **only what is in
this repository** — no private packs, no PCGen corpus, no source PDFs, no SRD mirror.

Source of the findings: TODO §8 and TODO §1, produced by the 2026-07-27 LST audit and the
2026-07-28 Fiendish Codex PDF audit. Read TODO §8 before starting; it has the reasoning behind
each item. This file is the executable version of it.

---

## Environment — read first

This machine is **missing** the following, and that is expected, not a fault to repair:

| absent | consequence |
|---|---|
| `EXTRA_PACKS_PATH` (private packs) | ~23 `[RequiresPrivatePacks*]` test methods **skip**. That is correct. |
| `PCGEN_CHARACTERS_PATH` (54 `.pcg` corpus) | `PcgImportRegression` and `PcgReconstructionTests` **skip**. |
| `PCGEN_DATA_PATH` (PCGen LSTs) | content audits are impossible here. Do not attempt any. |
| `SOURCE_PDFS_PATH` (Fiendish Codex PDFs) | as above. |
| `NotOnlyFiendsStudio/Content/srd_html/` (SRD mirror) | may or may not be present; it is gitignored. **Check before Task 5b**, and skip that sub-task if absent. |

Therefore:

- **A green suite here does not mean the private packs are fine.** Skipped ≠ passed. Do not
  treat a skip as a problem, and **never** remove or weaken a `[RequiresPrivatePacks*]` gate to
  make something run.
- Expect roughly **40 skipped** tests and ~730 passing. If the *passing* count drops relative
  to the baseline you record in step 0, you broke something.

## Ground rules

1. **Branch first.** `git checkout -b engine-ui-<task>` per task, or one branch for the run.
   Never commit to `main`. **Never push.**
2. **One task, one commit**, with a message explaining *why*, not just what. Keep the working
   tree clean between tasks.
3. **Never invent rules values.** If a rule's correct value is not derivable from code or from
   content already in this repo, stop that task, write what you found in
   `ENGINE_UI_TASKS_REPORT.md`, and move to the next. There is a live example of why: five
   Fiendish Codex races carried invented level adjustments for months. Absent data is a
   finding to report, never a number to guess.
4. **Do not run `UPDATE_PCG_BASELINE=1`**, and do not modify anything under a `test-reports/`
   directory. The golden baseline lives with the private packs and cannot be validated here.
5. **Do not touch content under `NotOnlyFiendsStudio/Content/packs/`** except where a task
   explicitly says so (only Task 2 does).
6. If a task turns out bigger than described, do the part you can verify, commit it, and note
   the remainder. Partial and correct beats complete and unverified.

## Step 0 — establish the baseline

```bash
dotnet build 2>&1 | tail -5
dotnet test  2>&1 | tail -5      # record the passed/skipped/failed numbers
```

Write those numbers at the top of `ENGINE_UI_TASKS_REPORT.md`. Every later task is judged
against them.

---

## Task 1 — Skill totals, synergies, and skill bonuses  ← highest value, do first

**The problem.** There is no skill total anywhere in the codebase. `SheetView.razor:150-167`
renders a two-column table of skill name and rank count. The number a player actually rolls
does not exist. Two sub-features hang off this:

- `SkillDefinition.Synergies` / `SkillSynergy` (`Models/Skill.cs:15,18`) are **the only two
  references in the solution** — 9 skills carry 12 entries (5 ranks in X → +2 on Y) that
  nothing reads.
- `CharacterState.SkillBonuses` is **write-only**: `GrantSkillBonus` (`Models/Permabuff.cs:560`)
  writes it, `CharacterSheet` carries it (`Models/Character.cs:117,150`), and nothing reads it.

**Do:**

1. Add a skill-total computation as a **tail pass** in `ReplayStudio`, alongside the existing
   `FinalizeRacialSpellcasting` (`Studio/ReplayEngine.cs:255`) and
   `FinalizeCompanionAndLeadership` (`:312`). Total per skill =
   `whole ranks + key-ability modifier + SkillBonuses[skill]`.
   Whole ranks are `SkillHalfRanks[id] / 2` (integer division — the half-rank representation is
   how cross-class ranks are stored; see `Models/CharacterState.cs:46`).
2. Apply synergies **in that same tail pass**, granting `+bonus` to `targetSkillId` when the
   source skill has **≥ 5 whole ranks**.
   - It must be a tail pass, not per-tick: a character crossing 5 ranks at 7th level would
     otherwise get an order-dependent answer. This is the same reason racial spellcasting is
     finalised late.
   - Synergies key off **ranks**, not totals, so they do **not** chain. Do not implement
     iteration to a fixed point.
   - Multiple synergies into one skill **do** stack: Diplomacy legitimately receives three
     separate +2s (from Bluff, Sense Motive and Knowledge (nobility)). Do not deduplicate.
3. Surface it: add the total to `CharacterSheet`, and render **ranks / ability / misc / total**
   as columns in the sheet's skill table. Keep the existing "(class)" marker.

**Out of scope** (note them in the report, do not attempt): armor check penalty, untrained-use
restrictions, size and other situational modifiers.

**Acceptance:**
- A new test class `SkillTotalTests` covering: ranks-only, ranks + ability modifier,
  ranks + `GrantSkillBonus`, synergy at exactly 5 ranks (fires) and at 4 ranks (does not),
  three-source stacking on Diplomacy, and that a synergy-granted +2 does **not** itself
  count toward another synergy's 5-rank threshold.
- All previously passing tests still pass.
- `SkillBonuses` and `Synergies` each now have a real consumer.

---

## Task 2 — Dangling domain spell references (public packs)

**The problem.** 11 domain bonus-spell slots point at spell ids that do not exist, so those
domains silently grant nothing at that level. Eight are core SRD domains. All are id-naming
mismatches left behind by an earlier id unification — the target spell exists under another id.
Full table with real ids is in **TODO §1**; use it verbatim rather than re-deriving.

There is no design decision here: `elemental_swarm` and `summon_monster_ix` are each a single
spell (the element / alignment is chosen at casting), and no per-element or per-alignment
spell exists in any pack or should be created.

**Do:**

1. Fix the 11 references in `NotOnlyFiendsStudio/Content/packs/*/domains/*.json`.
2. Add the missing **domain assignments on the spell side**, which are also absent:
   `spell:elemental_swarm` currently has only `class:druid: 9` and needs
   `domain:air/earth/fire/water: 9`; `spell:summon_monster_ix` needs
   `domain:chaos/evil/good/law: 9`. The `domain:<id>` key inside `classLevels` is the
   established convention.
3. **Generalise the dangling-reference guard.** `FiendishCodexDomains_ReferenceOnlyRealSpells`
   in `PrivatePackRulesAccuracyTests` is currently scoped to seven domains because it would
   otherwise fail on these 11. Replace it with an ungated test asserting that **every** domain
   in every loaded pack references only real spells.

**Then go further, because this is the real lesson:** nothing checks that *any* cross-reference
resolves. Add a content-integrity test that walks every loaded definition and asserts that
referenced ids exist — feat prerequisites (`HasFeat.FeatId`), class skills, `GrantBonusFeat`,
racial HD driver ids, domain bonus spells, `bonusSpells`, `classLevels` keys. Report anything
it finds; fix only unambiguous id-naming mismatches, and report rather than guess where a
target genuinely does not exist.

**Acceptance:** the new integrity test passes with the fixes; it fails if any reference is
broken (verify by temporarily breaking one, then reverting).

---

## Task 3 — Builder offers non-playable races

**The problem.** `BuilderView.razor:1182` populates the race picker from
`registry.GetAllRaces()`, **unfiltered**. Every monster race, companion race and creature never
priced as a PC race is offered as a character choice with nothing marking it.

`RaceDefinition.LevelAdjustment` is `int?` (`Models/Race.cs:13`) specifically so this is
detectable: **`null` means the source never priced it as a PC race**; `0` means playable at no
cost, like a Human. Null contributes 0 to ECL — it is a provenance signal, not a number.

**Do:** mark and/or filter null-LA races in the picker. Prefer a visible badge plus a
"show non-PC races" toggle **defaulting to hidden**, rather than hard filtering — the builder is
also used to construct companions and monsters, and removing them outright may break that
workflow. Also make `SheetView.razor:40` distinguish "no sanctioned LA" from "LA +0", which it
currently cannot (it prints `(LA +N)` only when `> 0`).

**Acceptance:** a component or unit test asserting null-LA races are excluded from the default
list and included when the toggle is on. Note in the report how many races in the bundled packs
are null-LA (expected: 0 in public packs — the null ones live in the private Fiendish Codex
pack, so this may only be testable with a synthetic fixture; construct one rather than skipping
the test).

---

## Task 4 — Surface write-only state on the sheet

Two small display gaps, both found by sweeping for state nothing reads:

1. **`Capabilities` is entirely write-only.** `GrantCapability` (`Models/Permabuff.cs:475`)
   writes it, state and sheet carry it, nothing anywhere reads it. Content grants 17: the
   druid's whole wild shape matrix (`wild_shape:{animal,plant,elemental}:{tiny…huge}`) plus
   three `blood_witch:*` entries. Wild Shape itself *is* visible as a granted ability with a
   uses/day counter, so what is missing is **which forms** a druid can assume. Render them
   (grouping the `wild_shape:*` family into something readable beats dumping 14 raw strings).
2. **`SLA.SaveDC` is never displayed.** `SheetView.razor:224` renders spell-like abilities as
   name plus uses/day only. One-line fix.

**Acceptance:** both appear on the sheet; a druid of sufficient level shows its available wild
shape forms. No engine changes should be needed.

---

## Task 5 — Languages, code-only slices

**The problem.** The whole chain exists and every end of it is missing.
`CharacterState.Languages`, `GrantLanguage` (`Models/Permabuff.cs:182`) and `HasLanguage`
(`Models/Prerequisite.cs:268`) all exist; there is exactly one piece of content that grants a
language, no race-language schema field, no Int-based selection, and no presence at all in
`CharacterSheet`, the API DTOs or the UI. Net effect: `class:dragon_disciple`'s
`HasLanguage{draconic}` prerequisite is satisfiable by **nothing**.

**5a — PCG importer (do this; fully testable here).** `PcgParser` does not parse `LANGUAGE:`
lines at all, yet every `.pcg` carries one, pipe-delimited:

```
LANGUAGE:Abyssal|LANGUAGE:Auran|LANGUAGE:Celestial|LANGUAGE:Common|LANGUAGE:Draconic|…
```

Parse it into `PcgCharacterData`, map names to lowercase ids via the conventions in
`PcGen/PcgIdMapper.cs`, and carry them onto the imported character so they land in
`CharacterState.Languages`. **This needs no corpus**: `PcgParser.ParseText(string, string)`
(`PcGen/PcgParser.cs:46`) takes inline content, and `PcgConverterTests` already runs ungated.
Write fixtures inline.

**5b — Race automatic languages (conditional).** Requires the SRD mirror at
`NotOnlyFiendsStudio/Content/srd_html/`. **If that directory is absent or empty, skip this
entirely and say so in the report** — do not populate languages from memory. If present: add a
race schema field, backfill via `GrantLanguage`, and update `.claude/skills/extract-race/SKILL.md`,
which currently says to treat "Automatic Languages / Bonus Languages" as flavor only.

**5c — Display.** Add languages to `CharacterSheet` and render them on the sheet.

**Explicitly NOT in scope:** Int-based bonus-language selection (needs a race bonus-language
list plus a `TickChoices` mechanism plus builder UI — design work for a human), and restoring
the three Fiendish Codex II prestige classes' "Language: Infernal" prerequisites. **Do not
restore those**: until a general language-selection mechanism exists, that prerequisite would
make those classes hellbred-only, which the book does not intend.

---

## Reporting

Maintain `ENGINE_UI_TASKS_REPORT.md` as you go — assume the reader has none of your context:

- The step-0 baseline numbers, and the numbers after each task.
- Per task: done / partial / blocked, what changed, and the commit sha.
- Every judgment call you made, and why. Especially anywhere you chose a value.
- Anything you found and did **not** fix, with enough detail to act on it.
- Anything that looked wrong but that you concluded was correct — that is as useful as a fix
  and stops the next person re-investigating it.

If you finish everything, do **not** invent further work in the content packs. Re-read TODO §8
and §1 and extend the cross-reference integrity checking from Task 2 instead; that is the
highest-value open-ended work available without the private data.
