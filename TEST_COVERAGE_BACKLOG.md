# Test Coverage Backlog

This file tracks active behavioral coverage gaps only. Completed test work and dated suite
snapshots belong in Git history, not in the live backlog.

## Current verification

- The suite contains more than 1,000 test cases in the configured checkout. The exact discovered
  count varies with external-data configuration and parameterization.
- No UI component test suite is checked in yet.
- PCGen and private-pack cases remain environment-gated when their paths are unavailable.
- No line/branch coverage report is checked in; this file tracks behavior, not a percentage.

## Active backlog

### UI

- [ ] Add bUnit coverage for `BuilderView`: initial load, HD/template mutations, feat changes,
  auto-evaluation, and save/open status handling.
- [ ] Add bUnit coverage for `SheetView`: initial load, level-slider re-evaluation, spellcasting,
  and warning display.

### API and editor workflows

- [ ] Add integration coverage for the UI-facing content loader and remaining character
  mutation/API error paths.
- [ ] Track editor workflows separately: skill allocation, spell and domain selection, permanent
  events, equipment, and pack/config selection.

## Ongoing targets

- `ReplayStudio`: branch and warning coverage in `Evaluate`, `ApplyTickChoices`, and
  `ApplyEquipment`.
- `Permabuff` and `Prerequisite`: every subtype, including negative and edge cases.
- `ContentRegistry` and `PackLoader`: every content type, validation rule, conflict mode,
  discovery path, ordering rule, and failure mode.
- `Formula`: parser success/failure and live evaluation against real state.
- `PcGen`: parser correctness, mapping completeness, and stable reconstruction regressions with
  external data enabled.
- Blazor UI: builder, sheet, content-loader, and API workflows.
