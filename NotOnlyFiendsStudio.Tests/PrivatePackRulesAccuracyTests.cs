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

    // PRESKILL:1,Knowledge (Necrology)=10 (necromancy_classes.lst). User ruling
    // 2026-07-28: Necrology is deliberately a costly niche skill — it exists as
    // skill:knowledge_necrology rather than being mapped onto arcana/religion.
    [RequiresPrivatePacksFact]
    public void Deathseeker_RequiresTenKnowledgeNecrologyRanks()
    {
        Assert.Equal(10, SkillPrereq("class:deathseeker", "skill:knowledge_necrology").Value);
    }

    // PRESKILL:2,Knowledge (Arcana)=7,Knowledge (Necrology)=7 (necromancy_classes.lst)
    [RequiresPrivatePacksFact]
    public void SpectralLoremaster_RequiresSevenArcanaAndNecrologyRanks()
    {
        Assert.Equal(7, SkillPrereq("class:spectral_loremaster", "skill:knowledge_arcana").Value);
        Assert.Equal(7, SkillPrereq("class:spectral_loremaster", "skill:knowledge_necrology").Value);
    }

    // PRESKILL:2,Knowledge (Arcana)=8,Knowledge (Nature)=8 (deceit LST — whole ranks,
    // not the half-rank-doubled 16 the extraction produced)
    [RequiresPrivatePacksFact]
    public void ArcaneHierophant_RequiresEightKnowledgeRanks()
    {
        Assert.Equal(8, SkillPrereq("class:arcane_hierophant", "skill:knowledge_arcana").Value);
        Assert.Equal(8, SkillPrereq("class:arcane_hierophant", "skill:knowledge_nature").Value);
    }

    // Class line 10: DR:10/Cold Iron or Good (enchantment_classes_35e.lst); template
    // line: BONUS:SKILL|Listen,Spot|8|TYPE=Racial (enchantment_templates.lst:8) —
    // these were prose-only before the P3 fix pass.
    [RequiresPrivatePacksFact]
    public void DarkTemptress_LevelTenGrantsStructuredDrAndTemplateGrantsListenSpot()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var dr = ((HDDriver)registry.GetDriver("class:dark_temptress")).LevelPermabuffs[10]
            .OfType<GrantDR>().Single();
        Assert.Equal(10, dr.Value);
        Assert.Equal("cold iron or good", dr.BypassedBy);

        var listenSpot = registry.GetTemplate("template:dark_temptress_succubized")
            .CreationPermabuffs.OfType<GrantSkillBonus>()
            .Where(b => b.Value == 8)
            .Select(b => b.SkillId)
            .ToHashSet();
        Assert.Superset(new HashSet<string> { "skill:listen", "skill:spot" }, listenSpot);
    }

    // FACT:SpellType|Arcane (fairytale_advancedfiend_classes.lst). User ruling
    // 2026-07-28: archfiends cast as arcane (sorcerer-style spontaneous); cleric/druid
    // access comes from the spell-list selection templates, not divine casting.
    [RequiresPrivatePacksFact]
    public void Archfiend_CastsArcane()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var driver = (HDDriver)registry.GetDriver("class:archfiend");
        Assert.Equal(CastingType.Arcane, driver.Spellcasting!.CastingType);
    }

    // BONUS:ABILITYPOOL|Blood Hexer Feat|1 at 3/7/10, restricted via
    // ABILITYCATEGORY:Blood Hexer Feat ... TYPE:Metamagic (curses_abilitycategories.lst)
    [RequiresPrivatePacksFact]
    public void BloodHexer_BonusFeatSlotsAreMetamagicOnly()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var slots = ((HDDriver)registry.GetDriver("class:blood_hexer")).LevelPermabuffs
            .SelectMany(kv => kv.Value.OfType<GrantFeatSlot>())
            .ToList();
        Assert.Equal(3, slots.Count);
        Assert.All(slots, s => Assert.Equal("metamagic", s.Restriction));
    }
}
