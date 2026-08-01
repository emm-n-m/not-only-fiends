using NotOnlyFiendsStudio.PcGen;

namespace NotOnlyFiendsStudio.Tests.PcGen;

public class PcgSkillImportTests
{
    [Theory]
    [InlineData("Knowledge (Ancient History)", "skill:knowledge_ancient_history")]
    [InlineData("Knowledge (Demonology)", "skill:knowledge_demonology")]
    [InlineData("Knowledge (Fey)", "skill:knowledge_fey")]
    [InlineData("Knowledge (History/Abyss)", "skill:knowledge_history_abyss")]
    [InlineData("Knowledge (Monster Lore)", "skill:knowledge_monster_lore")]
    [InlineData("Craft (Tattoo)", "skill:craft_tattoo")]
    [InlineData("Knowledge (Architecture and Engineering)", "skill:knowledge_architecture")]
    public void PcGenSkillNames_MapToCanonicalIds(string pcgenName, string expectedId)
    {
        Assert.Equal(expectedId, new PcgIdMapper().MapSkill(pcgenName));
    }

    [Fact]
    public void ArchitectureAndEngineering_HasCanonicalNameAndSearchSynergy()
    {
        var skill = TestContentHelper.LoadBundledPacks().GetAllSkills()
            .Single(s => s.Id == "skill:knowledge_architecture");

        Assert.Equal("Knowledge (Architecture and Engineering)", skill.Name);
        Assert.Equal("skill:knowledge", skill.ParentSkill);
        var synergy = Assert.Single(skill.Synergies);
        Assert.Equal("skill:search", synergy.TargetSkillId);
        Assert.Equal(2, synergy.Bonus);
    }

    [RequiresPrivatePacksFact]
    public void ThirdPartySkillDefinitions_AreAvailableToImporter()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var expected = new Dictionary<string, string>
        {
            ["skill:knowledge_ancient_history"] = "skill:knowledge",
            ["skill:knowledge_demonology"] = "skill:knowledge",
            ["skill:knowledge_fey"] = "skill:knowledge",
            ["skill:knowledge_history_abyss"] = "skill:knowledge",
            ["skill:knowledge_monster_lore"] = "skill:knowledge",
            ["skill:craft_tattoo"] = "skill:craft",
        };

        foreach (var (id, parent) in expected)
        {
            Assert.True(registry.TryGetSkill(id, out var skill), $"Missing {id}");
            Assert.NotNull(skill);
            Assert.Equal(parent, skill.ParentSkill);
            Assert.True(skill.TrainedOnly);
            Assert.False(skill.ArmorCheckPenalty);
        }
    }
}
