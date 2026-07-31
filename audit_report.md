# Public content audit report

Audit date: 2026-07-31  
Authority: the repository-local `NotOnlyFiendsStudio/Content/srd_html/` mirror  
Scope: public packs selected by `content-public.json` (`srd_core`, `srd_epic`,
`srd_monsters`, and `srd_unearthed_arcana`). Private packs were deliberately
excluded: SRD text cannot authoritatively audit them. No content was changed.

## Outcome

The audit found rules-accuracy gaps that affect legal character choices or
computed results. The highest-priority work is in templates/classes,
feat/domain prerequisites and effects, and three missing domain spell-list
entries. Every finding is source-quoted in the focused reports below.

| Area | Inventory checked | High-priority findings | Focused report |
| --- | ---: | --- | --- |
| Classes, racial HD, races, templates | 186 definitions | Barbarian damage reduction; Half-Dragon HD/flight; Half-Fiend eligibility/flight; Fiendish eligibility, type/subtype, darkvision, and Smite Good | [drivers-races.md](audit_reports/drivers-races.md) |
| Feats and domains | 327 feats; 35 domains | Incomplete feat prerequisites/selections and domain powers that are stored only as prose rather than evaluated mechanics | [feats-domains.md](audit_reports/feats-domains.md) |
| Spells and equipment | 617 spells; 903 equipment definitions | `blacklight` lacks Darkness 3; `hardening` lacks Artifice 7; `maddening_scream` lacks Madness 8 | [spells-equipment.md](audit_reports/spells-equipment.md) |

## Recommended remediation order

1. Correct legal-build and replay errors: Barbarian DR, template gates and
   state changes, feat prerequisites/selected targets, and the three omitted
   domain spell levels.
2. Model domain mechanics that currently have only display text: class-skill
   grants, the War domain's selected weapon feats, and scoped caster-level
   bonuses.
3. Fill the quoted spell descriptions and correct Blacklight's `radius` typo.
4. Add the focused regression assertions proposed in each detailed report,
   then run the PCGen baseline before accepting any content change.

## Remediation update — 2026-07-31

Implemented in this repository:

- Barbarian DR 1/— through 5/— at the SRD levels, with same-bypass DR replacing
  rather than stacking.
- Bardic Music's base daily-use counter and Extra Music's bardic-music gate and
  four additional uses.
- Selectable targets for Ability Focus, Empower/Quicken Spell-Like Ability,
  and Spell Mastery; Spell Mastery now requires Wizard 1. Rapid Reload is
  classified as a general feat.
- Domain class-skill grants for Animal, Plant, Trickery, Knowledge, and Travel;
  Artifice's Craft bonus; and the missing Blacklight, Hardening, and Maddening
  Scream domain spell links and descriptions.

Still deferred because the current data model cannot express them faithfully:

- Half-Dragon's racial-HD die upgrade and size-conditional flight.
- Half-Fiend's living/corporeal gate and flight derived from the base land
  speed.
- Conditional caster-level effects (alignment descriptor, school/subschool,
  and Artifice/Creation interaction), Magic domain item-use level, and the War
  domain's deity-specific selected weapon grants.
- SLA caster-level thresholds and fully typed special-attack eligibility. The
  current model records SLAs and selected targets but does not retain a
  source-level SLA caster level or a general special-attack taxonomy.

## Important limits

"Verified clean" means the enumerated fields and source blocks checked in the
corresponding focused report matched. It is not a claim that every prose field
of every definition received a complete line-by-line reconstruction. Entries
without applicable local SRD evidence are explicitly marked `UNVERIFIABLE` in
the detailed reports.
