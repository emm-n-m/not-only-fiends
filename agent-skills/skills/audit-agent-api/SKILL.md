---
name: audit-agent-api
description: Drive the REST API end-to-end as an agent would, building a character from scratch to find discoverability gaps, silent-accept validation holes, and oversized payloads. Use after API or engine changes, or before publishing.
---

# Audit the Agent-Facing API

The project's premise is that an AI agent can drive character creation through `/api/*`
unaided. This skill tests that claim the only way it can honestly be tested: **build a real
character using nothing but the API**, and record every point of friction.

A 2026-07 run of this found an 8.3 MB `next-step` response, a 400 with an empty body, five
silently-accepted illegal inputs, and — because it forced a real build — the Eldritch Knight
save-progression bug. The 2026-08 corpus rebuild test (55 agents, see
[pcg-rebuild-regression](../pcg-rebuild-regression/SKILL.md)) added the findings now listed
under "Agent-facing API issues" in `KNOWN_ISSUES.md` — read that section first and audit for
*new* problems rather than rediscovering those; the verified build mechanics live in
[api-build-character](../api-build-character/SKILL.md).

## Ground rules

1. **Use the API only.** Do not read `AgentApiService.cs` or the content JSON to answer a
   question the API should answer. When you *have* to fall back to source, that is a finding —
   record what you needed and why the API could not tell you.
2. **Build something demanding.** A single-class level-1 fighter exercises nothing. Use a
   multiclass with a prestige class and spellcasting (e.g. Fighter 1 / Wizard 5 / Eldritch
   Knight 2) so you hit feats, skills, spell selection, prerequisites, and the spell-advancement
   choice.
3. **Verify the arithmetic by hand.** Recompute HP, BAB and saves from the class tables and
   compare. This is what catches content bugs, and it is the step most likely to be skipped.
4. **Measure payloads.** `wc -c` every response. Context is the scarce resource for an agent;
   a correct 8 MB response is a broken response.

## Setup

```bash
curl -s -m 3 http://localhost:5000/api/health   # already running?
dotnet run --project NotOnlyFiendsFeed --no-build   # if not (background it)
```

To stop it cleanly, kill the listener rather than pattern-matching on `dotnet`:
`ss -ltnp | grep 5000 | grep -oP 'pid=\K[0-9]+' | xargs -r kill` — a broad `pkill -f` will
take out the calling shell.

**Characters persist to a real user directory** (`CHARACTERS_PATH` in `.env`, often a
cloud-synced folder). Prefix test characters with `API Test -` and `DELETE` them at the end.

## Walkthrough

1. **Discovery** — `/api/health`, `/api/rules`, `/api/content/catalog`, `/openapi/v1.json`.
   Note sizes. Can an agent learn the ID conventions from these alone? (As of 2026-08 all ids
   are uniformly prefixed — `race:human`, `class:wizard` — and the catalog is ~840KB, so an
   agent must be able to learn the conventions without dumping it.)
2. **Create** — `POST /api/characters`. Enum values come from the OpenAPI schema
   (`alignment` is `ng`, not `NeutralGood`).
3. **Per level**, the loop an agent should follow:
   - `GET /api/characters/{id}/next-step` — cheap survey of legal drivers, with choice *counts*
   - `GET …/next-step?driverIds=a,b&optionDetail=full` — options for the shortlist only
   - `POST …/simulate` — dry run, **inspect `warnings`**
   - `POST …/ticks` — commit only if clean
4. **Finish** — `GET …/sheet`, hand-verify every number, then `DELETE` the character.

## Probe for silent accepts

The highest-value part. Every one of these should produce a warning; each was silently
accepted at some point. Run them through `simulate` (non-destructive) and diff the warnings:

```bash
# skills
{"driverId":"class:fighter","choices":{"skillAllocations":[{"skillId":"not_a_skill","halfRanks":8}]}}
{"driverId":"class:fighter","choices":{"skillAllocations":[{"skillId":"climb","halfRanks":200}]}}
# feats
{"driverId":"class:fighter","choices":{"featIds":["dodge","dodge"]}}
{"driverId":"class:fighter","choices":{"featIds":["cleave"]}}                    # prereq unmet
{"driverId":"class:fighter","choices":{"featIds":["weapon_proficiency_martial"]}} # grant-only
{"driverId":"class:fighter","choices":{"featIds":["epic_toughness"]}}            # epic at level 1
# spells
{"driverId":"class:wizard","choices":{"spellSelections":[{"classId":"class:wizard","spellLevel":1,"spellId":"fake_spell"}]}}
{"driverId":"class:wizard","choices":{"spellSelections":[{"classId":"class:wizard","spellLevel":1,"spellId":"cure_light_wounds"}]}}
# sorcerer knowing more than its spells-known table allows (a wizard's spellbook is unbounded — not a bug)
```

Also check the error surface: a bad enum, a malformed body, and an unknown ID should each
return an `ErrorResponse` with a `code` and an actionable `message` — never an empty body.

## Judging what you find

- **Warnings vs. rejection.** The engine is deliberately permissive: it applies the choice and
  warns rather than refusing. That is fine, but it means a caller *must* read `warnings`, and
  warnings are whole-replay rather than per-tick — so diff before/after to attribute them.
- **Payload size is a correctness issue.** Note the shape too: an option list repeated once per
  candidate driver is the failure mode to watch for.
- **Distinguish gaps that are content from gaps that are engine.** "Prestige class offers itself
  to a level-1 character" is content (no prerequisites authored); "unknown skill silently
  consumes points" is engine. They route to different fixes — see
  [verify-content](../verify-content/SKILL.md) for the content side.

## Output

A findings list ordered by severity, each with the exact request and response that shows it,
separating **blocking** (an agent cannot proceed or is silently misled) from **friction**
(costs time but is recoverable). Engine-side findings become tests in
[RulesAccuracyTests.cs](../../../NotOnlyFiendsStudio.Tests/RulesAccuracyTests.cs) or
[Api/AgentApiServiceTests.cs](../../../NotOnlyFiendsStudio.Tests/Api/AgentApiServiceTests.cs);
Public content-side findings go to `CONTENT_GAPS.md`; third-party/campaign findings go to the
private materials repo's `CONTENT_GAPS.md`.
