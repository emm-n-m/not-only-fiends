# TODO

Outstanding work, captured 2026-07-27 after an agent-driven audit of the REST API and an
SRD verification pass over all 48 drivers in the public packs.

Two documents already cover adjacent ground: [TEST_COVERAGE_BACKLOG.md](TEST_COVERAGE_BACKLOG.md)
for test gaps, [CONTENT_POLICY.md](CONTENT_POLICY.md) for the public/private pack split.

---

## 1. Rules accuracy — known-wrong content

Found by diffing content against `NotOnlyFiendsStudio/Content/srd_html/` (the SRD mirror).
Each needs a regression test in `NotOnlyFiendsStudio.Tests/RulesAccuracyTests.cs` alongside
the fix — that file is the home for SRD-verified assertions.

### Hierophant grants 5 caster levels it should not — HIGH — **Fixed**

`class:hierophant` has `perLevelPermabuffs: [AdvanceSpellcasting divine]`, which fires at all
five levels. The SRD is precise:

> Levels in the hierophant prestige class, **even though they do not advance spell progression**
> in the character's base class, still stack with the character's base spellcasting levels to
> determine caster level.

So caster level *should* advance; spells per day should *not*. `AdvanceSpellcasting.Apply`
did both (`sc.CasterLevel++` then `UpdateSpellcastingFromProgression`). Added a
`CasterLevelOnly` flag on the permabuff (`Permabuff.cs`), set it on hierophant. Regression:
`Hierophant_AdvancesCasterLevelButNotSpellsPerDay`.

### Elemental saves are element-dependent — HIGH — **Fixed**

> Good saves depend on the element: Fortitude (earth, water) or Reflex (air, fire).

`racial_hd:elemental` was a single generic driver hardcoded to the air/fire answer, so
`companion_elemental_water_small` got the wrong save. Split into `racial_hd:elemental_air`
and `racial_hd:elemental_water` (only elements with an existing companion race); kept the
old generic `racial_hd:elemental` id as-is for PCGen import fallback
(`PcgIdMapper.cs` maps PCGen's generic "Elemental" creature type to it, with no per-element
data to disambiguate). Regressions: `AirElemental_HasGoodReflexSave`,
`WaterElemental_HasGoodFortitudeSave`.

### Smaller gaps

- **Shadowdancer** — **Fixed.** Added `levelPermabuffs` at 6 (shadow jump 40 ft. + shadow
  companion +2 HD), 8 (shadow jump 80 ft.), 9 (shadow companion +2 HD — this was the "summon
  shadow" gap; the companion is granted once at 3rd level, HD increases every third level
  thereafter), and 10 (shadow jump 160 ft. — not originally listed here, but the same SRD
  progression implies it and it was fixed in the same pass). Proficiency note corrected from
  "gains no new weapon or armor proficiencies" to the actual SRD weapon list plus light
  armor (no shields). Regressions: `Shadowdancer_GrantsCorrectProficiencies`,
  `Shadowdancer_HasLevelPermabuffsAtGapLevels`, `Shadowdancer_ShadowJumpDistanceDoublesEveryTwoLevels`,
  `Shadowdancer_ShadowCompanionGainsHDAtSixthAndNinthLevel`.
- **Loremaster** — **Fixed.** Bonus language now also granted at 8th level (was 4th only).
  Regression: `Loremaster_GrantsBonusLanguageAtFourthAndEighthLevel`.
- **Cosmic Descryer** — ~~prerequisite "Epic Feats: Energy Resistance" could not be added
  because no `energy_resistance` feat exists in the content.~~ **Fixed**: no generic
  `energy_resistance` feat exists, but five element-specific ones do
  (`energy_resistance_acid/cold/electricity/fire/sonic`), and `HasFeatSelections`' existing
  `FeatId`-or-`FeatId + "_"` prefix match already expresses "any one of them" with no new
  primitive needed. Still missing: "ability to cast *gate*", blocked on the `KnowsSpell`
  primitive in §2. Its BAB and save progressions are populated (poor BAB; poor/poor/good
  saves) but permanently **unverifiable against source**: `cosmicDescryer.html` has no
  attack or save columns at all. Treat as best-effort; a future mismatch here is not
  necessarily a regression.

---

## 2. Missing prerequisite primitives

These SRD requirements have no expressible form, so the affected classes are under-gated.
Add to `NotOnlyFiendsStudio/Models/Prerequisite.cs` (each is a `Prerequisite` subclass plus a
`[JsonDerivedType]` entry), then backfill the content.

| primitive | needed for | SRD wording |
|---|---|---|
| `HasAnyRace` | Arcane Archer | "Race: Elf **or** half-elf" |
| `NotRace` / excluded template | Dragon Disciple | "Any nondragon (cannot already be a half-dragon)" |
| `KnowsSpell` | Arcane Trickster, Thaumaturgist, Cosmic Descryer | "Ability to cast *mage hand*", "*lesser planar ally*", "*gate*" |
| `HasLanguage` | Dragon Disciple | "Languages: Draconic" |
| ability magnitude threshold | Arcane Trickster | "Sneak attack +2d6" (`HasAbility` is presence-only) |

Deliberately **not** modelled — narrative gates with no mechanical test:
Assassin ("must kill someone for no other reason than to join the assassins"), Blackguard
(peaceful contact with an evil outsider), Cosmic Descryer (must have travelled to another plane).

