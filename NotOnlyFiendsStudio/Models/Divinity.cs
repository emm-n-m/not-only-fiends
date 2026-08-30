namespace NotOnlyFiendsStudio.Models;

/// <summary>
/// Player-authored inputs for a character that is itself a deity. This is deliberately separate
/// from <see cref="Character.Deity"/>, which names a patron the character worships.
/// </summary>
public class DivinityChoices
{
    /// <summary>0 = quasi-deity; 1–20 = deity. Rank 21+ is an overdeity, for which the SRD gives no statistics.</summary>
    public int DivineRank { get; set; }
    public DivineForm Form { get; set; } = DivineForm.Biped;
    public List<string> Titles { get; set; } = new();
    public List<string> Portfolio { get; set; } = new();
    public List<string> DomainIds { get; set; } = new();
    public List<string> SalientDivineAbilityIds { get; set; } = new();
    public string? FavoredWeaponId { get; set; }
    public string? Symbol { get; set; }

    public DivinityChoices Clone() => new()
    {
        DivineRank = DivineRank,
        Form = Form,
        Titles = new List<string>(Titles),
        Portfolio = new List<string>(Portfolio),
        DomainIds = new List<string>(DomainIds),
        SalientDivineAbilityIds = new List<string>(SalientDivineAbilityIds),
        FavoredWeaponId = FavoredWeaponId,
        Symbol = Symbol,
    };
}

public enum DivineForm { Biped, Quadruped }

public enum DivineStatus
{
    QuasiDeity,
    Demigod,
    LesserDeity,
    IntermediateDeity,
    GreaterDeity,
    Overdeity,
}

/// <summary>Computed SRD divine-rank characteristics exposed to sheets and API clients.</summary>
public class DivineCharacteristics
{
    public int DivineRank { get; set; }
    public DivineStatus Status { get; set; }
    public DivineForm Form { get; set; }
    public List<string> Titles { get; set; } = new();
    public List<string> Portfolio { get; set; } = new();
    public List<string> DomainIds { get; set; } = new();
    public List<string> SalientDivineAbilityIds { get; set; } = new();
    public string? FavoredWeaponId { get; set; }
    public string? Symbol { get; set; }
    public int SalientDivineAbilitySlots { get; set; }
    public int PendingSalientDivineAbilitySlots { get; set; }
    public int? SensesRadiusMiles { get; set; }
    public int RemoteSensingLocations { get; set; }
    public int? AutomaticActionMaximumDc { get; set; }
    public int AutomaticActionsPerRound { get; set; }
    public long? MaximumPortfolioItemValueGp { get; set; }
    public bool CanCreateArtifacts { get; set; }
    public string? DivineAuraRadius { get; set; }
    public int? DivineAuraSaveDc { get; set; }
    public string PortfolioSense { get; set; } = string.Empty;
    public string GodlyRealmControl { get; set; } = string.Empty;
    public bool GrantsSpells { get; set; }
    public int DomainPowerUsesPerDay { get; set; }
    public int DomainPowerEffectiveClericLevel { get; set; }
    public bool AlwaysMaximizesRolls { get; set; }
    public bool CanTakeTenOnChecks { get; set; }
    public bool AlwaysGetsTwentyOnChecks { get; set; }
}

/// <summary>A selectable salient divine ability extracted from the local SRD mirror.</summary>
public class SalientDivineAbilityDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? PrerequisiteText { get; set; }
    public string? Notes { get; set; }
    public string? Rest { get; set; }
    public int MinimumDivineRank { get; set; } = 1;
    public List<Prerequisite> Prerequisites { get; set; } = new();
    /// <summary>True when some printed condition cannot yet be expressed by the prerequisite grammar.</summary>
    public bool RequiresManualReview { get; set; }
    public List<string> SuggestedPortfolioElements { get; set; } = new();
    public bool Repeatable { get; set; }
}

public static class DivineRankRules
{
    public static DivineStatus Status(int rank) => rank switch
    {
        <= 0 => DivineStatus.QuasiDeity,
        <= 5 => DivineStatus.Demigod,
        <= 10 => DivineStatus.LesserDeity,
        <= 15 => DivineStatus.IntermediateDeity,
        <= 20 => DivineStatus.GreaterDeity,
        _ => DivineStatus.Overdeity,
    };

    public static int SalientAbilitySlots(int rank) => rank switch
    {
        <= 0 => 0,
        <= 5 => rank + 1,
        <= 10 => rank + 2,
        <= 15 => rank + 3,
        <= 20 => rank + 5,
        _ => 0,
    };

    public static int DamageReduction(int rank) => rank switch
    {
        <= 0 => 10,
        <= 5 => 15,
        <= 10 => 20,
        <= 15 => 25,
        _ => 30,
    };

    public static (int Biped, int Quadruped) BaseLandSpeed(Size size) => size switch
    {
        Size.Fine => (20, 60),
        Size.Diminutive => (30, 70),
        Size.Tiny => (40, 80),
        Size.Small => (50, 90),
        Size.Medium => (60, 100),
        Size.Large => (80, 120),
        Size.Huge => (100, 140),
        Size.Gargantuan => (120, 160),
        _ => (140, 180),
    };
}
