# 3.5e Character Leveling Tool — Architecture

## Overview

A character building and display tool for D&D 3.5e, mechanically accurate through epic levels.

- **Studio**: C# class library (net10.0) — pure rules logic, no UI dependencies. Produces content.
- **Feed**: Blazor Server app — character sheet display, level builder, and REST API. Loads content from filesystem. Displays content.
- **Future**: Unity integration — engine library referenced directly, shared with visual novel project

### Core Principle: Store Inputs, Compute Everything

The only persisted data is user decisions. All derived values are computed by ordered replay of the character's HD timeline. There is no cached or stored state for computed values.

### Human and agent interfaces are peers

The Blazor UI and REST API are two consumers of the same replay and content services. Agent
support is a product boundary, not a development convenience:

| Interface | Contract |
|---|---|
| Human UI | Builder and sheet views expose choices and computed state interactively. |
| Agent API | Discovery, next-step previews, simulation, mutation, evaluation, and cleanup are available under `/api/*` with OpenAPI metadata. |
| Agent workflows | Portable skills under `agent-skills/skills/` teach Codex and Claude Code the same safe operating and content-authoring procedures. |

`.agents/skills/` and `.claude/skills/` are generated host discovery trees. The neutral source,
platform overlays, thin orchestration adapters, and parity check are documented in
`agent-skills/README.md`. Domain workflow knowledge must not diverge by agent host.

---

## Three-Layer Architecture

| Layer | What | Where |
|-------|------|-------|
| **1. Object Definitions** | C# abstract classes and interfaces — the structural grammar of the system. What a Driver is, what a Permabuff is, what a Character looks like. | `Studio/Models/` |
| **2. Object Implementations** | Concrete content — Fighter class, Half-Fiend template, Outsider racial HD. JSON data files deserialized into Layer 1 types. SRD-safe content only in engine; homebrew in consumer projects. | `Studio/Content/` |
| **3. Character Instances** | A specific character's initial state + per-tick user choices. JSON save files. | Consumer project |

### Separation Boundary

The engine (Layers 1+2) has zero knowledge of any specific character or UI. It provides:
- The type system (models)
- The replay engine (evaluation)
- SRD content (data)
- Content loading (deserialization of custom JSON into engine types)
- Data-driven rules (`GameRules`)

Consumer projects (Layer 3 + UI) provide:
- Character build files
- Homebrew content
- Display and interaction

---

## Layer 1: Object Definitions (Class Hierarchy)

### Character (persisted — the save format)

```csharp
public class Character
{
    public string Name { get; set; }

    // Initial State
    public string RaceId { get; set; }
    public List<string> TemplateIds { get; set; } = new();
    // Optional 1-based HD at which an acquired template becomes active. Missing entries
    // preserve the creation-time behavior used by inherited templates and old saves.
    public Dictionary<string, int> TemplateAcquisitionHD { get; set; } = new();
    public AbilityScoreSet BaseAbilityScores { get; set; }

    // HD Timeline — the build
    public List<Tick> Ticks { get; set; } = new();

    // Permanent events between ticks (Tomes, Wish inherent bonuses)
    public List<PermanentEvent> PermanentEvents { get; set; } = new();

    // Post-tick modifiers
    public List<EquipmentEntry> Equipment { get; set; } = new();
}

public class PermanentEvent
{
    public int BeforeTick { get; set; }    // applied before this tick index (0 = before HD 1)
    public List<Permabuff> Permabuffs { get; set; } = new();
}

public class Tick
{
    public string DriverId { get; set; }       // e.g., "racial_hd:outsider", "class:sorcerer"
    public TickChoices Choices { get; set; } = new();
}

public class TickChoices
{
    public Ability? AbilityIncrease { get; set; }                    // every 4th HD
    public List<string>? FeatIds { get; set; }                       // when feat slot(s) open
    public List<SkillAllocation>? SkillAllocations { get; set; }
    public List<SpellSelection>? SpellSelections { get; set; }
    public Dictionary<string, List<string>>? ClassFeatureChoices { get; set; }
}

public class AbilityScoreSet
{
    public int STR, DEX, CON, INT, WIS, CHA { get; set; }

    public int GetScore(Ability ability) => ability switch { ... };
    public void SetScore(Ability ability, int value) { ... }
    public static int Modifier(int score) => (score - 10) / 2;
}
```

### CharacterState (computed — never persisted)

