namespace NotOnlyFiendsStudio.Models;

public class TemplateDriver
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // Validated against the FINISHED state (tail pass), not at creation: acquired
    // templates like Unseelie Champion gate on class levels that do not exist yet
    // when templates are applied. Unmet prerequisites warn, matching driver behavior.
    public List<Prerequisite> Prerequisites { get; set; } = new();

    // One-time modifications at creation
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

    // Formula-based abilities recalculated every tick
    public List<ScalingFormula> ScalingFormulas { get; set; } = new();

    // Companion scaling: keyed by effective MASTER level. Fired in ReplayStudio's
    // tail pass for every entry with key <= state.EffectiveMasterLevel. Used by
    // animal_companion / familiar / special_mount / wild_cohort templates.
    public SortedDictionary<int, List<Permabuff>> CompanionScalingPermabuffs { get; set; } = new();

    public List<Permabuff> GetTickPermabuffs(int totalHD, CharacterState state)
    {
        var buffs = new List<Permabuff>();

        if (ScalingPermabuffs.TryGetValue(totalHD, out var thresholdBuffs))
            buffs.AddRange(thresholdBuffs);

        foreach (var sf in ScalingFormulas)
            buffs.Add(new SetAttribute(sf.Target, sf.Formula.Evaluate(state), sf.ResistanceElement, sf.AbilityScore));

        return buffs;
    }

    // Returns all CompanionScalingPermabuffs entries with key <= effectiveMasterLevel.
    // Called once at the end of evaluation, NOT per tick.
    public List<Permabuff> GetCompanionScalingPermabuffs(int effectiveMasterLevel)
    {
        var buffs = new List<Permabuff>();
        foreach (var (key, perms) in CompanionScalingPermabuffs)
        {
            if (key <= effectiveMasterLevel)
                buffs.AddRange(perms);
            else
                break; // SortedDictionary iterates in key order
        }
        return buffs;
    }
}
