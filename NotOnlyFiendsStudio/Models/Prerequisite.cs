using System.Text.Json.Serialization;

namespace NotOnlyFiendsStudio.Models;

[JsonDerivedType(typeof(MinBAB), "MinBAB")]
[JsonDerivedType(typeof(MinAbility), "MinAbility")]
[JsonDerivedType(typeof(MinSkillRanks), "MinSkillRanks")]
[JsonDerivedType(typeof(MinClassLevel), "MinClassLevel")]
[JsonDerivedType(typeof(HasFeat), "HasFeat")]
[JsonDerivedType(typeof(AlignmentReq), "AlignmentReq")]
[JsonDerivedType(typeof(MinHD), "MinHD")]
[JsonDerivedType(typeof(MinCasterLevel), "MinCasterLevel")]
[JsonDerivedType(typeof(CanCastSpellLevel), "CanCastSpellLevel")]
[JsonDerivedType(typeof(HasRace), "HasRace")]
[JsonDerivedType(typeof(MinSave), "MinSave")]
[JsonDerivedType(typeof(HasAbility), "HasAbility")]
[JsonDerivedType(typeof(HasSpellcasting), "HasSpellcasting")]
[JsonDerivedType(typeof(HasFeatOfType), "HasFeatOfType")]
[JsonDerivedType(typeof(HasFeatWithTag), "HasFeatWithTag")]
[JsonDerivedType(typeof(HasFeatSelections), "HasFeatSelections")]
public abstract class Prerequisite
{
    public abstract bool IsMet(CharacterState state);
    public abstract string Description { get; }
}

public class MinBAB : Prerequisite
{
    public int Value { get; set; }
    public override bool IsMet(CharacterState state) => state.EffectiveBAB >= Value;
    public override string Description => $"BAB +{Value}";
}

public class MinAbility : Prerequisite
{
    public Ability Ability { get; set; }
    public int Value { get; set; }
    public override bool IsMet(CharacterState state) =>
        state.AbilityScores.GetScore(Ability) >= Value;
    public override string Description => $"{Ability} {Value}+";
}

public class MinSkillRanks : Prerequisite
{
    public string SkillId { get; set; } = string.Empty;
    // Value is in whole ranks (e.g., 5 = 5 ranks). State stores half-ranks.
    public int Value { get; set; }
    public override bool IsMet(CharacterState state) =>
        state.SkillRanks.GetValueOrDefault(SkillId) >= Value * 2;
    public override string Description => $"{SkillId} {Value} ranks";
}

public class MinClassLevel : Prerequisite
{
    public string ClassId { get; set; } = string.Empty;
    public int Value { get; set; }
    public override bool IsMet(CharacterState state) =>
        state.ClassLevels.GetValueOrDefault(ClassId) >= Value;
    public override string Description => $"{ClassId} level {Value}+";
}

public class HasFeat : Prerequisite
{
    public string FeatId { get; set; } = string.Empty;
    // Matches exact FeatId OR any selection variant `{FeatId}_*` (e.g., "spell_focus" satisfied by "spell_focus_evocation").
    public override bool IsMet(CharacterState state)
    {
        var prefix = FeatId + "_";
        return state.Feats.Any(f => f == FeatId || f.StartsWith(prefix, StringComparison.Ordinal));
    }
    public override string Description => $"Feat: {FeatId}";
}

public class AlignmentReq : Prerequisite
{
    public HashSet<Alignment> Allowed { get; set; } = new();
    public override bool IsMet(CharacterState state) => Allowed.Contains(state.Alignment);
    public override string Description => $"Alignment: {string.Join("/", Allowed)}";
}

public class MinHD : Prerequisite
{
    public int Value { get; set; }
    public override bool IsMet(CharacterState state) => state.TotalHD >= Value;
    public override string Description => $"{Value}+ HD";
}

public class MinCasterLevel : Prerequisite
{
    public int Value { get; set; }
    public override bool IsMet(CharacterState state) =>
        state.Spellcasting.Values.Any(s => s.CasterLevel >= Value);
    public override string Description => $"Caster level {Value}+";
}

public class CanCastSpellLevel : Prerequisite
{
    public int SpellLevel { get; set; }
    public CastingType? CastingType { get; set; }
    public override bool IsMet(CharacterState state) =>
        state.Spellcasting.Values.Any(s =>
            s.MaxSpellLevel >= SpellLevel
            && (!CastingType.HasValue || s.CastingType == CastingType.Value));
    public override string Description => CastingType.HasValue
        ? $"Able to cast {SpellLevel}th-level {CastingType.Value} spells"
        : $"Able to cast {SpellLevel}th-level spells";
}

public class HasRace : Prerequisite
{
    public string RaceId { get; set; } = string.Empty;
    public override bool IsMet(CharacterState state) => state.RaceId == RaceId;
    public override string Description => $"Race: {RaceId}";
}

public class MinSave : Prerequisite
{
    public string Save { get; set; } = string.Empty;
    public int Value { get; set; }
    public override bool IsMet(CharacterState state) =>
        state.EffectiveSaves.GetSave(Save) >= Value;
    public override string Description => $"Base {Save} save +{Value}";
}

public class HasAbility : Prerequisite
{
    public string AbilityId { get; set; } = string.Empty;
    public override bool IsMet(CharacterState state) =>
        state.Abilities.Any(a => a.Id == AbilityId);
    public override string Description => $"Ability: {AbilityId}";
}

public class HasSpellcasting : Prerequisite
{
    public CastingType? CastingType { get; set; }
    public override bool IsMet(CharacterState state) =>
        CastingType.HasValue
            ? state.Spellcasting.Values.Any(s => s.CastingType == CastingType.Value)
            : state.Spellcasting.Count > 0;
    public override string Description => CastingType.HasValue
        ? $"Able to cast {CastingType.Value} spells"
        : "Able to cast spells";
}

public class HasFeatOfType : Prerequisite
{
    public FeatType FeatType { get; set; }
    public int MinCount { get; set; } = 1;
    public override bool IsMet(CharacterState state) =>
        state.FeatTypeCounts.GetValueOrDefault(FeatType) >= MinCount;
    public override string Description => MinCount == 1
        ? $"Any {FeatType} feat"
        : $"Any {MinCount} {FeatType} feats";
}

public class HasFeatWithTag : Prerequisite
{
    public string Tag { get; set; } = string.Empty;
    public int MinCount { get; set; } = 1;
    public override bool IsMet(CharacterState state) =>
        state.FeatTagCounts.GetValueOrDefault(Tag) >= MinCount;
    public override string Description => MinCount == 1
        ? $"Any {Tag} feat"
        : $"Any {MinCount} {Tag} feats";
}

/// <summary>
/// Requires a repeatable/selectable feat taken at least MinCount times.
/// Counts feats in state whose ID equals FeatId or starts with FeatId + "_".
/// E.g. FeatId="spell_focus", MinCount=2 matches spell_focus_conjuration + spell_focus_evocation.
/// </summary>
public class HasFeatSelections : Prerequisite
{
    public string FeatId { get; set; } = string.Empty;
    public int MinCount { get; set; } = 1;
    public override bool IsMet(CharacterState state)
    {
        var prefix = FeatId + "_";
        var count = state.Feats.Count(f => f == FeatId || f.StartsWith(prefix, StringComparison.Ordinal));
        return count >= MinCount;
    }
    public override string Description => MinCount == 1
        ? $"Feat: {FeatId}"
        : $"{MinCount} selections of {FeatId}";
}
