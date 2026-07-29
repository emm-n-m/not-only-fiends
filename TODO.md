# TODO

Outstanding work, captured 2026-07-27 after an agent-driven audit of the REST API and an
SRD verification pass over all 48 drivers in the public packs.

**Reconciled against the code 2026-07-28** after the engine/UI run landed. Several entries
described as open had in fact been fixed, and were verified here by inspection rather than
taken from commit messages: §1's dangling domain references (0 remain), and §8's skill totals,
`Capabilities`, `SLA.SaveDC` and Level Adjustment. Still genuinely open, in rough priority
order: the P1 dropped-prerequisites sweep and Tier 2/3 SRD verification (§3), content
fingerprints (§4.2), ETag on content endpoints (§5), and the README / contribution-policy /
OGL-attribution decisions (§6). The authoring half of languages (§8) closed 2026-07-29.

**Known blocker for the P1 sweep, found 2026-07-29 while sizing it:** of the four missing
prerequisite primitives, `KnowsSpell` cannot be written against the current interface.
`Prerequisite.IsMet(CharacterState)` takes no content lookup, so "knows spell X" can only be
tested against `SpellcastingState.SelectedSpells` — which spontaneous and spellbook casters
populate but full-list casters (cleric, druid) never do, since they select nothing. Answering
it for them needs the class spell list, i.e. content. So P1 starts with a decision about
widening the prerequisite interface (an `IsMet` overload taking `IContentLookup`, mirroring
`PermabuffContext`), not with content edits.

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
### Dangling domain spell references in the public packs — HIGH — **Fixed**

**Fixed 2026-07-28** (`a8b200a`). All the references below were repointed, both halves of the
two-way links closed (`spell:elemental_swarm` gained `domain:air/earth/fire/water: 9` and
`spell:summon_monster_ix` gained `domain:chaos/evil/good/law: 9`), and the guard was
generalised out of the Fiendish-Codex-only test into `ContentIntegrityTests`, which now walks
cross-references across the packs rather than one book. Re-verified 2026-07-28: a sweep of
every domain bonus-spell slot in every public pack resolves — **0 dangling references**.
Original analysis retained below.


Found 2026-07-28 while re-extracting the Fiendish Codex domains. **11 domain bonus-spell slots
across 11 public-pack domains point at spell ids that do not exist**, so those domains silently
grant no bonus spell at that level. Nothing in the test suite catches a dangling reference.
Every one is an id-naming mismatch with a spell that *is* present — almost certainly fallout
from §5's id unification or §7's spell split renaming the spells without updating the domains:

| domain | slot | broken reference | real id |
|---|---|---|---|
| `domain:strength` | 2 | `spell:bull_s_strength` | `spell:bulls_strength` |
| `domain:strength` | 7 | `spell:bigby_s_grasping_hand` | `spell:grasping_hand` |
| `domain:strength` | 8 | `spell:bigby_s_clenched_fist` | `spell:clenched_fist` |
| `domain:strength` | 9 | `spell:bigby_s_crushing_hand` | `spell:crushing_hand` |
| `domain:magic` | 9 | `spell:mordenkainen_s_disjunction` | `spell:mages_disjunction` |
| `domain:travel` | 7 | `spell:greater_teleport` | `spell:teleport_greater` |
| `domain:air` / `earth` / `fire` / `water` | 9 | `spell:elemental_swarm_<element>` | `spell:elemental_swarm` |
| `domain:chaos` / `evil` / `good` / `law` | 9 | `spell:summon_monster_ix_<alignment>` | `spell:summon_monster_ix` |

Eight of these are core SRD domains, so the blast radius is any cleric using them.

**All 11 are mechanical renames — no design decision is needed.** (An earlier draft of this
entry claimed the element/alignment variants needed a ruling first; that was wrong. A summoning
spell takes the alignment descriptor of whichever creature is summoned, so `summon_monster_ix`
is one spell with a casting-time creature choice and has no elemental dimension at all;
`elemental_swarm` likewise opens a portal to the plane of the caster's choosing. No per-element
or per-alignment spell exists in any pack, and none should.)

Both sides of those two links are broken, so fixing the domain refs is only half of it:
`spell:elemental_swarm` carries only `class:druid: 9` and is missing `domain:air/earth/fire/
water: 9`, and `spell:summon_monster_ix` is missing `domain:chaos/evil/good/law: 9`. The
`domain:` keys in a spell's `classLevels` are the established convention (the Fiendish Codex
spells use them).

**Add a loader-level or test-level guard for dangling content references at the same time** —
this class of breakage is invisible today, and the equivalent private-pack bug (`domain:fury`
2 → `spell:bull_s_strength`) was correct-per-the-book and still broken, so a content audit
alone would not have found it. A guard currently exists for the Fiendish Codex domains only
(`FiendishCodexDomains_ReferenceOnlyRealSpells`); generalise it once the 11 above are fixed,
since today it would fail immediately. Worth widening beyond domains too — nothing checks that
feat, class or race cross-references resolve either.

- **Skill synergies unimplemented** (found 2026-07-28 by the strict-deserialization
  test). `srd_core/skills/srd.json` carries structured synergy data on 9 skills
  (5 ranks in X → +2 on Y), now preserved on `SkillDefinition.Synergies`, but no
  engine code consumes it. **Moved to §8** — the original wording said this "means
  applying them in the skill-total computation", but there is no skill-total
  computation anywhere in the codebase to apply them to. See §8 for the real scope.

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

