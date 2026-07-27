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
[JsonDerivedType(typeof(MinSkillRanksAcross), "MinSkillRanksAcross")]
[JsonDerivedType(typeof(HasFeatOfAnyType), "HasFeatOfAnyType")]
[JsonDerivedType(typeof(HasSpontaneousCasting), "HasSpontaneousCasting")]
[JsonDerivedType(typeof(HasAnyRace), "HasAnyRace")]
[JsonDerivedType(typeof(LacksTemplate), "LacksTemplate")]
[JsonDerivedType(typeof(HasLanguage), "HasLanguage")]
[JsonDerivedType(typeof(MinCounter), "MinCounter")]
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

/// <summary>
/// Requires <see cref="MinCount"/> of the listed skills to each have at least
/// <see cref="Value"/> ranks. Models 3.5e's "Knowledge (any two) 10 ranks" style
/// requirements, where the specific skills are the player's choice.
/// </summary>
public class MinSkillRanksAcross : Prerequisite
{
    public List<string> SkillIds { get; set; } = new();

    /// <summary>Whole ranks, as with <see cref="MinSkillRanks"/>. State stores half-ranks.</summary>
    public int Value { get; set; }

    public int MinCount { get; set; } = 1;

    public override bool IsMet(CharacterState state) =>
        SkillIds.Count(id => state.SkillRanks.GetValueOrDefault(id) >= Value * 2) >= MinCount;

    public override string Description =>
        $"{MinCount} of ({string.Join(", ", SkillIds)}) at {Value} ranks";
}

/// <summary>
/// Requires <see cref="MinCount"/> feats drawn from any of <see cref="FeatTypes"/>
/// combined. Distinct from stacking several <see cref="HasFeatOfType"/> entries, which
/// would AND together and demand the count from each type separately.
/// </summary>
public class HasFeatOfAnyType : Prerequisite
{
    public List<FeatType> FeatTypes { get; set; } = new();
    public int MinCount { get; set; } = 1;

    public override bool IsMet(CharacterState state) =>
        FeatTypes.Sum(type => state.FeatTypeCounts.GetValueOrDefault(type)) >= MinCount;

    public override string Description =>
        $"Any {MinCount} {string.Join(" or ", FeatTypes)} feats";
}

/// <summary>
/// Requires a class that casts without preparation (sorcerer, bard). Spontaneous casters
/// are the ones with a spells-known table, so that is what identifies them here.
/// </summary>
public class HasSpontaneousCasting : Prerequisite
{
    public CastingType? CastingType { get; set; }

    public override bool IsMet(CharacterState state) =>
        state.Spellcasting.Values.Any(s =>
            s.SpellsKnown != null
            && (!CastingType.HasValue || s.CastingType == CastingType.Value));

    public override string Description => CastingType.HasValue
        ? $"Able to cast {CastingType.Value} spells without preparation"
        : "Able to cast spells without preparation";
}

public class HasAnyRace : Prerequisite
{
    public List<string> RaceIds { get; set; } = new();
    public override bool IsMet(CharacterState state) => RaceIds.Contains(state.RaceId);
    public override string Description => $"Race: {string.Join(" or ", RaceIds)}";
}

/// <summary>
/// Excludes characters who already carry a given template — e.g. Dragon Disciple
/// forbidding an existing half-dragon template. Checks state.TemplateIds, not RaceId.
/// </summary>
public class LacksTemplate : Prerequisite
{
    public string TemplateId { get; set; } = string.Empty;
    public override bool IsMet(CharacterState state) => !state.TemplateIds.Contains(TemplateId);
    public override string Description => $"Must not have the {TemplateId} template";
}

public class HasLanguage : Prerequisite
{
    public string LanguageId { get; set; } = string.Empty;
    public override bool IsMet(CharacterState state) => state.Languages.Contains(LanguageId);
    public override string Description => $"Language: {LanguageId}";
}

/// <summary>
/// Generic threshold check against state.Counters (populated by the ModifyCounter
/// permabuff) — e.g. "sneak attack +2d6" for Arcane Trickster via counterId
/// "sneak_attack_dice". Not sneak-attack-specific; reusable for any counter.
/// </summary>
public class MinCounter : Prerequisite
{
    public string CounterId { get; set; } = string.Empty;
    public int Value { get; set; }
    public override bool IsMet(CharacterState state) => state.Counters.GetValueOrDefault(CounterId) >= Value;
    public override string Description => $"{CounterId} {Value}+";
}
