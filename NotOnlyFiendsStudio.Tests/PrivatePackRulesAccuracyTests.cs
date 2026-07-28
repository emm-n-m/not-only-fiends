using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// Assertions from the 2026-07-27 LST audit of the private packs (see
/// {EXTRA_PACKS_PATH}/test-reports/lst_audit_2026-07-27.md). Each carries the
/// PCGen LST fragment that is its ground truth. Only runs when the private
/// packs are available.
/// </summary>
public class PrivatePackRulesAccuracyTests
{
    private static MinSkillRanks SkillPrereq(string driverId, string skillId)
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var driver = registry.GetDriver(driverId);
        return Assert.Single(driver.Prerequisites.OfType<MinSkillRanks>(), p => p.SkillId == skillId);
    }

    // PRESKILL:1,Knowledge (Necrology)=10 (necromancy_classes.lst; knowledge_arcana
    // substituted for the engine-less Necrology skill)
    [RequiresPrivatePacksFact]
    public void Deathseeker_RequiresTenKnowledgeRanks()
    {
        Assert.Equal(10, SkillPrereq("class:deathseeker", "skill:knowledge_arcana").Value);
    }

    // PRESKILL:2,Knowledge (Arcana)=7,Knowledge (Necrology)=7 (necromancy_classes.lst)
    [RequiresPrivatePacksFact]
    public void SpectralLoremaster_RequiresSevenKnowledgeArcanaRanks()
    {
        Assert.Equal(7, SkillPrereq("class:spectral_loremaster", "skill:knowledge_arcana").Value);
    }

    // PRESKILL:2,Knowledge (Arcana)=8,Knowledge (Nature)=8 (deceit LST — whole ranks,
    // not the half-rank-doubled 16 the extraction produced)
    [RequiresPrivatePacksFact]
    public void ArcaneHierophant_RequiresEightKnowledgeRanks()
    {
        Assert.Equal(8, SkillPrereq("class:arcane_hierophant", "skill:knowledge_arcana").Value);
        Assert.Equal(8, SkillPrereq("class:arcane_hierophant", "skill:knowledge_nature").Value);
    }
}
