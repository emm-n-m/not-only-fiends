using System.Text.Json.Serialization;

namespace NotOnlyFiendsStudio.Models;

public enum DriverKind { Class, RacialHD }

[JsonDerivedType(typeof(HDDriver), "HDDriver")]
public abstract class Driver
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<Prerequisite> Prerequisites { get; set; } = new();
    public ViolationEffect? ViolationEffect { get; set; }

    public abstract List<Permabuff> GetPermabuffs(CharacterState state, int driverLevel, GameRules rules, int? effectiveLevel = null, int previousEffectiveLevel = 0);

    // Backward-compatible convenience: uses default rules
    public List<Permabuff> GetPermabuffs(CharacterState state, int driverLevel) =>
        GetPermabuffs(state, driverLevel, GameRules.Standard35e());
}

public class ViolationEffect
{
    public string Description { get; set; } = string.Empty;
    public List<string> RevokeAbilityIds { get; set; } = new();
}

public class HDDriver : Driver
{
    public DriverKind Kind { get; set; }

    /// <summary>
    /// The class this driver is a variant of, e.g. <c>class:bard</c> for the druid-like bard.
    /// A variant is its own driver, because that is the only way to express the features a
    /// variant drops — but the rest of the game still calls it by its base class's name. A nymph
    /// "casts as a 7th-level druid" whichever druid she is, and a template that raises "your
    /// ranger level" cannot know which ranger the character took. Rules targeting the base
    /// therefore apply to the variant too; see <see cref="EffectiveLevelRule"/>.
    ///
    /// This is one-directional and one level deep: it does not make base levels count as variant
    /// levels, and it does not merge the two in <c>ClassLevel()</c> or <c>EffectiveClassLevel()</c>,
    /// which stay literal about which class was taken.
    /// </summary>
    public string? VariantOf { get; set; }

    public int HitDie { get; set; }
    public int SkillPointsPerLevel { get; set; }
    public List<string> ClassSkills { get; set; } = new();
    public BABProgression BABProgression { get; set; }
    public SaveProgression SaveProgression { get; set; } = new();
    public int? MaxLevel { get; set; }

    public SpellcastingProgression? Spellcasting { get; set; }

    public Dictionary<int, List<Permabuff>> LevelPermabuffs { get; set; } = new();
    public List<Permabuff> PerLevelPermabuffs { get; set; } = new();

    /// <summary>
    /// Whether an effective-level rule aimed at a class applies to this driver — true for the
    /// driver itself and for the class it is a <see cref="VariantOf"/>.
    /// </summary>
    public bool Targets(EffectiveLevelRule rule) =>
        rule.TargetDriverId == Id
        || (VariantOf != null && rule.TargetDriverId == VariantOf);

    public override List<Permabuff> GetPermabuffs(CharacterState state, int driverLevel, GameRules rules, int? effectiveLevel = null, int previousEffectiveLevel = 0)
    {
        var featureLevel = effectiveLevel ?? driverLevel;
        var spellcastingLevel = featureLevel + state.EffectiveLevelRules
            .Where(rule => Targets(rule) && rule.Scope == EffectiveLevelScope.SpellcastingOnly)
            .Sum(rule => rule.BonusFormula.Evaluate(state));
        // When no effective level override, default high-water to driverLevel-1 (normal single-level behavior)
        if (effectiveLevel == null)
            previousEffectiveLevel = driverLevel - 1;
        var buffs = new List<Permabuff>();

        buffs.Add(new AddHitDie(HitDie));
        buffs.Add(new GrantSkillPoints(SkillPointsPerLevel));
        buffs.Add(new AddClassSkills(ClassSkills));

        // BAB/saves use actual driverLevel (not boosted by effective level)
        if (state.TotalHD <= rules.EpicThreshold)
        {
            buffs.Add(new AddBAB(BABProgression, driverLevel));
            buffs.Add(new AddSaves(SaveProgression, driverLevel));
        }

        // Spellcasting includes rules scoped only to casting in addition to any feature-level
        // bonuses already present in featureLevel.
        var progressionLevel = spellcastingLevel;
        var hasMappedProgressionLevel = Spellcasting?.ProgressionLevelByDriverLevel is { Count: > 0 };
        if (hasMappedProgressionLevel &&
            !Spellcasting!.ProgressionLevelByDriverLevel.TryGetValue(spellcastingLevel, out progressionLevel))
        {
            progressionLevel = 0;
        }

        if (Spellcasting != null && progressionLevel > 0 &&
            Spellcasting.SpellsPerDay.ContainsKey(progressionLevel))
        {
            var spd = Spellcasting.SpellsPerDay[progressionLevel];
            Dictionary<int, int>? sk = null;
            Spellcasting.SpellsKnown?.TryGetValue(progressionLevel, out sk);
            var casterLevel = progressionLevel;
            if (Spellcasting.CasterLevelByDriverLevel?.TryGetValue(spellcastingLevel, out var mappedCasterLevel) == true)
                casterLevel = mappedCasterLevel;

            buffs.Add(new UpdateSpellcasting
            {
                ClassId = Id,
                CastingType = Spellcasting.CastingType,
                CastingStat = Spellcasting.CastingStat,
                CasterLevel = casterLevel,
                SpellsPerDay = spd,
                SpellsKnown = sk,
                ProgressionRef = Spellcasting
            });
        }

        buffs.AddRange(PerLevelPermabuffs);

        // Fire all level permabuffs newly reached by effective level
        foreach (var (level, perms) in LevelPermabuffs)
        {
            if (level > previousEffectiveLevel && level <= featureLevel)
                buffs.AddRange(perms);
        }

        return buffs;
    }
}

public class SpellcastingProgression
{
    public CastingType CastingType { get; set; }
    public Ability CastingStat { get; set; }
    public Dictionary<int, Dictionary<int, int>> SpellsPerDay { get; set; } = new();
    public Dictionary<int, Dictionary<int, int>>? SpellsKnown { get; set; }

    /// <summary>
    /// Optional mapping for monster classes whose racial-HD level and spell-progression level
    /// are different. Unmapped driver levels do not update spellcasting.
    /// </summary>
    public Dictionary<int, int> ProgressionLevelByDriverLevel { get; set; } = new();

    /// <summary>Optional actual caster level at each driver level when it differs from progression.</summary>
    public Dictionary<int, int>? CasterLevelByDriverLevel { get; set; }

    /// <summary>Additional class/domain lists this progression may learn spells from.</summary>
    public List<string> SpellListSources { get; set; } = new();

    /// <summary>Spell IDs removed from the inherited list sources for this class.</summary>
    public List<string> SpellListExclusions { get; set; } = new();

    /// <summary>
    /// Set only where the default inference is wrong — in practice only the wizard, which has no
    /// <c>spellsKnown</c> progression but is not a full-list caster either.
    /// </summary>
    public SpellAcquisition? Acquisition { get; set; }

    /// <summary>
    /// <see cref="Acquisition"/> if content states one, otherwise inferred: a class with a
    /// <c>spellsKnown</c> progression knows a fixed number of spells, and everything else has its
    /// whole list available. That inference is the rule the engine already used implicitly —
    /// <see cref="HasSpontaneousCasting"/> tests exactly this — so existing content needs no edit.
    /// </summary>
    public SpellAcquisition ResolvedAcquisition =>
        Acquisition ?? (SpellsKnown != null ? SpellAcquisition.SpellsKnown : SpellAcquisition.FullList);
}
