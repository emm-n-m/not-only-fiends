# Handoff — acquired templates apply at the HD they are acquired, not at creation

A self-contained brief for the session that implements this. Everything needed to start is here;
the background analysis is in [KNOWN_ISSUES.md](KNOWN_ISSUES.md) §"An acquired template applies
from creation, so it pays for levels taken before it".

Status: **not started.** Written 2026-08-11.

---

## 1. The principle this serves

A character is **one authoritative sheet** whose timeline can be evaluated at any HD. That is the
engine's reason to exist: PCGen needs a separate saved sheet per level, and this needs one record
that answers "what was she at 8th?" and "what is she now?" from the same inputs.

So anything that happens mid-career must be modelled **at the HD it happens, applying forward
only**. Today every template is applied before the first tick, so a template acquired at 12th
level silently rewrites levels 1–11.

**Import fidelity is not the goal — correct modelling is.** Where reproducing a `.pcg` and
modelling the character truthfully disagree, the timeline wins. Do not add fudges to keep an
import matching.

## 2. The bug, with the arithmetic that proves it

`Lich Recruiter.pcg` — human bard 13, Int 17, all three level-up increases spent on Cha. The
builder says **"Decisions owed: Skill ranks — 14 unspent"**.

Lichdom requires a caster level of 11 to make the phylactery, so she reached 11th as a living
human and became a lich after. The template's +2 Int must feed forward from that HD. Applied at
creation instead, it buys skill points at every level she lived through as a human:

| Int used per level | 1st | 2nd–11th | 12th–13th | total |
|:--|--:|--:|--:|--:|
| applied at creation (today) | 19 → +4 → 44 | 12 each | 12 each | **176** |
| acquired at 12th (correct) | 17 → +3 → 40 | 10 each | 19 → +4 → 11 each | **162** |
| PCGen's own per-level record | 40 | 10 each | 11 each | **162** |

She spent 162. The 14 is `4 (first level ×4) + 10 (levels 2–11)` — exactly the levels before the
Int increase. Read PCGen's figures straight out of the file to confirm:

```bash
grep -oE "SKILLSGAINED:[0-9]+" "$PCGEN_CHARACTERS_PATH/Lich Recruiter.pcg" | cut -d: -f2 | paste -sd+ | bc
```

## 3. The model to build

### 3.1 Nothing reaches backwards in time

At HD 8 she is a **living human bard** — Int 17, d6 Hit Dice, no undead traits — and evaluating
the timeline at HD 8 must say exactly that.

### 3.2 But an event may restate the sheet at the HD it fires

The SRD lich says "increase **all current and future** Hit Dice to d12s". At the moment she
becomes a lich, dice she rolled at 1st–11th become d12. That is not the template applying at HD 1;
it is the template applying at HD 12 and rewriting what is on the sheet *then*. Evaluated at HD 8
those dice are still d6.

The same entry says "**Do not recalculate** base attack bonus, saves, or skill points" — which is
the rules spelling out the other half.

### 3.3 So the line to draw is accrued vs re-derived

- **Accrued per tick, never re-opened.** Skill points, and anything else banked at the level it was
  earned. These read the ability scores *as they were at that tick*. This is the half that is
  broken today. BAB and saves are progression-derived here and already immune.
- **Re-derived from the state at the evaluated HD.** Hit die size and hit points, creature type and
  everything following from it (life state, corporeality, nonabilities), natural armor, DR,
  immunities, level adjustment. These come out right on their own once the template fires at the
  right tick, because they are computed rather than accumulated.

If you move the template wholesale to tick N without keeping this split, you will trade the skill
bug for a hit-point bug (HD 1–11 revert to d6).

## 4. Two kinds of mid-career change

| | Trigger | Needs a decision? |
|:--|:--|:--|
| **Acquired template** — lichdom | none; gated only by prerequisites | **Yes.** No source records when it happened. |
| **Class capstone** — a prestige class whose 10th level changes creature type | reaching that class level | No — derivable, author it on the class |

