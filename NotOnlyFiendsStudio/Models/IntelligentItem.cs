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
    public string Vision { get; set; } = "vision";
    public int RangeFt { get; set; }
    public bool Hearing { get; set; } = true;
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
    public List<IntelligentItemPower> Powers { get; set; } = new();
    public string? SpecialPurpose { get; set; }
    public IntelligentItemPower? DedicatedPower { get; set; }
    public int? EgoOverride { get; set; }

    public bool HasTelepathy => Communication is IntelligentItemCommunication.Telepathy
        or IntelligentItemCommunication.SpeechAndTelepathy;

    public bool HasSpecialPurpose => !string.IsNullOrWhiteSpace(SpecialPurpose);

    /// <summary>Calculates SRD Ego from the item's enhancement, powers, communication, senses, and mind.</summary>
    public int CalculateEgo(int enhancementBonus = 0, int specialAbilityBonus = 0)
    {
        if (EgoOverride.HasValue) return EgoOverride.Value;

        var ego = Math.Max(0, enhancementBonus) + Math.Max(0, specialAbilityBonus)
            + Powers.Sum(p => p.EgoPoints)
            + (HasSpecialPurpose ? 4 : 0)
            + (HasTelepathy ? 1 : 0)
            + (Senses.ReadsAllLanguages ? 1 : 0)
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

    public bool CausesPersonalityConflict(bool wielderAlignmentMatches, bool pursuesPurpose) =>
        !wielderAlignmentMatches || (HasSpecialPurpose && !pursuesPurpose)
        || (CalculateEgo() >= 20 && !pursuesPurpose);

    private static bool AlignmentsCorrespond(Alignment item, Alignment wielder)
    {
        if (item == wielder) return true;
        if (item == Alignment.N) return false;

        var itemLaw = item is Alignment.LG or Alignment.LN or Alignment.LE;
        var itemChaos = item is Alignment.CG or Alignment.CN or Alignment.CE;
        var itemGood = item is Alignment.LG or Alignment.NG or Alignment.CG;
        var itemEvil = item is Alignment.LE or Alignment.NE or Alignment.CE;
        var wielderLaw = wielder is Alignment.LG or Alignment.LN or Alignment.LE;
        var wielderChaos = wielder is Alignment.CG or Alignment.CN or Alignment.CE;
        var wielderGood = wielder is Alignment.LG or Alignment.NG or Alignment.CG;
        var wielderEvil = wielder is Alignment.LE or Alignment.NE or Alignment.CE;

        return (itemLaw && wielderLaw) || (itemChaos && wielderChaos)
            || (itemGood && wielderGood) || (itemEvil && wielderEvil);
    }
}
