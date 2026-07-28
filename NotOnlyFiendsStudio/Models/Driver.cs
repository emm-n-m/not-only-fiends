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
    public int HitDie { get; set; }
    public int SkillPointsPerLevel { get; set; }
    public List<string> ClassSkills { get; set; } = new();
    public BABProgression BABProgression { get; set; }
    public SaveProgression SaveProgression { get; set; } = new();
    public int? MaxLevel { get; set; }

    public SpellcastingProgression? Spellcasting { get; set; }

    public Dictionary<int, List<Permabuff>> LevelPermabuffs { get; set; } = new();
    public List<Permabuff> PerLevelPermabuffs { get; set; } = new();

    public override List<Permabuff> GetPermabuffs(CharacterState state, int driverLevel, GameRules rules, int? effectiveLevel = null, int previousEffectiveLevel = 0)
    {
        var featureLevel = effectiveLevel ?? driverLevel;
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

        // Spellcasting uses featureLevel (boosted by effective level rules)
        if (Spellcasting != null && Spellcasting.SpellsPerDay.ContainsKey(featureLevel))
        {
            var spd = Spellcasting.SpellsPerDay[featureLevel];
            Dictionary<int, int>? sk = null;
            Spellcasting.SpellsKnown?.TryGetValue(featureLevel, out sk);

            buffs.Add(new UpdateSpellcasting
            {
                ClassId = Id,
                CastingType = Spellcasting.CastingType,
                CastingStat = Spellcasting.CastingStat,
                CasterLevel = featureLevel,
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
