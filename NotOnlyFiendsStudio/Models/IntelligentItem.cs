namespace NotOnlyFiendsStudio.Models;

/// <summary>How an intelligent item communicates with its wielder.</summary>
public enum IntelligentItemCommunication
{
    Empathy,
    Speech,
    SpeechAndTelepathy,
    Telepathy
}

public enum IntelligentItemPowerKind
{
    Lesser,
    Greater,
    Dedicated
}

public enum IntelligentItemVision
{
    Vision,
    Darkvision
}

public sealed class IntelligentItemMentalAbilities
{
    public int Intelligence { get; set; } = 10;
    public int Wisdom { get; set; } = 10;
    public int Charisma { get; set; } = 10;

    public int IntelligenceBonus => Math.Max(0, (Intelligence - 10) / 2);
    public int WisdomBonus => Math.Max(0, (Wisdom - 10) / 2);
    public int CharismaBonus => Math.Max(0, (Charisma - 10) / 2);
}

public sealed class IntelligentItemSenses
{
    public IntelligentItemVision Vision { get; set; } = IntelligentItemVision.Vision;
    public int RangeFt { get; set; }
    public bool Hearing { get; set; } = true;
    public bool Blindsense { get; set; }
    /// <summary>Legacy misspelling retained for existing pack JSON; SRD intelligent items use blindsense.</summary>
    public bool Blindsight { get; set; }
    public bool ReadsSpokenLanguages { get; set; }
    public bool ReadsAllLanguages { get; set; }
    public bool ReadsMagic { get; set; }
}

/// <summary>
/// A power granted to an intelligent item. The description is intentionally retained as
/// source text: activation, save DCs, targets, and spell-like effects are not permanent
/// character buffs and must not be applied through EquipmentDefinition.GrantedPermabuffs.
/// </summary>
public sealed class IntelligentItemPower
{
    public IntelligentItemPowerKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Activation { get; set; }
    public int BasePriceModifierGp { get; set; }
    public string Description { get; set; } = string.Empty;

    public int EgoPoints => Kind switch
    {
        IntelligentItemPowerKind.Lesser => 1,
        IntelligentItemPowerKind.Greater => 2,
        IntelligentItemPowerKind.Dedicated => 0,
        _ => 0
    };
}

/// <summary>Structured SRD intelligent-item personality data.</summary>
public sealed class IntelligentItemDefinition
{
    public Alignment Alignment { get; set; } = Alignment.N;
    public IntelligentItemMentalAbilities MentalAbilities { get; set; } = new();
    public IntelligentItemCommunication Communication { get; set; } = IntelligentItemCommunication.Empathy;
    public IntelligentItemSenses Senses { get; set; } = new();
    public int BasePriceModifierGp { get; set; }
    public List<string> LanguageIds { get; set; } = new();
    public List<IntelligentItemPower> Powers { get; set; } = new();
    public string? SpecialPurpose { get; set; }
    public IntelligentItemPower? DedicatedPower { get; set; }
    public int? EgoOverride { get; set; }

    public bool HasTelepathy => Communication is IntelligentItemCommunication.Telepathy
        or IntelligentItemCommunication.SpeechAndTelepathy;

    public bool HasSpecialPurpose => !string.IsNullOrWhiteSpace(SpecialPurpose);

    public int IntelligenceLanguageAllowance => Math.Max(0, MentalAbilities.IntelligenceBonus);

    public int TotalPriceModifierGp => BasePriceModifierGp
        + Powers.Sum(power => Math.Max(0, power.BasePriceModifierGp))
        + Math.Max(0, DedicatedPower?.BasePriceModifierGp ?? 0);

    /// <summary>Calculates SRD Ego from the item's enhancement, powers, communication, senses, and mind.</summary>
    public int CalculateEgo(int enhancementBonus = 0, int specialAbilityBonus = 0)
    {
        if (EgoOverride.HasValue) return EgoOverride.Value;

        var ego = Math.Max(0, enhancementBonus) + Math.Max(0, specialAbilityBonus)
            + Powers.Sum(p => p.EgoPoints)
            + (HasSpecialPurpose ? 4 : 0)
            + (HasTelepathy ? 1 : 0)
            + (Senses.ReadsSpokenLanguages || Senses.ReadsAllLanguages ? 1 : 0)
            + (Senses.ReadsMagic ? 1 : 0)
            + MentalAbilities.IntelligenceBonus
            + MentalAbilities.WisdomBonus
            + MentalAbilities.CharismaBonus;
        return ego;
    }

