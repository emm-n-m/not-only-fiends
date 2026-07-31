---
name: verify-content
description: Audit existing public content JSON against the local authoritative SRD HTML and report source-quoted mismatches without editing content. Use for rules-accuracy verification of classes, racial HD, races, feats, domains, spells, or equipment rather than new extraction.
---

# Verify Public Content Against the SRD

Audit existing public-pack definitions against
`NotOnlyFiendsStudio/Content/srd_html/`. The mirror—not D&D knowledge from memory—is ground
truth.

## Non-negotiable rules

- Quote the exact SRD text supporting every finding.
- Mark fields `UNVERIFIABLE` when the mirror provides no evidence.
- Keep private/non-SRD packs out of scope; use `verify-content-lst` or an owned PDF for them.
- Report findings and proposed diffs only. Do not edit content unless the user separately asks
  for fixes.

Content changes alter replayed characters without changing their saved inputs, so findings
require review and later regression coverage.

## Workflow

1. Derive public pack IDs from `content-public.json`.
2. Enumerate only content files under those packs; do not assume a historical count.
3. Resolve each item to an authoritative SRD page:
   - base classes: dedicated `<classname>.html`;
   - prestige classes: dedicated camel-case HTML;
   - NPC classes: `npcClasses.html`;
   - UA variants: `unearthedCoreClass.html` plus the base page for inherited rules;
   - creature types/racial HD: `monsterTypes.html`;
   - races: `races.html`, `monstersAsRaces.html`, and relevant `monsters*.html`;
   - feats/domains/spells/equipment: their dedicated SRD index or entry pages.
4. Read the complete source block, including prose before and after tables.
5. Compare every field in scope and retain exact source excerpts with file/anchor context.
6. Before reporting, check model semantics and repository conventions to eliminate false
   positives.
7. Return the report in the required format.

## Priority

Prioritize blast radius:

1. drivers and racial HD;
2. races, feat prerequisites/types, and domains;
3. spells and equipment.

When the user selects a tier or IDs, stay within that scope.

## High-value comparisons

### Classes and racial HD

Compare hit die, skill points, class skills, maximum level, BAB/saves, proficiencies,
prerequisites, level features, and spell-advancement levels. Derive progression from the
actual table; check the first row because a good save begins at +2 while a poor save begins at
+0.

### Races and templates

Compare type/subtypes, size, speed, ability adjustments, racial HD, level adjustment, every
trait, and scaling/choice behavior. Never infer LA when the source omits it.

### Feats and domains

Compare feat type, all prerequisites, repeatability, granted mechanics, domain power, and all
nine domain spell links.

### Spells

Compare class/domain levels first, then school/subschool/descriptors, components, casting time,
range, target/area/effect, duration, save, spell resistance, and description.

### Equipment

Compare finite item presence, price, weight, slot/category, weapon/armor profiles, named
enhancement baseline, and persistent mechanics. Distinguish finite named items from
generative modifiers and spell-derived consumables.

## False-positive guards

- Bare `craft`, `knowledge`, `perform`, and `profession` in `classSkills` are parent umbrellas
  expanded through `parentSkill`; they are not dangling skill IDs.
- `HasFeat` on a selectable base feat may match `{featId}_*` selections.
- `MinSkillRanks.value` is authored in whole ranks even though stored character allocations use
  half-ranks.
- Equipment armor bonuses may already include the named item's enhancement while weapon
  enhancement uses top-level `enhancementBonus`.
- Inspect current model/schema definitions before declaring a source requirement
  unrepresentable; do not rely on an old prerequisite list.

## Output

Return a markdown table:

`Item | Field | JSON value | SRD value | SRD quote | Severity`

Use `HIGH` when the mismatch changes computed mechanics or legal build choices, otherwise
`LOW`. Follow with:

- `VERIFIED CLEAN`
- `UNVERIFIABLE`
- proposed focused regression assertions

If the user later authorizes fixes, put rules-accuracy regressions in
`NotOnlyFiendsStudio.Tests/RulesAccuracyTests.cs` or the relevant focused test class.

## References

- `content-public.json`
- `NotOnlyFiendsStudio/Content/srd_html/`
- `NotOnlyFiendsStudio/Models/`
- `schemas/`
- `TODO.md`
