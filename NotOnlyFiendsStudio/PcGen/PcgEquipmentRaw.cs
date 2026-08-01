namespace NotOnlyFiendsStudio.PcGen;

/// <summary>
/// One item declared by an EQUIPNAME row in a .pcg file, with slot/active info
/// resolved by joining against EQUIPSET + CALCEQUIPSET during parsing.
/// </summary>
public class PcgEquipmentRaw
{
    public string Name { get; set; } = "";
    public double Quantity { get; set; } = 1;
    public double WeightLbs { get; set; }
    public long PriceCp { get; set; }
    public string? Customization { get; set; }
    public string? BaseItemName { get; set; }
    public string? SlotName { get; set; }
    public bool InActiveSet { get; set; }
}