Also unmodellable as stated: Expert's "can choose any ten skills to be class skills"
(player-chosen class skills have no representation), and Loremaster's "seven different
divination spells" (only the "3rd level or higher" half is captured).

---

## 3. Verification coverage still to run

Use the `verify-content` skill. Tier 1 (all 48 drivers in the public packs) is **done**.

- **Tier 2** — SRD races, feat prerequisites and feat `type` values, domains.
- **Tier 3** — the 1,466 spells. Highest count, lowest per-item blast radius.
- **Private packs** — the 17 non-SRD classes (Hellfire Warlock, Archfiend, Blood Witch,
  Dark Temptress, Hellreaver, …) have **no SRD ground truth**. They cannot be verified this
  way; verifying them from model recall would manufacture false findings. They need either
  the source books to hand or an explicit decision to leave them unverified.

---

## 4. Content drift — the structural problem

A character sheet is a pure function of (character JSON × content). Change the content and
every saved character silently becomes a different character. This session demonstrated it
twice: fixing Eldritch Knight's saves moved a saved character's Fort from 5 to 8, and the
giant BAB fix changes every giant PC's attack bonus. No warning, no record.

Do these **in order** — the first is cheap and catches the most:

1. **Extend the PCG golden baseline to computed sheets.** `PcgImportRegression` already
   replays 54 real characters against a stored baseline with diff-on-mismatch and
   `UPDATE_PCG_BASELINE=1` to accept changes. But the per-character record holds only import
   *mapping* fidelity (`droppedFeats`, `raceDropped`, warnings) — no computed values, which is
   why it passed unchanged through every rules fix this session. Add `hp / bab / saves /
   skillRanks / feats / classLevels / casterLevels` and content drift becomes a failing test
   with a reviewable diff. **No version numbers needed.**
2. **Derived per-character content fingerprints.** For saves outside the corpus: hash only the
   definitions a character actually references (replay already walks exactly that set), store
   on the character, compare at load. Gives "class:eldritch_knight changed since this was
   saved" with no noise from unrelated content and no authoring discipline.
3. **Semantic pack versions.** Only genuinely needed for *interchange* — a character built
   against packs you don't have, where nothing can be recomputed. `PackManifest.Version`
   exists but is decorative (it stayed `"3.5"` through every breaking change this session).
   Hand-maintained semver will not survive an agent-driven extraction pipeline, so treat this
   as the last resort rather than the first move.

---

## 5. API surface

Done this session: `optionDetail` / `driverIds` on `next-step` (8.3 MB → 93 KB), a real error
body on malformed request bodies, and validation for unknown skills/spells, off-list spells,
duplicate non-repeatable feats and spontaneous-caster spells-known.

Remaining:

- **`skillRanks` units differ by endpoint** — doubled in `/state`, whole ranks in `/sheet`,
  with no unit marker or schema description on either. At minimum document it; better, name
  the fields differently.
- **Warnings are whole-replay, not per-tick.** A mistake at HD 6 keeps resurfacing in every
  later simulation, so a caller must diff warnings before/after to know whether *its* tick
  caused anything. Consider tagging each warning with the tick index that produced it.
- **ID convention inconsistency** — drivers are prefixed (`class:wizard`), races are bare
  (`human`). Guessing `race:human` is a 404. Fixing it is a breaking change to every saved
  character, so it needs a decision, not a patch. Cheap interim: document it, or accept both.
- **ETag / conditional GET on content endpoints.** Content is immutable between restarts, so
  an ETag derived from loaded pack versions would let a polling agent skip refetching. This is
  the one idea worth taking from dnd5eapi.co — its `{index, name, url}` reference envelope was
  evaluated and rejected, because it optimises for *browsing* and this is a character builder.

---

## 6. Repo hygiene before going public

Blocking, and irreversible once cloned or forked:

- **Squash git history.** 66 `.pcg` paths are still retrievable from history (deleted in
  `544617c`) — personal campaign characters, which also name non-SRD sources. Also present:
  `.dotnet-cli/` telemetry blobs including `MachineId` caches. Only 11 commits, so squashing
  to a clean initial commit is far simpler than `filter-repo`.
- **Scrub `.claude/settings.json`** — contains hardcoded `/mnt/c/Users/<user>/…` PCGen paths.

Non-blocking:

- **README "verification status" section.** The engine is well-tested; SRD content is
  best-effort. Say so plainly, so nobody builds a character for a real game on an unverified
  prestige class. One paragraph turns a liability into an invitation to report discrepancies.
- **Decide the contribution policy** — accepting issues/PRs, or source-available only.
  Worth choosing up front rather than disappointing someone later.
- **Check whether `pcgen_srd` warrants a PCGen attribution** in the OGL Section 15 list.
  That pack derives from PCGen's LST files rather than the SRD directly. The existing notices
  (SRD, Unearthed Arcana, OGL 1.0a) match the other four packs.

---

## 7. File organisation

`srd_core/classes/srd.json` holds 26 classes in ~100 KB, and editing it surgically is painful
— a first attempt at a four-class change produced a 470-line diff that was almost entirely
reformatting churn, and had to be redone as line-targeted surgery. The right pattern already
exists next to it (`classes/base/fighter.json`, `classes/prestige/eldritch_knight.json`, one
class per file). Split it, and `spells/srd.json` (379 KB) after.

This is the cheapest quality-of-life win in the list, and it makes every future content diff
reviewable.
