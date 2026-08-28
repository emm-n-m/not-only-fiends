---
name: api-build-character
description: Build a character end-to-end through the REST API as a player or external agent would — the verified request shapes, the per-level tick loop, and the traps that silently eat choices. Use to reproduce a bug, verify an engine change with a real build, or brief an independent worker that must drive the API.
---

# Build a Character Through the API

The verified protocol from the 2026-08 corpus rebuild test (55 agents, 13 exact baseline
matches). [audit-agent-api](../audit-agent-api/SKILL.md) is for *finding* API problems; this
skill is for *getting a build done*. Everything below was confirmed against a running instance
— when reality disagrees, that's a new finding for `KNOWN_ISSUES.md`.

## Setup

`curl -s -m 3 http://localhost:5000/api/health` — or launch your own instance on a spare port
per [run-app](../run-app/SKILL.md). Health lists loaded packs; private packs need `.env`.

**Characters persist to the user's real store.** Name test characters `API Test - <name>` and
DELETE them when done, even on failure. Sweep afterward: `GET /api/characters` must show no
`API Test -` leftovers.

## Verified protocol

**IDs are prefixed everywhere**: `race:human`, `class:cleric`, `racial_hd:outsider`,
`feat:iron_will`, `skill:concentration`, `spell:bless`, `domain:war`, `template:...`,
`wondrous:cloak_of_resistance_1`. Alignment is a lowercase enum: `lg`, `ng`, `ce`… and True
Neutral is `"n"` (not `"tn"` — a wrong value 400s with an empty body).

**Create** — `POST /api/characters` with a full Character body:
`{"name","alignment","deity","raceId","templateIds":[…],"baseAbilityScores":{str..cha},"bonusLanguageIds":[…]}`
→ response carries `.id`. Set bonus languages HERE: there is no good post-creation path.
Templates apply at creation via `templateIds`. A template earned mid-career (lichdom) takes a
1-based entry in `templateAcquisitionHD` (`{"template:lich": 12}`): it applies at the start of
that HD, forward only, and an evaluation truncated below it never sees the template. Missing
entry = creation.

**Level loop** — once per HD (racial HD tick like classes via `racial_hd:*` drivers; monsters
usually take racial HD first):

1. `GET …/next-step` — legal drivers + pending choice counts
2. `GET …/next-step?driverIds=class:X&optionDetail=full` — option lists (with ids) for the
   shortlisted driver only; this is also the only sane way to find feat ids
3. `POST …/simulate` with `{"driverId","choices":{…}}` — dry run; warnings are whole-replay,
   so diff against warnings already accepted
4. `POST …/ticks` — commit

**TickChoices**: `hitPointsRolled` (explicit roll — makes builds reproducible), `featIds`,
`skillAllocations` (`[{"skillId","halfRanks"}]` — halfRanks = 2× ranks, cross-class ranks cost
double), `spellSelections` (`[{"classId","spellLevel","spellId"}]`), `abilityIncrease`
(`"str"…"cha"`, due every 4 HD), `classFeatureChoices` (feature id → array of option ids).
Cleric domains go in `classFeatureChoices` under the `"domains"` key. The familiar pick
surfaces only in `currentPendingChoices` after leveling — resolve it by adding
`classFeatureChoices: {"class_feature:familiar_options": ["race:companion_…"]}` to an existing
tick via a full-character PUT (key copied exactly from the pending choice's `featureType`).

**Parametrized feats** (Skill Focus, Spell Focus, Weapon Focus, Spell Mastery…): the selection
is encoded into the feat id itself — there is no separate selection field. Append `_` plus the
selection to the base id. The suffix vocabulary differs by kind: skills use the bare id
(`feat:skill_focus_concentration` — the form prestige prerequisites gate on; the full-id
dialect `feat:skill_focus_skill:concentration` also replays), schools use the eight SRD school
names (`feat:spell_focus_conjuration`), weapons and spells use the full content id
(`feat:weapon_focus_weapon:longsword`, `feat:spell_mastery_spell:fireball` — the full weapon id
is what links the bonus to the equipped weapon's attack line). Feat listings mark these feats
with `selectionRequired`, and their `selection` object carries the exact `idPattern`, a `hint`,
and the legal values (`options` inline or `optionsEndpoint`). Submitting the base id without a
suffix replays with a warning and grants nothing — the feat has no target — and a missing
suffix also silently disqualifies prestige classes that gate on the variant ids
(`feat:skill_focus_spellcraft`).

**Equipment** — after leveling: `GET /api/characters/{id}` → take `.character`, append to
`.equipment`: `{"itemId":"<display name>","contentId":"<catalog id>","slot":"<slot from
catalog entry>","quantity":1}` → `PUT` the full body back (ticks are preserved). Equipment
changes saves/HP/abilities on the sheet, so a baseline comparison without the gear will be off
by exactly the gear.

**Finish** — `GET …/sheet`, extract what you need with a filter, then `DELETE` (204).

## Payload discipline

Never GET `/api/content/catalog` (~840KB) or dump full lists into context. `?q=` filters work
on `spells` and `equipment` ONLY — `skills` and `languages` ignore it and return everything
(KNOWN_ISSUES). Pipe every curl through `python3 -c` extraction.

## Traps (all in KNOWN_ISSUES.md — re-verify before relying on a fix)

- **Unknown choice keys are silently ignored** with HTTP 200 and no warning (`domainSelections`
  no-ops; wrong `classFeatureChoices` keys get only a soft warning). After any
  novel choice, confirm the effect landed on the sheet — 200 ≠ applied.
- **Ineligible ≠ nonexistent is invisible**: `next-step` omits prerequisite-gated drivers
  entirely, indistinguishable from missing content. Before concluding a class doesn't exist,
  check `/api/content/drivers/{id}` (it serves authored prerequisites) — the driver catalog is
  the ground truth, not `next-step`'s silence.
- **Invalid skill ids half-apply**: soft "unknown skill" warning, but ranks land on the sheet
  under the bogus id.
- Underspending skill points never warns; only overspending does. Unspent points accrue per
  driver and can be spent in a later tick of the same driver.
- `GET /api/content/classes/...` does not exist — drivers live at `/api/content/drivers/{id}`.