~~**Content gap, not a code defect:** Dragon Disciple's `HasLanguage{"draconic"}` and
`LacksTemplate{"half_dragon"}` prerequisites are unsatisfiable by any real character build.~~
**Both halves closed.** `draconic` became reachable when the authoring side of languages landed
(2026-07-29); `template:half_dragon` was extracted 2026-07-29 from the SRD mirror
(`monstersHtoI.html`, "Creating a Half-Dragon") into
`Content/packs/srd_core/templates/half_dragon.json`. Dragon Disciple is now enterable and its
exclusion gates against a template that exists. Removed from `ContentIntegrityTests`' known-gaps
list; regressions `HalfDragon_MatchesSrdTemplate`,
`HalfDragon_GrantsNoWingsToAMediumCharacter`, and an added registry assertion in
`DragonDisciple_RequiresDraconicAndExcludesHalfDragon`.

Three parts of the SRD text are **not** modelled, none of which bite a PC build:

- **Dragon variety is not selectable.** The 10 varieties differ only in breath energy/shape and
  one extra energy immunity. Kept as a single generic `template:half_dragon` rather than 10
  variants, because `LacksTemplate.IsMet` is an exact `Contains` (`Prerequisite.cs:264`) — a
  split would silently stop matching Dragon Disciple's exclusion. Same call the elemental-saves
  fix in §1 made in reverse, and for the same reason: split only when a consumer needs the
  distinction. Both variety-dependent effects are recorded as descriptive `GrantAbility` text so
  they reach the sheet rather than vanishing.
- **Wings are size-conditional** ("Large or larger has wings … Medium or smaller does not").
  Not expressible, and every PC race here is Medium or smaller, so the template grants no fly
  speed — the correct answer for every character that can actually take it.
