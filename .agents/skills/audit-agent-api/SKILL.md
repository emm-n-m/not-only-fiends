---
name: audit-agent-api
description: Drive the NotOnlyFiends REST API end to end as an external agent, building a demanding character to find discoverability gaps, silent validation failures, arithmetic bugs, bad errors, and oversized payloads. Use after API or replay changes or before publishing.
---

# Audit the Agent-Facing API

Test the claim that an agent can build a legal character using only `/api/*`. Do not inspect
`AgentApiService.cs` or content JSON to answer questions the API should answer. Record any
source-code fallback as a discoverability finding.

## Safety and setup

1. Check `/api/health` on the supplied base URL.
2. If no URL is supplied and no instance is running, invoke the `run-app` skill to start an
   isolated instance on a spare port.
3. Determine where characters will persist before creating one. Use a unique name prefixed
   `API Test -`, record its returned ID, and delete exactly that character at the end.
4. Never stop an existing listener or use broad process-kill commands.

## End-to-end build

Build a multiclass spellcaster that enters a prestige class, such as Fighter 1 / Wizard 5 /
Eldritch Knight 2. Exercise feats, skill allocation, spell selection, prerequisites, and
prestige spell advancement.

1. Discover `/api/health`, `/api/rules`, `/api/content/catalog`, and `/openapi/v1.json`.
2. Save every response and measure its byte size.
3. Create the test character using only values discoverable from those responses.
4. For each HD:
   - request `next-step` for the cheap legal-options survey;
   - request full option details only for shortlisted drivers;
   - use `simulate` and inspect/diff `warnings`;
   - commit the tick only when the simulated outcome is understood.
5. Fetch the final sheet and independently recompute HP, BAB, saves, caster levels, feats, and
   skill ranks from API-visible rules and class tables.
6. Delete the exact test character and verify it is gone.

## Validation probes

Run malformed or illegal choices through `simulate`, never destructive mutation:

- unknown skill ID and excessive skill ranks;
- duplicate feat, unmet feat prerequisite, grant-only feat, epic feat at level 1;
- unknown spell and a spell from the wrong class list;
- spontaneous caster exceeding its spells-known allowance;
- malformed body, bad enum, and unknown content/character ID.

The engine may permissively apply a choice while warning. That is acceptable only when the
warning is unambiguous and discoverable. Error responses must contain a stable code and an
actionable message.

## Evaluation

Treat payload size as correctness: measure every response and identify repeated option trees.
Separate:

- **blocking**: the caller cannot proceed or is silently misled;
- **friction**: recoverable but unnecessarily expensive or obscure;
- **content defect**: a definition/table is wrong;
- **engine/API defect**: validation, replay, contract, or response behavior is wrong.

## Output

Report findings in severity order. Include the exact request, essential response excerpt,
payload size, expected behavior, and category. Add regression tests to
`NotOnlyFiendsStudio.Tests/Api/AgentApiServiceTests.cs` or rules tests when the user asks to
fix confirmed defects; content findings belong in `TODO.md` until separately approved.
