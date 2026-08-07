using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class ContentConflictTests
{
    private static FeatDefinition MakeFeat(string id, FeatType type = FeatType.General) =>
        new() { Id = id, Name = id, Type = type };

    [Fact]
    public void LastWins_SecondDefinitionUsed()
    {
        var registry = new ContentRegistry { OnConflict = ConflictResolution.LastWins };
        registry.RegisterFeat(MakeFeat("test_feat", FeatType.General));
        registry.RegisterFeat(MakeFeat("test_feat", FeatType.FighterBonus));

        var feat = registry.GetFeat("test_feat");
        Assert.Equal(FeatType.FighterBonus, feat.Type);
        Assert.False(registry.HasErrors);
    }

    [Fact]
    public void FirstWins_OriginalKept()
    {
        var registry = new ContentRegistry { OnConflict = ConflictResolution.FirstWins };
        registry.RegisterFeat(MakeFeat("test_feat", FeatType.General));
        registry.RegisterFeat(MakeFeat("test_feat", FeatType.FighterBonus));

        var feat = registry.GetFeat("test_feat");
        Assert.Equal(FeatType.General, feat.Type);
        Assert.False(registry.HasErrors);
    }

    [Fact]
    public void Warn_AcceptsLastAndLogsWarning()
    {
        var registry = new ContentRegistry { OnConflict = ConflictResolution.Warn };
        registry.RegisterFeat(MakeFeat("test_feat", FeatType.General));
        registry.RegisterFeat(MakeFeat("test_feat", FeatType.FighterBonus));

        var feat = registry.GetFeat("test_feat");
        Assert.Equal(FeatType.FighterBonus, feat.Type);
        Assert.False(registry.HasErrors);
        Assert.True(registry.HasWarnings);
        Assert.Contains(registry.Errors, e => e.Kind == ContentErrorKind.DuplicateId && e.IsWarning);
    }

    [Fact]
    public void Error_RejectsAndKeepsFirst()
    {
        var registry = new ContentRegistry { OnConflict = ConflictResolution.Error };
        registry.RegisterFeat(MakeFeat("test_feat", FeatType.General));
        registry.RegisterFeat(MakeFeat("test_feat", FeatType.FighterBonus));

        var feat = registry.GetFeat("test_feat");
        Assert.Equal(FeatType.General, feat.Type);
        Assert.True(registry.HasErrors);
        Assert.Contains(registry.Errors, e => e.Kind == ContentErrorKind.DuplicateId);
    }

    [Fact]
    public void Validate_DoesNotEraseLoadConflicts()
    {
        var registry = new ContentRegistry { OnConflict = ConflictResolution.Error };
        registry.RegisterFeat(MakeFeat("test_feat", FeatType.General));
        registry.RegisterFeat(MakeFeat("test_feat", FeatType.FighterBonus));

        registry.Validate();

        Assert.True(registry.HasErrors);
        Assert.Contains(registry.Errors, e => e.Kind == ContentErrorKind.DuplicateId);
    }

    [Fact]
    public void NoDuplicate_NoErrorsAnyMode()
    {
        foreach (var mode in Enum.GetValues<ConflictResolution>())
        {
            var registry = new ContentRegistry { OnConflict = mode };
            registry.RegisterFeat(MakeFeat("feat_a"));
            registry.RegisterFeat(MakeFeat("feat_b"));

            Assert.False(registry.HasErrors);
            Assert.Equal(2, registry.GetAllFeats().Count());
        }
    }

    [Fact]
    public void WarnAndError_ApplyToRepresentativeNonFeatContent()
    {
        var warningRegistry = new ContentRegistry { OnConflict = ConflictResolution.Warn };
        warningRegistry.RegisterRace(new RaceDefinition { Id = "race:duplicate" });
        warningRegistry.RegisterRace(new RaceDefinition { Id = "race:duplicate", Name = "Later" });
        warningRegistry.RegisterTemplate(new TemplateDriver { Id = "template:duplicate" });
        warningRegistry.RegisterTemplate(new TemplateDriver { Id = "template:duplicate", Name = "Later" });
        warningRegistry.RegisterDomain(new DomainDefinition { Id = "domain:duplicate" });
        warningRegistry.RegisterDomain(new DomainDefinition { Id = "domain:duplicate", Name = "Later" });
        warningRegistry.RegisterSpell(new SpellDefinition { Id = "spell:duplicate" });
        warningRegistry.RegisterSpell(new SpellDefinition { Id = "spell:duplicate", Name = "Later" });
        warningRegistry.RegisterDriver(new HDDriver { Id = "class:duplicate" });
        warningRegistry.RegisterDriver(new HDDriver { Id = "class:duplicate", Name = "Later" });

        Assert.False(warningRegistry.HasErrors);
        Assert.Equal(5, warningRegistry.Errors.Count(error => error.IsWarning));

        var errorRegistry = new ContentRegistry { OnConflict = ConflictResolution.Error };
        errorRegistry.RegisterRace(new RaceDefinition { Id = "race:duplicate" });
        errorRegistry.RegisterRace(new RaceDefinition { Id = "race:duplicate", Name = "Later" });
        errorRegistry.RegisterTemplate(new TemplateDriver { Id = "template:duplicate" });
        errorRegistry.RegisterTemplate(new TemplateDriver { Id = "template:duplicate", Name = "Later" });
        errorRegistry.RegisterDomain(new DomainDefinition { Id = "domain:duplicate" });
        errorRegistry.RegisterDomain(new DomainDefinition { Id = "domain:duplicate", Name = "Later" });
        errorRegistry.RegisterSpell(new SpellDefinition { Id = "spell:duplicate" });
        errorRegistry.RegisterSpell(new SpellDefinition { Id = "spell:duplicate", Name = "Later" });
        errorRegistry.RegisterDriver(new HDDriver { Id = "class:duplicate" });
        errorRegistry.RegisterDriver(new HDDriver { Id = "class:duplicate", Name = "Later" });

        Assert.True(errorRegistry.HasErrors);
        Assert.Equal(5, errorRegistry.Errors.Count(error => !error.IsWarning));
    }
}
