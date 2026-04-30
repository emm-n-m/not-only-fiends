---
name: extract-skill
description: Extract skill definitions from a D&D 3.5e source (HTML preferred, PDF fallback). Produces skill JSON matching the SkillDefinition schema.
argument-hint: <source-path> [skill-ids...]
---

# Extract Skills from D&D 3.5e Source Material

You are extracting skill data for the NotOnlyFiendsStudio content pipeline.

## Source selection

Prefer HTML over PDF — each skill is a clean `<h5>` entry with the key ability and flags (Trained Only, Armor Check Penalty) encoded directly in the heading. PDF stays the fallback for supplements.

Dispatch on the argument:
- Ends in `.html`/`.htm` → HTML extraction (see below).
- Ends in `.pdf` → PDF extraction (original workflow).
- No path given → default to the SRD mirror files below.

### SRD HTML landmark files

- [skillsAll.html](../../../NotOnlyFiendsStudio/Content/srd_html/skillsAll.html) — master alphabetical list of all SRD skills. Use for bulk extraction.
- [skills.html](../../../NotOnlyFiendsStudio/Content/srd_html/skills.html) — overview + rules chapter.
- Per-ability shards: [skillsStr.html](../../../NotOnlyFiendsStudio/Content/srd_html/skillsStr.html), [skillsDex.html](../../../NotOnlyFiendsStudio/Content/srd_html/skillsDex.html), [skillsCon.html](../../../NotOnlyFiendsStudio/Content/srd_html/skillsCon.html), [skillsInt.html](../../../NotOnlyFiendsStudio/Content/srd_html/skillsInt.html), [skillsWis.html](../../../NotOnlyFiendsStudio/Content/srd_html/skillsWis.html), [skillsCha.html](../../../NotOnlyFiendsStudio/Content/srd_html/skillsCha.html).
- Filters: [skillsTro.html](../../../NotOnlyFiendsStudio/Content/srd_html/skillsTro.html) (trained-only only), [skillsAcp.html](../../../NotOnlyFiendsStudio/Content/srd_html/skillsAcp.html) (ACP only).
- Supplements: [epicSkills.html](../../../NotOnlyFiendsStudio/Content/srd_html/epicSkills.html), [psionicSkills.html](../../../NotOnlyFiendsStudio/Content/srd_html/psionicSkills.html).

## HTML extraction workflow

1. **Read schema & prompt** — [schemas/skill.schema.json](../../../schemas/skill.schema.json) and [schemas/prompts/extract-skill.md](../../../schemas/prompts/extract-skill.md) are authoritative.
2. **Load the HTML file** — each skill is delimited by `<h5><a id="skill_id">SKILL NAME (KEY; FLAGS)</a></h5>`. Parse the header:
   - Anchor id is the canonical skill ID (hyphens → underscores: `disable-device` → `disable_device`).
   - Parenthesized token before `;` is the key ability (`STR`, `DEX`, `CON`, `INT`, `WIS`, `CHA`) → lowercase.
   - `TRAINED ONLY` → `trainedOnly: true`.
   - `ARMOR CHECK PENALTY` → `armorCheckPenalty: true`.
3. **Parse the skill body**:
   - First narrative paragraph is the description.
   - `<p><b>Check</b>: ...</p>`, `<p><b>Action</b>: ...</p>`, `<p><b>Try Again</b>: ...</p>` — structured metadata; fold into the description.
   - `<p><b>Synergy</b>: ...</p>` — 5+ rank bonuses to related skills. Parse the "5 ranks in X gives +2 on …" pattern and emit `synergies` entries.
   - `<p><b>Special</b>: ...</p>` — usually flavor/edge cases.
4. **Subspecialties** (Craft, Perform, Profession, Knowledge) — the skill entry often lists the subcategories in a nested paragraph. Emit one skill per subcategory with IDs like `craft_alchemy`, `perform_sing`, `knowledge_arcana`, `profession_hunter`. Set `parentSkill` to the parent (`craft`, `perform`, etc.) and omit the parent itself as a standalone skill unless the schema expects one.
5. **Cross-reference existing class/racial skills** — before adopting new skill IDs, grep the existing `classSkills` arrays in [NotOnlyFiendsStudio/Content/packs/srd_core/classes/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/classes/) and [NotOnlyFiendsStudio/Content/packs/srd_core/racial_hd/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/racial_hd/) to confirm naming.
6. **Write output** — SRD skills go to [NotOnlyFiendsStudio/Content/packs/srd_core/skills/srd.json](../../../NotOnlyFiendsStudio/Content/packs/srd_core/skills/srd.json) (append). Supplement/homebrew skills go to a new pack's `skills/` dir.
7. **Run tests** — `dotnet test`.

## PDF extraction workflow (fallback)

1. Locate the skill chapter from the table of contents.
2. Parse each skill's header line (skill name, key ability, flags) and synergy list.
3. Write output and test as in steps 6–7 above.

## Key conventions

- Skill IDs: `snake_case` (`balance`, `use_magic_device`).
- Subspecialty IDs: `parent_subcategory` (`knowledge_arcana`, `craft_alchemy`, `perform_sing`).
- IDs must match existing `classSkills` references in HDDriver content.

## Reference files

- Schema: [schemas/skill.schema.json](../../../schemas/skill.schema.json)
- Prompt: [schemas/prompts/extract-skill.md](../../../schemas/prompts/extract-skill.md)
- Existing skills: [NotOnlyFiendsStudio/Content/packs/srd_core/skills/srd.json](../../../NotOnlyFiendsStudio/Content/packs/srd_core/skills/srd.json)