    /// <summary>SRD negative levels imposed merely by picking up an item with a mismatched alignment.</summary>
    public int AlignmentNegativeLevels(Alignment wielderAlignment, int enhancementBonus = 0, int specialAbilityBonus = 0)
    {
        if (AlignmentsCorrespond(Alignment, wielderAlignment)) return 0;
        var ego = CalculateEgo(enhancementBonus, specialAbilityBonus);
        return ego >= 30 ? 3 : ego >= 20 ? 2 : 1;
    }

    /// <summary>
    /// Personality conflict is an encounter judgment, not persistent character state. The caller
    /// supplies the two SRD triggers; high-Ego disagreement matters only at Ego 20 or higher.
    /// </summary>
    public bool RequiresPersonalityConflict(
        bool actsAgainstAlignmentOrPurpose,
        bool disagreesWithHighEgoItem,
        int enhancementBonus = 0,
        int specialAbilityBonus = 0) =>
        actsAgainstAlignmentOrPurpose
        || (CalculateEgo(enhancementBonus, specialAbilityBonus) >= 20 && disagreesWithHighEgoItem);

    public bool AlignmentCorresponds(Alignment wielder) => AlignmentsCorrespond(Alignment, wielder);

    public IntelligentItemDefinition Clone() => new()
    {
        Alignment = Alignment,
        MentalAbilities = new IntelligentItemMentalAbilities
        {
            Intelligence = MentalAbilities.Intelligence,
            Wisdom = MentalAbilities.Wisdom,
            Charisma = MentalAbilities.Charisma,
        },
        Communication = Communication,
        Senses = new IntelligentItemSenses
        {
            Vision = Senses.Vision,
            RangeFt = Senses.RangeFt,
            Hearing = Senses.Hearing,
            Blindsense = Senses.Blindsense,
            Blindsight = Senses.Blindsight,
            ReadsSpokenLanguages = Senses.ReadsSpokenLanguages,
            ReadsAllLanguages = Senses.ReadsAllLanguages,
            ReadsMagic = Senses.ReadsMagic,
        },
        BasePriceModifierGp = BasePriceModifierGp,
        LanguageIds = new List<string>(LanguageIds),
        Powers = Powers.Select(ClonePower).ToList(),
        SpecialPurpose = SpecialPurpose,
        DedicatedPower = DedicatedPower == null ? null : ClonePower(DedicatedPower),
        EgoOverride = EgoOverride,
    };

    private static IntelligentItemPower ClonePower(IntelligentItemPower power) => new()
    {
        Kind = power.Kind,
        Name = power.Name,
        Activation = power.Activation,
        BasePriceModifierGp = power.BasePriceModifierGp,
        Description = power.Description,
    };

    private static bool AlignmentsCorrespond(Alignment item, Alignment wielder)
    {
        if (item == wielder) return true;
        return item switch
        {
            Alignment.LN => wielder is Alignment.LG or Alignment.LN or Alignment.LE,
            Alignment.CN => wielder is Alignment.CG or Alignment.CN or Alignment.CE,
            Alignment.NG => wielder is Alignment.LG or Alignment.NG or Alignment.CG,
            Alignment.NE => wielder is Alignment.LE or Alignment.NE or Alignment.CE,
            _ => false,
        };
    }
}

/// <summary>Post-replay readout for an intelligent item currently in use.</summary>
public sealed class IntelligentItemState
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IntelligentItemDefinition Definition { get; set; } = new();
    public int EnhancementBonus { get; set; }
    public int SpecialAbilityBonusEquivalent { get; set; }
    public int Ego { get; set; }
    public int ConflictDc => Ego;
    public bool AlignmentCorresponds { get; set; }
    public int NegativeLevels { get; set; }
}
