---
name: extract-race
description: Extract race definitions from a D&D 3.5e source (HTML preferred, PDF fallback). Reads the source, identifies playable races, and produces race JSON + racial HD driver JSON.
---

# Extract Races from D&D 3.5e Source Material

You are extracting race data for the NotOnlyFiendsStudio content pipeline.

## Source selection

Prefer HTML over PDF — the d20srd.org mirror at [NotOnlyFiendsStudio/Content/srd_html/](../../../NotOnlyFiendsStudio/Content/srd_html/) has stable anchors, consistent templates, and hyperlinked cross-references that parse cleanly. PDFs stay the fallback for non-SRD supplements (Mongoose, S&S, homebrew) that have no HTML mirror.

Dispatch on the argument:
- Ends in `.html`/`.htm` → HTML extraction (see below).
- Ends in `.pdf` → PDF extraction (original workflow).
- No path given → ask the user; default suggestion is the local SRD mirror files below.

### SRD HTML landmark files

- [races.html](../../../NotOnlyFiendsStudio/Content/srd_html/races.html) — core PC races (human, dwarf, elf, gnome, half-elf, half-orc, halfling).
- [monstersAsRaces.html](../../../NotOnlyFiendsStudio/Content/srd_html/monstersAsRaces.html) — monster entries playable as PCs (aasimar, tiefling, drow, goblinoids, etc.).
- [psionicRaces.html](../../../NotOnlyFiendsStudio/Content/srd_html/psionicRaces.html) — dromite, duergar (psionic), elan, half-giant, maenad, xeph.
- [unearthedRaces.html](../../../NotOnlyFiendsStudio/Content/srd_html/unearthedRaces.html) — UA variant races.
- Monsters A–Z (`monstersA.html` … `monstersZ.html`) — statblock-style entries for monster races.

## HTML extraction workflow

