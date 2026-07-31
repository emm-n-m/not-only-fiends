# Public SRD feat and domain audit

Audit date: 2026-07-31. Scope is limited to the public packs selected by
`content-public.json`: `srd_core`, `srd_epic`, `srd_monsters`, and
`srd_unearthed_arcana`. I enumerated 327 feat definitions from the public
feat files and 35 domain definitions from `srd_core`. Private packs were not
read. The local `NotOnlyFiendsStudio/Content/srd_html/` mirror is the sole
rules source.

`GrantAbility` only appends descriptive text to `CharacterState.Abilities`;
it does not alter class skills, caster level, feat ownership, or item-use
capability. Findings that call out `GrantAbility` are therefore mechanical,
not merely display-text differences.

| Item | Field | JSON value | SRD value | SRD quote | Severity |
| --- | --- | --- | --- | --- | --- |
| `feat:ability_focus` | prerequisite and required choice | No prerequisites or `selectionRequired`; repeatable feature has no target recorded. | A creature must have a special attack and must choose a special attack each time. | `monsterFeats.html#ability-focus`: “Choose one of the creature’s special attacks.” “Prerequisite: Special attack.” “Each time the creature takes the feat it applies to a different special attack.” | HIGH |
| `feat:empower_spell_like_ability` | prerequisite, choice, daily use text | No prerequisites/choice; description says usable **twice** per day. | Requires an SLA at CL 6+; choose one SLA; it is usable **three** times/day (or fewer if normally limited). | `monsterFeats.html#empower-spell-like-ability`: “Prerequisite: Spell-like ability at caster level 6th or higher.” “Choose one of the creature’s spell-like abilities…” “three times per day (or less…)”. | HIGH |
| `feat:quicken_spell_like_ability` | prerequisite and required choice | No prerequisites or `selectionRequired`. | Requires an SLA at CL 10+ and a selected SLA. | `monsterFeats.html#quicken-spell-like-ability`: “Prerequisite: Spell-like ability at caster level 10th or higher.” “Choose one of the creature’s spell-like abilities…” | HIGH |
| `feat:extra_music` | prerequisite and granted mechanics | No prerequisite and no permabuff. | Requires bardic music and grants four extra uses/day for each acquisition. | `divineFeats.html#extra-music`: “Prerequisite: Bardic music.” “You can use your bardic music four extra times per day.” | HIGH |
| `feat:rapid_reload` | type and prerequisite | `fighterBonus`; no prerequisite. | General feat; must have proficiency with the selected crossbow type. | `featsAll.html#rapid-reload`: heading “RAPID RELOAD [GENERAL]”; “Prerequisite: Weapon Proficiency (crossbow type chosen).” | HIGH |
| `feat:spell_mastery` | type, prerequisite, selection | `general`, no prerequisite, no spell selection. | Special feat; Wizard 1st; choose Int-modifier spells already known on each acquisition. | `featsAll.html#spell-mastery`: heading “SPELL MASTERY [SPECIAL]”; “Prerequisite: Wizard level 1st.” “Each time you take this feat, choose a number of spells equal to your Intelligence modifier that you already know.” | HIGH |
| `domain:animal` and `domain:plant` | granted class skill | Only `GrantAbility` prose; neither adds `skill:knowledge_nature` to current class skills. | Knowledge (nature) becomes a cleric class skill. | `clericDomains.html#animal`: “Add Knowledge (nature) to your list of cleric class skills.” `clericDomains.html#plant`: “Add Knowledge (nature) to your list of cleric class skills.” | HIGH |
| `domain:trickery` | granted class skills | Only `GrantAbility` prose; no `AddClassSkills`. | Bluff, Disguise, and Hide become cleric class skills. | `clericDomains.html#trickery`: “Add Bluff, Disguise, and Hide to your list of cleric class skills.” | HIGH |
| `domain:knowledge` | granted class skills and caster-level bonus | Only `GrantAbility` prose; no class-skill addition or conditional CL modifier. | All Knowledge skills are cleric class skills; divination spells are cast at +1 CL. | `clericDomains.html#knowledge`: “Add all Knowledge skills to your list of cleric class skills.” “You cast divination spells at +1 caster level.” | HIGH |
| `domain:chaos`, `domain:evil`, `domain:good`, and `domain:law` | conditional caster-level bonus | Only `GrantAbility` prose; no evaluated modifier. | The relevant alignment-descriptor spells are cast at +1 CL. | `clericDomains.html#chaos`: “You cast chaos spells at +1 caster level.” Equivalent text appears at `#evil`, `#good`, and `#law`. | HIGH |
| `domain:artifice` | Craft bonus and conditional caster-level bonus | Only `GrantAbility` prose; neither mechanic is represented. | +4 Craft; +1 CL for conjuration (creation), with the Artifice/Creation interaction. | `divineDomains.html#artifice`: “Gain +4 bonus on Craft checks.” “casts conjuration (creation) spells at +1 caster level.” | HIGH |
| `domain:creation` | conditional caster-level bonus | Only `GrantAbility` prose; no evaluated modifier. | +2 CL for conjuration (creation), or +3 with Artifice. | `divineDomains.html#creation`: “Cast conjuration (creation) spells at +2 caster level.” “both the Artifice and Creation domains cast … at +3 caster level.” | HIGH |
| `domain:magic` | item-use effective wizard level | Only `GrantAbility` prose; no capability/effective-level state. | Spell-completion/trigger items use effective wizard level = half cleric level (minimum 1), stacking with wizard levels. | `clericDomains.html#magic`: “Use scrolls, wands, and other devices with spell completion or spell trigger activation as a wizard of one-half your cleric level (at least 1st level).” | HIGH |
| `domain:travel` | granted Survival skill | Only `GrantAbility` prose; does not add `skill:survival` to current class skills. | Survival is added to cleric class skills. | `clericDomains.html#travel`: “Survival is a class skill.” | HIGH |
| `domain:war` | granted selected feats | Only `GrantAbility` prose; no favored-weapon choice, Martial Weapon Proficiency, or Weapon Focus is granted. | Free MWP (if needed) and Weapon Focus for the deity’s favored weapon. | `clericDomains.html#war`: “Free Martial Weapon Proficiency with deity’s favored weapon (if necessary) and Weapon Focus with the deity’s favored weapon.” | HIGH |

