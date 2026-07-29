using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// Three different ways a class comes by its spells, which the builder previously collapsed into
/// one: it offered a "spells known" style picker to every caster. A cleric and a druid know their
/// entire list and prepare from it daily — there is no level-up choice to make — and a wizard's
/// spellbook is bounded by its own rule rather than by a per-level "known" count.
///
/// The engine already drew the prepared/spontaneous line (<c>SpellsKnown == null</c>, the test
/// <see cref="HasSpontaneousCasting"/> uses); what was missing was a third case for the wizard and
/// anything at all consuming the distinction.
/// </summary>
public class SpellAcquisitionTests
{
    private static readonly Lazy<ContentRegistry> Content =
        new(() => TestContentHelper.LoadBundledPacks());

    private static SpellcastingProgression Progression(string classId) =>
        ((HDDriver)Content.Value.GetDriver(classId)).Spellcasting!;

    [Theory]
    // Divine and arcane full-list preparers alike — the distinction is not arcane vs divine.
    [InlineData("class:cleric", SpellAcquisition.FullList)]
    [InlineData("class:cloistered_cleric", SpellAcquisition.FullList)]
    [InlineData("class:druid", SpellAcquisition.FullList)]
    [InlineData("class:paladin", SpellAcquisition.FullList)]
    [InlineData("class:ranger", SpellAcquisition.FullList)]
    [InlineData("class:adept", SpellAcquisition.FullList)]
    [InlineData("class:blackguard", SpellAcquisition.FullList)]
    [InlineData("class:wizard", SpellAcquisition.Spellbook)]
    [InlineData("class:sorcerer", SpellAcquisition.SpellsKnown)]
    [InlineData("class:bard", SpellAcquisition.SpellsKnown)]
    [InlineData("class:assassin", SpellAcquisition.SpellsKnown)]
    public void EveryBundledCaster_ResolvesToTheRightAcquisition(string classId, SpellAcquisition expected)
    {
        Assert.Equal(expected, Progression(classId).ResolvedAcquisition);
    }

