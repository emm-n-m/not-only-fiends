using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// The Archfiend rebuilt from first principles: outsider racial HD carrying the chassis, and
/// <c>template:archfiend</c> carrying what actually makes something an archfiend — casting equal to
/// its own Hit Dice, Rebuke Undead, two domains. The old <c>class:archfiend</c> stays in content as
/// the casting identity the template seeds and the list templates point at, but nobody takes levels
/// of it: more archfiend is more racial HD, of which 24 is the floor rather than an allotment.
/// </summary>
public class ArchfiendRacialCastingTests
{
    // Ember: 29 racial HD, then the classes she bought on top.
    private const int RacialHD = 29;

    private static Character Archfiend(int racialHd = RacialHD, int paladinLevels = 2, int archmageLevels = 5)
    {
        var character = new Character
        {
            Name = "Archfiend",
            RaceId = "race:archfiend",
            Alignment = Alignment.LE,
            TemplateIds = new List<string> { "template:archfiend", "template:archfiend_arcane_list" },
            BaseAbilityScores = new AbilityScoreSet { STR = 8, DEX = 14, CON = 14, INT = 14, WIS = 10, CHA = 18 },
            Ticks = Enumerable.Range(0, racialHd)
                .Select(_ => new Tick { DriverId = "racial_hd:outsider" })
                .ToList(),
        };

        character.Ticks.AddRange(Enumerable.Range(0, paladinLevels)
            .Select(_ => new Tick { DriverId = "class:paladin_of_tyranny" }));

        character.Ticks.AddRange(Enumerable.Range(0, archmageLevels).Select(_ => new Tick
        {
            DriverId = "class:archmage",
            Choices = new TickChoices
            {
                ClassFeatureChoices = new Dictionary<string, List<string>>
                {
                    ["advance_spellcasting"] = new() { "class:archfiend" },
                },
            },
        }));

        return character;
    }

    private static CharacterState Evaluate(Character character) =>
        new ReplayStudio(TestContentHelper.LoadBundledAndPrivatePacksIfAvailable()).Evaluate(character);

    /// <summary>
    /// The template seeds the caster even though no tick is a <c>class:archfiend</c> level, which is
    /// the point of moving the grant off the race: before this, only a race could say "casts as".
    /// </summary>
    [RequiresPrivatePacksFact]
    public void Template_SeedsCastingWithNoClassLevelsTaken()
    {
        var state = Evaluate(Archfiend(archmageLevels: 0));

        Assert.False(state.ClassLevels.ContainsKey("class:archfiend"));
        Assert.Equal(RacialHD, state.Spellcasting["class:archfiend"].CasterLevel);
        Assert.Equal(CastingType.Arcane, state.Spellcasting["class:archfiend"].CastingType);
    }

    /// <summary>
    /// PCGen computes Ember's caster level as <c>classlevel()</c> 29 plus five Archmage levels. The
    /// rebuild reaches the same 34 from the other direction: 29 racial HD granted by the template,
    /// advanced five times.
    ///
    /// Slots come from the 20th-level row, where the printed progression stops — epic levels raise
    /// caster level without adding slots. That row is the sorcerer's, which is the whole point: the
    /// class has the sorcerer's spells known and casting stat and prepares nothing, so it casts on
    /// the sorcerer line. It carried the cleric's per-day table until 2026-08-15.
    /// </summary>
    [RequiresPrivatePacksFact]
    public void CasterLevel_IsRacialHDPlusArchmage_WithSlotsFromTheTablesLastRow()
    {
        var state = Evaluate(Archfiend());
        var casting = state.Spellcasting["class:archfiend"];

        Assert.Equal(RacialHD + 5, casting.CasterLevel);
        Assert.Equal(new[] { 6, 6, 6, 6, 6, 6, 6, 6, 6, 6 },
            Enumerable.Range(0, 10).Select(level => casting.SpellsPerDay.GetValueOrDefault(level)).ToArray());
    }

