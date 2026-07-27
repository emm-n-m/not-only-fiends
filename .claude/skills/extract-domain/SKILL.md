---
name: extract-domain
description: Extract cleric domain definitions from a D&D 3.5e source (HTML preferred, PDF fallback). Produces domain JSON matching the DomainDefinition schema.
argument-hint: <source-path> [domain-ids...]
---

# Extract Domains from D&D 3.5e Source Material

You are extracting cleric domain data for the NotOnlyFiendsStudio content pipeline.

## Source selection

Prefer HTML over PDF — each domain is a clean `<h5>` section followed by `<h6>X Domain Spells</h6>` with hyperlinked spell names whose anchors match our spell IDs. PDF remains the fallback for supplements (Mongoose, Sword & Sorcery, homebrew).

Dispatch on the argument:
- Ends in `.html`/`.htm` → HTML extraction (see below).
- Ends in `.pdf` → PDF extraction (original workflow).
- No path given → default to the SRD mirror files below.

### SRD HTML landmark files

- [clericDomains.html](../../../NotOnlyFiendsStudio/Content/srd_html/clericDomains.html) — core 20+ domains (Air, Animal, Artifice, Chaos, Charm, Community, Creation, Darkness, Death, Destruction, Earth, Evil, Fire, Good, Healing, Knowledge, Law, Liberation, Luck, Madness, Magic, Mind, Plant, Protection, Repose, Rune, Scalykind, Strength, Sun, Travel, Trickery, War, Water, Weather).
- [divineDomains.html](../../../NotOnlyFiendsStudio/Content/srd_html/divineDomains.html) — additional divine domains (Book of Exalted Deeds / Book of Vile Darkness style).

## HTML extraction workflow

1. **Read schema & prompt** — [schemas/domain.schema.json](../../../schemas/domain.schema.json) and [schemas/prompts/extract-domain.md](../../../schemas/prompts/extract-domain.md) are authoritative.
2. **Load the HTML file** — each domain is delimited by `<h5><a id="DOMAIN"></a>NAME DOMAIN</h5>` (sometimes the anchor pair includes `<a id="name-domain">` and `<a id="name">`; use the domain-name fragment).
3. **Pick domains** — if the user supplied IDs, extract only those. Otherwise grep the `<h5>` anchors and confirm scope.
4. **Parse each domain block**:
   - A `<p><b>Granted Power</b>: ...</p>` paragraph (or unlabeled narrative paragraph immediately after the `<h5>`) describes the persistent benefit the deity grants. Model as one `GrantAbility` permabuff with `ability.id = "domain_<name>_power"`.
   - `<h6>X Domain Spells</h6>` opens a numbered list (1st–9th). Each spell link's anchor fragment (`spellsAtoB.html#fireball`) maps to our spell ID (hyphens → underscores). Output an ordered 9-element `bonusSpells` array keyed by level (index 0 = 1st).
   - Hyperlinked spells with non-SRD targets (rare) → leave the spell as-is but flag to the user.
5. **Write output** — SRD domains go to [NotOnlyFiendsStudio/Content/packs/srd_core/domains/srd.json](../../../NotOnlyFiendsStudio/Content/packs/srd_core/domains/srd.json) (append). Supplement domains go to a new pack's `domains/` directory.
6. **Run tests** — `dotnet test`.

## PDF extraction workflow (fallback)

1. Locate the domain section from the table of contents.
2. Parse each domain's granted power and spell list.
3. Write output and test as in steps 5–6 above.

## Key conventions

- Domain IDs: `domain:<snake_case>` (`domain:knowledge`, `domain:corruption`).
- Granted-power ability IDs: `domain_<name>_power` (`domain_knowledge_power`).
- Spell IDs in `bonusSpells`: `spell:<snake_case>` (must match existing SRD spell IDs).

## Reference files

- Schema: [schemas/domain.schema.json](../../../schemas/domain.schema.json)
- Prompt: [schemas/prompts/extract-domain.md](../../../schemas/prompts/extract-domain.md)
- Existing domains: [NotOnlyFiendsStudio/Content/packs/srd_core/domains/srd.json](../../../NotOnlyFiendsStudio/Content/packs/srd_core/domains/srd.json)