```csharp
public class CharacterState
{
    // Identity
    public string RaceId { get; set; }
    public CreatureType Type { get; set; }
    public HashSet<string> Subtypes { get; set; } = new();
    public Size Size { get; set; }
    public Alignment Alignment { get; set; }
    public List<string> TemplateIds { get; set; } = new();

    // Ability Scores (fully modified at current HD)
    public AbilityScoreSet AbilityScores { get; set; } = new();

    // Progression
    public int TotalHD { get; set; }
    public List<string> HDList { get; set; } = new();           // ordered driver IDs
    public Dictionary<string, int> ClassLevels { get; set; } = new();

    // Combat — pre-epic base values (frozen at epic threshold)
    public int BaseBAB { get; set; }
    public SaveSet BaseSaves { get; set; } = new();

    // Epic bonuses (past epic threshold)
    public int EpicAttackBonus { get; set; }
    public int EpicSaveBonus { get; set; }

    // Effective totals (base + epic)
    public int EffectiveBAB => BaseBAB + EpicAttackBonus;
    public SaveSet EffectiveSaves => new() { ... };

    // HP
    public int HP { get; set; }

    // Skills — ranks stored as half-ranks (int). 5 ranks = 10, 2.5 ranks = 5. Display divides by 2.
    public Dictionary<string, int> SkillRanks { get; set; } = new();
    public HashSet<string> ClassSkills { get; set; } = new();
    public int UnspentSkillPoints { get; set; }
    public int MaxHalfRanks { get; set; }     // set by engine from GameRules per tick

    // Feats — typed slots
    public List<string> Feats { get; set; } = new();
    public List<FeatSlot> FeatSlots { get; set; } = new();
    public int PendingFeatSlots => FeatSlots.Count(s => s.Restriction == null);
    public int PendingBonusFeatSlots => FeatSlots.Count(s => s.Restriction != null);

    // Spellcasting
    public Dictionary<string, SpellcastingState> Spellcasting { get; set; } = new();

    // Domains — each selected domain is owned by the class that granted the pick.
    public List<string> Domains { get; set; } = new();
    public Dictionary<string, string> DomainOwners { get; set; } = new(); // domainId → granting classId
    public Dictionary<string, int> PendingDomainSelections { get; set; } = new(); // classId → remaining picks

    // Combat — natural
    public int NaturalArmor { get; set; }
    public List<NaturalAttack> NaturalAttacks { get; set; } = new();

    // Level Adjustment / ECL
    public int LevelAdjustment { get; set; }
    public int ECL => TotalHD + LevelAdjustment;

    // Special Abilities
    public List<GrantedAbility> Abilities { get; set; } = new();
    public List<SLA> SLAs { get; set; } = new();
    public HashSet<string> Immunities { get; set; } = new();
    public Dictionary<string, int> Resistances { get; set; } = new();
    public List<DREntry> DamageReduction { get; set; } = new();
    public int? SpellResistance { get; set; }

    // Movement
    public Dictionary<MovementMode, int> Speeds { get; set; } = new();

    // Validation
    public List<string> Warnings { get; set; } = new();
}

public class FeatSlot
{
    public string? Restriction { get; set; }    // null = unrestricted, "fighter_bonus" = fighter bonus feats
}
```

### Driver (abstract base) + HDDriver

All progression sources inherit from Driver. `HDDriver` is the unified concrete class for both class drivers and racial HD drivers, distinguished by `DriverKind`.

```csharp
public enum DriverKind { Class, RacialHD }

[JsonDerivedType(typeof(HDDriver), "HDDriver")]
public abstract class Driver
{
    public string Id { get; set; }              // e.g., "class:fighter", "racial_hd:outsider"
    public string Name { get; set; }
    public List<Prerequisite> Prerequisites { get; set; } = new();
    public ViolationEffect? ViolationEffect { get; set; }

    public abstract List<Permabuff> GetPermabuffs(CharacterState state, int driverLevel, GameRules rules);
}

public class HDDriver : Driver
{
    public DriverKind Kind { get; set; }        // Class or RacialHD
    public int HitDie { get; set; }
    public int SkillPointsPerLevel { get; set; }
    public List<string> ClassSkills { get; set; } = new();
    public BABProgression BABProgression { get; set; }
    public SaveProgression SaveProgression { get; set; }
    public int? MaxLevel { get; set; }

    public SpellcastingProgression? Spellcasting { get; set; }

    public Dictionary<int, List<Permabuff>> LevelPermabuffs { get; set; } = new();
    public List<Permabuff> PerLevelPermabuffs { get; set; } = new();

    public override List<Permabuff> GetPermabuffs(CharacterState state, int driverLevel, GameRules rules)
    {
        // HD, skills, class skills always
        // BAB + saves only pre-epic (state.TotalHD <= rules.EpicThreshold)
        // Spellcasting progression if applicable
        // PerLevelPermabuffs + level-specific LevelPermabuffs
    }
}
```