Build one mechanism that covers both. The capstone half is the easier one and is a good first
target because it needs no new user input.

## 5. Where the code is

| What | Where |
|:--|:--|
| Templates applied before any tick | `ReplayEngine.cs:59` `// 2. Apply templates (in order)` → `ApplyTemplateCreation` (`:1122`) |
| Permanent events already applied per tick — the model to copy | `ReplayEngine.cs:93` (`BeforeTick == i`) and `:314` (past the last tick) |
| `PermanentEvent { BeforeTick, Permabuffs }` | `Models/Character.cs:52`, definition further down the same file |
| Skill points accrued per tick from the Int modifier | `Models/Permabuff.cs:166` `GrantSkillPoints` |
| Template fields, incl. `Prerequisites` (final-state) vs `ApplicabilityPrerequisites` (pre-mutation) | `Models/Template.cs` |
| Lich prerequisites, already authored | `Content/packs/srd_core/templates/lich.json` |

`Template.Prerequisites` are already documented as "validated against the FINISHED state (tail
pass), not at creation: acquired templates gate on class levels that do not exist yet when
templates are applied" — the existing code already anticipates this work.

**The lich's prerequisites are authored and bound the earliest legal acquisition HD**:
`HasFeat feat:craft_wondrous_item`, `HasSpellcasting`, `MinCasterLevel 11`, evil alignment, and
`HasCreatureType Humanoid` as an applicability check. A sensible default for an unanswered
acquisition HD is the earliest tick at which all of them are met.

## 6. Acceptance criteria

1. **`Lich Recruiter` reports 0 unspent skill points**, having granted 162. No other value on her
   sheet moves — her hit dice stay d12 throughout and her hit points stay 90.
2. **Duchess Rose round-trips.** The corpus holds one character as two PCGen sheets because PCGen
   cannot express both from one record. Import the later sheet, evaluate the timeline at the
   earlier HD, and the result must equal the earlier sheet. The pairing and the exact diff between
   them are in the **private** repo's `CONTENT_GAPS.md` §"Acceptance test for acquired-template
   support" (the classes and templates involved are not OGC, so they are not named here).
3. **Nothing else in the corpus moves.** Every existing character has inherited templates, which
   must keep applying at creation — that is the correct default and covers every saved character.
4. `dotnet test` green, and the golden build tests still assert their exact values.

## 7. Guard rails

- **Do not accept the PCG baseline.** Surface the diff and its arithmetic and stop. Accepting is
  the user's call. `UPDATE_PCG_BASELINE=1` is theirs to run, not yours. See the `pcg-baseline`
  skill, including how to tell whether the baseline moved under you.
- **Read every per-character block of the diff**, not just the headline. "0 regressions" counts
  status changes, not wrong numbers. In one session that diff caught two bugs 1,200 unit tests
  missed.
- **This changes the character save format.** Existing saved characters have no acquisition HD;
  absent must mean "applies at creation" so every stored character keeps working untouched.
- **A test whose comment explains why the engine differs from its source is a suspect, not a
  specification.** Three such tests turned out to be asserting bugs. Re-derive expected values from
  the SRD or the fixture, per `AGENTS.md` §Assertion discipline.
- The private packs are a **separate git repo** at `EXTRA_PACKS_PATH`; main-repo sweeps do not
  touch it, and non-OGC names must not appear in this repository.
- Start from a clean tree. Other sessions have been working in parallel; check `git status` in both
  repos before beginning.

## 8. Suggested order

1. Capstone-triggered type change first — no new user input, and it exercises the whole
   accrued-vs-re-derived split.
2. Acquisition HD as character input, defaulting to absent = creation.
3. Surface it as a decision owed in the builder, defaulted to the earliest tick meeting the
   template's prerequisites.
4. Duchess Rose as a test, then the lich's 162.