    [Fact]
    public void OnlyTheWizard_NeedsAnExplicitAcquisitionInContent()
    {
        // The inference (spellsKnown present → SpellsKnown, else FullList) is what keeps this
        // change from touching a dozen class files. If a second spellbook class is ever added it
        // must set the field explicitly, and this records that expectation.
        var explicitlySet = Content.Value.GetAllDrivers().OfType<HDDriver>()
            .Where(d => d.Spellcasting?.Acquisition != null)
            .Select(d => d.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "class:wizard" }, explicitlySet);
    }

    [Fact]
    public void AcquisitionReachesEvaluatedState_AndTheSheet()
    {
        var cleric = Evaluate("class:cleric", levels: 5);
        Assert.Equal(SpellAcquisition.FullList, cleric.Spellcasting["class:cleric"].Acquisition);

        var sheet = CharacterSheet.FromState(cleric);
        Assert.Equal(SpellAcquisition.FullList, sheet.Spellcasting["class:cleric"].Acquisition);
    }

    private static CharacterState Evaluate(string classId, int levels, int intelligence = 10,
        List<SpellSelection>? spells = null)
    {
        var character = new Character
        {
            Name = "Caster",
            RaceId = "race:human",
            Alignment = Alignment.LG,
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = intelligence, WIS = 14, CHA = 14,
            },
        };
        for (int i = 0; i < levels; i++)
            character.Ticks.Add(new Tick { DriverId = classId });

        if (spells != null)
            character.Ticks[^1].Choices.SpellSelections = spells;

        return new ReplayStudio(Content.Value).Evaluate(character);
    }

    // --- Wizard spellbook budget ---
    //
    // SRD: "A wizard begins play with a spellbook containing all 0-level wizard spells (except
    // those from her prohibited school or schools, if any) plus three 1st-level spells of your
    // choice. For each point of Intelligence bonus the wizard has, the spellbook holds one
    // additional 1st-level spell of your choice. At each new wizard level, she gains two new
    // spells of any spell level or levels that she can cast (based on her new wizard level) for
    // her spellbook."
    //
    // The starting spells are specifically 1st level, which needs no separate rule here: at 1st
    // level that is the only level a wizard can cast, and the existing max-spell-level check
    // already rejects anything above it. So a flat pool of "spells of level 1+" is equivalent.

    [Theory]
    [InlineData(1, 0, 3)]    // 1st level, INT 10: the base three
    [InlineData(1, 3, 6)]    // 1st level, INT 16: three plus the +3 bonus
    [InlineData(2, 0, 5)]    // one level up: +2
    [InlineData(5, 0, 11)]   // 3 + 2 x 4
    [InlineData(5, 4, 15)]   // ... plus INT 18
    [InlineData(20, 5, 46)]  // 3 + 5 + 2 x 19
    [InlineData(0, 3, 0)]    // not a wizard at all
    public void SpellbookBudget_FollowsTheSrdFormula(int wizardLevel, int intModifier, int expected)
    {
        Assert.Equal(expected, ReplayStudio.SpellbookSpellsAllowed(wizardLevel, intModifier));
    }

    [Fact]
    public void SpellbookBudget_IgnoresAnIntelligencePenalty()
    {
        // "For each point of Intelligence bonus" — a penalty does not shrink the starting three.
        Assert.Equal(3, ReplayStudio.SpellbookSpellsAllowed(wizardLevel: 1, intelligenceModifier: -2));
    }

    [Fact]
    public void WizardWithinItsSpellbookBudget_ProducesNoWarning()
    {
        // 3rd-level wizard, INT 16 → 3 + 3 + 2*2 = 10 allowed.
        var state = Evaluate("class:wizard", levels: 3, intelligence: 16, spells: new()
        {
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:magic_missile" },
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:shield" },
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 2, SpellId = "spell:invisibility" },
        });

        Assert.Equal(SpellAcquisition.Spellbook, state.Spellcasting["class:wizard"].Acquisition);
        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("spellbook"));
    }

    [Fact]
    public void WizardOverItsSpellbookBudget_Warns()
    {
        // 1st-level wizard, INT 10 → 3 allowed; four chosen.
        var state = Evaluate("class:wizard", levels: 1, intelligence: 10, spells: new()
        {
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:magic_missile" },
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:shield" },
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:mage_armor" },
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:sleep" },
        });

        Assert.Contains(state.Warnings, w => w.Message.Contains("spellbook holds 4") && w.Message.Contains("exceeding 3"));
    }

    [Fact]
    public void CantripsDoNotCountAgainstTheSpellbookBudget()
    {
        // Every 0-level spell is in the book from 1st level, so selecting some cannot overrun it.
        var state = Evaluate("class:wizard", levels: 1, intelligence: 10, spells: new()
        {
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 0, SpellId = "spell:detect_magic" },
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 0, SpellId = "spell:light" },
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 0, SpellId = "spell:read_magic" },
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 0, SpellId = "spell:mage_hand" },
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:magic_missile" },
        });

        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("spellbook"));
    }

    [Fact]
    public void FullListCasters_AreNeverBudgetChecked()
    {
        // A cleric picking spells is meaningless rather than illegal, so the engine stays permissive
        // — the builder simply stops offering the choice. Nothing here should warn.
        var state = Evaluate("class:cleric", levels: 1, spells: new()
        {
            new SpellSelection { ClassId = "class:cleric", SpellLevel = 1, SpellId = "spell:bless" },
            new SpellSelection { ClassId = "class:cleric", SpellLevel = 1, SpellId = "spell:command" },
            new SpellSelection { ClassId = "class:cleric", SpellLevel = 1, SpellId = "spell:doom" },
            new SpellSelection { ClassId = "class:cleric", SpellLevel = 1, SpellId = "spell:entropic_shield" },
        });

        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("spellbook") || w.Message.Contains("knows"));
    }

    [Fact]
    public void SpellsKnownLimits_StillApplyToSpontaneousCasters()
    {
        // Guards against the new branch accidentally taking over the sorcerer path: a 1st-level
        // sorcerer knows two 1st-level spells.
        var state = Evaluate("class:sorcerer", levels: 1, spells: new()
        {
            new SpellSelection { ClassId = "class:sorcerer", SpellLevel = 1, SpellId = "spell:magic_missile" },
            new SpellSelection { ClassId = "class:sorcerer", SpellLevel = 1, SpellId = "spell:shield" },
            new SpellSelection { ClassId = "class:sorcerer", SpellLevel = 1, SpellId = "spell:mage_armor" },
        });

        Assert.Contains(state.Warnings, w => w.Message.Contains("knows 3 level-1 spells"));
    }

    [Fact]
    public void DomainPicksDoNotCountAgainstAnyBudget()
    {
        // Domain slots are granted, not chosen, and both checks exclude them explicitly.
        var wizard = Evaluate("class:wizard", levels: 1, intelligence: 10, spells: new()
        {
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:magic_missile" },
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:shield" },
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:mage_armor" },
            new SpellSelection { ClassId = "domain:magic", SpellLevel = 1, SpellId = "spell:magic_aura" },
        });

        Assert.DoesNotContain(wizard.Warnings, w => w.Message.Contains("spellbook"));
    }
}

