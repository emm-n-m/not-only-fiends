---
name: extract-domain
description: Extract D&D 3.5e cleric domains from SRD HTML or supplement PDFs into validated DomainDefinition JSON. Use when adding domain granted powers, bonus-spell lists, or repairing domain content.
---

# Extract Domains

Extract cleric domains for the NotOnlyFiendsStudio content pipeline. Use source text for every
claim and never invent missing spell mappings.

## Workflow

1. Read `schemas/domain.schema.json` and `schemas/prompts/extract-domain.md` completely.
2. Prefer the local SRD mirror:
   - `NotOnlyFiendsStudio/Content/srd_html/clericDomains.html`
   - `NotOnlyFiendsStudio/Content/srd_html/divineDomains.html`
   Use PDFs only for supplements without SRD HTML.
3. Determine the requested domains. If neither source nor IDs establish scope, inspect the
   domain anchors and ask the user which batch to extract.
4. For each domain, capture the granted power and the ordered 1st–9th-level spell list.
5. Represent the granted power with the most specific available permabuffs. If it is not
   mechanically expressible, use a descriptive `GrantAbility` without approximating it.
6. Resolve every `bonusSpells` ID against loaded spell content. Report a source spell that has
   no content definition instead of silently renaming or dropping it.
7. Write core domains to `NotOnlyFiendsStudio/Content/packs/srd_core/domains/srd.json`;
   write supplements to their pack's `domains/` directory.
8. Add focused assertions for spell-list links and structured granted powers.
9. Run focused tests, strict content validation, then `dotnet test`.

## HTML landmarks

- Domain blocks usually begin with an `<h5>` domain heading.
- The granted-power paragraph follows the heading.
- An `X Domain Spells` heading introduces the numbered list.
- Spell-link anchor fragments are useful evidence, but the existing content registry remains
  the authority for the final `spell:<snake_case>` ID.

## Conventions

- Domain IDs: `domain:<snake_case>`.
- Granted-power ability IDs: `domain_<name>_power`.
- Spell IDs: `spell:<snake_case>`.
- Preserve exactly nine ordered domain spell slots unless the source explicitly defines a
  different structure.

## References

- `schemas/domain.schema.json`
- `schemas/prompts/extract-domain.md`
- `NotOnlyFiendsStudio/Content/packs/srd_core/domains/srd.json`