    /// <summary>
    /// The mismatch that made her sheet wrong: a spontaneous caster on a prepared caster's slot
    /// table. Spells known and spells per day must come from the same class, and for this class
    /// that is the sorcerer.
    /// </summary>
    [RequiresPrivatePacksFact]
    public void SpellsPerDayAndSpellsKnown_BothFollowTheSorcerer()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var archfiend = ((HDDriver)registry.GetDriver("class:archfiend")).Spellcasting!;
        var sorcerer = ((HDDriver)registry.GetDriver("class:sorcerer")).Spellcasting!;

        for (var level = 1; level <= 20; level++)
        {
            Assert.Equal(sorcerer.SpellsPerDay[level], archfiend.SpellsPerDay[level]);
            Assert.Equal(sorcerer.SpellsKnown![level], archfiend.SpellsKnown![level]);
        }
    }

    /// <summary>
    /// The chassis is the shared outsider driver, so the race has to carry the skill list the old
    /// class did or a caster's own Concentration and Spellcraft would cost double for 29 HD.
    /// </summary>
    [RequiresPrivatePacksFact]
    public void RacialHD_KeepTheArchfiendClassSkills()
    {
        var state = Evaluate(Archfiend());

        Assert.Contains("skill:concentration", state.ClassSkills);
        Assert.Contains("skill:spellcraft", state.ClassSkills);
        Assert.Contains("skill:knowledge_arcana", state.ClassSkills);
        Assert.Contains("skill:use_magic_device", state.ClassSkills);
    }

    /// <summary>
    /// Racial HD grant no every-four-levels ability increase, so the six prompts that used to sit on
    /// HD 4–24 cannot come back by this route: only the bought class levels can take one.
    /// </summary>
    [RequiresPrivatePacksFact]
    public void RacialHD_OweNoAbilityIncrease()
    {
        var rules = GameRules.Standard35e();

        Assert.False(rules.GrantsAbilityIncrease(4, DriverKind.RacialHD));
        Assert.False(rules.GrantsAbilityIncrease(24, DriverKind.RacialHD));
        Assert.True(rules.GrantsAbilityIncrease(32, DriverKind.Class));
    }

    /// <summary>
    /// Ember's real shape: she goes back for more racial HD after her class levels. The grant is
    /// seeded before the first class tick so Archmage can find it, at which point only 24 of her 29
    /// racial HD exist — the finalize pass owes her the other five. PCGen agrees: classlevel() 29
    /// plus five Archmage is 34.
    /// </summary>
    [RequiresPrivatePacksFact]
    public void RacialHD_GainedAfterTheFirstClassTick_StillCount()
    {
        var character = Archfiend(racialHd: 24);
        character.Ticks.AddRange(Enumerable.Range(0, 5)
            .Select(_ => new Tick { DriverId = "racial_hd:outsider" }));

        var state = Evaluate(character);

        Assert.Equal(29, state.TotalHD - 7);
        Assert.Equal(34, state.Spellcasting["class:archfiend"].CasterLevel);
    }

    /// <summary>
    /// A domain picked on a racial-HD tick still belongs to the caster the template granted it to.
    /// Handing it to the tick's own driver put her domain spells on the outsider chassis, which has
    /// no spell list to put them on — eight of her spells fell off the Archfiend list.
    /// </summary>
    [RequiresPrivatePacksFact]
    public void DomainsPickedOnARacialTick_BelongToTheGrantedCaster()
    {
        var character = Archfiend();
        character.Ticks[0].Choices = new TickChoices
        {
            ClassFeatureChoices = new Dictionary<string, List<string>>
            {
                // The key the importer writes when the tick's own driver grants no domain slots.
                ["imported_source_domains"] = new() { "domain:lust", "domain:fire" },
            },
        };

        var state = Evaluate(character);

        Assert.Equal("class:archfiend", state.DomainOwners["domain:lust"]);
        Assert.Equal("class:archfiend", state.DomainOwners["domain:fire"]);
    }

    /// <summary>The two domains and Rebuke Undead ride the template now, not the class's 1st level.</summary>
    [RequiresPrivatePacksFact]
    public void Template_CarriesTheDomainsAndRebukeUndead()
    {
        var state = Evaluate(Archfiend());

        Assert.Equal(2, state.PendingDomainSelections.Values.Sum());
        Assert.Contains(state.Abilities, ability => ability.Id == "archfiend_rebuke_undead");
    }
}
