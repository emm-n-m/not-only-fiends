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
}
