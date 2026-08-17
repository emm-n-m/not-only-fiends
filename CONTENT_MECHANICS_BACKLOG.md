# Content Mechanics Backlog

This is the durable successor to the deleted dated permabuff audit queue. It preserves unresolved
or not-yet-revalidated work without treating one audit run's counts as current truth. Re-run the
`audit-cosmetic-permabuffs` skill before acting on a row, verify public findings against the SRD,
and split private-pack findings into the materials repository.

## Active engine/model gaps

- [ ] Model effects applied to summoned creatures before treating `feat:augment_summoning` as a
  character-state content fix.
- [ ] Finish Dragon Disciple's ancestry-dependent breath weapon and energy immunity, structured
  blindsense range, and apotheosis type/vision behavior.
- [ ] Model conditional and encounter-only effects that cannot currently be represented as static
  character state: terrain/opponent conditions, action economy, activation timing, flat-footed and
  anti-flanking rules, and effects on other creatures.
- [ ] Finish weapon-identity and conditional weapon effects, including range- and
  critical-hit-specific bonuses and complete natural/unarmed weapon handling.
- [ ] Audit the no-description blind spot: objects with empty `grantedPermabuffs` and no prose
  cannot be classified by the cosmetic-permabuff audit and require `verify-content` against an
  authoritative source.

## Public content requiring a fresh item-by-item audit

These paths/categories were present in the historical inventory. Some individual entries have
since been fixed, but the category has not been replaced by a fresh complete report.

### Races and creature traits

- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/races/srd_monsters.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/races/srd_dragons.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/races/srd_companions.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/races/srd_core_races.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_monsters/races/srd_pc_monsters_extended.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_monsters/races/srd_monster_pcs.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_monsters/races/srd_monsters.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_monsters/races/srd_nymph.json`

### Classes, class features, templates, feats, and domains

- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/class_features/high_arcana.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/classes/base/{druid,monk,paladin}.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/classes/prestige/{dragon_disciple,duelist,dwarven_defender,thaumaturgist}.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/templates/{fiendish,half_fiend,lich,srd_monsters}.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/feats/{general,epic}.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/domains/{srd,srd_deity}.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_unearthed_arcana/classes/base/{paladin_variants,unearthed_arcana}.json`

### Equipment

- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/equipment/wondrous_srd.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/equipment/rings_rods_staffs.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_core/equipment/magic_armor_weapons.json`
- [ ] `NotOnlyFiendsStudio/Content/packs/srd_epic/equipment/epic_{rings,rods,staffs,wondrous,armor_weapons}.json`

## Completion rule

A checkbox closes only after the whole file/category has been source-verified, every durable
character-state mechanic is either encoded or linked to a specific engine gap above, focused tests
cover changes, and the PCG baseline diff has been reviewed. A shrinking candidate count alone does
not close a row.