1. **Read the schema & prompt** — [schemas/race.schema.json](../../../schemas/race.schema.json) and [schemas/prompts/extract-race.md](../../../schemas/prompts/extract-race.md) are authoritative for field names and enums.
2. **Load the HTML file** — use Read. Each race is delimited by `<h3><a id="RACE"></a>RACE NAME</h3>` (monster-as-races and psionic races use the same pattern). Extract the block until the next `<h3>` or document end.
3. **Pick races** — if the user supplied race IDs, extract only those. Otherwise list the anchors found (`grep for <h3><a id=`) and ask which to extract.
4. **Parse each race block** — traits are in `<ul><li><p>...</p></li></ul>`:
   - `<p class="initial">` on the first `<li>` is usually the ability-score line (e.g., `+2 Constitution, –2 Charisma.`). Humans/half-elves use it for size instead.
   - `<p>... base land speed is N feet.</p>` → `speeds.land = N`.
   - Size word (Small/Medium/Large) appears either in a `<p class="initial">` sizing paragraph or in a leading trait.
   - Named racial traits use nested anchors like `<a id="dwarf-stonecunning"></a><p>Stonecunning: …</p>` → one `GrantAbility` permabuff per trait.
   - `+N racial bonus on X` patterns inside `<p>` without a named anchor → `ModifyAttribute` permabuffs.
   - Hyperlinked skills (`<a href="skillsAll.html#search">Search</a>`) give canonical skill IDs — the anchor fragment matches our snake_case skill IDs (prefix with `skill:` for the content field).
   - "Automatic Languages / Bonus Languages" → flavor only (no engine field yet).
   - "Favored Class" → flavor only (favored-class mechanic isn't modeled).
5. **Transcribe level adjustment — never estimate it.** Core SRD races are LA 0. For
   monster-as-races, copy the explicit `Level Adjustment` line. **If the source prints no
   Level Adjustment, write `null`, not 0.** In 3.5 a printed LA is what marks a creature as
   PC-legal, so its absence is a real statement ("this was never priced as a PC race") and is
   a different claim from 0 ("playable at no cost", like a Human). Null contributes 0 to ECL,
   so this costs nothing mechanically and keeps the provenance honest. Do not infer an LA from
   an "Advancement: by character class" or "Favored Class" line — those are NPC-advancement
   fields that appear on plenty of unplayable monsters. If a value is wanted for play, that is
   a house rule for the user to make per race, not an extraction output.
6. **Racial HD** — only for monster/psionic races that list Hit Dice. Derive as in step 7 below.
7. **Extract racial HD driver** (if any) — for each race with racial HD:
   - Class skills from the race's Skills line or inferred from type (outsider/dragon/etc.).
   - Creature-type defaults: outsider = d8/8sp/good BAB/3 good saves; dragon = d12/6sp/good BAB/3 good saves; magical beast = d10/2sp/good BAB/Fort+Ref good.
   - Prerequisite: `{"$type": "HasRace", "raceId": "<id>"}`.
8. **Write output** — into the appropriate pack:
   - Core PC races → [NotOnlyFiendsStudio/Content/packs/srd_core/races/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/races/).
   - Monster races → [NotOnlyFiendsStudio/Content/packs/srd_monsters/races/](../../../NotOnlyFiendsStudio/Content/packs/srd_monsters/races/).
   - Racial HD drivers → sibling `racial_hd/` directory in the same pack.
   - Group by logical batch (e.g., `srd_core_races.json`) — all files are JSON arrays.
9. **Run tests** — `dotnet test` to verify content loads and schemas validate.

## PDF extraction workflow (fallback)

1. Read table of contents (pages 1–5) to locate monster/race chapters, then scan those pages.
2. Identify entries with "Advancement by character class" or explicit PC notes.
3. For each race, parse the stat block header for size/type/subtypes/speeds, the Skills line for class skills, and the special-ability paragraphs for permabuffs.
4. Derive ability modifiers as **printed score minus the nonelite array** (11/11/11/10/10/10):
   subtract 11 from an odd printed score, 10 from an even one. Verified against the PCGen RSRD
   LST `BONUS:STAT` rows (succubus 13/13/13/16/14/26 → +2/+2/+2/+6/+4/+16, erinyes
   21/21/21/14/18/20 → +10/+10/+10/+4/+8/+10). Never use flat score−10 — odd scores encode an
   11 base, and a stock character built on the nonelite array must reproduce the statblock.
5. Transcribe level adjustment exactly as the HTML rule above: copy a printed LA, and write
   `null` when the book prints none — never estimate one from ability power or CR.
6. Write output and run tests as in steps 8–9 above.

## Key conventions

- Race IDs: `race:<snake_case>` (`race:dwarf`, `race:half_elf`, `race:aasimar`, `race:juvenile_nabassu`).
- Racial HD driver IDs: `racial_hd:<race_id>`.
- Subtypes: include alignment + extraplanar + creature subtypes (`["chaotic", "evil", "extraplanar", "tanar'ri"]`).
- Ability modifiers are printed score minus the nonelite array (odd score → −11, even → −10),
  so odd modifiers are normal. Base creatures for stock characters use 11s where the printed
  score is odd, 10s where even.
- Include all six abilities when any are modified (unmodified → 0); use `null` only when no racial modifiers exist (human).
- Omit `racialHDDriverId` when the race has no racial HD.

## Reference files

- Schema: [schemas/race.schema.json](../../../schemas/race.schema.json)
- Prompt: [schemas/prompts/extract-race.md](../../../schemas/prompts/extract-race.md)
- Existing core races: [NotOnlyFiendsStudio/Content/packs/srd_core/races/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/races/)
- Existing monster races: [NotOnlyFiendsStudio/Content/packs/srd_monsters/races/](../../../NotOnlyFiendsStudio/Content/packs/srd_monsters/races/)
- Existing racial HD drivers: [NotOnlyFiendsStudio/Content/packs/srd_core/racial_hd/](../../../NotOnlyFiendsStudio/Content/packs/srd_core/racial_hd/)