/// <summary>
/// Specialist wizards. A wizard may specialize in one school of magic and gives up others for it;
/// spells of a given-up school can never be learned, written into the spellbook or cast. None of
/// this was modelled — the spell schools existed on <c>SpellDefinition</c> and nothing read them.
///
/// Both choices ride on the existing class-feature selection machinery, so they need no new state
/// and reach the sheet and the API through <c>ClassFeatureSelections</c> for free.
/// </summary>
public class WizardSchoolTests
{
    private static readonly Lazy<ContentRegistry> Content =
        new(() => TestContentHelper.LoadBundledPacks());

    private static CharacterState Evaluate(
        string? specialty,
        string[]? prohibited = null,
        List<SpellSelection>? spells = null,
        int levels = 1)
    {
        var character = new Character
        {
            Name = "Specialist",
            RaceId = "race:human",
            Alignment = Alignment.N,
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 16, WIS = 10, CHA = 10,
            },
        };
        for (int i = 0; i < levels; i++)
            character.Ticks.Add(new Tick { DriverId = "class:wizard" });

        var choices = new Dictionary<string, List<string>>();
        if (specialty != null)
            choices[WizardSchools.SpecializationFeature] = new List<string> { WizardSchools.ToOptionId(specialty) };
        if (prohibited is { Length: > 0 })
            choices[WizardSchools.ProhibitedFeature] = prohibited.Select(WizardSchools.ToOptionId).ToList();
        if (choices.Count > 0)
            character.Ticks[0].Choices.ClassFeatureChoices = choices;

        if (spells != null)
            character.Ticks[^1].Choices.SpellSelections = spells;

