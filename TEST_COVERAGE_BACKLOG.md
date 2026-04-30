# Test Coverage Backlog

Concrete backlog and test targets for moving the repository toward full feature coverage.

## Studio Replay Correctness

- Add tests for feat slot consumption precedence in `ReplayStudio`: standard slot use, bonus slot fallback, and over-selection behavior.
- Add tests for ability increases at valid and invalid HDs.
- Add tests for `upToHD` replay with templates, permanent events, domains, and spellcasting active at partial progression.
- Add tests for warning generation when driver prerequisites fail across multiclass and prestige-class entry paths.
- Add tests for racial HD mixed with class HD and template scaling in the same build.
- Add tests for equipment application semantics in `ReplayStudio`: stacked permabuffs and post-tick-only effects.

## Permabuff Coverage

- Add direct unit tests for every `Permabuff` subtype in `NotOnlyFiendsStudio/Models/Permabuff.cs`.
- Replace the stale `GrantBonusFeat` test in `NotOnlyFiendsStudio.Tests/FeatTests.cs` with assertions for actual cascade behavior.
- Add tests for `ModifyAttribute` and `SetAttribute` against each supported `AttributeTarget`.
- Add tests for `AdvanceSpellcasting` when there are zero, one, and multiple eligible spellcasting classes.
- Add tests for `UpdateSpellcasting` domain bonus slot recalculation after domains already exist.
- Add tests for `GrantDomainSelection` accumulation and downstream consumption.

## Feat System

- Add tests for repeatable feats and selected-feat variants using `SelectionRequired` and `Tags`.
- Add tests for `HasFeatOfType`, `HasFeatWithTag`, and `HasFeatSelections` prerequisites.
- Add tests for `GetAvailableFeats` filtering with repeatable feats already taken, typed restrictions, and selectable variants.
- Add tests for invalid feat IDs in tick choices and verify warning behavior.
- Add tests for feats granted by classes, templates, and races versus feats explicitly chosen by the player.

## Spellcasting And Domains

- Add tests for prepared versus spontaneous spellcasting state transitions.
- Add tests for spell selection warnings: unknown class, invalid level, above-max level, and blank selections.
- Add tests for domain selection warnings on unknown domains and duplicate domain picks.
- Add tests for prestige-class spell advancement selection workflows once `advance_spellcasting` choices are implemented.
- Add tests for domain spell list availability via `GetSpellsForList` and class spell list availability via `GetSpellsForClass`.
- Add scenario tests for multiclass divine plus arcane characters with domains and prestige advancement.

## Formula And Model Behavior

- Add tests for formula parsing edge cases, invalid syntax, divide and truncation behavior, nested `min` and `max`, and class and caster lookups against live `CharacterState`.
- Add tests for `AbilityScoreSet`, `SaveSet`, `SpellcastingState`, and `FeatSlot` helper behavior where not already covered.
- Add tests for enum-backed model serialization round-trips where behavior depends on JSON options.

## Content Registry And Validation

- Add tests for `LoadJsonForDirectory` with every registered content type in `ContentRegistry`.
- Add tests for unknown directory handling in `LoadJsonForDirectory`.
- Add tests for spell validation failures: unknown spell list and negative spell level.
- Add tests for selectable feat variant validation in `IsSelectableFeatVariant`.
- Add tests for `Warn` and `Error` conflict modes across domains, spells, races, templates, and drivers, not just feats.
- Add tests for multiple-root override behavior using real content types other than feats.

## Pack Loading

- Add tests for `PackConfig` override precedence over manifest conflict settings in `PackLoader`.
- Add tests for disabled dependency chains and resulting failure modes.
- Add tests for manifest-only load-order resolution matching filesystem-discovered order.
- Add tests for duplicate pack IDs and malformed `pack.json`.
- Add smoke tests that load each shipped pack and validate the resulting registry.

## Golden Scenarios

- Expand `NotOnlyFiendsStudio.Tests/PcGen/PcgReconstructionTests.cs` into a fixed regression suite with expected outputs for a curated set of characters.
- Add one golden build each for a straight martial, straight divine caster with domains, straight arcane caster, multiclass martial and skill build, prestige spell advancement build, racial HD creature, templated creature, and epic progression build.

## UI Coverage

- Add bUnit tests for `NotOnlyFiendsFeed/Components/Pages/BuilderView.razor`: initial load, add and remove HD, template add and remove, feat add, evaluate, and save and open status handling.
- Add bUnit tests for `NotOnlyFiendsFeed/Components/Pages/SheetView.razor`: initial character load, slider-driven re-evaluation, spellcasting display, and warning display.
- Add backlog items for missing UI features: skill allocation editing, spell selection editing, domain selection editing, permanent events editing, equipment editing, and pack and config selection.

## Test Targets

- `ReplayStudio`: all branches in `Evaluate`, `ApplyTickChoices`, and `ApplyEquipment`.
- `Permabuff`: every subtype in `NotOnlyFiendsStudio/Models/Permabuff.cs`.
- `Prerequisite`: every subtype in `NotOnlyFiendsStudio/Models/Prerequisite.cs`, including negative and edge cases.
- `ContentRegistry`: each content type, each validation rule, and each conflict mode.
- `PackLoader`: discovery, ordering, filtering, dependency validation, and per-pack conflict behavior.
- `Formula`: parser success, parser failure, and live evaluation against real state.
- `PcGen`: parser correctness, mapping completeness, and reconstruction regression cases.
- `Blazor UI`: builder workflow, sheet workflow, and content loader workflow.
