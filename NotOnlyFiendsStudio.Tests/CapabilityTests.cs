using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// <c>CharacterState.Capabilities</c> was entirely write-only: <c>GrantCapability</c> wrote it,
/// state and sheet carried it, and no engine logic, prerequisite, UI, API or test read it anywhere.
/// The sheet now renders it, so these pin the data that display depends on — a content regression
/// that emptied the wild shape matrix would otherwise just make the card quietly disappear.
///
/// The rendering itself (grouping <c>wild_shape:&lt;kind&gt;:&lt;size&gt;</c> into per-kind lines)
/// lives in SheetView.razor and has no unit-test harness in this project; it is verified by running
/// the app.
/// </summary>
public class CapabilityTests
{
    private static CharacterState Druid(int levels)
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var character = new Character
        {
            Name = "Wild Shaper",
            RaceId = "race:human",
            Alignment = Alignment.N,
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 16, CHA = 10 },
        };
        for (int i = 0; i < levels; i++)
            character.Ticks.Add(new Tick { DriverId = "class:druid" });

        return new ReplayStudio(registry).Evaluate(character);
    }

    [Fact]
    public void DruidBelowFifth_HasNoWildShapeForms()
    {
        Assert.Empty(Druid(4).Capabilities);
    }

    [Fact]
    public void DruidAtFifth_CanTakeSmallAndMediumAnimalForms()
    {
        var capabilities = Druid(5).Capabilities;

        Assert.Contains("wild_shape:animal:small", capabilities);
        Assert.Contains("wild_shape:animal:medium", capabilities);
        Assert.DoesNotContain("wild_shape:animal:large", capabilities);
        Assert.DoesNotContain("wild_shape:plant:small", capabilities);
    }

    [Fact]
    public void DruidAtTwentieth_HasTheWholeWildShapeMatrix()
    {
        // 14 entries across three kinds — the reason the sheet groups them rather than listing
        // raw strings.
        var capabilities = Druid(20).Capabilities;

        var wildShape = capabilities.Where(c => c.StartsWith("wild_shape:", StringComparison.Ordinal)).ToList();
        Assert.Equal(14, wildShape.Count);

        foreach (var size in new[] { "tiny", "small", "medium", "large", "huge" })
        {
            Assert.Contains($"wild_shape:animal:{size}", capabilities);
            Assert.Contains($"wild_shape:plant:{size}", capabilities);
        }

        // Elemental forms start at Small — a druid never takes a Tiny elemental.
        foreach (var size in new[] { "small", "medium", "large", "huge" })
            Assert.Contains($"wild_shape:elemental:{size}", capabilities);
        Assert.DoesNotContain("wild_shape:elemental:tiny", capabilities);
    }

    [Fact]
    public void Capabilities_ReachTheCharacterSheet()
    {
        // The sheet snapshot is also the REST API payload (AgentApiService builds every response
        // from CharacterSheet.FromState), so this covers both surfaces.
        var sheet = CharacterSheet.FromState(Druid(16));

        Assert.Contains("wild_shape:elemental:large", sheet.Capabilities);
    }

    [Fact]
    public void EveryCapabilityInBundledContent_IsColonDelimited()
    {
        // The sheet's grouping assumes "family:detail[:detail]". Nothing enforced a shape on these
        // strings before, so this records the convention the display now relies on.
        var registry = TestContentHelper.LoadBundledPacks();

        var capabilities = registry.GetAllDrivers().OfType<HDDriver>()
            .SelectMany(d => d.LevelPermabuffs.Values.SelectMany(b => b).Concat(d.PerLevelPermabuffs))
            .OfType<GrantCapability>()
            .Select(g => g.Capability)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(capabilities);
        Assert.All(capabilities, c => Assert.Contains(':', c));
    }
}

/// <summary>
/// <c>SLA.SaveDC</c> was stored by content and never displayed: the sheet rendered spell-like
/// abilities as name plus uses/day only, so a save DC a player needs at the table never reached
/// them. Eight SLAs across six bundled races carry one.
/// </summary>
public class SpellLikeAbilitySaveDCTests
{
    [Fact]
    public void GrigSpellLikeAbilities_CarryTheirSaveDCs()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var character = new Character
        {
            Name = "Grig",
            RaceId = "race:grig",
            Alignment = Alignment.NG,
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
        };

        var state = new ReplayStudio(registry).Evaluate(character);

        var entangle = state.SLAs.Single(s => s.Name.Contains("Entangle", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(13, entangle.SaveDC);

        var pyrotechnics = state.SLAs.Single(s => s.Name.Contains("Pyrotechnics", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(14, pyrotechnics.SaveDC);
    }

    [Fact]
    public void SaveDCsSurviveOntoTheCharacterSheet()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var character = new Character
        {
            Name = "Couatl",
            RaceId = "race:couatl",
            Alignment = Alignment.LG,
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
        };

        var sheet = CharacterSheet.FromState(new ReplayStudio(registry).Evaluate(character));

        Assert.Contains(sheet.SLAs, s => s.SaveDC == 20);
    }
}
