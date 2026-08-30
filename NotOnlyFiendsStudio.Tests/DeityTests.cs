using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

public class DeityTests
{
    [Fact]
    public void Registry_LoadsAndResolvesDeityByIdOrImportedName()
    {
        var registry = new ContentRegistry();
        registry.LoadDeitiesFromJson(
            """
            [{
              "id": "deity:test_patron",
              "name": "Test Patron",
              "description": "A test-only patron.",
              "alignment": "lg",
              "titles": ["The Fixture"],
              "portfolio": ["tests", "certainty"],
              "domainIds": ["domain:law", "domain:war"],
              "favoredWeaponId": "weapon:longsword",
              "symbol": "A green check"
            }]
            """);

        var byId = registry.GetDeity("deity:test_patron");
        Assert.Equal(Alignment.LG, byId.Alignment);
        Assert.Equal(new[] { "domain:law", "domain:war" }, byId.DomainIds);
        Assert.True(registry.TryResolveDeity("test patron", out var byName));
        Assert.Same(byId, byName);
    }

    [Fact]
    public void Validate_RejectsUnknownDeityDomainsAndFavoredWeapons()
    {
        var registry = new ContentRegistry();
        registry.RegisterDeity(new DeityDefinition
        {
            Id = "deity:broken",
            Name = "Broken Patron",
            DomainIds = new() { "domain:not_real" },
            FavoredWeaponId = "weapon:not_real"
        });

        registry.Validate();

        Assert.Contains(registry.Errors, error =>
            error.Kind == ContentErrorKind.BrokenReference
            && error.Message.Contains("domain:not_real", StringComparison.Ordinal));
        Assert.Contains(registry.Errors, error =>
            error.Kind == ContentErrorKind.BrokenReference
            && error.Message.Contains("weapon:not_real", StringComparison.Ordinal));
    }

    [Fact]
    public void WarDomain_DerivesFavoredWeaponFromCataloguedDeity()
    {
        var content = TestContentHelper.LoadBundledPacks();
        content.RegisterDeity(new DeityDefinition
        {
            Id = "deity:test_patron",
            Name = "Test Patron",
            Alignment = Alignment.LG,
            DomainIds = new() { "domain:law", "domain:war" },
            FavoredWeaponId = "weapon:longsword"
        });
        var character = new Character
        {
            Name = "Deity Test",
            Deity = "Test Patron", // PCGen/imported display-name form
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 12, DEX = 10, CON = 12, INT = 10, WIS = 16, CHA = 12 },
            Ticks = new()
            {
                new Tick
                {
                    DriverId = "class:cleric",
                    Choices = new TickChoices
                    {
                        ClassFeatureChoices = new()
                        {
                            ["domains"] = new() { "domain:war", "domain:law" },
                            // A stale manual fallback must not override the deity definition.
                            [GrantWarDomainWeaponFeats.ChoiceKey] = new() { "weapon:flail" }
                        }
                    }
                }
            }
        };

        var state = new ReplayStudio(content).Evaluate(character);

        Assert.Contains("feat:martial_weapon_proficiency:longsword", state.Feats);
        Assert.Contains("feat:weapon_focus:longsword", state.Feats);
        Assert.DoesNotContain("feat:weapon_focus:flail", state.Feats);
        Assert.DoesNotContain(state.Warnings,
            warning => warning.Message.Contains("War domain requires", StringComparison.Ordinal));
    }

    [Fact]
    public void WarDomain_KeepsManualFallbackForUncataloguedPatrons()
    {
        var content = TestContentHelper.LoadBundledPacks();
        var character = new Character
        {
            Name = "Legacy Deity Test",
            Deity = "A setting-specific patron",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 12, DEX = 10, CON = 12, INT = 10, WIS = 16, CHA = 12 },
            Ticks = new()
            {
                new Tick
                {
                    DriverId = "class:cleric",
                    Choices = new TickChoices
                    {
                        ClassFeatureChoices = new()
                        {
                            ["domains"] = new() { "domain:war", "domain:law" },
                            [GrantWarDomainWeaponFeats.ChoiceKey] = new() { "weapon:longsword" }
                        }
                    }
                }
            }
        };

        var state = new ReplayStudio(content).Evaluate(character);

        Assert.Contains("feat:weapon_focus:longsword", state.Feats);
    }
}