- **"Increase racial HD by one die size, to a maximum of d12"** and the dragon skill-point
  formula `(6 + Int) × (HD + 3)` have no schema field. Only affects characters with racial HD.

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
- **Private packs** — ~~no ground truth~~ **audited 2026-07-27** with the
  `verify-content-lst` skill against the PCGen LST data set (`PCGEN_DATA_PATH`). Full
  report + fix status: `{EXTRA_PACKS_PATH}/test-reports/lst_audit_2026-07-27.md`.
  Patterns P2/P3/P5 and all user rulings are **fixed** (2026-07-28). Remaining:
  - **P1** — the dropped-prerequisites sweep (largest batch; ~40 findings across
    12_to_midnight, curses, mongoose, necromancer, sword_and_sorcery, deceit classes).
    Cross-check restored prereqs against the `.pcg` corpus (see skill's
    "second witness" section). Needs schema additions: KnowsSpell-or-equivalent,
    school-gated casting, deity, PC-level.
  - **P4** — Eldritch Sorcery spells: 51 `DOMAINS:` and 10 Assassin/Blackguard
    `classLevels` assignments (representable with existing conventions).
  - **Fiendish Codex 1/2** — ~~no LSTs exist; audit against the PDFs~~ **audited 2026-07-28**
    against the PDFs at `SOURCE_PDFS_PATH`. Report:
    `{EXTRA_PACKS_PATH}/test-reports/fc_pdf_audit_2026-07-28.md`. All 114 items covered.
    Spells (42/42) and racial HD (5/5) fully clean; FC1's five demon races clean on every
    stated field. Nothing applied yet — the fixes, in suggested order:
    - ~~**All six FC1 domains are substantially wrong** — 5 of 6 granted powers are a
      different power than the book's, and 23 of 54 bonus-spell slots name the wrong spell.~~
      **Re-extracted 2026-07-28** from pp. 88–90: 5 powers replaced, 23 slots corrected, all
      54 verified to resolve. Every wrong slot had been an SRD-spell slot while every
      FC1-native one was right — invented substitutions, not fallbacks. FC2's one domain was
      already perfect. 13 assertions added. Also fixed three **dangling** references found in
      passing (`domain:fury` 2/6, `domain:ooze` 2); the same class of bug in the public packs
      is now its own §1 entry.
    - ~~Three one-line fixes~~ **Fixed 2026-07-28**: `class:hellfire_warlock` BAB
      `poor`→`average`; `class:soulguard`'s `CanCastSpellLevel` gained `castingType: Divine`;
      `feat:ordered_chaos` lost its wrong `abyssal_heritor` tag.
    - ~~Five FC2 divine feats dropped their "ability to turn or rebuke undead"
      prerequisite~~ **Fixed 2026-07-28** with `HasAbility{turn_undead}` (content models
      turning and rebuking as one ability, so that expresses it exactly).
    - **Spells fully verified 2026-07-28** — the deferred second half (school, components,
      casting time, range, target/area/effect, duration, save, SR for all 42, ~290
      comparisons) found **zero** new content bugs, on top of the 42/42 clean level
      assignments. The one discrepancy, `spell:morality_undone`'s `V, S, M/DF`, is the
      known component-alternation gap, not an extraction error.
    - ~~`race:hellbred` has `abilityModifiers: null`, dropping the mandatory Infernal Aspect
      choice.~~ **Fixed 2026-07-28.** Added `class_feature:hellbred_infernal_aspect` (body /
      spirit) modelled on `loremaster_secret`, wired to the race via
      `GrantClassFeatureSelection`; also added `GrantLanguage{infernal}` (the first content
      anywhere to use it) and made Infernal Mien a structured `GrantSkillBonus` instead of
      prose. Five assertions in `PrivatePackRulesAccuracyTests`; suite 744/744, PCG baseline
      verifies clean and unchanged. Still gaps: the HD-gated parts of each aspect (bonus
      devil-touched feats at 4/14 HD, the darkvision 30→60→120 ladder, see-in-darkness at 12
      HD, telepathy at 15 HD) need HD-conditional racial grants, which don't exist.
    - The three FC2 prestige classes' dropped "Language: Infernal" prereq stays dropped.
      `GrantLanguage` is the **only** writer to `CharacterState.Languages` — there is no
      Int-based bonus-language selection — so restoring it would make Hellbreaker, Hellfire
      Warlock and Soulguard *hellbred-only*, which the book does not intend. The blocker is
      now a general language-selection mechanism, not the missing grant. (Same underlying
      gap as Dragon Disciple's `draconic` in §2.)
    - ~~Needs a user ruling: FC1 states **no** Level Adjustment anywhere, yet all five demon
      races carry one (2–6).~~ **Ruled and applied 2026-07-28: they now carry `null`.** FC1
      prints no LA for any creature; Lilitu (p43) and Yochlol (p55) do read "Advancement by
      character class", but that is the NPC-advancement field, not 3.5's PC-legality marker.
      **Null, not 0** — 0 asserts "playable at no cost" (Human), a different and equally
      unsourced claim. That distinction was previously inexpressible, so
      `RaceDefinition.LevelAdjustment` became `int?` (`ReplayEngine.ApplyRace` reads
      `?? 0`; null still contributes 0 to ECL) and `race.schema.json` now accepts
      `["integer", "null"]` while keeping the field required. `extract-race`'s SKILL.md said
      "**Estimate** level adjustment … absence means LA 0" — the source of the problem — and
      now says to transcribe rather than estimate, write `null` when the source prints none,
      and never infer an LA from "Advancement: by character class" or "Favored Class".
      Asserted by `FiendishCodex1Races_CarryNoUnsourcedLevelAdjustment` and
      `NullLevelAdjustment_ContributesZeroToEcl`.
    - New engine gaps: no creature-type gate, no patron/allegiance gate (blocks the 9 Marks),
      no any-of prerequisite wrapper, no prepared-only casting check, no choice-bearing racial
      traits, no favored class.

    No corpus exposure: none of the 54 `.pcg` characters uses any FC1/FC2 class or feat, so
    these fixes are unusually low-risk to the golden baseline.
  - Engine gaps noted in the report: flat-HP grant, non-equipment typed AC bonuses,
    class/feat speed grants, feat selections (Elemental Resistance), Curse Repertoire
    spells-known feature, template prerequisites, HDDriver spell-list field.

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
   **Extended 2026-07-28** with `languages`, `skillTotals` and `spellAcquisition`. The §8 work
   had added three computed surfaces the record did not capture, so the baseline could pass
   clean while saying nothing about them — the same blind spot this item was written to close,
   one layer up. Languages are sorted on capture (`HashSet` order is unstable, and a field that
   diffs every run trains you to ignore the report), and all three are wired into the
   comparison *and* the markdown diff, not just stored. Accepted after a VERIFY run confirmed
   the diff was additions-only: 0 regressions, 0 aggregate tally changes, and no
   hp/bab/saves/skillRanks/classLevels/casterLevels line anywhere in it.
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

See also **§8**, which is blocking for the same milestone for product rather than repo reasons:
languages are absent from the character sheet entirely, and the builder offers monster and
companion races as PC choices with nothing marking them.

Blocking, and irreversible once cloned or forked:

- ~~**Squash git history.**~~ **Already done** (verified 2026-07-28). History was rewritten in
  `0917c0c "Squash local development history"` on 2026-05-11; `544617c` no longer exists and
  `git rev-list --all --objects` finds zero `.pcg` and zero `.dotnet-cli` blobs. The entry
  below was written against the pre-rewrite history and was stale.
- ~~**Scrub `.claude/settings.json`**~~ — **Fixed 2026-07-28.** It leaked two identities, not
  one: the `USER` Windows username in the PCGen paths *and* `/home/USER/source/repos/…`, a
  different machine's checkout, plus a `/mnt/c/pandoc-3.9` install. Nine of those entries were
  dead on this machine (`/home/USER`, the pandoc binary and the `Content/srd/*.rtf` sources all
  no longer exist) and were deleted outright; the two live PCGen ones moved to
  `.claude/settings.local.json`, which is gitignored, so the working setup is preserved without
  publishing a path. The tracked file is now portable — `dotnet build/test`, the package
  searches, `gap-analysis` and `/tmp`. A sweep of every tracked file for
  `USER|/home/USER|/home/USER|OneDrive|AppData` now returns only generic README examples
  (`~/OneDrive/characters` as illustration) with no username in them.

Non-blocking:

- **README "verification status" section.** The engine is well-tested; SRD content is
  best-effort. Say so plainly, so nobody builds a character for a real game on an unverified
  prestige class. One paragraph turns a liability into an invitation to report discrepancies.
- **Decide the contribution policy** — accepting issues/PRs, or source-available only.
  Worth choosing up front rather than disappointing someone later.
- ~~**Check whether `pcgen_srd` warrants a PCGen attribution** in the OGL Section 15 list.~~
  **Moot as of 2026-07-29 — the pack is deleted.** See the "fully retired" note at the end of
  this entry. The history below is kept because it records what each pass established.
  That pack derived from PCGen's LST files rather than the SRD directly. The existing notices
  (SRD, Unearthed Arcana, OGL 1.0a) match the other four packs.
  **Confirmed load-bearing 2026-07-28, so this is a live licensing question, not a formality.**
  The pack is not vestigial gap-filler: 222 of its items exist in no other pack (all the magic
  and epic gear — `srd_core` carries only 78 mostly-mundane items), and 19 of those are
  equipped on 20 of the 54 corpus characters (`wondrous:headband_of_epic_intellect_12` alone on
  12). Removing it would dangle real references. Cleaned up in the same pass: its 33 entries
  that `srd_core` already shadowed were deleted (`srd_core` wins on priority 0 vs −10, and the
  duplicates had **zero** conflicting values — every difference was an optional field, with
  `srd_core` strictly richer for armor speeds), and 8 live items carried a garbled
  LST-conversion name of the form `"Flail, Flail (Heavy)"`, now `"Flail, Heavy"` to match
  `srd_core`'s convention. 255 items → 222; PCG baseline verifies unchanged.
  **2026-07-29: the entire ring/rod/staff/wondrous slice is now retired from `pcgen_srd`.**
  Hand-extracting the missing SRD/epic rings, rods, staffs, and wondrous items (from the local
  SRD mirror, not PCGen LST) let all of `srd_equipment_epic.json`'s `ring`/`wondrous` entries
  (40 items — 5 Ring of Protection grades + 35 wondrous, including
  `wondrous:headband_of_epic_intellect_12`) get deleted outright rather than merely shadowed;
  `srd_core` now carries the sole, richer copy of each (fuller prose plus, where the earlier
  pcgen conversion had it right, matching `grantedPermabuffs` — one bug found and fixed in the
  process: the four alignment-ward epic rings' shield-of-law/cloak-of-chaos/holy-aura/unholy-aura
  auras really do carry a +4 deflection/+4 resistance bonus per the SRD spell text, so that
  mechanic was ported forward, not dropped). 222 → 178, and **zero** `ring`/`rod`/`staff`/
  `wondrous` categories remain in `pcgen_srd` — what's left is entirely `weapon`/`armor`/`shield`
  (66 in the epic file, 112 in the base file). Full retirement of the pack now only needs that
  remaining slice hand-extracted the same way; once it's empty the licensing question disappears
  rather than needing an answer. PCG baseline verifies unchanged (all removed entries were
  already shadowed by priority, so no computed value moved).
  **2026-07-29 (later): `pcgen_srd` is fully retired — directory deleted, dropped from
  `content-public.json`.** The remaining 178 `weapon`/`armor`/`shield` entries were hand-extracted
  from the SRD mirror into three new `srd_core/equipment/` files: `weapons_srd.json` (49 — every
  mundane row of Table: Weapons that `srd_core` lacked), `magic_armor_weapons.json` (54 — the
  Specific Armors / Specific Shields / Specific Weapons sections of `magicItemsAW.html`) and
  `epic_armor_weapons.json` (40 — `epicMagicItems.html` plus three arms-and-armor artifacts from
  `epicArtifacts.html`). 844 equipment items in `srd_core`, 0 schema errors, no duplicate ids.
  No PCGen-derived content remains anywhere in the repo. See
  "Retiring pcgen_srd: what the SRD changed" below for what the swap corrected and what it dropped.

---

### Retiring `pcgen_srd`: what the SRD changed

**The pcgen entries were base stats only.** Every special-material and magic item carried the
*unmodified base item*: `armor:adamantine_breastplate` was priced at the bare breastplate's 200 gp
with the full −4 check penalty and no damage reduction (SRD: 10,200 gp, −3, DR 2/−);
`weapon:stormbrand` was priced at 50 gp (SRD: 235,350 gp); `armor:golem_armor` at 0 gp. None had a
description, and no armor folded in its enhancement bonus. `armor:dragonskin_armor_*` was recorded
as *medium* armor when the SRD makes it +5 full plate. The swap is a fidelity gain, not just a
licensing one.

**Conventions followed (matching the earlier rings/wondrous pass):**

- **Magic armor and shields fold the enhancement bonus into `armor.armorBonus`** — Celestial Armor
  is `+3 chainmail`, so 8. 3.5e enhancement-to-armor always stacks with the armor bonus and
  `ArmorProfile` is the only vehicle, so summing is safe.
- **Magic weapons do *not* model their enhancement bonus.** The engine can carry one only via a
  `GrantWeaponLine` permabuff, and `ReplayEngine.EvaluateEquipment` auto-derives a second weapon
  line from `def.Weapon`, so setting both double-counts. Setting only the permabuff would drop the
  damage badge `BuilderView` renders off `contentDef.Weapon`. Base profile + prose was chosen;
  **the enhancement bonus is therefore documentation, not mechanics.** Worth revisiting if
  `EquipmentDefinition` ever grows an `enhancementBonus` field next to `Weapon`.
- **Magic armor is masterwork**, so its check penalty is the base armor's lessened by 1. Adamantine
  and mithral include masterwork in their own modifiers rather than stacking a second −1.
- **An item's granted attack folds into the parent** as a `GrantWeaponLine` — Demon Armor's claws
  and the Armor of the Abyssal Horde's clawed gauntlets, which pcgen carried as free-standing
  weapon rows.
- **Artifacts store `priceCp: 0`** (Golem Armor, Invulnerable Coat, Axe of the Dwarvish Lords); the
  SRD gives them no market price and a guess would read as transcribed.

**Deliberately not carried forward** (PCGen-generated pseudo-items, not SRD equipment):
`weapon:flurry_of_blows`, `weapon:boulder` (a giant's thrown rock), `weapon:leshay_weapon` (a
leShay's innate swords, from the monster entry), the four `*_epic_might` weapon rows (`+8 Battleaxe`
and friends — what Rod of Epic Might *becomes*), the two claw-attack rows, and the 16
`weapon:rod_*` / `weapon:staff_*` rows duplicating `srd_core`'s `rod:` / `staff:` items. The two
composite-bow "+0" twins collapse into one item each. `weapon:sun_blade_bastard` and
`weapon:sun_blade_short` collapse into `weapon:sun_blade`.

**Also gained** (same SRD sections, absent from pcgen): the four specific magic ammunition entries —
screaming bolt, slaying arrow, greater slaying arrow, sleep arrow.

**Two judgement calls worth re-checking against a book:**

- Armor of the Celestial Battalion has **no weight in the SRD**. 20 lb. is carried over from
  Celestial Armor, the non-epic item described in the same "fine and light" terms.
- Bulwark of the Great Dragon is priced **1,612,970 gp in its description and 1,612,980 gp in the
  random-item table**. The description's figure is used; both are noted in the item text.

**Not verified on this machine: the PCG import baseline.** `PcgImportRegression` and
`PcgReconstructionTests` skip here because no `.pcg` corpus is present (`PCGEN_CHARACTERS_PATH`
unset). `PcgIdMapper` resolves equipment **by display name**, so retiring a pack can silently turn
a mapped item into a dropped one. Mitigations: the golden report shows **zero** corpus references to
any `pcgen_srd` equipment id; every LST-style name the pack used to answer now resolves either by
name in `srd_core` or through a new `EquipmentOverrides` entry; and
`RetiredPcgenEquipmentNames_StillResolveForPcgImport` pins that. `["Masterwork Cold Iron Longsword
+2"]` was also repointed from `weapon:longsword` to the real
`weapon:masterwork_cold_iron_longsword`, which **will** move that character's weapon line — expected,
but it means the baseline needs a re-run and review on a machine that has the corpus.

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

---

## 8. Core features still half-built — **mostly fixed 2026-07-28**

**Status after the engine/UI run (`e9a8a99`…`415450e`), re-verified against the code
2026-07-28.** Three of the four subsections below are now closed and one is partly closed:

| item | state |
|---|---|
| Skills — no total computed | **Fixed.** `SkillTotals` + `SkillSynergyBonuses` computed in a `ReplayEngine` tail pass (synergies consumed at `ReplayEngine.cs:357-362`, `SkillBonuses` finally read at `:382`), surfaced on `CharacterSheet` and rendered in `SheetView`. |
| `Capabilities` write-only | **Fixed.** `SheetView` renders them grouped, so a druid's wild-shape forms are visible. |
| `SLA.SaveDC` never displayed | **Fixed.** `SheetView.razor:252-254`. |
| Level Adjustment — picker offers every race | **Fixed.** `RaceCatalog` gates the picker on a printed LA, marks non-PC races, and keeps an already-selected one via `alwaysIncludeId`; the sheet distinguishes "no sanctioned LA" from "LA +0". The REST API applied none of this until 2026-07-28 — `GetCatalog`/`GetRaces` returned one flat unmarked list — and now returns `levelAdjustment` + `isPcRace` on a `RaceSummaryDto`. Verified across the corpus: **0 of 54 characters' races are hidden from the default picker**, so nothing became un-editable. |
| Languages | **Partly fixed** — see below. |

Languages: **Fixed 2026-07-29.** The import and display halves landed first (`.pcg` `LANGUAGE:`
lines are parsed, `CharacterState.Languages` reaches the sheet) — verified across the corpus:
all 45 characters with source languages import them completely, including `Drow Sign Language →
drow_sign_language`; the 9 with none are animal companions whose files genuinely carry no
`LANGUAGE:` line. The authoring half is now done too:

- **Languages are real content.** New `LanguageDefinition` type and `languages/` content
  directory, with the 20 SRD languages in `srd_core/languages/srd.json`. This exists so choices
  can be *offered* — "any bonus language except secret ones" is not expressible without a list.
  `CharacterState.Languages` deliberately stays a bag of free-form string ids and is **never**
  validated against the catalogue: PCGen import mints ids from arbitrary source text
  (`daemonic`, `fae`, `telepathy`), and a character who speaks something no pack defines is
  still a valid character.
- **Races carry their language lines.** `automaticLanguages`, `bonusLanguages` and
  `bonusLanguagesAny` on `RaceDefinition` and in `race.schema.json`; populated for the seven PC
  races plus drow. `bonusLanguagesAny` is a flag rather than a wildcard list entry so the
  "except secret languages" half of the SRD rule lives in the data model, not in a magic string.
- **Int-based selection.** `Character.BonusLanguageIds` is a creation-time input alongside
  `BaseAbilityScores` — 3.5 prices these off *starting* Int, so a later ability increase does not
  buy another language. `LanguageCatalog` owns the allowance and offer rules (in Studio, not the
  UI, so the builder and the API cannot drift apart the way the race picker and the API did).
  Spent in `ReplayStudio` after base abilities and before the tick loop, warning rather than
  failing on an over-spend, an unoffered pick or a duplicate.
- **Surfaced everywhere:** builder card with per-race checkboxes and a picks-used counter,
  `/api/content/languages`, languages on the catalogue, and the race language lines on
  `RaceSummaryDto`.

**Dragon Disciple is now enterable by a built character** — asserted by
`DragonDiscipleIsEnterableByABuiltCharacter` and confirmed over HTTP. 14 assertions in
`LanguageTests`; suite 891/891; PCG baseline verifies unchanged (PCGen already wrote every
race's automatic languages into the `.pcg` files, so granting them racially changed nothing).

Still open: the three Fiendish Codex II prestige classes' "Language: Infernal" prerequisite is
now *mechanically* restorable — a non-hellbred character can take Infernal as a bonus language —
but the FC2 packs have not been re-audited to put it back. Also unbuilt: languages from
Speak Language skill ranks, and race language lines for the private packs' races.

Original write-up follows.



Two features the Fiendish Codex audit (2026-07-28) pushed into view. Both are cases where the
*data model* exists but nothing upstream or downstream connects to it, so they read as present
in the code and are absent in the product. The per-pack engine gaps from the LST and PDF
audits are listed in their own reports; these two are big enough to track here.

**Priority raised 2026-07-28 (user ruling): treat both as blocking alongside §6, not as
backlog.** They were first written up as "neither is urgent — no corpus character uses a
null-LA race", which was the wrong test. The corpus is 54 characters built by someone who
knows the tool's edges. A public user meets both of these in the first five minutes: they open
the race picker and are offered companion and monster races with nothing marking them, and
they look for languages on a character sheet and find the field does not exist. Dragon
Disciple — core SRD, not third-party — is simply unenterable. "No corpus character hits it" is
an argument about *regression risk*, not about whether the feature is finished.

### Languages — modelled, never assignable, never displayed

The whole chain exists except every end of it:

| piece | state |
|---|---|
| `CharacterState.Languages` (`HashSet<string>`) | exists |
| `GrantLanguage` permabuff | exists, writes to it |
| `HasLanguage` prerequisite | exists, reads it |
| content that grants a language | **exactly one** — `race:hellbred` → `infernal`, added 2026-07-28 |
| race "Automatic / Bonus Languages" | **no schema field**; `extract-race` is told to treat it as "flavor only" |
| Int-based bonus-language selection | **does not exist** |
| `CharacterSheet`, API DTOs, Blazor UI | **absent entirely** — a character's languages are never shown or chosen |
| PCG import | **`LANGUAGE:` lines are not parsed at all** |

Consequences, in order of how much they bite:

1. `class:dragon_disciple`'s `HasLanguage{draconic}` prerequisite is satisfiable by **nothing**.
   It is correctly implemented and unit-tested (§2) and gates a class no character can enter.
2. The three Fiendish Codex II prestige classes' "Language: Infernal" requirement stays
   dropped, because restoring it would make them hellbred-only rather than merely gated.
3. Languages never reach the sheet, so even the hellbred grant is invisible to a user.

**The cheapest first move is the PCG importer.** Every `.pcg` in the corpus already carries a
full pipe-delimited language list (`Archfiend Lilly.pcg` has
`LANGUAGE:Abyssal|LANGUAGE:Auran|…|LANGUAGE:Draconic|LANGUAGE:Infernal|…`, 11 of them), and the
importer drops all of it. Parsing that line is small, immediately makes Dragon Disciple's
prerequisite real for imported characters, and gives the sheet something to display — without
needing the race-language schema or the Int-based selection UI first. Do that before the
authoring side.

### Skills — no total is computed anywhere, so the sheet shows bare ranks

Three layered gaps, of which the synergy item in §1 is only the top one. Found 2026-07-28
while checking that item:

1. **Synergies are modelled and never consumed.** `SkillDefinition.Synergies` /
   `SkillSynergy` exist and 9 skills carry 12 entries (`bluff` → sleight of hand, diplomacy,
   intimidate; `tumble` → balance, jump; and so on). `Skill.cs:15` and `Skill.cs:18` are the
   **only** references in the entire solution — no engine code reads them.
2. **`SkillBonuses` is write-only.** `GrantSkillBonus` writes it, `CharacterSheet` carries it,
   and *nothing reads it* — not the UI, not the API, not prerequisites (`MinSkillRanks`
   correctly keys off ranks). So every racial and class skill bonus in every pack currently
   affects nothing observable.
3. **There is no skill total.** No `SkillTotal`, no summing of ranks + ability modifier +
   bonuses, anywhere in engine, sheet, API or UI. `SheetView.razor:150-167` renders a two
   column table of skill name and rank count. The number a player actually rolls — the total
   modifier — does not exist in this codebase.

(3) is the real defect and it subsumes the other two: implementing synergies alone would just
add invisible numbers to an invisible dictionary. The fix is one tail pass plus a display
change:

- Compute totals after ranks are final — `ranks + abilityMod(skill.KeyAbility) + SkillBonuses`,
  with armor check penalty and untrained-use rules as follow-ups.
- Apply synergies in the same pass, keyed off **final whole ranks ≥ 5** (`SkillHalfRanks / 2`).
  Must be a tail pass, not per-tick, for the same reason `FinalizeRacialSpellcasting` is:
  a character crossing 5 ranks at 7th level would otherwise get an order-dependent answer.
  Synergies key off *ranks*, not totals, so they do not chain; and multiple sources stack
  (Diplomacy legitimately takes three separate +2s from bluff, sense motive and knowledge
  nobility).
- Surface it: add the total to `CharacterSheet`, render ranks / ability / misc / total in the
  sheet table, and expose it on the API.

This is squarely a going-public blocker: skill modifiers are among the most-used numbers on a
character sheet, and the tool currently cannot state one.

### Smaller write-only state (same pattern, lower stakes)

Found 2026-07-28 by sweeping every `Permabuff`/`Prerequisite` subclass for content usage,
every `CharacterState` property for readers, and every `CharacterSheet` field for a UI or API
consumer. Two real hits beyond the skills gap above:

- **`Capabilities` is write-only.** `GrantCapability` writes it, `CharacterState` and
  `CharacterSheet` carry it, and **nothing reads it anywhere** — no engine logic, no
  prerequisite, no UI, no API, no test. Content grants 17: the druid's whole wild shape matrix
  (`wild_shape:{animal,plant,elemental}:{tiny…huge}`, 14 entries) and three
  `blood_witch:*` sacrifice capabilities. Mitigated in practice — Wild Shape is *also* a
  granted ability with a `wild_shape_uses_per_day` counter, so the feature shows on the sheet;
  what is missing is which **forms** a druid can actually assume. Either surface it or drop the
  primitive; carrying it half-connected is the worst of both.
- **`SLA.SaveDC` is never displayed.** `SheetView.razor:224` renders spell-like abilities as
  name plus uses/day only, so a stored save DC never reaches the player. One-line display fix.

Checked and **not** problems, recorded so the sweep is not repeated: `AddHitDie`, `AddBAB`,
`AddSaves`, `GrantSkillPoints`, `AddClassSkills` are unauthored in content by design —
`Driver.cs:51-59` constructs them from HDDriver progressions. `UpdateSpellcasting`,
`GrantArmorProfile`, `GrantWeaponLine` are likewise engine-internal. `ModifyLeadershipScore`
is unit-tested but unused by content, which is correct: 3.5 leadership modifiers are
DM-assigned. `NaturalAttack.IsPrimary` is read by the sheet. Every `Prerequisite` subclass is
now used by at least one pack.

### Level Adjustment — the builder offers every race, playable or not

`RaceDefinition.LevelAdjustment` became `int?` on 2026-07-28 so that "playable at no cost" (0)
is distinguishable from "never priced as a PC race" (null). Nothing consumes the distinction
yet:

- `BuilderView.razor:1182` populates the race picker from `registry.GetAllRaces()`, **unfiltered**.
  Every monster race, every companion race and every null-LA creature is offered as a PC choice,
  with nothing marking them as unsupported. Null LA is now exactly the signal that could drive a
  filter or an "unofficial as a PC race" badge — that was the point of making it nullable.
- `SheetView.razor:40` shows `(LA +N)` only when `> 0`, so null and 0 render identically. Fine
  arithmetically (null contributes 0 to ECL), but the sheet cannot say "this race has no
  sanctioned LA", which is the one place a player would want to know.

The builder listing is the user-visible half and the one that matters for going public: it is
a discoverability trap on the very first step of character creation. It is also exactly what
the `audit-agent-api` skill exists to catch, so re-run that once a filter or badge exists.

---

## 9. SRD equipment extraction — 16% -> 53% on 2026-07-29

Measured by diffing item anchors in the SRD mirror's item pages against every
`equipment/*.json` in all packs. At the start of the day **~145 of 888 SRD item entries were
present (16%)**, and five schema categories were entirely empty — `gear`, `rod`, `staff`,
`ammunition` plus `potion`/`scroll`/`wand` — so a character could buy a longsword but not a
backpack, a rope, a quiver of arrows or a staff. **Now 471 of 888 (53%)**, 819 items total.

| page | entries | covered | missing |
|---|---|---|---|
| `magicItemsWI.html` — wondrous | 199 | 181 | 18 |
| `magicItemsAW.html` — magic armor & weapons | 136 | 50 | **86** |
| `magicItemsICA.html` — intelligent/cursed/artifacts | 89 | 0 | **89** |
| `goodsAndServices.html` — mundane goods | 97 | 73 | 24 (rules anchors) |
| `weapons.html` — mundane weapons | 76 | 33 | 43 (rules anchors) |
| `epicMagicItemsOther.html` | 67 | 17 | 50 |
| `magicItemsPRR.html` — potions/rings/rods | 63 | 58 | 5 |
| `epicMagicItems.html` | 57 | 18 | 39 |
| `armor.html` — mundane armor | 40 | 12 | 28 (rules anchors) |
| `magicItemsSSW.html` — staffs/scrolls/wands | 28 | 21 | 7 |
| `epicArtifacts.html` — epic artifacts | 28 | 3 | 25 |
| `specialMaterials.html` | 8 | 5 | 3 |

The residual "missing" on the three mundane pages is almost entirely rules-section anchors
(`armor-check-penalty`, `weapon-qualities`), not items — every base weapon, armor and shield
is present.

**The table above is the morning measurement and now understates three rows.** The later
`pcgen_srd` retirement pass added 143 items off `weapons.html`, `magicItemsAW.html`,
`epicMagicItems.html` and `epicArtifacts.html` — in particular it closed the whole
"magic armor & weapons" row's Specific Armors / Shields / Weapons sections, which were the
bulk of that page's **86** missing entries. Exact totals: `srd_core` now holds **844**
equipment items (was 701). The anchor-diff has not been re-run, so the percentages are stale
rather than wrong; re-run it before quoting a new headline number.

### Done 2026-07-29

- **Mundane goods & services** — 152 items from tables 2–8 of `goodsAndServices.html` into
  `goods_and_services.json`, populating the empty `gear` category.
  Regression: `SrdGoodsAndServices_LoadIntoTheGearCategory`.
- **Ammunition and the mundane weapon gaps** — `ammunition_and_gaps.json`. The `ammunition`
  category held nothing at all, so no bow in the game was usable. Added arrows, crossbow
  bolts, repeating-crossbow bolts, sling bullets, the net, spiked shields, and the armor
  table's three "Extras" rows (armor spikes, shield spikes, locked gauntlet).
  Regression: `SrdAmmunition_AndTheRemainingMundaneWeaponGaps_Load`.
- **Rings, rods and staffs** — 100 items from `magicItemsPRR.html` / `magicItemsSSW.html`.
  `rod` and `staff` were empty categories; `ring` held only the protection ladder.
  Regression: `SrdRingsRodsAndStaffs_Load`.
- **Wondrous items** — 257 items from `magicItemsWI.html`, the largest batch.
  Regression: `SrdWondrousItems_Load`.
- **Arms and armor, retiring `pcgen_srd`** — 143 items across `weapons_srd.json` (49),
  `magic_armor_weapons.json` (54) and `epic_armor_weapons.json` (40), from `weapons.html`,
  `magicItemsAW.html`, `epicMagicItems.html` and `epicArtifacts.html`. Replaces the last
  PCGen-derived pack, which is now deleted. Regressions:
  `SrdArmsAndArmor_ReplaceTheRetiredPcgenPack` and
  `RetiredPcgenEquipmentNames_StillResolveForPcgImport`.

### Conventions settled while doing it — the remaining batches should follow these

- Prices are stored in **copper** (`priceCp`), so a 30,000 gp galley is 3,000,000 cp.
- `weightLbs` is an **integer**, so sub-pound items floor to 0. Pre-existing convention
  (`weapon:dart` is 1/2 lb and already stored 0), kept deliberately rather than widening the
  schema mid-extraction (user ruling 2026-07-29). Worth revisiting if encumbrance is ever
  computed — 33 of the gear items weigh under a pound and now read as weightless.
- **A tiered price clause becomes one item per tier**, not one item at the cheapest price:
  `Price 3,000 gp (lesser), 11,000 gp (normal), 24,500 gp (greater)` produces three rods.
- **The enhancement bonus is part of an item's identity, not a suffix to strip.** An early
  dedup pass normalised `+N` away and silently dropped Bracers of Armor +2/+4/+6/+7 as
  "already present" — the packs in fact carried only +1/+3/+5/+8. Any name-matching against
  existing content must keep the bonus as a token.
- **Items priced by a variant table** (bag of holding, carpet of flying, crystal ball,
  necklace of fireballs, ioun stones) have no `Price N gp` clause; read the column headed
  "Market Price". Their descriptions also embed `table-*` anchors, so slicing an entry at the
  next anchor truncates it before its price — slice at the next *item* anchor.
- Sub-priced rows are qualified with their parent row (`Lock, amazing`, not `Amazing`).
- Slot inference is order-sensitive: "Bracers of Armor" is a **wrists** item, and a naive
  keyword scan calls it torso because the name contains "armor".
- **Services are deliberately excluded** (`goodsAndServices.html` table 9: hirelings,
  messengers, road tolls). Priced services, not ownable equipment, with no schema
  representation. Barding's x2/x4 multipliers are likewise a rule, not an item.
- **Potions, scrolls and wands are deliberately not extracted.** The SRD defines them
  generatively ("a wand of any 4th-level or lower spell"), priced by spell and caster level
  rather than enumerated. Emitting them means generating one item per spell per caster level —
  a content-design decision, not a transcription. Decide the shape before extracting.

### Known gaps in what was extracted

- **Figurines of Wondrous Power** (9 variants) and **Feather Token** (6 variants) are not
  extracted. Both are composite entries whose variants are inline sub-sections with their own
  Price clauses but no usable per-variant anchor boundary. Deferred rather than guessed at.
- Wondrous descriptions retain the trailing `School; CL Nth; Craft ...` clause. It is SRD text
  and harmless, but a display layer may want it split into fields.

**Pre-existing content bugs found in passing — ~~not fixed~~ resolved by deleting the pack:**
`pcgen_srd/equipment/srd_equipment_epic.json` had doubled-prefix names —
`"Rod of Rod (Besiegement)"`, `"Rod of Rod (Fortification)"`, `"Staff of Staff (Fiery Power)"`,
`"Staff of Staff (Nature's Fury)"`. `srd_equipment.json` had garbled composite-bow names —
`"Longbow (Composite) Longbow STR"`, `"Shortbow (Composite +0) Shortbow STR0"`. Same class of
defect as the garbled LST names fixed in `34a0f3a`. All eight are gone with the pack; the
clean PCGen spellings (`"Rod (Besiegement)"`, `"Longbow (Composite)"`, …) are now
`EquipmentOverrides` entries pointing at the `srd_core` items.

**Remaining order**, by how often a real character touches them: ~~magic armour & weapons (86 —
the enchantment lines, `+1` through `+5` and the named properties)~~ *(the specific-item half was
done in the `pcgen_srd` retirement pass; what is left on that page is the **generic enhancement
and special-ability lines** — `+1` … `+5` armor/shield/weapon and named properties like `flaming`,
`keen`, `holy`. Those are modifiers applied to a base item, not enumerable items, so they need a
schema decision first — the same open question as potions/scrolls/wands above)* →
intelligent/cursed items and artifacts (89) → the remaining epic items (`epicMagicItemsOther.html`,
and the non-arms artifacts in `epicArtifacts.html`) last.
