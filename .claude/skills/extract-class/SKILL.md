---
name: extract-class
description: Extract class (or prestige class) definitions from a D&D 3.5e source (HTML preferred, PDF fallback). Produces HDDriver JSON matching the schema.
argument-hint: <source-path> [class-ids...]
---

# Extract Classes from D&D 3.5e Source Material

You are extracting class/prestige class data for the NotOnlyFiendsStudio content pipeline.

## Source selection

Prefer HTML over PDF — the d20srd.org mirror has each class in a dedicated file with labeled sections for Class Skills, the class table (BAB/saves/features by level), and per-feature descriptions. PDF remains the fallback for supplements.

Dispatch on the argument:
- Ends in `.html`/`.htm` → HTML extraction (see below).
- Ends in `.pdf` → PDF extraction (original workflow).
- No path given → ask the user; default suggestion depends on whether they want a base class, prestige class, or racial HD driver.

### SRD HTML landmark files

Base classes (one file each): `barbarian.html`, `bard.html`, `cleric.html`, `druid.html`, `fighter.html`, `monk.html`, `paladin.html`, `ranger.html`, `rogue.html`, `sorcerer.html`, `wizard.html`. All under [NotOnlyFiendsStudio/Content/srd_html/](../../../NotOnlyFiendsStudio/Content/srd_html/).

Prestige classes (one file each): `arcaneArcher.html`, `arcaneTrickster.html`, `archmage.html`, `assassin.html`, `blackguard.html`, `dragonDisciple.html`, `duelist.html`, `dwarvenDefender.html`, `eldritchKnight.html`, `hierophant.html`, `horizonWalker.html`, `loremaster.html`, `mysticTheurge.html`, `redDwarf.html`, `shadowdancer.html`, `thaumaturgist.html`, plus the divine/epic prestige set (`cosmicDescryer.html`, `divineEmissary.html`, `divineMinions.html`, `epicInfiltrator.html`, `agentRetriever.html`, etc.).

Racial HD drivers: for creature-type bases, read the type in [monsterTypes.html](../../../NotOnlyFiendsStudio/Content/srd_html/monsterTypes.html) and derive from the type entry (hit die, BAB, skills, saves).

## HTML extraction workflow

1. **Read schema & prompt** — [schemas/hddriver.schema.json](../../../schemas/hddriver.schema.json) and [schemas/prompts/extract-class.md](../../../schemas/prompts/extract-class.md) are authoritative.
2. **Load the HTML file** — class name is the `<h3>` entry at the top. Key sections:
   - `<h6>Class Skills</h6>` or `<h6><a id="NAME-class-skills"></a>Class Skills</h6>` — paragraph lists skills as hyperlinked `<a href="skillsAll.html#skill-id">Skill</a>` — anchor fragments map directly to our skill IDs.
   - `<b>Hit Die</b>: dN` — `hitDie` (4, 6, 8, 10, 12).
   - `<b>Skill Points at 1st Level</b>: (N + Int modifier) ×4` and `<b>Skill Points at Each Additional Level</b>: N + Int modifier` — `skillPointsPerLevel = N`.
   - **Class table** (look for `<table>` following `<a id="table-the-CLASSNAME"></a>`): `Level` / `Base Attack Bonus` / `Fort` / `Ref` / `Will` / `Special` columns. Derive BAB progression (level-20 BAB: +20=good, +15=average, +10=poor) and save progressions (level-20 save: +12=good, +6=poor). The `Special` column lists class features at each level.
   - `<h6>Class Features</h6>` and `<h6><a id="CLASSNAME-CLASS-FEATURES"></a>…</h6>` — per-feature descriptions drive `levelPermabuffs` entries.
3. **Pick classes** — if the user supplied IDs, extract only those. Otherwise confirm scope.
4. **Build the HDDriver**:
   - `kind: "Class"` for base/prestige classes (PascalCase discriminator); `kind: "RacialHD"` for racial HD drivers.
   - `id: "class:<snake_case>"` for classes; `id: "racial_hd:<name>"` for racial HD.
   - `babProgression` / `saveProgression` from the table.
   - `classSkills` deduplicated from the skills paragraph, hyphens → underscores.
   - Prestige prerequisites → typed list (see mapping in extract-feat skill).
5. **Spellcasting** — if the class casts spells, include `UpdateSpellcasting` at level 1 (for base classes) or `AdvanceSpellcasting` at each level (for prestige classes that advance an existing class).
6. **Level permabuffs** — for each class feature at level N, emit an entry in `levelPermabuffs[N]` that matches the feature's mechanics (use `GrantAbility` with a descriptive body for narrative features; reserve structured permabuffs like `GrantCompanionSlot` for concrete game-engine effects).
7. **Write output** — one file per class, filename = the bare id after `class:` (e.g. `class:loremaster` → `loremaster.json`), matching the format of any existing file in the target directory (JSON array of one entry, array bracket and object's opening brace both at column 0, 2-space field indent). There is no flat `srd.json` to append to anymore — every class gets its own file.
   - Base SRD classes → [NotOnlyFiendsStudio/Content/packs/srd_core/classes/base/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/classes/base/).
   - Prestige SRD classes → [NotOnlyFiendsStudio/Content/packs/srd_core/classes/prestige/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/classes/prestige/).
   - NPC classes → [NotOnlyFiendsStudio/Content/packs/srd_core/classes/npc/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/classes/npc/).
   - Racial HD drivers → [NotOnlyFiendsStudio/Content/packs/srd_core/racial_hd/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/racial_hd/).
   - Supplement classes → new pack dir.
8. **Run tests** — `dotnet test`.

## PDF extraction workflow (fallback)

1. Locate the class chapter from the table of contents.
2. Parse hit die, skills, BAB/save progressions, and per-level features.
3. For prestige classes, parse the Requirements block into typed prerequisites.
4. Write output and test as in steps 7–8 above.

## Key conventions

- Class IDs: `class:<snake_case>` (`class:fighter`, `class:eldritch_knight`).
- Racial HD IDs: `racial_hd:<name>` (`racial_hd:outsider`, `racial_hd:giant`).
- `Permabuff` `$type` discriminator required on every permabuff object.
- `MinSkillRanks.value` is **whole ranks** (the printed number); engine doubles internally.
- `HasFeat` on a selectable base feat matches any `{featId}_*` variant.

## Reference files

- Schema: [schemas/hddriver.schema.json](../../../schemas/hddriver.schema.json)
- Prompt: [schemas/prompts/extract-class.md](../../../schemas/prompts/extract-class.md)
- Base classes: [NotOnlyFiendsStudio/Content/packs/srd_core/classes/base/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/classes/base/)
- Prestige classes: [NotOnlyFiendsStudio/Content/packs/srd_core/classes/prestige/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/classes/prestige/)
- NPC classes: [NotOnlyFiendsStudio/Content/packs/srd_core/classes/npc/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/classes/npc/)