### TemplateDriver

Templates don't contribute HD/BAB/saves — they modify what's already there.

Template scaling uses **declarative mutations** (POST/DELETE/PUT): each HD threshold declares exactly what to add, remove, or replace at that point. Thresholds fire once during replay when `totalHD == key`. `ScalingFormulas` are recalculated every tick (via `SetAttribute`, which naturally overwrites).

```csharp
public class TemplateDriver
{
    public string Id { get; set; }
    public string Name { get; set; }
    public TemplateAcquisitionKind AcquisitionKind { get; set; }

    // One-time modifications when the template is applied. Inherited templates apply at
    // creation; acquired templates apply at their recorded acquisition HD.
    public CreatureType? TypeOverride { get; set; }
    public List<string> SubtypeAdditions { get; set; } = new();
    public AbilityScoreSet? AbilityModifiers { get; set; }
    public int? NaturalArmor { get; set; }
    public Dictionary<MovementMode, int> SpeedModifiers { get; set; } = new();
    public int LevelAdjustment { get; set; }
    public List<NaturalAttack> NaturalAttacks { get; set; } = new();
    public List<Permabuff> CreationPermabuffs { get; set; } = new();

    // Scaling: keyed by exact HD. Permabuffs fire ONCE when totalHD == key.
    public SortedDictionary<int, List<Permabuff>> ScalingPermabuffs { get; set; } = new();

    // Formula-based abilities recalculated every tick (SetAttribute — naturally overwrites)
    public List<ScalingFormula> ScalingFormulas { get; set; } = new();

    public List<Permabuff> GetTickPermabuffs(int totalHD, CharacterState state) { ... }
}

public enum TemplateAcquisitionKind { Internal, Inherited, Acquired }
```

### GameRules + PermabuffContext

All D&D 3.5e rule parameters are encapsulated in `GameRules` with a `Standard35e()` factory. This enables variant rule sets without code changes.

`PermabuffContext` bundles `CharacterState` + `GameRules` + `IContentLookup` and is passed to every `Permabuff.Apply()` call.

```csharp
public class GameRules
{
    public int EpicThreshold { get; init; } = 20;
    public int AbilityIncreaseInterval { get; init; } = 4;
    public bool FirstHDMaxHP { get; init; } = true;
    public int FirstHDSkillMultiplier { get; init; } = 4;
    public int RacialBonusSkillFirstHDMultiplier { get; init; } = 4;
    public HashSet<int> StandardFeatHDs { get; init; } = new() { 1, 3, 6, 9, 12, 15, 18 };
    public int EpicFeatInterval { get; init; } = 3;
    public int EpicFeatStartHD { get; init; } = 21;
    public Func<int, int> MaxHalfRanks { get; init; }
    public Func<BABProgression, int, int> CalculateBABTotal { get; init; }
    public Func<ProgressionRate, int, int> CalculateSaveTotal { get; init; }

    public static GameRules Standard35e() => new();
    public bool GrantsStandardFeat(int totalHD) => StandardFeatHDs.Contains(totalHD);
    public bool GrantsEpicFeat(int totalHD) => totalHD >= EpicFeatStartHD && ...;
}

public interface IContentLookup
{
    bool TryGetFeat(string id, out FeatDefinition? feat);
}

public class PermabuffContext
{
    public CharacterState State { get; }
    public GameRules Rules { get; }
    public IContentLookup? Content { get; }
}
```

### Permabuff (class hierarchy)

