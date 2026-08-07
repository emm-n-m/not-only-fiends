# Test Coverage Backlog

Current status of the repository's test coverage and the concrete work still
needed to move toward full feature coverage.

## Current snapshot

- `dotnet test`: **1051 passed, 0 skipped, 0 failed, 1051 total**.
- Nothing skips any more: `.env` now supplies `CHARACTERS_PATH`,
  `EXTRA_PACKS_PATH` and `PCGEN_CHARACTERS_PATH`, so the PCGen and
  private-pack tests all run. Without those the same tests skip rather than
  fail, so a clean run on an unconfigured machine reports fewer tests.
- No UI component tests were found. No line/branch coverage report is checked
  in, so this document tracks behavioral coverage rather than a percentage.

## Covered since the previous backlog

The following original backlog targets now have direct tests and should not be
treated as active work:

- Replay: valid and invalid ability increases, partial `upToHD` replay,
  multiclass prerequisite warnings, racial HD, template scaling, permanent
  events, and post-tick equipment (including stacking).
- Feats and permabuffs: feat-slot precedence/enforcement, bonus slots,
  `GrantBonusFeat` cascades, supported attribute targets, repeatable/selected
  feat handling, available-feat prerequisite filtering, and the prerequisite
  helper families (`HasFeatOfType`, `HasFeatWithTag`, and related checks).
- Spellcasting and domains: prepared versus spontaneous state, unknown-class
  and above-max spell warnings, multiple-class `AdvanceSpellcasting` choices,
  unknown-domain warnings, domain accumulation, domain spell lists, and class
  spell lists.
- Formula/model behavior: arithmetic and truncation, unary operators,
  parentheses, nested `min`/`max`, live state lookups, invalid syntax, and core
  model helpers.
- Content and packs: content validation errors, multiple-root overrides,
  conflict modes, pack discovery/order/dependencies, cycles, missing
  dependencies, disabled packs, per-pack conflict behavior, and manifest
  serialization.
- PCGen: parser/converter coverage, a fixed High Priestess reconstruction, and
  buildability/gap-analysis entry points. The external-data cases remain
  environment-gated.

## Active backlog

### Test-suite maintenance

- [x] Resolve the spell catalog count failure in
  `SpellContentTests.ContentRegistry_LoadsSpells`.
- [x] Run the skipped PCGen/private-pack tests with configured external data
  and capture the resulting baseline or failures. All pass with the external
  paths configured; the PCG import baseline lives in
  `{EXTRA_PACKS_PATH}/test-reports/pcg_import_report.json`.

### Studio replay and permabuffs

- [x] Add explicit tests for feat-slot consumption precedence when standard and
  restricted bonus slots compete, including over-selection and invalid-slot
  cases not covered by current enforcement tests.
- [x] Add direct tests for the remaining `Permabuff` subtypes and edge cases,
  including immunity/DR replacement behavior, level adjustment, and resistance stacking.
- [x] Add `UpdateSpellcasting` coverage for domain bonus-slot recalculation
  after domains already exist.
- [x] Add mixed racial-HD/class-HD/template scenarios that assert the complete
  final state, not only individual template or effective-level behavior.

### Feats, spells, and domains

- [x] Add invalid feat-ID tick-choice tests and assert warning text/behavior.
- [x] Add explicit tests for feats granted by races, templates, and classes,
  distinguished from feats selected by the player.
- [x] Cover blank spell selections and invalid spell levels in an end-to-end
  warning test. Unknown class and spell-list paths are covered separately.
- [x] Cover duplicate domain selections.
- [x] Cover the full multi-domain downstream spell-slot recalculation path.
- [x] Add a multiclass divine-plus-arcane scenario combining domains and
  prestige spell advancement.

### Content registry and pack loading

- [x] Test `LoadJsonForDirectory` for every registered content type and for an
  unknown directory.
- [x] Add spell validation tests for unknown spell lists and negative spell
  levels, plus selectable-feat-variant validation.
- [x] Exercise `Warn` and `Error` conflicts across representative non-feat
  content types (domains, spells, races, templates, and drivers).
- [x] Test pack-config conflict overrides, disabled dependency chains,
  manifest-only load-order resolution, duplicate pack IDs, malformed
  `pack.json`, and loading every shipped pack as a smoke test.

### Golden scenarios and PCGen

- [x] Expand `PcgReconstructionTests` into a fixed regression corpus with
  expected outputs rather than only parser/buildability checks. Added as
  `PcgGoldenBuildTests`, which states the 3.5e arithmetic (BAB and save
  progression, level adjustment, caster level, epic bonuses, hit points) so a
  wrong value fails against the SRD rather than against yesterday's snapshot —
  the gap `PcgImportRegression` cannot close on its own.
- [x] Add golden builds for straight martial, straight divine caster with
  domains, straight arcane caster, multiclass skill build, prestige
  spell-advancement build, racial-HD creature, templated creature, and epic
  progression. One fixed `.pcg` per archetype: Fighter 7; Cleric 6 with
  domains; Drow Wizard 5; Drow Rogue 7/Assassin 10; Cleric 7/Thaumaturgist 2;
  Nymph (6 fey HD)/Druid 6; Lich Bard 13; Wizard 7/Loremaster 10/Archmage
  5/Cosmic Descryer 10 at HD 32.
- [x] Add explicit assertions for reconstruction warnings, dropped content,
  and intentional unsupported PCGen features — `PcgReconstructionFidelityTests`
  covers corpus-wide mapping completeness, hit-point-roll preservation,
  discarded PCGen temporary modifiers, filtered internal templates, rolls
  outside the driver die, and external companion file references.

The remaining source-data notes are recorded in
[KNOWN_ISSUES.md](KNOWN_ISSUES.md): War-domain deity/favored-weapon mappings,
the Nymph Archdruid's over-tier tiger, and two spells whose mirror lacks a
standalone stat block. The former replay/import/content defects are covered by
focused regressions.

### UI and API coverage

- [ ] Add bUnit tests for `BuilderView`: initial load, HD/template mutations,
  feat changes, auto-evaluation, and save/open status handling.
- [ ] Add bUnit tests for `SheetView`: initial load, slider re-evaluation,
  spellcasting display, and warning display.
- [ ] Add integration tests for the UI-facing content loader and the remaining
  character mutation/API error paths.
- [ ] Track missing editor workflows separately: skill allocation, spell and
  domain selection, permanent events, equipment, and pack/config selection.

## Ongoing test targets

- `ReplayStudio`: branch and warning coverage in `Evaluate`,
  `ApplyTickChoices`, and `ApplyEquipment`.
- `Permabuff` and `Prerequisite`: every subtype, including negative and edge
  cases.
- `ContentRegistry` and `PackLoader`: every content type, validation rule,
  conflict mode, discovery path, ordering rule, and failure mode.
- `Formula`: parser success/failure and live evaluation against real state.
- `PcGen`: parser correctness, mapping completeness, and stable reconstruction
  regressions with external data enabled.
- `Blazor UI`: builder, sheet, content-loader, and API workflows.
