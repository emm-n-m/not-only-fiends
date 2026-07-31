---
name: extract-template
description: Extract D&D 3.5e creature templates from SRD HTML or supplement PDFs into validated TemplateDefinition JSON. Use when adding inherited or acquired templates, mutations, scaling abilities, thresholds, or level adjustment.
---

# Extract Templates

Extract template content for the NotOnlyFiendsStudio pipeline. Prefer the local SRD mirror and
use a supplement PDF only when the template is absent from it.

## Workflow

1. Read `schemas/template.schema.json` and `schemas/prompts/extract-template.md` completely.
2. Search `NotOnlyFiendsStudio/Content/srd_html/*.html` for the canonical
   `CREATING A <NAME>` or `CREATING AN <NAME>` heading before requesting a PDF.
3. Resolve requested template IDs and inspect existing template definitions to avoid
   duplicates.
4. Parse the complete template block: type/subtype changes, Hit Dice, speed, AC, attacks,
   special attacks, special qualities, saves, abilities, skills, feats, CR, and LA.
5. Model changes using the repository's POST/DELETE/PUT mutation semantics.
6. Use thresholds only for one-time exact-HD grants. Use scaling formulas for values that must
   be recalculated each replay tick.
7. Represent each mechanic with the most specific available mutation or permabuff. Preserve
   choice-bearing or unsupported behavior as source-accurate descriptive abilities and report
   the model gap.
8. Write SRD templates under `NotOnlyFiendsStudio/Content/packs/srd_core/templates/`;
   write supplements to their corresponding pack.
9. Add focused assertions for mutations, scaling, thresholds, and LA. Run focused tests,
   strict validation, then `dotnet test`.

## Confirmed SRD landmarks

- Celestial: `monstersBtoC.html`
- Fiendish: `monstersEtoF.html`
- Ghost: `monstersG.html`
- Half-celestial, half-dragon, half-fiend: `monstersHtoI.html`
- Lich, lycanthrope: `monstersKtoL.html`
- Skeleton: `monstersS.html`
- Vampire, zombie: `monstersTtoZ.html`
- Demilich, paragon, pseudonatural, worm that walks: `epicNonAbominations.html`
- Phrenic: `psionicMonsters.html`

## Conventions

- Template IDs: `template:<snake_case>`.
- Capture acquired/inherited status in the description when the model does not distinguish it.
- Base formulas on current supported variables in `NotOnlyFiendsStudio/Models/Formula.cs`;
  do not invent variable
  names from examples.
- Never infer level adjustment when the source omits it.

## References

- `schemas/template.schema.json`
- `schemas/prompts/extract-template.md`
- `NotOnlyFiendsStudio/Content/packs/srd_core/templates/`
- `NotOnlyFiendsStudio/Models/Template.cs`