## VERIFIED CLEAN

- Public-pack inventory was derived from `content-public.json`, not a historical
  file count: 327 feats across six public feat files and 35 domains across two
  public domain files.
- The 23 core-domain bonus-spell lists were compared against the corresponding
  complete tables in `clericDomains.html`; no wrong level-to-spell link was
  found.
- The 12 non-core SRD deity-domain bonus-spell lists were compared against
  `divineDomains.html`; no wrong level-to-spell link was found.
- Spot checks of feat repeatability and selection metadata agree for
  `feat:skill_focus`, `feat:spell_focus`, and `feat:toughness`.
  In particular, `featsAll.html#skill-focus` says “Choose a skill” and allows
  the feat multiple times; `#spell-focus` says “Choose a school of magic” and
  likewise allows multiple acquisitions; `#toughness` says “A character may
  gain this feat multiple times. Its effects stack.”
- `feat:extra_turning` has a correctly modeled `HasAbility` prerequisite;
  `featsAll.html#extra-turning` says “Prerequisite: Ability to turn or rebuke
  creatures.”

## UNVERIFIABLE

- The current model has no `Special` member in `FeatType`, so the precise
  storage classification for SRD “SPECIAL” feats (notably Spell Mastery) is
  unrepresentable without a schema/model decision. Its prerequisite and choice
  are nevertheless representable and should be modeled.
- The local SRD text identifies several conditional effects but does not define
  an engine-level representation for descriptor/school-specific caster-level
  modifiers or duration-limited domain powers. The report records these as
  missing evaluated mechanics, not as proposed JSON syntax.
- Domain powers whose entire effect is an activation/combat procedure (for
  example Air turning/rebuking, Death Touch, and Sun’s greater turning) are
  retained as `GrantedAbility` text. Their availability is represented on the
  sheet, but their encounter-resolution effects cannot be verified as computed
  by the replay model.

## Proposed focused regression assertions

- `GetAvailableFeats` must reject Extra Music without bardic music, Rapid
  Reload without matching crossbow proficiency, Spell Mastery for a non-wizard,
  and both SLA feats below their source caster-level threshold.
- Selecting Ability Focus, Empower SLA, or Quicken SLA must require and retain
  a distinct valid special-attack/SLA target for each acquisition; Empower SLA
  must state three uses/day.
- Choosing Animal, Plant, Trickery, Knowledge, or Travel must add exactly the
  SRD-listed skills to the owning cleric’s class-skill set.
- Choosing War must request/record the deity’s favored weapon and grant the
  required proficiency (when absent) plus the matching Weapon Focus.
- Evaluating Knowledge, Chaos/Evil/Good/Law, Artifice, and Creation must expose
  their source-scoped caster-level modifiers, including Artifice + Creation =
  +3 for conjuration (creation).
