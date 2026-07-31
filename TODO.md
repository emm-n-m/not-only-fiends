# TODO

Open work only. Completed items and their implementation notes are available in git history.
Test-only gaps live in [TEST_COVERAGE_BACKLOG.md](TEST_COVERAGE_BACKLOG.md), and the public/private
content rules live in [CONTENT_POLICY.md](CONTENT_POLICY.md).

Last consolidated: 2026-07-30.

## 1. Content correctness and verification

### Public SRD packs

- [ ] Run Tier 2 verification against the SRD mirror:
  - races;
  - feat prerequisites and feat `type` values;
  - domains.
- [ ] Run Tier 3 verification over all 617 spells.
- [ ] Add a content-aware exact-spell prerequisite. The current
  `Prerequisite.IsMet(CharacterState)` cannot determine whether a full-list caster such as a
  cleric or druid can cast a particular spell because the class spell list lives in content.
  Current `CanCastSpellLevel` checks for Arcane Trickster, Thaumaturgist, and Cosmic Descryer are
  approximations of requirements for *mage hand*, *lesser planar ally*, and *gate*.

### Private packs

Source audit reports:

- PCGen LST audit: `{EXTRA_PACKS_PATH}/test-reports/lst_audit_2026-07-27.md`
- Fiendish Codex PDF audit: `{EXTRA_PACKS_PATH}/test-reports/fc_pdf_audit_2026-07-28.md`

- [ ] Complete P1, the dropped-prerequisites sweep: roughly 40 findings across
  `12_to_midnight`, curses, mongoose, necromancer, sword-and-sorcery, and deceit classes.
  Cross-check every restored prerequisite against the `.pcg` corpus as a second witness.
- [ ] Add the prerequisite/model primitives needed by P1 and the PDF audit:
  - spell-school-gated casting;
  - deity requirements;
  - total PC-level requirements;
  - creature-type requirements;
  - patron/allegiance requirements (needed by the nine Marks);
  - a general any-of prerequisite wrapper;
  - prepared-casting-only requirements;
  - template prerequisites.
- [ ] Complete P4 for Eldritch Sorcery spells: restore 51 `DOMAINS:` assignments and 10
  Assassin/Blackguard `classLevels` assignments.
- [ ] Restore the "Language: Infernal" prerequisite on the three Fiendish Codex II prestige
  classes now that Infernal can be selected by non-hellbred characters.
- [ ] Add the Unseelie Champion homebrew template (deceit LSTs, 2026-03). Grants effective
  ranger level = ranger (or planar ranger) levels + outsider HD for ranger class features,
  favored enemy, animal companion level, wild empathy, and spellcasting. Needs an
  effective-class-level engine primitive; currently the one WARN in the PCG baseline
  (`Vzraella, Abyssal Herald`).
- [ ] Add the HD-gated parts of the hellbred Infernal Aspect choices:
  - bonus devil-touched feats at 4 and 14 HD;
  - darkvision progression from 30 to 60 to 120 feet;
  - see in darkness at 12 HD;
  - telepathy at 15 HD.
- [ ] Add language lines for private-pack races.

## 2. Engine and rules-model gaps

These block accurate content rather than representing bad source transcription.

- [ ] Add HD-conditional racial grants and choice-bearing racial traits.
- [ ] Add flat hit-point grants.
- [ ] Add non-equipment typed AC bonuses.
- [ ] Add speed grants from classes and feats.
- [ ] Add general feat selections, including Elemental Resistance.
- [ ] Model Curse Repertoire spell knowledge.
- [ ] Add an `HDDriver` spell-list field.
- [ ] Add favored-class data and behavior.
- [ ] Support alternative spell components such as `M/DF`.
- [ ] Apply armor check penalties and untrained-use rules to computed skill totals.
- [ ] Grant languages purchased through Speak Language skill ranks.
- [ ] Decide whether sub-pound equipment weights justify changing `weightLbs` from integer to a
  fractional type. Thirty-three extracted gear items currently round down to zero pounds.
- [ ] Sync the schema `Prerequisite` definitions with the model. The `_common`, `feat`,
  `hddriver`, and `equipment` schemas lag `Prerequisite.cs` — missing `MinSkillRanksAcross`,
  `HasFeatOfAnyType`, `HasSpontaneousCasting`, `HasAnyRace`, `LacksTemplate`, `HasLanguage`,
  `MinCounter`, and (outside `_common`) `AnyOf`/`HasCreatureType`.

### Known lower-priority fidelity limits

- [ ] Half-dragon variety selection, including breath weapon shape/energy and the additional
  immunity.
- [ ] Size-conditional half-dragon wings.
- [ ] Half-dragon racial-HD die-size and skill-point changes.
- [ ] Expert's player-selected ten class skills.
- [ ] Loremaster's requirement for seven distinct divination spells.

## 3. Content drift and caching

- [ ] Add per-character content fingerprints. Hash only definitions touched during replay, store
  that fingerprint with the character, and report which referenced definitions changed when the
  character is loaded later.
- [ ] Add ETag/conditional GET support to content endpoints. Derive the ETag from the loaded
  content snapshot so polling clients can avoid downloading unchanged catalogs.
- [ ] Decide whether semantic pack versions are needed for character interchange when the
  originating packs are unavailable. `PackManifest.Version` currently carries no compatibility
  guarantee; prefer generated fingerprints over manually maintained semver where possible.

## 4. Equipment completeness

- [ ] Design composable equipment modifiers. All individually named SRD equipment is now in the
  catalog, and named magic weapons carry their baseline `enhancementBonus`, but the source's
  generic `+1` through `+5` armor/shield/weapon entries and special abilities such as `flaming`,
  `keen`, and `holy` modify a selected base item rather than standing alone. The same system
  should represent intelligent-item ability scores, communication, powers, purpose, and Ego.
- [ ] Design spell-based consumable generation before adding potions, scrolls, and wands. These
  are formulas over spell, spell level, caster level, and casting tradition rather than a finite
  source list; avoid checking in thousands of redundant static definitions.
- [ ] Consider splitting the trailing school/caster-level/crafting clauses from wondrous-item
  descriptions into structured display fields.
- [ ] Re-check two source ambiguities against a book source:
  - Armor of the Celestial Battalion uses an inferred 20 lb. weight because the SRD omits it;
  - Bulwark of the Great Dragon uses 1,612,970 gp from its description rather than the
    1,612,980 gp shown in the random-item table.

## 5. Before making the repository public

- [ ] Add a README verification-status section explaining that the engine is well-tested while
  SRD content remains best-effort and is still being audited.
- [ ] Decide and document the contribution policy: issues and pull requests accepted, or
  source-available only.