Permabuffs are permanent, irreversible modifications applied to CharacterState during tick replay. Named to distinguish from temporary in-game buffs (Bull's Strength, Haste, etc.) which are not modeled by the engine.

All permabuffs receive a `PermabuffContext` containing state, rules, and content lookup.

```csharp
public abstract class Permabuff
{
    public abstract void Apply(PermabuffContext ctx);

    // Backward-compatible convenience: apply with default rules and no content
    public void Apply(CharacterState state) => Apply(new PermabuffContext(state, GameRules.Standard35e()));
}

// --- Computed Permabuffs (no user input) ---

public class AddHitDie : Permabuff         // HP: max at first HD (rules), avg thereafter + CON mod
public class AddBAB : Permabuff            // Incremental BAB from rules.CalculateBABTotal
public class AddSaves : Permabuff          // Incremental saves from rules.CalculateSaveTotal
public class GrantSkillPoints : Permabuff  // (base + INT mod) * rules.FirstHDSkillMultiplier at HD 1
public class AddClassSkills : Permabuff    // Adds skill IDs to ClassSkills set

// --- Grant/Revoke Permabuffs ---

public class GrantAbility : Permabuff      // Adds a GrantedAbility
public class RevokeAbility : Permabuff     // Removes by ID
public class GrantSLA : Permabuff          // Adds a spell-like ability
public class RevokeSLA : Permabuff         // Removes by ID
public class GrantBonusFeat : Permabuff    // Adds feat + cascades its GrantedPermabuffs via ctx.Content

// --- Attribute Permabuffs (typed targeting) ---

public enum AttributeTarget
{
    NaturalArmor, SpellResistance, LevelAdjustment, Resistance, AbilityScore
}

public class ModifyAttribute : Permabuff
{
    public AttributeTarget Target { get; set; }
    public int Value { get; set; }
    public string? ResistanceElement { get; set; }    // for Target == Resistance
    public Ability? AbilityScore { get; set; }        // for Target == AbilityScore
}

public class SetAttribute : Permabuff
{
    public AttributeTarget Target { get; set; }
    public int Value { get; set; }
    public string? ResistanceElement { get; set; }
    public Ability? AbilityScore { get; set; }
}

// --- Slot Permabuffs ---

public class GrantFeatSlot : Permabuff
{
    public string? Restriction { get; set; }   // null = unrestricted, "fighter_bonus" = fighter bonus
    // Adds FeatSlot { Restriction } to state.FeatSlots
}

// --- Spellcasting Permabuffs ---

public class AdvanceSpellcasting : Permabuff   // Advances existing caster level by type
public class UpdateSpellcasting : Permabuff    // Sets/creates spellcasting state for a class
```

### Feat Definition

```csharp
public class FeatDefinition
{
    public string Id { get; set; }
    public string Name { get; set; }
    public FeatType Type { get; set; }
    public List<Prerequisite> Prerequisites { get; set; } = new();
    public List<Permabuff> GrantedPermabuffs { get; set; } = new();
    public bool Repeatable { get; set; }
    public string? SelectionRequired { get; set; }
}
```

### Prerequisite (class hierarchy)

```csharp
public abstract class Prerequisite
{
    public abstract bool IsMet(CharacterState state);
    public abstract string Description { get; }
}

// Concrete: MinBAB, MinAbility, MinSkillRanks, MinClassLevel, MinHD,
//           MinCasterLevel, CanCastSpellLevel, HasFeat, HasRace,
//           AlignmentReq, CustomPrerequisite
```

### Race Definition + ScalingFormula

```csharp
public class RaceDefinition
{
    public string Id { get; set; }
    public string Name { get; set; }
    public CreatureType Type { get; set; }
    public List<string> Subtypes { get; set; } = new();
    public Size Size { get; set; }
    public Dictionary<MovementMode, int> Speeds { get; set; } = new();
    public AbilityScoreSet? AbilityModifiers { get; set; }
    public int LevelAdjustment { get; set; }
    public int BonusFeats { get; set; }
    public int BonusSkillPointsPerHD { get; set; }
    public string? RacialHDDriverId { get; set; }
    public List<Permabuff> RacialPermabuffs { get; set; } = new();
    public List<ScalingFormula> ScalingFormulas { get; set; } = new();
}

public class ScalingFormula
{
    public AttributeTarget Target { get; set; }
    public string? ResistanceElement { get; set; }
    public Ability? AbilityScore { get; set; }
    public Formula Formula { get; set; } = new();
}
```

### Formula (string DSL)

Formulas are string expressions evaluated against `CharacterState`. Parsed at content load time, evaluated during replay.

```csharp
public class Formula
{
    public string Expression { get; set; }      // e.g., "10 + TotalHD / 2 + Mod(CON)"
    public int Evaluate(CharacterState state) { /* parse tree evaluated against state */ }
}
```

#### DSL Grammar

```
expr     = term (('+' | '-') term)*
term     = factor (('*' | '/') factor)*
factor   = number | attribute | function '(' args ')' | '(' expr ')'

number   = integer literal

attribute:
  TotalHD            — total hit dice
  BaseBAB            — pre-epic BAB
  EffectiveBAB       — BAB + epic attack bonus
  SpellResistance    — current SR (0 if none)

function:
  Mod(ability)       — ability modifier, e.g., Mod(CON)
  Score(ability)     — ability score, e.g., Score(INT)
  ClassLevel(id)     — levels in a specific class, e.g., ClassLevel(sorcerer)
  CasterLevel(id)    — caster level for a class
  min(expr, expr)    — minimum of two values
  max(expr, expr)    — maximum of two values
```

Division is integer (floor). Parsed once at content load; evaluation is just tree-walking.

### Enums

```csharp
public enum Ability { STR, DEX, CON, INT, WIS, CHA }
public enum Size { Fine, Diminutive, Tiny, Small, Medium, Large, Huge, Gargantuan, Colossal }
public enum CreatureType { Aberration, Animal, Construct, Dragon, Elemental, Fey, Giant, Humanoid, MagicalBeast, MonstrousHumanoid, Ooze, Outsider, Plant, Undead, Vermin }
public enum MovementMode { Land, Fly, Swim, Burrow, Climb }
public enum Alignment { LG, LN, LE, NG, N, NE, CG, CN, CE }
public enum CastingType { Arcane, Divine }
public enum BABProgression { Good, Average, Poor }
public enum ProgressionRate { Good, Poor }
public enum FeatType { General, FighterBonus, Metamagic, ItemCreation, Epic, Divine, Vile, Exalted, Tactical, Other }
public enum AttributeTarget { NaturalArmor, SpellResistance, LevelAdjustment, Resistance, AbilityScore }
public enum DriverKind { Class, RacialHD }
```

---

## Replay Studio

### Evaluation Algorithm

```csharp
public class ReplayStudio
{
    private readonly ContentRegistry _content;
    private readonly GameRules _rules;

    public ReplayStudio(ContentRegistry content, GameRules? rules = null)
    {
        _content = content;
        _rules = rules ?? GameRules.Standard35e();
    }

    public CharacterState Evaluate(Character character, int? upToHD = null)
    {
        var state = new CharacterState();
        var ctx = new PermabuffContext(state, _rules, _content);

        // 1. Apply race
        // 2. Apply creation-time templates (inherited templates and old saves)
        // 3. Apply base ability scores (added to racial/template modifiers)

        // 4. Process each tick
        for (int i = 0; i < maxTick; i++)
        {
            // Apply permanent events scheduled before this tick
            state.TotalHD = i + 1;
            state.MaxHalfRanks = _rules.MaxHalfRanks(state.TotalHD);

            // Apply templates acquired at this HD before the driver's grants. Their
            // ability modifiers affect this tick's skill points and prerequisites; accrued
            // values from earlier ticks are not reopened. Hit-die floors explicitly restate
            // current and future dice as required by the SRD.
            // a. Validate driver prerequisites → Warnings
            // b. Track class levels (driver is HDDriver hd && hd.Kind == DriverKind.Class)
            // c. Get and apply driver permabuffs (via ctx)
            // d. Epic progression (past _rules.EpicThreshold)
            // e. Template tick injections from templates active so far
            // f. Racial bonus skill points per HD
            // g. Race scaling formulas → SetAttribute
            // h. Ability score increase (every _rules.AbilityIncreaseInterval HD)
            // i. Feat slots: _rules.GrantsStandardFeat(), _rules.GrantsEpicFeat(), racial bonus
            // j. Resolve user choices: feats, skills, spells
        }

        // 5. Apply equipment (post-tick only)
        return state;
    }
}
```

### BAB and Save Formulas (pre-epic only, from GameRules)

```
Good BAB:     class_level
Average BAB:  floor(class_level * 3/4)
Poor BAB:     floor(class_level / 2)

Good Save:    2 + floor(class_level / 2)
Poor Save:    floor(class_level / 3)
```

Applied incrementally: at each tick, compute total for current class level minus total for previous class level = increment for this tick.

### Feat Slot Schedule (from GameRules)

```
Standard:     HD 1, 3, 6, 9, 12, 15, 18     (StandardFeatHDs set)
Epic bonus:   HD 21, 24, 27, 30, 33...       (EpicFeatStartHD + every EpicFeatInterval)
Racial bonus: per race definition             (e.g., Human: 1 at HD 1)
Class bonus:  per driver LevelPermabuffs      (GrantFeatSlot permabuffs with typed FeatSlot)
```

---

## Content Pipeline

### ContentTypeHandler System

Content loading uses a generic handler pattern. Each content type has a registered `ContentTypeHandler<T>` that knows its directory name, how to deserialize JSON files, and how to register items.

```csharp
public abstract class ContentTypeHandler
{
    public string DirectoryName { get; }
    public abstract void LoadFromDirectory(string basePath, JsonSerializerOptions options);
    public abstract void LoadFromJson(string json, JsonSerializerOptions options);
}

public class ContentTypeHandler<T> : ContentTypeHandler where T : class
{
    private readonly Action<T> _register;
    // Scans directory recursively, deserializes each file as List<T>, calls _register per item
}
```

**Registration in ContentRegistry:**
```csharp
RegisterContentType(new ContentTypeHandler<RaceDefinition>("races", race => _races[race.Id] = race));
RegisterContentType(new ContentTypeHandler<Driver>("classes", driver => _drivers[driver.Id] = driver));
RegisterContentType(new ContentTypeHandler<Driver>("racial_hd", driver => _drivers[driver.Id] = driver));
RegisterContentType(new ContentTypeHandler<TemplateDriver>("templates", template => _templates[template.Id] = template));
RegisterContentType(new ContentTypeHandler<FeatDefinition>("feats", feat => _feats[feat.Id] = feat));
```

### Uniform List Format

**Every content file is `List<T>`.** A single item is a list of one. Multiple files of the same type get their lists merged. Same-ID items from later files replace earlier ones (homebrew override).

```
feats/general.json          →  List<FeatDefinition> (18 feats)
feats/fighter_bonus.json    →  List<FeatDefinition> (3 feats)
feats/homebrew/custom.json  →  List<FeatDefinition> (1 feat)
                            →  22 feats total (merged)
```

### Multiple Content Roots

```csharp
registry.LoadContent("Content/srd", "Content/homebrew");
// Later roots override earlier roots for same IDs
```

### Content Loading (Blazor Server)

`ServerContentService` loads content directly from the filesystem — no manifest or build-time pipeline needed.

**Two modes:**
- **Local dev:** Finds the solution root, reads `content-public.json` for bundled pack IDs from `NotOnlyFiendsStudio/Content/packs/`, and optionally reads `.env` for `EXTRA_PACKS_PATH` and `CHARACTERS_PATH`.
- **Docker:** Env vars (`Content__BundledPacksPath`, `Content__ExtraPacksPath`, `Content__CharactersPath`) provide explicit paths. Bundled packs are baked into the image; extra packs and characters are volume-mounted.

Bundled packs are filtered through `content-public.json`. Private/extra packs load from `EXTRA_PACKS_PATH` in `.env` (local dev) or the `Content__ExtraPacksPath` env var (Docker). Pack load order is resolved via `PackLoader` with priority-based ordering.

### Content Validation

`ContentRegistry.Validate()` checks cross-references after all loading:
- Empty IDs
- Race → RacialHDDriverId exists as a driver
- HasFeat prerequisites → feat ID exists
- MinClassLevel prerequisites → driver ID exists
- GrantBonusFeat → feat ID exists

```csharp
registry.LoadContentDirectory(path);
registry.Validate();
if (registry.HasErrors)
    foreach (var error in registry.Errors) // ContentError(Kind, Message)
        ...
```

---

## Layer 2: Content Data

JSON files deserialized into Layer 1 types. All files use list format. Content is organized into packs, and each pack organizes files by category:

```
Content/packs/
  srd_core/
    pack.json
    classes/
      base/
        fighter.json      ← List<Driver> with $type: "HDDriver", kind: "Class"
        sorcerer.json
        barbarian.json
        cleric.json
      prestige/
        eldritch_knight.json
    racial_hd/
      outsider.json       ← List<Driver> with $type: "HDDriver", kind: "RacialHD"
    races/
      human.json          ← List<RaceDefinition>
      outsider.json
    templates/
      half_fiend.json     ← List<TemplateDriver>
    feats/
      general.json        ← List<FeatDefinition>
      fighter_bonus.json
      epic.json
    domains/
      srd.json            ← List<DomainDefinition>
    spells/
      srd.json            ← List<SpellDefinition>
    skills/
      srd.json            ← List<SkillDefinition>
```

---

## Project Structure

```
NotOnlyFiendsStudio/                          # Class library — "Studio" produces content
  Models/
    Character.cs                      # Character, Tick, TickChoices
    CharacterState.cs                 # Computed state, FeatSlot
    Driver.cs                         # Driver (abstract), HDDriver, DriverKind
    Template.cs                       # TemplateDriver
    Permabuff.cs                      # Permabuff hierarchy, PermabuffContext
    Feat.cs                           # FeatDefinition
    Race.cs                           # RaceDefinition, ScalingFormula
    Prerequisite.cs                   # Prerequisite hierarchy
    Enums.cs                          # Ability, Size, Alignment, AttributeTarget, etc.
    Formula.cs                        # Formula DSL
    GameRules.cs                      # GameRules, PermabuffContext, IContentLookup
  Studio/
    ReplayEngine.cs                   # Core ReplayStudio: Character → CharacterState
    ContentRegistry.cs                # Loads, indexes, validates all content JSON
    ContentTypeHandler.cs             # Generic content loading handlers
  Content/
    packs/                           # Pack directories with pack.json manifests

NotOnlyFiendsFeed/                             # Blazor Server app — "Feed" displays content
  Components/
    Pages/
      SheetView.razor                 # Read-only character sheet display at any HD
      BuilderView.razor               # Full builder: race, abilities, HD timeline, feats, skills, spells, domains, permanent events, equipment
      ImportView.razor                # PCGen .pcg file import
      SettingsView.razor              # Read-only view of loaded packs and content summary
    Layout/
      MainLayout.razor                # App shell with sidebar navigation
    App.razor                         # Root HTML document
    Routes.razor                      # Router
    SearchSelect.razor                # Generic searchable dropdown component
  Services/
    ServerContentService.cs           # Singleton content loader (filesystem, dual-mode)
    CharacterStore.cs                 # Server-side character persistence (atomic writes)
    AgentApiService.cs                # REST API logic: catalog, evaluate, next-step, CRUD
    BrowserFileService.cs             # IJSRuntime wrapper for browser file download/upload
  Contracts/
    ApiContracts.cs                   # REST API request/response DTOs
  Program.cs                          # Host setup, service registration, API endpoint mapping
  wwwroot/                            # Static assets (CSS, JS, icons)

NotOnlyFiendsStudio.Tests/                    # xUnit test suite (1,000+ cases)
  ReplayStudioTests.cs
  DriverTests.cs
  PermabuffTests.cs
  PrerequisiteTests.cs
  TemplateTests.cs
  FeatTests.cs
  SpellcastingTests.cs
  DomainTests.cs
  EpicIntegrationTests.cs
  ContentValidationTests.cs
  ContentConflictTests.cs
  PackLoaderTests.cs
  SpellContentTests.cs
  JsonContentTests.cs
  FormulaTests.cs
  CoreModelTests.cs
  PcGen/                              # PCGen character reconstruction tests
  Api/
    AgentApiServiceTests.cs           # REST API integration tests

Repo root:
  content-public.json                 # Checked-in bundled public pack allowlist
  .env                                # Optional untracked local paths (characters, extra packs, PCGen)
  Dockerfile                          # Multi-stage build for Feed app
  docker-compose.yml                  # Single-service with volume mounts
```

---

## Resolved Design Decisions

- **Store inputs, compute everything**: No cached derived values. Full ordered replay.
- **Permabuff naming**: The atomic unit of character modification is called `Permabuff` — distinguishes from C#'s `System.Action` and from D&D's in-combat "action" and temporary "buff" concepts.
- **PermabuffContext**: Every `Permabuff.Apply()` receives a context with state, rules, and content lookup — enabling data-driven rules and feat cascade.
- **GameRules as data**: All D&D 3.5e parameters (epic threshold, feat schedules, BAB/save formulas, first-HD rules) are properties on `GameRules`. Custom rules are supported by creating a new `GameRules` instance.
- **Unified HDDriver**: A single `HDDriver` class with `DriverKind` enum (Class/RacialHD) replaces separate ClassDriver and RacialHDDriver classes. Both share identical progression mechanics.
- **AttributeTarget enum**: `ModifyAttribute` and `SetAttribute` use typed `AttributeTarget` enum with sub-properties (`ResistanceElement`, `AbilityScore`) instead of magic strings.
- **Typed FeatSlots**: `List<FeatSlot>` with optional `Restriction` string replaces separate `PendingFeatSlots`/`PendingBonusFeatSlots` int counters. Computed properties provide backward-compatible counts.
- **ScalingFormulas**: Both Race and Template use `List<ScalingFormula>` for formula-based abilities that scale with HD. Each formula targets a typed `AttributeTarget`.
- **Content type handlers**: Generic `ContentTypeHandler<T>` pattern enables extensible content types. Adding spells just requires registering a new handler — no engine changes.
- **List-format content files**: Every content file is a JSON array. Merging multiple files of the same type is automatic.
- **Content validation**: Cross-reference checking catches broken references at load time instead of runtime.
- **Template declarative mutations**: Template scaling uses POST/DELETE/PUT semantics. Each HD threshold declares exactly what to add, remove, or replace — fires once when `totalHD == key`. Only formula-based abilities recalculate every tick (via `SetAttribute`, which overwrites). No idempotency or provenance tracking needed.
- **Template timing**: Inherited templates apply at creation. Acquired templates use
  `Character.TemplateAcquisitionHD` and apply forward at the start of their acquisition tick;
  missing entries retain creation-time behavior for backwards-compatible saves. The replay
  keeps accrued per-tick values (such as skill points) intact while re-deriving effects the SRD
  says apply to current and future Hit Dice.
- **Skill ranks as doubled ints**: Stored as half-ranks (`int`). 5 ranks = 10, cross-class 2.5 ranks = 5. Display divides by 2. Avoids floating-point comparison issues. **Content authoring uses whole ranks**: `MinSkillRanks.Value` is authored in whole ranks (e.g., "5 ranks" → `value: 5`) — the prerequisite doubles at comparison time. Do not pre-double `value` in content files.
- **Equipment is post-tick only**: Equipment never retroactively affects per-level calculations. Post-tick only.
- **Tomes and inherent bonuses**: `PermanentEvent`s slotted between ticks. Affect all subsequent ticks naturally.
- **Epic progression**: Tick system modifier, independent of class. BAB/saves stop at `rules.EpicThreshold`. +1 epic attack at odd HD, +1 epic saves at even HD past threshold.
- **Racial HD as normal drivers**: HDDriver with `Kind = RacialHD` and a `HasRace` prerequisite. Same tick/replay mechanics as class levels.
- **Prestige class spell advancement**: `AdvanceSpellcasting` permabuff with `CastingType`. One matching class → auto-advance. Multiple → user selects via `ClassFeatureChoices["advance_spellcasting"]`.
- **GrantBonusFeat cascade**: When a bonus feat is granted, its `GrantedPermabuffs` are automatically applied via `ctx.Content` lookup. Granted bonus feats also increment `FeatTypeCounts` and `FeatTagCounts`, so downstream `HasFeatOfType` / `HasFeatWithTag` prerequisites see them (no distinction between user-picked and granted).
- **Domain ownership**: Each selected domain is owned by the class that granted the selection. `GrantDomainSelection` writes its pending count into `PendingDomainSelections[classId]`, inferring the class from `PermabuffContext.CurrentDriverId` if not explicit. When a domain is picked, owner is recorded in `DomainOwners[domainId] = classId`; the +1 bonus spell slots per spell level are added only to that owning class's `SpellcastingState`. Domain spell picks travel as `SpellSelection { ClassId = "domain:*" }`; the replay looks up `DomainOwners` and routes the spell to the owner's `SelectedSpells` (preserving the `domain:*` ClassId for UI rendering). `ApplyTickChoices` processes domain picks before spell selections so same-tick domain spells can resolve their owner. **Orphan domains**: if a race or template fires `GrantDomainSelection` outside the tick loop (no `CurrentDriverId`), pending picks bucket under `GrantDomainSelection.OrphanOwner` (`""`). Subsequent picks record the orphan sentinel as owner — domain granted-powers fire, but no bonus slots are added and domain spell picks are dropped with a warning (there's no caster to host them).
- **Feat slot enforcement**: Feat picks resolve a slot (matching-restricted bonus slot first, then unrestricted) before any state mutation. If no slot fits, the feat is dropped entirely with a `"no available feat slot"` warning — illegal picks cannot slip through. Prerequisite violations remain soft (warning only, feat still added) so imported characters with missing prereq data still render.
- **Polymorphic JSON deserialization**: `Driver`, `Permabuff`, and `Prerequisite` use .NET 10 `[JsonDerivedType]` attributes with a `$type` discriminator string.
- **Language**: C# for OO strength, Unity compatibility, long-term maintainability.
- **UI**: Blazor Server — C# end-to-end, server-side rendering via SignalR. Direct filesystem access eliminates the WASM manifest pipeline.
