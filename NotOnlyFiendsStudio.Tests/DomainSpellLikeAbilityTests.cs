using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

public class DomainSpellLikeAbilityTests
{
    private static ContentRegistry Content() => TestContentHelper.LoadBundledPacks();

    /// <summary>A cleric who picks the given domains, then a template that grants domain SLAs.</summary>
    private static Character ClericWithDomains(params string[] domains)
    {
        var ticks = Enumerable.Range(0, 3)
            .Select(_ => new Tick { DriverId = "class:cleric" })
            .ToList();
        ticks[0].Choices.ClassFeatureChoices = new Dictionary<string, List<string>>
        {
            ["domains"] = domains.ToList()
        };

        return new Character
        {
            Name = "Domain SLA probe",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 14, CHA = 18
            },
            Ticks = ticks
        };
    }

    private static CharacterState EvaluateWithGrant(
        Character character, GrantDomainSpellLikeAbilities grant, ContentRegistry content)
    {
        // Applied as a permanent event before the first tick — the same "creation permabuff"
        // position the ascended-archfiend template uses, and the case the tail pass exists for:
        // domains are not chosen yet at this point.
        character.PermanentEvents.Add(new PermanentEvent
        {
            BeforeTick = 0,
            Permabuffs = new List<Permabuff> { grant }
        });
        return new ReplayStudio(content).Evaluate(character);
    }

    [Fact]
    public void DomainBonusSpellsBecomeSpellLikeAbilities()
    {
        var content = Content();
        var state = EvaluateWithGrant(
            ClericWithDomains("domain:fire"), new GrantDomainSpellLikeAbilities(), content);

        var fireDomain = content.GetDomain("domain:fire");
        Assert.NotEmpty(fireDomain.BonusSpells);

        // Every bonus spell in the chosen domain should have produced an SLA.
        foreach (var spellId in fireDomain.BonusSpells.Values)
            Assert.Contains(state.SLAs, s => s.Id == $"domain_sla_{spellId}");
    }

    [Fact]
    public void UsageTierFollowsSpellLevel()
    {
        var grant = new GrantDomainSpellLikeAbilities();

        Assert.Equal("at will", grant.UsesFor(1));
        Assert.Equal("at will", grant.UsesFor(3));
        Assert.Equal("3/day", grant.UsesFor(4));
        Assert.Equal("3/day", grant.UsesFor(6));
        Assert.Equal("1/day", grant.UsesFor(7));
        Assert.Equal("1/day", grant.UsesFor(9));
        // Above the top tier nothing is granted at all, rather than defaulting to once a day.
        Assert.Null(grant.UsesFor(10));
    }

    [Fact]
    public void SaveDcIsTenPlusSpellLevelPlusAbilityModifier()
    {
        var content = Content();
        var state = EvaluateWithGrant(
            ClericWithDomains("domain:fire"), new GrantDomainSpellLikeAbilities(), content);

        var fire = content.GetDomain("domain:fire");
        var (level, spellId) = fire.BonusSpells.OrderBy(kv => kv.Key).First();
        var sla = state.SLAs.Single(s => s.Id == $"domain_sla_{spellId}");

        // CHA 18 → +4.
        Assert.Equal(10 + level + 4, sla.SaveDC);
        Assert.Equal(state.TotalHD, sla.CasterLevel);
    }

    [Fact]
    public void NoGrantMeansNoDomainSlas()
    {
        // A plain cleric with domains must not sprout spell-like abilities.
        var state = new ReplayStudio(Content()).Evaluate(ClericWithDomains("domain:fire"));

        Assert.DoesNotContain(state.SLAs, s => s.Id.StartsWith("domain_sla_"));
    }

    [Fact]
    public void ASpellInTwoDomainsIsGrantedOnce()
    {
        var content = Content();
        var state = EvaluateWithGrant(
            ClericWithDomains("domain:good", "domain:law"), new GrantDomainSpellLikeAbilities(), content);

        var ids = state.SLAs.Where(s => s.Id.StartsWith("domain_sla_")).Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
