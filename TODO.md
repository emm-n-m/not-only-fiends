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
### Dangling domain spell references in the public packs — HIGH

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

---

## 8. Core features still half-built — **blocking for going public**

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
