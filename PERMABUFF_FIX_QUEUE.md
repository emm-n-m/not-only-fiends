# Permabuff fix queue

Work list from the `audit-cosmetic-permabuffs` run of 2026-08-09: content whose mechanics are
stated only in a description and never encoded, so the engine does nothing with them. The full
report is at `test-reports/cosmetic_permabuff_audit_2026-08-09.md` (gitignored — it quotes
third-party content).

**138 public-pack CONTENT-BUGs.** All 223 proposed permabuffs in the report deserialize through
`JsonOptions.Default`, and none duplicates something already encoded beside it, so the proposed
JSON is usable as written. Verify each against the SRD before applying anyway — the audit compares
a description to its neighbours, not to the rules, so it cannot tell a correct description from a
wrong one.

Priority below is **blast radius on the current 55-character roster first, then rule centrality**.
Roster counts are from 2026-08-09.

---

## Tier 1 — features already on saved characters

These change existing sheets the moment they land, so they carry the most review risk and the most
value. Expect the PCG baseline to move; check every delta before accepting.

| # | Where | What |
|--:|:--|:--|
| 3 | `class_features/loremaster_secret.json` | +2 Reflex / Will / Fortitude. **30 loremaster ticks on the roster.** |
| 1 | `class_features/high_arcana.json` | `spell_power` — +1 caster level. **35 archmage ticks.** |
| 2 | `classes/base/druid.json` | `nature_sense` +2 Knowledge (nature)/Survival, `venom_immunity`. **12 druid ticks.** |
| 1 | `classes/prestige/thaumaturgist.json` | `augment_summoning`. **2 ticks.** |
| 3 | `templates/lich.json` | undead traits, DR, immunities. **1 lich.** |
| 2 | `templates/srd_monsters.json` | `undead_traits` (**3 characters**), `vampire_dr` (**1**). |
| 2 | `templates/fiendish.json` | DR 5/magic and DR 10/magic. **1 character.** |
| 1 | `feats/general.json` | `feat:iron_will` — +2 Will. **1 character.** |

## Tier 2 — core rules, no roster impact yet

Nothing on the roster takes these today, but they are among the most commonly chosen options in
the game and will be wrong for the first character who does.

| # | Where | What |
|--:|:--|:--|
| 2 | `feats/general.json` | `great_fortitude`, `lightning_reflexes` — +2 to a save each |
| 3 | `feats/epic.json` | `epic_fortitude`, `epic_reflexes`, `epic_will` — +4 to a save each |
| 3 | `classes/base/monk.json` | `purity_of_body`, `diamond_body`, `perfect_self` — disease/poison immunities |
| 2 | `classes/base/paladin.json` | `aura_of_courage`, `divine_health` |
| 4 | `paladin_variants.json`, `unearthed_arcana.json` | the same two on the three UA paladin variants |
| 3 | `classes/prestige/dragon_disciple.json` | natural armour increase, wings, apotheosis |
| 2 | `classes/prestige/{duelist,dwarven_defender}.json` | `grace`, `damage_reduction` |
| 2 | `domains/srd.json`, `domains/srd_deity.json` | healing and community domain powers |
| 1 | `templates/half_fiend.json` | poison immunity |

## Tier 3 — races

Race fixes are cheap and self-contained; the animal familiars were already done on 2026-08-09 and
are the template for the rest.

| # | Where |
|--:|:--|
| 8 | `races/srd_monsters.json` |
| 6 | `races/srd_dragons.json` — red dragon fire/sleep immunity and DR, both size variants |
| 5 | `srd_monsters/races/srd_monster_pcs.json` — gargoyle, janni, svirfneblin, forest gnome |
| 5 | `srd_monsters/races/srd_monsters.json` — aranea, hell hound racial skill bonuses |
| 3 | `races/srd_companions.json` — air/water elemental and shadow traits |
| 2 | `races/srd_core_races.json` — gnome craft alchemy, halfling lucky |
| 2 | `srd_monsters/races/srd_nymph.json` — unearthly grace, DR cold iron |
| 1 | `srd_pc_monsters_extended.json` — shambling mound plant traits |

## Tier 4 — equipment (largest block, lowest urgency)

An item only matters once someone equips it, and none of these are on the roster.

| # | Where |
|--:|:--|
| 30 | `equipment/wondrous_srd.json` |
| 15 | `equipment/rings_rods_staffs.json` |
| 5 | `equipment/epic_{rings,rods,staffs,wondrous,armor_weapons}.json` |
| 2 | `equipment/magic_armor_weapons.json` — breastplate of command, frost brand |

## Private packs

12 further CONTENT-BUGs sit in the extra packs. They are **not** listed here — non-OGC content does
not belong in this repository. See the report's private-packs section, and track the work in the
materials repo's `CONTENT_GAPS.md`.

---

## Not content bugs — engine work these surfaced

- **`feat:epic_leadership` does nothing, and the audit could not see it.** It has an empty
  `grantedPermabuffs` *and no description*, so every description-based sweep skips it. SRD:
  "Multiply the number of followers of each level that the character can lead by 10." Needs a
  multiplier on `CharacterState.FollowerCounts` — `ModifyLeadershipScore` moves the score, not the
  counts derived from it. One roster character has the feat.
- **284 objects share that blind spot** (empty `grantedPermabuffs`, no description, mostly epic
  feats). The audit prompts now emit them under a `NO-DESCRIPTION` heading, but they cannot be
  judged by comparing a description to its neighbours — they need `verify-content`, which has an
  authoritative source.
- **The 2026-08-09 ENGINE-GAP section is unusable**: all 150 entries carry one boilerplate
  rationale, and some are misfiled (wizard specialisation is implemented by `SpecialtyBonusSlots`).
  Both prompts now require a specific "needs X on Y" and forbid a repeated rationale. Re-run that
  half before treating it as a work list.

## Applying a fix

Per `AGENTS.md`: re-derive every expected value from the SRD, never from new output. Then
`dotnet build`, `dotnet test`, read `pcg_import_report.diff.md` and confirm each changed value is
explained by the fix, and add a rule assertion for what you fixed.
