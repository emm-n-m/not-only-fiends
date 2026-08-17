---
name: extract-feat
description: Extract feat definitions from a D&D 3.5e source (HTML preferred, PDF fallback). Produces feat JSON matching the FeatDefinition schema.
---

# Extract Feats from D&D 3.5e Source Material

You are extracting feat data for the NotOnlyFiendsStudio content pipeline.

## Source selection

Prefer HTML over PDF — the d20srd.org mirror at [NotOnlyFiendsStudio/Content/srd_html/](../../../NotOnlyFiendsStudio/Content/srd_html/) has stable anchors, category tags in headings, and hyperlinked prerequisite refs. PDF stays the fallback for supplements.

Dispatch on the argument:
- Ends in `.html`/`.htm` → HTML extraction (see below).
- Ends in `.pdf` → PDF extraction (original workflow).
- No path given → ask the user; default suggestion is the local SRD mirror files below.

### SRD HTML landmark files

- [featsAll.html](../../../NotOnlyFiendsStudio/Content/srd_html/featsAll.html) — master index of every SRD feat; use this for bulk extraction.
- [featsGen.html](../../../NotOnlyFiendsStudio/Content/srd_html/featsGen.html) — general feats only.
- [featsFtb.html](../../../NotOnlyFiendsStudio/Content/srd_html/featsFtb.html) — fighter bonus feats.
- [featsItc.html](../../../NotOnlyFiendsStudio/Content/srd_html/featsItc.html) — item creation feats.
- [featsMtm.html](../../../NotOnlyFiendsStudio/Content/srd_html/featsMtm.html) — metamagic feats.
- [divineFeats.html](../../../NotOnlyFiendsStudio/Content/srd_html/divineFeats.html) — divine feats.
- [epicFeats.html](../../../NotOnlyFiendsStudio/Content/srd_html/epicFeats.html) — epic feats.
- [monsterFeats.html](../../../NotOnlyFiendsStudio/Content/srd_html/monsterFeats.html) — monster feats (Awesome Blow, Multiattack, Multiweapon Fighting, etc.).
- [psionicFeats.html](../../../NotOnlyFiendsStudio/Content/srd_html/psionicFeats.html) — psionic feats.

## HTML extraction workflow

1. **Read schema & prompt** — [schemas/feat.schema.json](../../../schemas/feat.schema.json) and [schemas/prompts/extract-feat.md](../../../schemas/prompts/extract-feat.md) are authoritative.
2. **Load the HTML file** — each feat is delimited by `<h5><a id="FEAT_ID"></a>FEAT NAME [CATEGORY]</h5>`. The `[CATEGORY]` token maps directly to our `type` enum: `[GENERAL]`, `[METAMAGIC]`, `[ITEM CREATION]`, `[FIGHTER]` (= `fighterBonus`), `[DIVINE]`, `[EPIC]`, `[MONSTER]` → `other`, etc.
3. **Pick feats** — if the user supplied IDs, extract only those. Otherwise grep the anchors and ask.
4. **Parse each feat block** — the `<h5>` is followed by paragraph blocks up to the next `<h5>`:
   - `<p><b>Prerequisite</b>: ...</p>` — parse into schema `prerequisites` list (split on comma; each clause becomes one typed prereq).
   - `<p><b>Benefit</b>: ...</p>` — goes into the `description` alongside the flavor paragraph.
   - `<p><b>Normal</b>: ...</p>` — flavor only.
   - `<p><b>Special</b>: ...</p>` — often mentions "may be taken multiple times" → sets `repeatable: true`.
   - Hyperlinked prereq feats (`<a href="featsAll.html#power-attack">Power Attack</a>`) give the canonical feat ID via the anchor fragment (hyphens → underscores).
5. **Map prerequisite phrases** to schema types:
   - "Str 13" / "Cha 13+" → `MinAbility`
   - "Base attack bonus +N" → `MinBAB`
   - "N ranks in X" → `MinSkillRanks` (value = whole ranks, **do not pre-double**)
   - "X level Nth" → `MinClassLevel`
   - "HD N" / "N HD" → `MinHD`
   - "Ability to cast Nth-level spells" → `CanCastSpellLevel`
   - Other feat names → `HasFeat` (use the base ID; reserve `HasFeatSelections` for `minCount ≥ 2` cases like "two different schools of spell_focus").
6. **Permabuffs** — most feats have empty `grantedPermabuffs`. Only populate for persistent mechanical effects that apply passively (e.g., Leadership → `GrantCompanionSlot`, Improved Familiar → changes familiar pool).
7. **Write output** — choose the correct pack-nested file:
   - Core SRD feats → [NotOnlyFiendsStudio/Content/packs/srd_core/feats/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/feats/) (grouped by type: `general.json`, `fighter_bonus.json`, `srd.json` for item-creation/metamagic/misc, `epic.json`).
   - Supplement/homebrew feats → new pack directory under `NotOnlyFiendsStudio/Content/packs/<pack_id>/feats/`.
8. **Run tests** — `dotnet test` to verify load + schema.

## PDF extraction workflow (fallback)

1. Locate the feat chapter from the table of contents (pages 1–5).
2. For each feat, capture: name → `id` (`feat:snake_case`), category → `type`, prerequisite clauses → typed prereqs, benefit → `description`.
3. Avoid duplicating IDs that already exist in the target pack.
4. Write output and test as in steps 7–8 above.

## Key conventions

- Feat IDs: `feat:<snake_case>` (`feat:power_attack`, `feat:improved_initiative`).
- Prerequisite `$type` values: `MinBAB`, `MinAbility`, `MinSkillRanks`, `MinClassLevel`, `HasFeat`, `AlignmentReq`, `MinHD`, `MinCasterLevel`, `CanCastSpellLevel`, `HasRace`, `MinSave`, `HasAbility`, `HasSpellcasting`, `HasFeatOfType`, `HasFeatWithTag`.
- `MinSkillRanks.value` is **whole ranks** (the number printed in the source); the engine doubles internally.
- `HasFeat` on a selectable base feat matches any `{featId}_*` variant.

## Reference files

- Schema: [schemas/feat.schema.json](../../../schemas/feat.schema.json)
- Prompt: [schemas/prompts/extract-feat.md](../../../schemas/prompts/extract-feat.md)
- Existing core feats: [NotOnlyFiendsStudio/Content/packs/srd_core/feats/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/feats/)
