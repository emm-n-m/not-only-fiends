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

## 2. Missing prerequisite primitives — **Fixed**

These SRD requirements had no expressible form, so the affected classes were under-gated.
Four new `Prerequisite` subclasses were added to `NotOnlyFiendsStudio/Models/Prerequisite.cs`,
and content backfilled. Two corrections to the original plan below.

| primitive | needed for | SRD wording | status |
|---|---|---|---|
| `HasAnyRace` | Arcane Archer | "Race: Elf **or** half-elf" | **Fixed** — `HasAnyRace{raceIds:[elf,half_elf]}` |
| `LacksTemplate` (renamed from `NotRace`) | Dragon Disciple | "Any nondragon (cannot already be a half-dragon)" | **Fixed**, but see content-gap note below |
| ~~`KnowsSpell`~~ | Arcane Trickster, Thaumaturgist, Cosmic Descryer | "Ability to cast *mage hand*", "*lesser planar ally*", "*gate*" | **Not built** — see note below |
| `HasLanguage` | Dragon Disciple | "Languages: Draconic" | **Fixed**, but see content-gap note below |
| `MinCounter` (ability magnitude threshold) | Arcane Trickster | "Sneak attack +2d6" (`HasAbility` is presence-only) | **Fixed** — `MinCounter{counterId:sneak_attack_dice,value:2}` |

Regression tests: `PrerequisiteTests.cs` (`HasAnyRace_*`, `LacksTemplate_*`, `HasLanguage_*`,
`MinCounter_*`) and `RulesAccuracyTests.cs` (`ArcaneArcher_RequiresElfOrHalfElf`,
`DragonDisciple_RequiresDraconicAndExcludesHalfDragon`,
`ArcaneTrickster_RequiresMageHandLevelAndSneakAttackTwoDice`,
`CosmicDescryer_RequiresAbilityToCastGate`).