        return new ReplayStudio(Content.Value).Evaluate(character);
    }

    [Fact]
    public void WizardIsOfferedBothChoicesAtFirstLevel()
    {
        var registry = Content.Value;
        var wizard = (HDDriver)registry.GetDriver("class:wizard");

        var granted = wizard.LevelPermabuffs[1].OfType<GrantClassFeatureSelection>().ToList();

        Assert.Contains(granted, g => g.FeatureType == WizardSchools.SpecializationFeature && g.Count == 1);
        Assert.Contains(granted, g => g.FeatureType == WizardSchools.ProhibitedFeature && g.Count == 2);

        // ... and only at 1st level: the school is chosen once and never changed.
        var laterGrants = wizard.LevelPermabuffs
            .Where(kv => kv.Key > 1)
            .SelectMany(kv => kv.Value)
            .OfType<GrantClassFeatureSelection>()
            .Where(g => g.FeatureType == WizardSchools.SpecializationFeature
                        || g.FeatureType == WizardSchools.ProhibitedFeature);
        Assert.Empty(laterGrants);
    }

    [Fact]
    public void AnySchoolCanBeSpecializedIn()
    {
        // SRD: "The eight schools of arcane magic are abjuration, conjuration, divination,
        // enchantment, evocation, illusion, necromancy, and transmutation."
        Assert.True(Content.Value.TryGetClassFeature(WizardSchools.SpecializationFeature, out var feature));
        var schools = feature!.Options.Select(o => WizardSchools.ToSchoolName(o.Id)).OrderBy(s => s).ToList();

        Assert.Equal(
            new[]
            {
                "abjuration", "conjuration", "divination", "enchantment",
                "evocation", "illusion", "necromancy", "transmutation",
            },
            schools);

        // SRD: "Spells that do not fall into any of these schools are called universal spells."
        Assert.DoesNotContain(WizardSchools.Universal, schools);
    }

    [Fact]
    public void DivinationIsNotOfferedAsAProhibitedSchool()
    {
        // SRD: "A wizard can never give up divination to fulfill this requirement." Its absence
        // from this pool is by rule, not oversight — every other school is present.
        Assert.True(Content.Value.TryGetClassFeature(WizardSchools.ProhibitedFeature, out var feature));
        var schools = feature!.Options.Select(o => WizardSchools.ToSchoolName(o.Id)).OrderBy(s => s).ToList();

        Assert.Equal(
            new[]
            {
                "abjuration", "conjuration", "enchantment",
                "evocation", "illusion", "necromancy", "transmutation",
            },
            schools);

        Assert.DoesNotContain(WizardSchools.Divination, schools);
        Assert.DoesNotContain(WizardSchools.Universal, schools);
    }

    [Fact]
    public void GivingUpDivinationIsRejected()
    {
        // Divination is not in the option pool, so the selection is refused before it is ever
        // recorded — the guarantee that matters is that it never becomes a prohibited school,
        // whichever layer catches it.
        var state = Evaluate("evocation", new[] { "divination", "necromancy" });

        Assert.DoesNotContain(WizardSchools.Divination, WizardSchools.ProhibitedSchools(state));
        Assert.Contains(state.Warnings, w => w.Message.Contains("unknown class feature option"));
    }

    [Fact]
    public void EvenIfAPackReAddsDivination_TheEngineStillRefusesIt()
    {
        // Second layer. A homebrew pack could override the option pool — content is last-wins —
        // so the rule is enforced in the engine too, not only by the absence of the option.
        var registry = TestContentHelper.LoadBundledPacks();
        registry.RegisterClassFeature(new ClassFeatureDefinition
        {
            Id = WizardSchools.ProhibitedFeature,
            Name = "Prohibited Schools (homebrew, divination re-added)",
            Options = new List<ClassFeatureOption>
            {
                new() { Id = WizardSchools.ToOptionId("divination"), Name = "Divination" },
                new() { Id = WizardSchools.ToOptionId("necromancy"), Name = "Necromancy" },
            },
        });

        var character = new Character
        {
            Name = "Homebrew",
            RaceId = "race:human",
            Alignment = Alignment.N,
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 16, WIS = 10, CHA = 10 },
        };
        character.Ticks.Add(new Tick
        {
            DriverId = "class:wizard",
            Choices = new TickChoices
            {
                ClassFeatureChoices = new Dictionary<string, List<string>>
                {
                    [WizardSchools.SpecializationFeature] = new() { WizardSchools.ToOptionId("evocation") },
                    [WizardSchools.ProhibitedFeature] = new()
                    {
                        WizardSchools.ToOptionId("divination"),
                        WizardSchools.ToOptionId("necromancy"),
                    },
                },
            },
        });

        var state = new ReplayStudio(registry).Evaluate(character);

        Assert.Contains(state.Warnings, w => w.Message.Contains("never be a prohibited school"));
    }

    [Fact]
    public void ASpecialistPreparesOneExtraSpellOfItsSchoolPerLevel()
    {
        // SRD: "A specialist wizard can prepare one additional spell of her specialty school per
        // spell level each day." Stated per spell level with no exception, so 0-level included.
        var state = Evaluate("evocation", new[] { "enchantment", "necromancy" }, levels: 5);
        var sc = state.Spellcasting["class:wizard"];

        Assert.NotEmpty(sc.SpecialtyBonusSlots);
        Assert.All(sc.SpellsPerDay.Keys, lvl => Assert.Equal(1, sc.SpecialtyBonusSlots[lvl]));
        Assert.Equal(sc.SpellsPerDay.Count, sc.SpecialtyBonusSlots.Count);
        Assert.Contains(0, sc.SpecialtyBonusSlots.Keys);
    }

    [Fact]
    public void AUniversalistGetsNoBonusSlots()
    {
        var state = Evaluate(specialty: null, levels: 5);

        Assert.Empty(state.Spellcasting["class:wizard"].SpecialtyBonusSlots);
    }

    [Fact]
    public void SpecialtyBonusSlotsReachTheSheet()
    {
        var sheet = CharacterSheet.FromState(
            Evaluate("evocation", new[] { "enchantment", "necromancy" }, levels: 5));

        Assert.Equal(1, sheet.Spellcasting["class:wizard"].SpecialtyBonusSlots[1]);
    }

    [Fact]
    public void EachSpecializationGrantsAnAbilityDescribingIt()
    {
        // The +2 Spellcraft bonus is prose on a granted ability rather than a GrantSkillBonus: it
        // applies only to checks made to learn spells of that school, and a flat bonus would
        // wrongly inflate the Spellcraft total the skill pass now computes.
        var state = Evaluate("evocation", new[] { "enchantment", "necromancy" });

        var ability = state.Abilities.Single(a => a.Id == "wizard_specialist_evocation");
        Assert.Contains("Spellcraft", ability.Description);
        Assert.DoesNotContain("skill:spellcraft", state.SkillBonuses.Keys);
    }

    [Fact]
    public void SpecialtyAndProhibitedSchools_AreReadableFromState()
    {
        var state = Evaluate("evocation", new[] { "enchantment", "necromancy" });

        Assert.Equal("evocation", WizardSchools.Specialty(state));
        Assert.Equal(
            new[] { "enchantment", "necromancy" },
            WizardSchools.ProhibitedSchools(state).OrderBy(s => s, StringComparer.Ordinal));
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void ProhibitedSchoolsReachTheSheet()
    {
        // ClassFeatureSelections is already on CharacterSheet, so the API carries these with no
        // new field.
        var sheet = CharacterSheet.FromState(Evaluate("evocation", new[] { "enchantment", "necromancy" }));

        Assert.Equal(
            new[] { "school:evocation" },
            sheet.ClassFeatureSelections[WizardSchools.SpecializationFeature]);
        Assert.Equal(2, sheet.ClassFeatureSelections[WizardSchools.ProhibitedFeature].Count);
    }

    [Fact]
    public void ASpellOfAProhibitedSchool_IsRejected()
    {
        // sleep is enchantment; this wizard gave enchantment up.
        var state = Evaluate("evocation", new[] { "enchantment", "necromancy" }, spells: new()
        {
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:sleep" },
        });

        Assert.Contains(state.Warnings, w =>
            w.Message.Contains("spell:sleep") && w.Message.Contains("given up"));
    }

    [Fact]
    public void ASpellOfAnAllowedSchool_IsFine()
    {
        // magic_missile is evocation — this wizard's specialty.
        var state = Evaluate("evocation", new[] { "enchantment", "necromancy" }, spells: new()
        {
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:magic_missile" },
        });

        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("given up"));
    }

    [Fact]
    public void SchoolsChosenOnTheSameTickAsSpells_AreStillEnforced()
    {
        // The reason this check is a tail pass: within a tick, spell selections are applied before
        // class feature choices, so a per-tick check would see no prohibited schools yet and let a
        // 1st-level wizard write a barred spell into its book.
        var state = Evaluate("evocation", new[] { "enchantment", "necromancy" }, spells: new()
        {
            new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:sleep" },
        }, levels: 1);

        Assert.Contains(state.Warnings, w => w.Message.Contains("given up"));
    }

    [Fact]
    public void UniversalSpells_AreNeverProhibited()
    {
        // arcane_mark is universal — it belongs to no school, so no specialist can lose it.
        // (The bundled universal spells are arcane_mark, prestidigitation, permanency, wish and
        // limited_wish; read_magic, despite the name, is divination.)
        var state = Evaluate("evocation", new[] { "enchantment", "necromancy" });

        var arcaneMark = Content.Value.GetSpell("spell:arcane_mark");
        Assert.Equal(WizardSchools.Universal, arcaneMark.School);
        Assert.False(WizardSchools.IsProhibited(state, arcaneMark.School));

        // A universal spell survives even if every school is somehow given up.
        Assert.False(WizardSchools.IsProhibited(state, WizardSchools.Universal));
    }

    [Fact]
    public void AUniversalistProhibitsNothing()
    {
        var state = Evaluate(specialty: null);

        Assert.Null(WizardSchools.Specialty(state));
        Assert.Empty(WizardSchools.ProhibitedSchools(state));
        Assert.False(WizardSchools.IsProhibited(state, "enchantment"));
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void ADivinerGivesUpOnlyOneSchool()
    {
        var diviner = Evaluate("divination", new[] { "necromancy" });
        Assert.Empty(diviner.Warnings);

        var tooMany = Evaluate("divination", new[] { "necromancy", "enchantment" });
        Assert.Contains(tooMany.Warnings, w => w.Message.Contains("must give up 1"));
    }

    [Fact]
    public void AnOrdinarySpecialistMustGiveUpTwo()
    {
        var state = Evaluate("evocation", new[] { "necromancy" });

        Assert.Contains(state.Warnings, w => w.Message.Contains("must give up 2"));
    }

    [Fact]
    public void ASpecialistWhoGivesUpNothingWarns()
    {
        // The builder let this through: it rendered the prohibited-school dropdowns but never
        // required them, so a specialist could take the bonus slot and keep every school.
        var state = Evaluate("evocation");

        Assert.Equal("evocation", WizardSchools.Specialty(state));
        Assert.Empty(WizardSchools.ProhibitedSchools(state));
        Assert.Contains(state.Warnings, w => w.Message.Contains("gives up 0 school(s), but must give up 2"));
    }

    [Fact]
    public void ProhibitingSchoolsWithoutASpecialty_Warns()
    {
        var state = Evaluate(specialty: null, prohibited: new[] { "necromancy", "enchantment" });

        Assert.Contains(state.Warnings, w => w.Message.Contains("universalist gives up none"));
    }

    [Fact]
    public void SpecializingInASchoolYouAlsoGiveUp_Warns()
    {
        var state = Evaluate("evocation", new[] { "evocation", "necromancy" });

        Assert.Contains(state.Warnings, w =>
            w.Message.Contains("specializes in evocation") && w.Message.Contains("prohibited"));
    }

    [Fact]
    public void AUniversalistHoldsEveryCantrip()
    {
        // SRD: "a spellbook containing all 0-level wizard spells (except those from her prohibited
        // school or schools, if any)". With no prohibited schools there is no exception to apply.
        var state = Evaluate(specialty: null);
        var spells = Content.Value.GetAllSpells().ToList();

        var held = WizardSchools.AutomaticCantrips(spells, "class:wizard", state).ToList();
        var allCantrips = spells.Where(s => s.ClassLevels.TryGetValue("class:wizard", out var l) && l == 0).ToList();

        Assert.NotEmpty(allCantrips);
        Assert.Equal(allCantrips.Count, held.Count);
    }

    [Fact]
    public void ASpecialistLosesTheCantripsOfItsGivenUpSchools()
    {
        // The part "all 0-level spells are automatic" glossed over: a specialist's free cantrips
        // are reduced by exactly the ones from the schools it gave up.
        var state = Evaluate("evocation", new[] { "enchantment", "necromancy" });
        var spells = Content.Value.GetAllSpells().ToList();

        var held = WizardSchools.AutomaticCantrips(spells, "class:wizard", state).ToList();

        Assert.DoesNotContain(held, s => s.School is "enchantment" or "necromancy");
        Assert.Contains(held, s => s.School == "evocation");

        var lost = spells.Count(s => s.ClassLevels.TryGetValue("class:wizard", out var l) && l == 0
                                     && s.School is "enchantment" or "necromancy");
        Assert.True(lost > 0, "expected at least one 0-level wizard spell in the given-up schools");

        var universalist = Evaluate(specialty: null);
        var all = WizardSchools.AutomaticCantrips(spells, "class:wizard", universalist).Count();
        Assert.Equal(all - lost, held.Count);
    }

    [Fact]
    public void UniversalCantripsSurviveAnySpecialization()
    {
        // arcane_mark and prestidigitation are 0-level universal spells, so no specialist loses them.
        var state = Evaluate("evocation", new[] { "enchantment", "necromancy" });

        var held = WizardSchools.AutomaticCantrips(Content.Value.GetAllSpells(), "class:wizard", state)
            .Select(s => s.Id)
            .ToList();

        Assert.Contains("spell:arcane_mark", held);
        Assert.Contains("spell:prestidigitation", held);
    }

    [Fact]
    public void NonWizardCasters_AreUnaffected()
    {
        // A sorcerer has no specialization machinery at all and must not acquire any.
        var character = new Character
        {
            Name = "Sorcerer",
            RaceId = "race:human",
            Alignment = Alignment.N,
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 16 },
        };
        character.Ticks.Add(new Tick { DriverId = "class:sorcerer" });

        var state = new ReplayStudio(Content.Value).Evaluate(character);

        Assert.Null(WizardSchools.Specialty(state));
        Assert.Empty(WizardSchools.ProhibitedSchools(state));
        Assert.Empty(state.Warnings);
    }
}
