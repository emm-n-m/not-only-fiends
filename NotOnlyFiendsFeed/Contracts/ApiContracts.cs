using System.Text.Json.Serialization;
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
    public List<Warning> Warnings { get; set; } = new();
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
    public List<RaceSummaryDto> Races { get; set; } = new();
    public List<DriverSummaryDto> Drivers { get; set; } = new();
    public List<ContentSummaryDto> Templates { get; set; } = new();
    public List<FeatSummaryDto> Feats { get; set; } = new();
    public List<ContentSummaryDto> Domains { get; set; } = new();
    public List<ContentSummaryDto> Skills { get; set; } = new();
    public List<ContentSummaryDto> ClassFeatures { get; set; } = new();
    public List<LanguageSummaryDto> Languages { get; set; } = new();
    public List<EquipmentSummaryDto> Equipment { get; set; } = new();
    public int SpellCount { get; set; }
}

/// <summary>
/// A language that can be offered as a choice. <c>isSecret</c> languages (Druidic) are never part
/// of a race's "any bonus language" allowance, so a caller filtering the list needs to see it.
/// </summary>
public sealed class LanguageSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSecret { get; set; }
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
    public TemplateAcquisitionKind? AcquisitionKind { get; set; }
}

/// <summary>
/// A race, carrying the same player-character sanctioning that the builder's picker shows.
///
/// Races are listed rather than filtered: the builder is also used to construct companions and
/// monsters, so removing unsanctioned entries would break that workflow (see
/// <see cref="NotOnlyFiendsStudio.Studio.RaceCatalog.ForPicker"/>). Callers that only want PC
/// options should select on <see cref="IsPcRace"/>.
/// </summary>
public sealed class RaceSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Null when the source never priced this race for player characters — see
    /// <see cref="IsPcRace"/>. Distinct from 0, which means "playable at no cost", like a Human.</summary>
    /// <remarks>
    /// Serialized even when null, overriding the app-wide <c>WhenWritingNull</c> policy: null here
    /// is the meaningful answer, and omitting the key would make a caller reading
    /// <c>levelAdjustment</c> hit a missing field exactly for the races the distinction is about.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? LevelAdjustment { get; set; }
    /// <summary>False for monster, companion and creature entries with no printed Level Adjustment.</summary>
    public bool IsPcRace { get; set; }

    /// <summary>Languages granted automatically at creation.</summary>
    public List<string> AutomaticLanguages { get; set; } = new();
    /// <summary>
    /// Languages this race may spend Int-based bonus picks on. Empty when
    /// <see cref="BonusLanguagesAny"/> is true — the offer is then every non-secret language.
    /// </summary>
    public List<string> BonusLanguages { get; set; } = new();
    /// <summary>True when the race may take any non-secret language (human, half-elf).</summary>
    public bool BonusLanguagesAny { get; set; }
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
    public int EnhancementBonus { get; set; }
    public int SpecialAbilityBonusEquivalent { get; set; }
    public int? ArmorBonus { get; set; }
    public bool IsIntelligent { get; set; }
    public int? IntelligentItemEgo { get; set; }
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
    public List<string> DroppedSpells { get; set; } = new();
    public List<string> DroppedClassAbilities { get; set; } = new();
    public List<string> DroppedEquipment { get; set; } = new();
    public List<string> UnsupportedCustomEquipmentModifiers { get; set; } = new();
    public List<string> IgnoredTemporaryBonuses { get; set; } = new();
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

/// <summary>
/// How much choice-option data to inline into each driver preview — feats, domains
/// and class features alike. Every candidate driver repeats these lists, so they
/// dominate the response; previews therefore default to <see cref="None"/>. The
/// intended flow is: read the summary, narrow to the drivers you care about with
/// <see cref="NextStepRequest.CandidateDriverIds"/>, then re-request at
/// <see cref="Full"/>.
/// </summary>
public enum OptionDetail
{
    /// <summary>Counts only — no option list.</summary>
    None,

    /// <summary>Option IDs only, no names, descriptions or prerequisites.</summary>
    Ids,

    /// <summary>Complete option objects.</summary>
    Full
}

public sealed class NextStepResponse
{
    public int NextHd { get; set; }
    public bool AbilityIncreaseDue { get; set; }
    public CharacterState CurrentState { get; set; } = new();
    public CharacterSheet CurrentSheet { get; set; } = new();
    public PendingChoicesDto CurrentPendingChoices { get; set; } = new();
    public List<DriverPreviewDto> DriverPreviews { get; set; } = new();
    public List<DriverExclusionDto> ExcludedDrivers { get; set; } = new();
    public List<string> UnknownDriverIds { get; set; } = new();
    public List<SkillPointAccrual> SkillPointAccruals { get; set; } = new();
}

public sealed class DriverExclusionDto
{
    public DriverSummaryDto Driver { get; set; } = new();
    public List<string> Reasons { get; set; } = new();
}

public sealed class DriverPreviewDto
{
    public DriverSummaryDto Driver { get; set; } = new();
    /// <summary>Whether choosing this specific driver grants the scheduled ability increase.</summary>
    public bool AbilityIncreaseDue { get; set; }
    public CharacterPreviewDto Preview { get; set; } = new();
    public PendingChoicesDto PendingChoices { get; set; } = new();

