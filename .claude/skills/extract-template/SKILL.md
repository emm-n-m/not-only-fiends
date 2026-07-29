---
name: extract-template
description: Extract template definitions from a D&D 3.5e source (HTML preferred — the SRD mirror carries 16 templates; PDF for supplements). Produces template JSON matching the schema.
argument-hint: <source-path> [template-ids...]
---

# Extract Templates from D&D 3.5e Source Material

You are extracting template data for the NotOnlyFiendsStudio content pipeline.

## Source selection

Prefer HTML. **The mirror carries all 16 monster files, not the three this doc used to claim**, and every iconic SRD template is in it — half-dragon, half-fiend, half-celestial, vampire, lich, ghost, lycanthrope and more. Corrected 2026-07-29 after `half_dragon` was extracted from `monstersHtoI.html`; the old text sent extractions to a PDF that was never needed. PDF stays the fallback for supplement templates with no SRD page.

To confirm what the mirror holds, the canonical section heading is `<h5>CREATING A <NAME></h5>`:

```
grep -rn -i "CREATING AN\? " NotOnlyFiendsStudio/Content/srd_html/*.html
```

Dispatch on the argument:
- Ends in `.html`/`.htm`, or names an SRD template → HTML extraction (primary workflow).
- Ends in `.pdf` → PDF extraction (supplements, and anything the grep above does not find).
- No path given → grep the mirror first; only ask the user for a PDF if the template is absent.

### SRD HTML landmark files

Every template below is confirmed present in the mirror (verified 2026-07-29):

| template | file |
|---|---|
| Celestial | `monstersBtoC.html` |
| Fiendish | `monstersEtoF.html` |
| Ghost | `monstersG.html` |
| Half-Celestial, **Half-Dragon**, Half-Fiend | `monstersHtoI.html` |
| Lich, Lycanthrope | `monstersKtoL.html` |
| Skeleton | `monstersS.html` |
| Vampire, Zombie | `monstersTtoZ.html` |
| Demilich, Paragon, Pseudonatural, Worm That Walks | `epicNonAbominations.html` |
| Phrenic | `psionicMonsters.html` |

Already extracted: `half_fiend`, `half_dragon`, `fiendish`, `lich` (see `srd_core/templates/`).

Other landmark files:

- [improvingMonsters.html](../../../NotOnlyFiendsStudio/Content/srd_html/improvingMonsters.html) — general rules for reading templates, acquired vs. inherited, stacking. **No specific templates here**, just rules.
- [monstersG.html](../../../NotOnlyFiendsStudio/Content/srd_html/monstersG.html) — contains **Ghost** template at `<h3><a id="ghost"></a>GHOST</h3>` (line ~1257) with the `<h5>CREATING A GHOST</h5>` section.
- [monstersS.html](../../../NotOnlyFiendsStudio/Content/srd_html/monstersS.html) — contains **Skeleton** template at `<h3><a id="skeleton"></a>SKELETON</h3>` (line ~1862) with `<h5>CREATING A SKELETON</h5>` and a size variant table.

Templates genuinely **not** in the mirror (use PDF): wraith, and any supplement template.

## HTML extraction workflow (when the template is available)

1. **Read schema & prompt** — [schemas/template.schema.json](../../../schemas/template.schema.json) and [schemas/prompts/extract-template.md](../../../schemas/prompts/extract-template.md) are authoritative.
2. **Locate the template** — search for `<h5>CREATING A <NAME></h5>` or the parent `<h3>` anchor. The "Creating a …" section is the canonical template description.
3. **Parse the template block**:
   - Opening paragraph: type changes ("a ghost is an undead creature"), subtype additions, acquired vs. inherited.
   - `<p><b>Hit Dice</b>: …</p>` — usually "change to dN" — captured as a type mutation.
   - `<p><b>Speed</b>: …</p>`, `<p><b>Armor Class</b>: …</p>`, `<p><b>Attacks</b>: …</p>`, etc. — structured as mutations (set speed, add natural armor, grant a new attack form).
   - `<p><b>Special Attacks</b>: …</p>` / `<p><b>Special Qualities</b>: …</p>` — each entry becomes a `GrantAbility`, `GrantImmunity`, `GrantDR`, `GrantSLA`, or `ScalingFormula`.
   - `<p><b>Abilities</b>: …</p>` — mutations to ability scores (add/subtract, set).
   - `<p><b>Level Adjustment</b>: +N or Same as base creature +N</p>` — goes into `levelAdjustment`.
4. **Build the template JSON** using POST/DELETE/PUT semantics for mutations against a base creature (see existing templates for the structure).
5. **Write output** — SRD templates go to [NotOnlyFiendsStudio/Content/packs/srd_core/templates/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/templates/). Supplement templates go to a new pack.
6. **Run tests** — `dotnet test`.

## PDF extraction workflow (primary for most templates)

1. Locate the template chapter (usually appendix or monster-manual sidebar) from the table of contents.
2. Parse the "Creating a …" section for mutations to type, size, speeds, ability scores, DR/SR/resistances/immunities, SLAs, and level adjustment.
3. For scaling abilities that depend on total HD, use the Formula DSL: `"TotalHD + 11"`, `"max(10, TotalHD)"`. Thresholds that fire at specific HD use thresholds; per-tick recalculations use ScalingFormulas.
4. Write output and test as in steps 5–6 above.

## Key conventions

- Template IDs: `template:<snake_case>` (`template:half_fiend`, `template:ghost`).
- Scaling formulas: `"TotalHD + 11"`, `"max(10, TotalHD)"`, `"BaseBAB + 3"`.
- Thresholds fire once at exact HD; ScalingFormulas recalculate every tick (SetAttribute semantics).
- Acquired vs. inherited: capture in the description; engine treats both the same.

## Reference files

- Schema: [schemas/template.schema.json](../../../schemas/template.schema.json)
- Prompt: [schemas/prompts/extract-template.md](../../../schemas/prompts/extract-template.md)
- Existing templates: [NotOnlyFiendsStudio/Content/packs/srd_core/templates/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/templates/)
