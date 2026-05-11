using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsFeed.Contracts;

public sealed class ErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object>? Details { get; set; }
}

public sealed class CharacterSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime ModifiedUtc { get; set; }
    public int? TotalHd { get; set; }
    public int? Ecl { get; set; }
    public string? Race { get; set; }
    public Dictionary<string, int>? ClassLevels { get; set; }
}

public sealed class CharacterEnvelopeDto
{
    public string Id { get; set; } = string.Empty;
    public Character Character { get; set; } = new();
}

public sealed class CharacterMutationResponseDto
{
    public string Id { get; set; } = string.Empty;
    public Character Character { get; set; } = new();
    public CharacterSheet Sheet { get; set; } = new();
    public CharacterState State { get; set; } = new();
    public PendingChoicesDto PendingChoices { get; set; } = new();
    public List<FeatSummaryDto> QualifiedFeats { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class RulesDto
{
    public int EpicThreshold { get; set; }
    public int AbilityIncreaseInterval { get; set; }
    public bool FirstHdMaxHp { get; set; }
    public int FirstHdSkillMultiplier { get; set; }
    public List<int> StandardFeatHds { get; set; } = new();
    public int EpicFeatInterval { get; set; }
    public int EpicFeatStartHd { get; set; }
}

public sealed class ApiHealthResponse
{
    public string Status { get; set; } = "ok";
    public List<PackSummaryDto> LoadedPacks { get; set; } = new();
    public Dictionary<string, int> Counts { get; set; } = new();
}

public sealed class ContentCatalogResponse
{
    public List<PackSummaryDto> LoadedPacks { get; set; } = new();
    public List<ContentSummaryDto> Races { get; set; } = new();
    public List<DriverSummaryDto> Drivers { get; set; } = new();
    public List<ContentSummaryDto> Templates { get; set; } = new();
    public List<FeatSummaryDto> Feats { get; set; } = new();
    public List<ContentSummaryDto> Domains { get; set; } = new();
    public List<ContentSummaryDto> Skills { get; set; } = new();
    public List<ContentSummaryDto> ClassFeatures { get; set; } = new();
    public List<EquipmentSummaryDto> Equipment { get; set; } = new();
    public int SpellCount { get; set; }
}

public sealed class PackSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Description { get; set; }
}

public sealed class ContentSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class DriverSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DriverKind Kind { get; set; }
    public int HitDie { get; set; }
    public int SkillPointsPerLevel { get; set; }
    public int? MaxLevel { get; set; }
    public bool HasSpellcasting { get; set; }
    public List<string> Prerequisites { get; set; } = new();
}

public sealed class FeatSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FeatType Type { get; set; }
    public bool Repeatable { get; set; }
    public string? SelectionRequired { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Prerequisites { get; set; } = new();
}

public sealed class EquipmentSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public EquipmentCategory Category { get; set; }
    public string? Slot { get; set; }
    public int WeightLbs { get; set; }
    public long PriceCp { get; set; }
    public string? Description { get; set; }
    public string? WeaponDamage { get; set; }
    public int? ArmorBonus { get; set; }
    public List<string> EffectSummary { get; set; } = new();
}

public sealed class SpellSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string School { get; set; } = string.Empty;
    public Dictionary<string, int> ClassLevels { get; set; } = new();
    public string Description { get; set; } = string.Empty;
}

public sealed class ImportPcgRequest
{
    public string FileName { get; set; } = "imported.pcg";
    public string Content { get; set; } = string.Empty;
}

public sealed class ImportPcgResponse
{
    public string? Id { get; set; }
    public Character Character { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
    public List<string> DroppedFeats { get; set; } = new();
    public List<string> DroppedSkills { get; set; } = new();
    public List<string> DroppedClasses { get; set; } = new();
    public List<string> DroppedTemplates { get; set; } = new();
    public List<string> DroppedDomains { get; set; } = new();
    public bool RaceDropped { get; set; }
}

public sealed class EvaluateCharacterRequest
{
    public Character Character { get; set; } = new();
    public int? UpToHd { get; set; }
}

public sealed class EvaluateCharacterResponse
{
    public CharacterState State { get; set; } = new();
    public CharacterSheet Sheet { get; set; } = new();
    public PendingChoicesDto PendingChoices { get; set; } = new();
    public List<FeatSummaryDto> QualifiedFeats { get; set; } = new();
}

public sealed class NextStepRequest
{
    public Character Character { get; set; } = new();
    public List<string>? CandidateDriverIds { get; set; }
}

public sealed class NextStepResponse
{
    public int NextHd { get; set; }
    public bool AbilityIncreaseDue { get; set; }
    public CharacterState CurrentState { get; set; } = new();
    public CharacterSheet CurrentSheet { get; set; } = new();
    public PendingChoicesDto CurrentPendingChoices { get; set; } = new();
    public List<DriverPreviewDto> DriverPreviews { get; set; } = new();
}

public sealed class DriverPreviewDto
{
    public DriverSummaryDto Driver { get; set; } = new();
    public CharacterPreviewDto Preview { get; set; } = new();
    public PendingChoicesDto PendingChoices { get; set; } = new();
    public List<FeatSummaryDto> QualifiedFeats { get; set; } = new();
}

public sealed class CharacterPreviewDto
{
    public int TotalHd { get; set; }
    public int Ecl { get; set; }
    public int Hp { get; set; }
    public int Bab { get; set; }
    public SaveSet Saves { get; set; } = new();
    public AbilityScoreSet AbilityScores { get; set; } = new();
    public Dictionary<string, int> ClassLevels { get; set; } = new();
    public int UnspentSkillPoints { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public sealed class PendingChoicesDto
{
    public List<FeatChoiceGroupDto> FeatChoices { get; set; } = new();
    public List<DomainChoiceGroupDto> DomainChoices { get; set; } = new();
    public List<ClassFeatureChoiceGroupDto> ClassFeatureChoices { get; set; } = new();
    public List<SpellcastingSummaryDto> SpellLists { get; set; } = new();
}

public sealed class FeatChoiceGroupDto
{
    public string SlotType { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<FeatSummaryDto> Options { get; set; } = new();
}

public sealed class DomainChoiceGroupDto
{
    public string OwnerClassId { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<ContentSummaryDto> Options { get; set; } = new();
}

public sealed class ClassFeatureChoiceGroupDto
{
    public string FeatureType { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<string> ExistingSelections { get; set; } = new();
    public DynamicChoiceSourceDto? DynamicSource { get; set; }
    public List<ChoiceOptionDto> Options { get; set; } = new();
}

public sealed class DynamicChoiceSourceDto
{
    public string Kind { get; set; } = string.Empty;
    public string? FeatType { get; set; }
    public string? Tag { get; set; }
}

public sealed class ChoiceOptionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public int? MinEffectiveLevel { get; set; }
    public string? RequiredAlignment { get; set; }
    public int? RequiredCasterLevel { get; set; }
}

public sealed class SpellcastingSummaryDto
{
    public string ClassId { get; set; } = string.Empty;
    public CastingType CastingType { get; set; }
    public Ability CastingStat { get; set; }
    public int CasterLevel { get; set; }
    public int MaxSpellLevel { get; set; }
    public Dictionary<int, int> SpellsPerDay { get; set; } = new();
    public Dictionary<int, int>? SpellsKnown { get; set; }
    public Dictionary<int, int> DomainBonusSlots { get; set; } = new();
}