    // No QualifiedFeats here: it was always identical to the "standard" slot's options
    // in PendingChoices, duplicating the largest field in the response once per
    // candidate driver. EvaluateCharacterResponse still carries it, where it is not
    // redundant — that response can have qualified feats with no pending slots to fill.
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
    public List<Warning> Warnings { get; set; } = new();
}

public sealed class PendingChoicesDto
{
    public List<FeatChoiceGroupDto> FeatChoices { get; set; } = new();
    public List<DomainChoiceGroupDto> DomainChoices { get; set; } = new();
    public List<ClassFeatureChoiceGroupDto> ClassFeatureChoices { get; set; } = new();
    public List<SpellSelectionChoiceGroupDto> SpellChoices { get; set; } = new();
    public List<PreparedSpellChoiceGroupDto> PreparedSpellChoices { get; set; } = new();
    public List<SpellcastingSummaryDto> SpellLists { get; set; } = new();
    public List<CompanionTemplateChoiceGroupDto> CompanionTemplateChoices { get; set; } = new();
}

public sealed class CompanionTemplateChoiceGroupDto
{
    public string LinkType { get; set; } = string.Empty;
    public string ChoiceKey { get; set; } = "companionTemplateChoices";
    public int Count { get; set; } = 1;
    public string? ExistingSelection { get; set; }
    public List<CompanionTemplateOptionDto> Options { get; set; } = new();
}

public sealed class CompanionTemplateOptionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class FeatChoiceGroupDto
{
    public string SlotType { get; set; } = string.Empty;

    /// <summary>Number of slots of this type to fill.</summary>
    public int Count { get; set; }

    /// <summary>Number of legal options. Always populated, at every <see cref="OptionDetail"/>.</summary>
    public int OptionCount { get; set; }

    /// <summary>Populated at <see cref="OptionDetail.Ids"/>.</summary>
    public List<string>? OptionIds { get; set; }

    /// <summary>Populated at <see cref="OptionDetail.Full"/>.</summary>
    public List<FeatSummaryDto>? Options { get; set; }
}

public sealed class DomainChoiceGroupDto
{
    public string OwnerClassId { get; set; } = string.Empty;

    /// <summary>Number of domains to pick.</summary>
    public int Count { get; set; }

    /// <summary>Number of legal options. Always populated, at every <see cref="OptionDetail"/>.</summary>
    public int OptionCount { get; set; }

    /// <summary>Populated at <see cref="OptionDetail.Ids"/>.</summary>
    public List<string>? OptionIds { get; set; }

    /// <summary>Populated at <see cref="OptionDetail.Full"/>.</summary>
    public List<ContentSummaryDto>? Options { get; set; }
}

public sealed class ClassFeatureChoiceGroupDto
{
    public string FeatureType { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<string> ExistingSelections { get; set; } = new();
    public DynamicChoiceSourceDto? DynamicSource { get; set; }

    /// <summary>Number of legal options. Always populated, at every <see cref="OptionDetail"/>.</summary>
    public int OptionCount { get; set; }

    /// <summary>Populated at <see cref="OptionDetail.Ids"/>.</summary>
    public List<string>? OptionIds { get; set; }

    /// <summary>Populated at <see cref="OptionDetail.Full"/>.</summary>
    public List<ChoiceOptionDto>? Options { get; set; }
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

public sealed class SpellSelectionChoiceGroupDto
{
    public string ClassId { get; set; } = string.Empty;
    public int SpellLevel { get; set; }
    public int OptionCount { get; set; }
    public List<string> ExistingSelections { get; set; } = new();
    public int SpellbookUsed { get; set; }
    public int SpellbookLimit { get; set; }
    public int SpellbookRemaining { get; set; }

    /// <summary>Populated at <see cref="OptionDetail.Ids"/>.</summary>
    public List<string>? OptionIds { get; set; }

    /// <summary>Populated at <see cref="OptionDetail.Full"/>.</summary>
    public List<SpellSummaryDto>? Options { get; set; }
}

public sealed class PreparedSpellChoiceGroupDto
{
    public string ClassId { get; set; } = string.Empty;
    public int SpellLevel { get; set; }
    public PreparedSpellSlotKind SlotKind { get; set; }
    public int SlotCount { get; set; }
    public int PreparedCount { get; set; }
    public List<string> ExistingSelections { get; set; } = new();
    public int OptionCount { get; set; }
    public List<string>? OptionIds { get; set; }
    public List<SpellSummaryDto>? Options { get; set; }
}

public sealed class SpellcastingSummaryDto
{
    public string ClassId { get; set; } = string.Empty;
    public CastingType CastingType { get; set; }
    public Ability CastingStat { get; set; }
    public SpellAcquisition Acquisition { get; set; }
    public int CasterLevel { get; set; }
    public int MaxSpellLevel { get; set; }
    public Dictionary<int, int> SpellsPerDay { get; set; } = new();
    public Dictionary<int, int>? SpellsKnown { get; set; }
    public Dictionary<int, int> DomainBonusSlots { get; set; } = new();
    public Dictionary<int, int> SpecialtyBonusSlots { get; set; } = new();
    public Dictionary<int, int> AbilityBonusSlots { get; set; } = new();
}