**`KnowsSpell` was deliberately not built.** The data to track known-spell identity exists
(`SpellcastingState.SelectedSpells`), but it's populated only from `TickChoices.SpellSelections`,
which the REST API never exposes — an agent-built character could never satisfy an identity
check. Reused `CanCastSpellLevel` instead (the codebase's existing idiom for "ability to cast
[spell]"), which also caught a real bug: Arcane Trickster's prerequisite required 3rd-level
arcane spells to stand in for "ability to cast *mage hand*", but `mage_hand` is a 0-level
spell — corrected to `spellLevel: 0`. Thaumaturgist's existing `CanCastSpellLevel(4, Divine)`
already exactly matched *lesser planar ally* (a real 4th-level cleric spell) — no change
needed there. Cosmic Descryer gained `CanCastSpellLevel(9, Arcane)` for *gate*.

**Content gap, not a code defect:** Dragon Disciple's `HasLanguage{"draconic"}` and
`LacksTemplate{"half_dragon"}` prerequisites are correctly implemented and unit-tested, but
currently unsatisfiable by any real character build — no race/class content grants
`draconic` as a fixed language (a new `GrantLanguage` permabuff and `CharacterState.Languages`
field were added, but nothing populates them yet), and **no `half_dragon` template exists in
content yet, even though Half-Dragon is a genuine SRD template** (not homebrew — it's the
standard inherited template applied to nondragon creatures, same category as `half_fiend`,
which already exists at `Content/packs/srd_core/templates/half_fiend.json`). It needs proper
extraction via the `extract-template` skill from the SRD mirror or a source PDF — neither was
available on this machine when this was written (mirror not synced here; author was away from
the machine with the source copy). **Next session: extract `half_dragon` as a template
(mirroring `half_fiend.json`'s shape — type override, ability modifiers, natural armor,
breath weapon, etc.), then this prerequisite becomes real.** Do not add a fabricated grant to
make it pass in the meantime.

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
- **Tier 3** — the 617 spells (corrected from an earlier estimate of ~1,466 — confirmed count
  while splitting `spells/srd.json` in §7). Highest count, lowest per-item blast radius.
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

1. **Extend the PCG golden baseline to computed sheets — Fixed.** `PcgImportRegression`
   already replayed 54 real characters against a stored baseline with diff-on-mismatch and
   `UPDATE_PCG_BASELINE=1` to accept changes, but the per-character record only held import
   *mapping* fidelity (`droppedFeats`, `raceDropped`, warnings) — no computed values, which is
   why it passed unchanged through every rules fix this session. Added `hp / bab / saves /
   skillRanks / feats / classLevels / casterLevels`, compared field-by-field, rendered in the
   markdown diff alongside the existing dropped-item lists. **Could not be executed or
   regenerated on this machine** — the test is gated behind `PCGEN_CHARACTERS_PATH`, unset
   here (the private PCGen character corpus lives elsewhere). It compiles clean and is
   structurally complete. **Done 2026-07-27 on the machine with the corpus:** baseline seeded
   via `UPDATE_PCG_BASELINE=1` after a VERIFY-mode inspection of the all-fields-added diff;
   all 54 characters now carry hp/bab/saves/skillRanks/feats/classLevels/casterLevels, and a
   follow-up VERIFY run passes clean.
2. **Derived per-character content fingerprints.** Not started. For saves outside the corpus:
   hash only the definitions a character actually references (replay already walks exactly
   that set), store on the character, compare at load. Gives "class:eldritch_knight changed
   since this was saved" with no noise from unrelated content and no authoring discipline.
   Genuinely new infrastructure (no hashing utility exists yet, no tracking of which content
   IDs a replay touches) — sized closer to its own feature than a quick fix.
3. **Semantic pack versions.** Only genuinely needed for *interchange* — a character built
   against packs you don't have, where nothing can be recomputed. `PackManifest.Version`
   exists but is decorative (it stayed `"3.5"` through every breaking change this session).
   Hand-maintained semver will not survive an agent-driven extraction pipeline, so treat this
   as the last resort rather than the first move.

---

## 5. API surface — 3 of 4 remaining items fixed

Done previously: `optionDetail` / `driverIds` on `next-step` (8.3 MB → 93 KB), a real error
body on malformed request bodies, and validation for unknown skills/spells, off-list spells,
duplicate non-repeatable feats and spontaneous-caster spells-known.

Done this session:

- **`skillRanks` units differ by endpoint** — **Fixed.** Renamed `CharacterState.SkillRanks`
  → `SkillHalfRanks` (confirmed zero JSON/HTTP consumers before renaming — the Blazor Server
  app reads C# objects in-process, not via HTTP). `CharacterSheet.Skills` (whole ranks) was
  already clearly named; no change needed there.
- **Warnings are whole-replay, not per-tick** — **Fixed.** `CharacterState.Warnings` is now
  `List<Warning>` (`{TickIndex, Message}`) instead of `List<string>`, across ~38 write sites.
  `CharacterSheet.Warnings` stays `List<string>` (a display snapshot, not something callers
  filter programmatically) with `TickIndex` folded back into the text. The API DTOs
  (`CharacterMutationResponseDto`, `CharacterPreviewDto`) are now structured — the actual point
  of this item, giving callers a real field to filter on. `ImportPcgResponse.Warnings` (a
  separate, unrelated PCGen-import warnings list) is untouched.
- **ID convention inconsistency** — **Fixed via full unification**, not the cheap-interim
  options originally listed. Since the project isn't public yet, there was no migration cost,
  so races/feats/skills/spells/class-features were all given the same prefix convention
  drivers/domains/equipment already had (`race:`, `feat:`, `skill:`, `spell:`,
  `class_feature:`) — ~1,300 definitions and ~1,500+ reference sites across all 5 content
  packs, scripted rather than hand-edited. Uncovered and fixed three sharp edges along the way:
  a double-prefix bug in `PcgConverter`'s compound feat-selection-suffix builder, a
  double-prefix in `GrantCompanionSlot`'s manual `"feat:"` concatenation, and two hardcoded
  bare feat literals in `ReplayEngine` (`leadership`, `two_weapon_fighting`). Also surfaced and
  fixed a pre-existing content bug unrelated to the rename: `srd_epic/feats/srd_epic.json`
  contained 715 raw, un-cleaned PCGen LST stub entries (`"CATEGORY=FEAT|...".MOD` artifacts,
  zero mechanical content) plus 5 genuine duplicate `epic_*` feats defined identically in two
  packs, silently shadowed by the engine's default `LastWins` conflict resolution — the
  rename's collision check caught what the loader was quietly hiding. All extraction skill
  docs (`extract-race/feat/skill/spell/domain`) updated so future extractions use the new
  convention instead of regrowing bare ids. **The private packs repo was unreachable from the
  machine that did the rename and was migrated separately on 2026-07-27** (1,048 definition
  ids + 156 reference sites, field rules derived from commit `a30df61`'s diff) — the gap had
  surfaced as a `PcgImportRegression` OK→WARN regression (private-pack feats/races silently
  dropping) plus one stale bare-id assertion in `SpellContentTests` that only runs with
  private packs loaded.
- **ETag / conditional GET on content endpoints** — not done this session (deprioritized in
  favor of the ID unification once its real scope became clear). Still worth doing: content is
  immutable between restarts, so an ETag derived from loaded pack versions would let a polling
  agent skip refetching.

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

## 7. File organisation — **Fixed**

`srd_core/classes/srd.json` held 26 classes in ~100 KB, and editing it surgically was painful
— a first attempt at a four-class change produced a 470-line diff that was almost entirely
reformatting churn, and had to be redone as line-targeted surgery. The right pattern already
existed next to it (`classes/base/fighter.json`, `classes/prestige/eldritch_knight.json`, one
class per file).

Split both flat files: the 26 remaining classes into `classes/{base,prestige,npc}/` (added a
new `npc/` bucket for adept/aristocrat/commoner/expert/warrior — no `category` field exists in
the data, so bucketing was a judgment call, not derived), and `spells/srd.json` (617 spells,
not the ~1,466 originally estimated here) into one file per spell — the spell split happened
*after* TODO §5's ID unification so it wrote already-`spell:`-prefixed content once rather than
touching 617 files twice. Confirmed purely mechanical: the loader already recurses over any
`*.json` file structure with no manifest, so zero loader/schema code changes were needed.
`extract-class`/`extract-spell` skill docs updated to point at the per-file convention instead
of a flat file to append to.
