using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests.PcGen;

/// <summary>
/// Golden reconstruction corpus: one fixed .pcg per build archetype, each asserted down to the
/// values a 3.5e player would compute by hand (BAB progression, save progression, caster level,
/// level adjustment, hit points).
///
/// This is deliberately different from <see cref="PcgImportRegression"/>. That harness diffs the
/// whole corpus against a recorded snapshot, so it catches *any* change but can only ever say
/// "something moved" — an incorrect value that was incorrect at snapshot time passes forever.
/// These tests instead spell out the rules arithmetic, so a wrong answer fails against the SRD
/// rather than against yesterday's output.
///
/// Archetype coverage: straight martial, straight divine caster with domains, straight arcane
/// caster, multiclass skill build, prestige spell-advancement, racial-HD creature, templated
/// creature, epic progression.
/// </summary>
public class PcgGoldenBuildTests
{
    // ---------------------------------------------------------------
    // 3.5e progression formulas — the expectations below are written in
    // terms of these rather than as bare literals, so the arithmetic is
    // visible at each call site.
    // ---------------------------------------------------------------

    /// <summary>Good save at the given number of levels in one class: 2 + level/2.</summary>
    private static int GoodSave(int levels) => 2 + levels / 2;

    /// <summary>Poor save at the given number of levels in one class: level/3.</summary>
    private static int PoorSave(int levels) => levels / 3;

    /// <summary>Full (fighter-type) BAB.</summary>
    private static int FullBab(int levels) => levels;

    /// <summary>Average (cleric/rogue-type) BAB: 3/4 per level, rounded down.</summary>
    private static int AverageBab(int levels) => levels * 3 / 4;

    /// <summary>Poor (wizard-type) BAB: 1/2 per level, rounded down.</summary>
    private static int PoorBab(int levels) => levels / 2;

    // Loading the packs is the expensive part and the registry is only read, so share one.
    private static readonly Lazy<ContentRegistry> SharedRegistry =
        new(TestContentHelper.LoadBundledAndPrivatePacksIfAvailable);

    private sealed record Build(
        PcgCharacterData Source,
        PcgConversionResult Result,
        Character Character,
        CharacterState State);

    // Frozen fixtures, not the live PCGen directory: every assertion below is an exact value, so
    // reading the working set would make an edit in PCGen look like a code regression.
    private static Build Load(string fileName)
    {
        var source = PcgParser.Parse(TestContentHelper.PcgFixture(fileName));
        var registry = SharedRegistry.Value;
        var result = PcgConverter.Convert(source, new PcgIdMapper(), registry);
        var state = new ReplayStudio(registry).Evaluate(result.Character);
        return new Build(source, result, result.Character, state);
    }

    /// <summary>
    /// Every golden build shares one hit-point rule: PCGen's per-level HITPOINTS roll is preserved
    /// verbatim as a character input, the *final* Constitution modifier is applied to each die, and
    /// no die contributes less than 1. Asserting it per archetype is what makes an HP regression
    /// point at the archetype that broke rather than at a corpus-wide total.
    ///
    /// The Constitution modifier is read through the nonability rule, so an undead — which has no
    /// Constitution score at all — contributes +0 per die rather than whatever its placeholder
    /// score would have produced.
    /// </summary>
    private static void AssertHitPointsFollowSourceRolls(Build build)
    {
        var conMod = build.State.AbilityModifier(Ability.CON);
        var expected = build.Source.Levels.Sum(level => Math.Max(1, level.HitPoints + conMod));

        // Flat HP sources are separate from die rolls: ordinary Toughness feats and
        // Loremaster's Secret Health both grant the SRD's +3 hit points.
        var toughnessFeats = build.Source.Feats.Count(feat =>
            feat.Key.Equals("Toughness", StringComparison.OrdinalIgnoreCase));
        var secretHealth = build.Source.ClassAbilities.Count(ability =>
            (ability.Key.Contains("Secret Health", StringComparison.OrdinalIgnoreCase)
             || (ability.Key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                 && ability.AppliedTo?.Contains("Health", StringComparison.OrdinalIgnoreCase) == true)));
        expected += 3 * (toughnessFeats + secretHealth);
        Assert.Equal(expected, build.State.HP);
    }

    private static void AssertProgressionSaves(Build build, int fort, int reflex, int will)
    {
        var saves = build.State.ProgressionBaseSaves;
        Assert.Equal((fort, reflex, will), (saves.Fort, saves.Ref, saves.Will));
    }

    // ---------------------------------------------------------------
    // Straight martial — Human Fighter 7
    // ---------------------------------------------------------------

    [RequiresPcgFixturesFact]
    public void Golden_StraightMartial_HumanFighter7()
    {
        var build = Load("High Priestess's Bodyguard.pcg");
        var state = build.State;

        Assert.Equal("race:human", state.RaceId);
        Assert.Equal(CreatureType.Humanoid, state.Type);
        Assert.Equal(Size.Medium, state.Size);

        // No racial HD, no template level adjustment: ECL is just the class levels.
        Assert.Equal(7, state.TotalHD);
        Assert.Equal(0, state.LevelAdjustment);
        Assert.Equal(7, state.ECL);
        Assert.Equal(new[] { "class:fighter" }, state.ClassLevels.Keys);
        Assert.Equal(7, state.ClassLevels["class:fighter"]);
        Assert.All(state.HDList, id => Assert.Equal("class:fighter", id));
        Assert.All(state.HitDice, die => Assert.Equal(10, die.DieSize));

        // Fighter: full BAB, good Fortitude, poor Reflex and Will.
        Assert.Equal(FullBab(7), state.BaseBAB);
        Assert.Equal(0, state.EpicAttackBonus);
        AssertProgressionSaves(build, GoodSave(7), PoorSave(7), PoorSave(7));

        // Fighters get every armor and shield proficiency including tower shields, plus
        // martial weapons — granted by the driver, not selected by the player.
        Assert.Contains("feat:weapon_proficiency_martial", state.Feats);
        Assert.Contains("feat:tower_shield_proficiency", state.Feats);
        Assert.Contains("feat:armor_proficiency_heavy", state.Feats);

        // A martial build casts nothing.
        Assert.Empty(state.Spellcasting);
        Assert.Empty(state.Domains);

        AssertHitPointsFollowSourceRolls(build);

        // Four of this sheet's eight feats came out of the fighter bonus pool — the .pcg writes
        // those as "ABILITY:Fighter Feat|…|CATEGORY:FEAT", a different opening tag from the
        // four the character bought with its general slots. Both kinds are feats, and Cleave
        // and Power Attack are the prerequisites for two of the general picks, so a build that
        // reads only one kind reports prerequisite failures the source sheet does not have.
        var bonusPoolFeats = File
            .ReadAllLines(TestContentHelper.PcgFixture("High Priestess's Bodyguard.pcg"))
            .Where(line => line.StartsWith("ABILITY:Fighter Feat|", StringComparison.Ordinal))
            .Select(line => line.Split('|').First(field => field.StartsWith("KEY:", StringComparison.Ordinal))["KEY:".Length..])
            // Weapon Focus and Weapon Specialization encode their chosen weapon into the id,
            // so match on the base id rather than on equality.
            .Select(key => new PcgIdMapper().MapFeat(key))
            .ToList();
        Assert.Equal(4, bonusPoolFeats.Count);
        Assert.All(bonusPoolFeats, featId =>
            Assert.Contains(state.Feats, granted => granted.StartsWith(featId, StringComparison.Ordinal)));
        Assert.Contains("feat:cleave", state.Feats);
        Assert.Contains("feat:power_attack", state.Feats);
        Assert.Contains("feat:great_cleave", state.Feats);
        Assert.Contains("feat:improved_bull_rush", state.Feats);
        Assert.Empty(state.Warnings);
    }

    // ---------------------------------------------------------------
    // Straight divine caster with domains — Human Cleric 6
    // ---------------------------------------------------------------

    [RequiresPcgFixturesFact]
    public void Golden_DivineCasterWithDomains_HumanCleric6()
    {
        var build = Load("High Priestess.pcg");
        var state = build.State;

        Assert.Equal("race:human", state.RaceId);
        Assert.Equal(6, state.TotalHD);
        Assert.Equal(6, state.ClassLevels["class:cleric"]);
        Assert.All(state.HitDice, die => Assert.Equal(8, die.DieSize));

        // Cleric: average BAB, good Fortitude and Will, poor Reflex.
        Assert.Equal(AverageBab(6), state.BaseBAB);
        AssertProgressionSaves(build, GoodSave(6), PoorSave(6), GoodSave(6));

        // Equipped Cloak of Resistance +1 lifts every save one above progression. Equipment is
        // post-tick, so it must show up in BaseSaves without touching ProgressionBaseSaves.
        Assert.Equal(state.ProgressionBaseSaves.Fort + 1, state.BaseSaves.Fort);
        Assert.Equal(state.ProgressionBaseSaves.Ref + 1, state.BaseSaves.Ref);
        Assert.Equal(state.ProgressionBaseSaves.Will + 1, state.BaseSaves.Will);

        // PCGen's STAT already bakes in level-up increases: STAT:WIS:17 is rolled 16 plus the
        // HD-4 increase, so the reconstructed base is 17, and the Periapt of Wisdom +2 takes
        // the displayed score to 19.
        Assert.Equal(19, state.AbilityScores.WIS);
        Assert.Equal(9, state.AbilityScores.STR);

        // Both domains come from the cleric class and are owned by it.
        Assert.Equal(new[] { "domain:lust", "domain:charm" }, state.Domains);
        Assert.All(state.Domains, d => Assert.Equal("class:cleric", state.DomainOwners[d]));

        var casting = Assert.Single(state.Spellcasting).Value;
        Assert.Equal("class:cleric", casting.ClassId);
        Assert.Equal(SpellAcquisition.FullList, casting.Acquisition);
        Assert.Equal(6, casting.CasterLevel);
        // SRD cleric spells per day at 6th: 5/3+1/3+1/2+1. The "+1" is the domain slot, tracked
        // separately in DomainBonusSlots, so SpellsPerDay carries the base numbers only.
        Assert.Equal(3, casting.MaxSpellLevel);
        Assert.Equal(new Dictionary<int, int> { [0] = 5, [1] = 3, [2] = 3, [3] = 2 }, casting.SpellsPerDay);
        // Domain slots exist for every castable level except 0 — clerics get no 0-level domain slot.
        Assert.False(casting.DomainBonusSlots.ContainsKey(0));
        Assert.Equal(new[] { 1, 2, 3 }, casting.DomainBonusSlots.Keys.Order());

        AssertHitPointsFollowSourceRolls(build);
    }

    // ---------------------------------------------------------------
    // Straight arcane caster — Drow Wizard 5
    // ---------------------------------------------------------------

    [RequiresPcgFixturesFact]
    public void Golden_ArcaneCaster_DrowWizard5()
    {
        var build = Load("Drow Cult Wizard.pcg");
        var state = build.State;

        Assert.Equal("race:drow", state.RaceId);
        // Drow carry a +2 level adjustment, so a 5-HD drow is ECL 7.
        Assert.Equal(5, state.TotalHD);
        Assert.Equal(2, state.LevelAdjustment);
        Assert.Equal(7, state.ECL);
        Assert.Equal(5, state.ClassLevels["class:wizard"]);
        Assert.All(state.HitDice, die => Assert.Equal(4, die.DieSize));

        // Wizard: poor BAB, good Will only.
        Assert.Equal(PoorBab(5), state.BaseBAB);
        AssertProgressionSaves(build, PoorSave(5), PoorSave(5), GoodSave(5));

        var casting = Assert.Single(state.Spellcasting).Value;
        Assert.Equal("class:wizard", casting.ClassId);
        Assert.Equal(SpellAcquisition.Spellbook, casting.Acquisition);
        Assert.Equal(5, casting.CasterLevel);
        // SRD wizard spells per day at 5th: 4/3/2/1.
        Assert.Equal(new Dictionary<int, int> { [0] = 4, [1] = 3, [2] = 2, [3] = 1 }, casting.SpellsPerDay);
        // Not a specialist: no prohibited schools and no specialty bonus slots.
        Assert.Empty(casting.SpecialtyBonusSlots);
        Assert.Empty(casting.DomainBonusSlots);

        // Item creation feats taken by the player, on top of the wizard's granted Scribe Scroll.
        Assert.Contains("feat:scribe_scroll", state.Feats);
        Assert.Contains("feat:craft_wand", state.Feats);

        // CON 7 (-2) against d4 hit dice: the per-die floor of 1 does real work here, so this
        // build is the one that proves HP never goes to zero or negative on a low-CON caster.
        Assert.Equal(7, state.AbilityScores.CON);
        AssertHitPointsFollowSourceRolls(build);
        Assert.True(state.HP >= state.TotalHD);

        // A clean import: nothing dropped, nothing warned, on either side of the conversion.
        Assert.Empty(build.Result.Warnings);
        Assert.Empty(state.Warnings);
    }

    // ---------------------------------------------------------------
    // Multiclass skill build — Drow Rogue 7 / Assassin 10
    // ---------------------------------------------------------------

    [RequiresPcgFixturesFact]
    public void Golden_MulticlassSkillBuild_DrowRogue7Assassin10()
    {
        var build = Load("Spymistress.pcg");
        var state = build.State;

        Assert.Equal("race:drow", state.RaceId);
        Assert.Equal(17, state.TotalHD);
        Assert.Equal(2, state.LevelAdjustment);
        Assert.Equal(19, state.ECL);
        Assert.Equal(7, state.ClassLevels["class:rogue"]);
        Assert.Equal(10, state.ClassLevels["class:assassin"]);

        // The source took rogue to 5, then all ten assassin levels, then returned to rogue.
        // Level order is a character input, so the reconstructed HD list must preserve it
        // rather than grouping levels by class.
        Assert.Equal(
            Enumerable.Repeat("class:rogue", 5)
                .Concat(Enumerable.Repeat("class:assassin", 10))
                .Concat(Enumerable.Repeat("class:rogue", 2)),
            state.HDList);

        // Both classes have average BAB and are summed per class, not off total HD.
        Assert.Equal(AverageBab(7) + AverageBab(10), state.BaseBAB);
        // Rogue and assassin both have good Reflex, poor Fortitude and Will — each class
        // contributes its own progression, which is why two poor saves still reach 5.
        AssertProgressionSaves(
            build,
            PoorSave(7) + PoorSave(10),
            GoodSave(7) + GoodSave(10),
            PoorSave(7) + PoorSave(10));

        // The assassin's spell list is the character's only casting, and it is a
        // spells-known caster topping out at 4th level.
        var casting = Assert.Single(state.Spellcasting).Value;
        Assert.Equal("class:assassin", casting.ClassId);
        Assert.Equal(SpellAcquisition.SpellsKnown, casting.Acquisition);
        Assert.Equal(10, casting.CasterLevel);
        Assert.Equal(4, casting.MaxSpellLevel);

        AssertHitPointsFollowSourceRolls(build);

        // PCGen let the assassin levels start before the class's skill-rank prerequisites were
        // met. The engine admits the levels — the .pcg records what was played — and warns once
        // per unmet prerequisite per tick. Assert which prerequisites are reported, not how many
        // ticks repeat them, so a change in warning cadence does not read as a rules regression.
        var prerequisiteWarnings = state.Warnings
            .Where(w => w.Message.Contains("prerequisite not met for Assassin"))
            .ToList();
        Assert.NotEmpty(prerequisiteWarnings);
        Assert.Equal(
            new[] { "skill:disguise 4 ranks", "skill:hide 8 ranks", "skill:move_silently 8 ranks" },
            prerequisiteWarnings
                .Select(w => w.Message[(w.Message.IndexOf(": skill:", StringComparison.Ordinal) + 2)..])
                .Distinct()
                .Order());

        // Every spell on the sheet resolves against the assassin's spell list, so the only other
        // warning family left is skill-point overspend against PCGen's pool.
        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("spell list"));
        Assert.All(
            state.Warnings.Except(prerequisiteWarnings),
            w => Assert.Contains("more skill points than available", w.Message));
        Assert.NotEmpty(casting.SelectedSpells);
    }

    // ---------------------------------------------------------------
    // Prestige spell advancement — Human Cleric 7 / Thaumaturgist 2
    // ---------------------------------------------------------------

    [RequiresPcgFixturesFact]
    public void Golden_PrestigeSpellAdvancement_ClericThaumaturgist()
    {
        var build = Load("Human High Priestess.pcg");
        var state = build.State;

        Assert.Equal(9, state.TotalHD);
        Assert.Equal(7, state.ClassLevels["class:cleric"]);
        Assert.Equal(2, state.ClassLevels["class:thaumaturgist"]);

        // Cleric is average BAB, thaumaturgist poor.
        Assert.Equal(AverageBab(7) + PoorBab(2), state.BaseBAB);
        // Cleric: good Fort/Will. Thaumaturgist: good Will only.
        AssertProgressionSaves(
            build,
            GoodSave(7) + PoorSave(2),
            PoorSave(7) + PoorSave(2),
            GoodSave(7) + GoodSave(2));

        // The point of the archetype: the prestige class advances the cleric's casting instead of
        // starting its own. One spellcasting entry, caster level equal to total HD.
        var casting = Assert.Single(state.Spellcasting).Value;
        Assert.Equal("class:cleric", casting.ClassId);
        Assert.Equal(9, casting.CasterLevel);
        Assert.Equal(state.TotalHD, casting.CasterLevel);
        // SRD cleric spells per day at caster level 9 — reached through the prestige class.
        Assert.Equal(5, casting.MaxSpellLevel);
        Assert.Equal(
            new Dictionary<int, int> { [0] = 6, [1] = 4, [2] = 4, [3] = 3, [4] = 2, [5] = 1 },
            casting.SpellsPerDay);

        // Advancement is an explicit per-tick choice, recorded on both thaumaturgist levels.
        var advancementTicks = build.Character.Ticks
            .Select((tick, index) => (tick, hd: index + 1))
            .Where(t => t.tick.Choices.ClassFeatureChoices?.ContainsKey("advance_spellcasting") == true)
            .ToList();
        Assert.Equal(new[] { 8, 9 }, advancementTicks.Select(t => t.hd));
        Assert.All(advancementTicks, t =>
            Assert.Equal(new[] { "class:cleric" }, t.tick.Choices.ClassFeatureChoices!["advance_spellcasting"]));

        // Domains selected at cleric 1 keep granting slots at the advanced caster level.
        Assert.Equal(new[] { "domain:lust", "domain:charm" }, state.Domains);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, casting.DomainBonusSlots.Keys.Order());

        AssertHitPointsFollowSourceRolls(build);
    }

    // ---------------------------------------------------------------
    // Racial-HD creature — Nymph (6 fey HD) / Druid 6
    // ---------------------------------------------------------------

    [RequiresPcgFixturesFact]
    public void Golden_RacialHdCreature_NymphDruid6()
    {
        var build = Load("Nymph Archdruid.pcg");
        var state = build.State;

        Assert.Equal("race:nymph", state.RaceId);
        Assert.Equal(CreatureType.Fey, state.Type);
        // Nymph is 6 fey HD with a +7 level adjustment; six druid levels bring it to ECL 19.
        Assert.Equal(12, state.TotalHD);
        Assert.Equal(7, state.LevelAdjustment);
        Assert.Equal(19, state.ECL);

        // Racial HD are laid down first, then class levels — and ClassLevels counts only the
        // latter, even though both are HD drivers.
        // Her druid levels are the Elemental Druid Option, a substitution class the .pcg records
        // on the level row rather than on the CLASS row, so it resolves to its own driver.
        Assert.Equal("Elemental Druid Option", build.Source.Levels[6].SubstitutionClass);
        Assert.Equal(
            Enumerable.Repeat("racial_hd:fey", 6).Concat(Enumerable.Repeat("class:elemental_druid", 6)),
            state.HDList);
        Assert.Equal(new[] { "class:elemental_druid" }, state.ClassLevels.Keys);
        Assert.Equal(6, state.ClassLevels["class:elemental_druid"]);
        Assert.Equal(6, state.HitDice.Count(die => die.DieSize == 6)); // fey d6
        Assert.Equal(6, state.HitDice.Count(die => die.DieSize == 8)); // druid d8

        // Fey: poor BAB, good Reflex and Will, poor Fortitude.
        // Druid: average BAB, good Fortitude and Will, poor Reflex.
        Assert.Equal(PoorBab(6) + AverageBab(6), state.BaseBAB);
        AssertProgressionSaves(
            build,
            PoorSave(6) + GoodSave(6),
            GoodSave(6) + PoorSave(6),
            GoodSave(6) + GoodSave(6));

        // A nymph casts as a 7th-level druid innately; six class levels stack on top of that
        // racial caster level rather than restarting the progression at 1. The race says
        // "class:druid" and she has none — the levels are the variant's — so this only holds
        // because a rule naming a base class reaches its variants. One caster, not two.
        var casting = Assert.Single(state.Spellcasting).Value;
        Assert.Equal("class:elemental_druid", casting.ClassId);
        Assert.Equal(7 + 6, casting.CasterLevel);
        Assert.Equal(7, casting.MaxSpellLevel);
        Assert.Equal(SpellAcquisition.FullList, casting.Acquisition);

        // The substitution level's one domain, spent on Plant — which is on the variant's list,
        // so no complaint — with a cleric-style bonus slot at every level she can cast.
        Assert.Equal(new[] { "domain:plant" }, state.Domains);
        Assert.Equal("class:elemental_druid", state.DomainOwners["domain:plant"]);
        Assert.Equal(Enumerable.Range(1, 7), casting.DomainBonusSlots.Keys.Order());
        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("is not on"));

        AssertHitPointsFollowSourceRolls(build);

        // The PCGen MONCSKILL list gives Nymph all of its trained skills as class skills. The
        // race data now mirrors that list, so the imported source ranks fit the replayed pool.
        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("more skill points than available"));
    }

    // ---------------------------------------------------------------
    // Templated creature — Human Bard 13 + Lich
    // ---------------------------------------------------------------

    [RequiresPcgFixturesFact]
    public void Golden_TemplatedCreature_LichBard13()
    {
        var build = Load("Lich Recruiter.pcg");
        var state = build.State;

        // Base race stays human; the template is what changes the creature type.
        Assert.Equal("race:human", state.RaceId);
        Assert.Contains("template:lich", state.TemplateIds);
        Assert.Equal(CreatureType.Undead, state.Type);
        // The template moved the type, so life state follows it — a lich is not a living creature
        // and must not satisfy prerequisites that require one. Corporeal is unchanged: a lich is
        // solid, unlike the shadow in TemplateTests.
        Assert.False(state.IsLiving);
        Assert.True(state.IsCorporeal);

        // "No Constitution score" — a nonability, not a zero, so its modifier is +0 and nothing
        // it feeds (hit points, Fortitude, Concentration) may read the placeholder the .pcg
        // carries. Strength is untouched: this lich is corporeal.
        Assert.False(state.HasAbility(Ability.CON));
        Assert.Equal(0, state.AbilityModifier(Ability.CON));
        Assert.True(state.HasAbility(Ability.STR));

        // Lich is a +4 level adjustment on top of 13 class levels.
        Assert.Equal(13, state.TotalHD);
        Assert.Equal(4, state.LevelAdjustment);
        Assert.Equal(17, state.ECL);
        Assert.Equal(13, state.ClassLevels["class:bard"]);

        // Bard: average BAB, good Reflex and Will, poor Fortitude. The template grants abilities,
        // not progression, so the class progression is untouched by it.
        Assert.Equal(AverageBab(13), state.BaseBAB);
        AssertProgressionSaves(build, PoorSave(13), GoodSave(13), GoodSave(13));

        var casting = Assert.Single(state.Spellcasting).Value;
        Assert.Equal("class:bard", casting.ClassId);
        Assert.Equal(SpellAcquisition.SpellsKnown, casting.Acquisition);
        Assert.Equal(13, casting.CasterLevel);

        AssertHitPointsFollowSourceRolls(build);

        // "Hit Dice: Increase all current and future Hit Dice to d12s" — what an undead gets in
        // exchange for having no Constitution score. It reaches the bard's class levels, not just
        // racial HD, so a lich bard rolls d12 throughout, which is what PCGen recorded: the
        // fixture holds rolls a d6 could not produce, and none of them is out of range here.
        Assert.All(state.HitDice, die => Assert.Equal(12, die.DieSize));
        Assert.Contains(build.Source.Levels, level => level.HitPoints > 6);
        Assert.Empty(state.Warnings);

        // Lichdom is acquired, not inherited: the importer stamps the earliest HD the
        // template's prerequisites allow — caster level 11, first met when bard 11 completes,
        // so the transformation lands at HD 12 and pays for nothing lived before it.
        Assert.Equal(12, build.Character.TemplateAcquisitionHD["template:lich"]);

        // Skill points accrue against the Intelligence of the level that earned them: Int 17
        // (+3) as a living human through bard 11, Int 19 (+4) from lichdom on. Per level that
        // is bard 6 + Int mod + human 1: (6+3+1)×4 + (6+3+1)×10 + (6+4+1)×2 = 162, exactly
        // PCGen's own per-level SKILLSGAINED record — and she spent every point of it.
        Assert.Equal(162, state.SkillPointAccruals.Sum(a => a.Points));
        Assert.Equal(0, state.UnspentSkillPoints);

        // Nothing about the acquisition reaches backwards: evaluated at HD 8 she is a living
        // human bard with d6 Hit Dice, still working towards the phylactery. Worn equipment
        // applies at every evaluation point, so Int reads 17 base + 6 item = 23 — the lich's
        // +2 is what must be absent here (25 would mean the template leaked backwards).
        var at8 = new ReplayStudio(SharedRegistry.Value).Evaluate(build.Character, upToHD: 8);
        Assert.Equal(CreatureType.Humanoid, at8.Type);
        Assert.True(at8.IsLiving);
        Assert.True(at8.HasAbility(Ability.CON));
        Assert.Equal(23, at8.AbilityScores.INT);
        Assert.Equal(25, state.AbilityScores.INT);
        Assert.All(at8.HitDice, die => Assert.Equal(6, die.DieSize));
    }

    // ---------------------------------------------------------------
    // Capstone transformation — one character saved as two PCGen sheets
    // ---------------------------------------------------------------

    /// <summary>
    /// PCGen cannot say "human until the prestige capstone, outsider after", so the corpus
    /// holds this character twice: a pre-transformation sheet at HD 24 and the transformed
    /// one at HD 29. The class's 10th level now applies the transformation templates itself,
    /// so ONE stored character answers as both sheets — the whole point of the timeline.
    /// </summary>
    [RequiresPcgFixturesFact]
    public void Golden_CapstoneTransformation_DuchessRoseIsBothHerSheets()
    {
        var build = Load("Duchess Rose, Elite Succubus.pcg");
        var state = build.State;

        // The importer strips the rows PCGen materialized for the capstone's templates —
        // the class grants them at the right tick, so on the character they would apply
        // at creation and make her an outsider at HD 1.
        Assert.DoesNotContain("template:dark_temptress_succubized", build.Character.TemplateIds);
        Assert.DoesNotContain("template:chief_succubus", build.Character.TemplateIds);
        Assert.DoesNotContain("template:abyssal_noble", build.Character.TemplateIds);
        Assert.DoesNotContain("template:racesubtype_augmented_humanoid", build.Character.TemplateIds);

        // Fully evaluated she is the transformed sheet. Natural armor is 9, not 18: the
        // source's class +5 and template 9 are typed, non-stacking natural armor bonuses,
        // which the template's floor now models instead of a hand-added delta.
        Assert.Equal(29, state.TotalHD);
        Assert.Equal(10, state.ClassLevels["class:dark_temptress"]);
        Assert.Equal(CreatureType.Outsider, state.Type);
        Assert.Contains("template:dark_temptress_succubized", state.TemplateIds);
        Assert.Contains("template:chief_succubus", state.TemplateIds);
        Assert.Contains("template:abyssal_noble", state.TemplateIds);
        Assert.Contains("template:racesubtype_augmented_humanoid", state.TemplateIds);
        Assert.Equal(9, state.NaturalArmor);
        Assert.Equal(50, state.Speeds[MovementMode.Fly]);

        // Evaluated at HD 24 she is the pre-transformation sheet: a living human with five
        // temptress levels, no succubus templates, and the class's own +5 natural armor
        // not yet earned (it arrives at temptress 6).
        var at24 = new ReplayStudio(SharedRegistry.Value).Evaluate(build.Character, upToHD: 24);
        Assert.Equal(24, at24.TotalHD);
        Assert.Equal(5, at24.ClassLevels["class:dark_temptress"]);
        Assert.Equal(CreatureType.Humanoid, at24.Type);
        Assert.True(at24.IsLiving);
        Assert.DoesNotContain(at24.TemplateIds, id =>
            id is "template:dark_temptress_succubized" or "template:chief_succubus"
               or "template:abyssal_noble" or "template:racesubtype_augmented_humanoid");
        Assert.Equal(0, at24.NaturalArmor);
        Assert.False(at24.Speeds.ContainsKey(MovementMode.Fly));
    }

    /// <summary>
    /// The equality half of the two-sheet acceptance: importing the pre-transformation sheet
    /// must equal evaluating the transformed character at HD 24 — one stored character, both
    /// answers. Runs once "Duchess Rose, Almost Succubus.pcg" is copied into
    /// test-fixtures/pcg (only the Elite sheet is frozen today); until then the numeric
    /// assertions in the golden test above stand in for it.
    /// </summary>
    [RequiresPcgFixturesFact]
    public void Golden_CapstoneTransformation_AlmostSheetIsEliteEvaluatedAt24()
    {
        var almostPath = Path.Combine(TestContentHelper.GetOptionalPcgFixturesPath()!,
            "Duchess Rose, Almost Succubus.pcg");
        if (!File.Exists(almostPath))
            return; // fixture not frozen yet — see summary

        var almost = Load("Duchess Rose, Almost Succubus.pcg").State;
        var eliteAt24 = new ReplayStudio(SharedRegistry.Value)
            .Evaluate(Load("Duchess Rose, Elite Succubus.pcg").Character, upToHD: 24);

        Assert.Equal(almost.TotalHD, eliteAt24.TotalHD);
        Assert.Equal(almost.Type, eliteAt24.Type);
        Assert.Equal(almost.ClassLevels, eliteAt24.ClassLevels);
        Assert.Equal(almost.NaturalArmor, eliteAt24.NaturalArmor);
        Assert.Equal(
            (almost.AbilityScores.STR, almost.AbilityScores.DEX, almost.AbilityScores.CON,
             almost.AbilityScores.INT, almost.AbilityScores.WIS, almost.AbilityScores.CHA),
            (eliteAt24.AbilityScores.STR, eliteAt24.AbilityScores.DEX, eliteAt24.AbilityScores.CON,
             eliteAt24.AbilityScores.INT, eliteAt24.AbilityScores.WIS, eliteAt24.AbilityScores.CHA));
        Assert.Equal(almost.SkillPointAccruals.Sum(a => a.Points),
            eliteAt24.SkillPointAccruals.Sum(a => a.Points));
        Assert.Equal(almost.BaseBAB, eliteAt24.BaseBAB);
        Assert.Equal(
            (almost.ProgressionBaseSaves.Fort, almost.ProgressionBaseSaves.Ref, almost.ProgressionBaseSaves.Will),
            (eliteAt24.ProgressionBaseSaves.Fort, eliteAt24.ProgressionBaseSaves.Ref, eliteAt24.ProgressionBaseSaves.Will));
    }

    // ---------------------------------------------------------------
    // Epic progression — Human Wizard 7 / Loremaster 10 / Archmage 5 / Cosmic Descryer 10
    // ---------------------------------------------------------------

    [RequiresPcgFixturesFact]
    public void Golden_EpicProgression_Wizard32()
    {
        var build = Load("Wizard.pcg");
        var state = build.State;

        Assert.Equal(32, state.TotalHD);
        Assert.Equal(7, state.ClassLevels["class:wizard"]);
        Assert.Equal(10, state.ClassLevels["class:loremaster"]);
        Assert.Equal(5, state.ClassLevels["class:archmage"]);
        Assert.Equal(10, state.ClassLevels["class:cosmic_descryer"]);

        // Classes stop contributing BAB and saves at the epic threshold (HD 20). The first 20 HD
        // are wizard 7, loremaster 10, then the first 3 archmage levels — all poor BAB.
        Assert.Equal(PoorBab(7) + PoorBab(10) + PoorBab(3), state.BaseBAB);
        // All three of those classes have good Will and poor Fort/Ref.
        AssertProgressionSaves(
            build,
            PoorSave(7) + PoorSave(10) + PoorSave(3),
            PoorSave(7) + PoorSave(10) + PoorSave(3),
            GoodSave(7) + GoodSave(10) + GoodSave(3));

        // Past the threshold: +1 attack at each odd HD (21, 23, 25, 27, 29, 31) and +1 to every
        // save at each even HD (22, 24, 26, 28, 30, 32).
        Assert.Equal(6, state.EpicAttackBonus);
        Assert.Equal(6, state.EpicSaveBonus);
        Assert.Equal(state.BaseBAB + 6, state.EffectiveBAB);

        // Caster level keeps advancing past 20 even though BAB and saves do not: wizard 7, then
        // every loremaster and archmage level, then every *other* cosmic descryer level.
        var wizardCasting = state.Spellcasting["class:wizard"];
        Assert.Equal(7 + 10 + 5 + 5, wizardCasting.CasterLevel);
        Assert.Equal(9, wizardCasting.MaxSpellLevel);
        Assert.Equal(SpellAcquisition.Spellbook, wizardCasting.Acquisition);

        // Conjuration specialist chosen at wizard 1: one bonus slot at every level she can cast,
        // 0-level included.
        Assert.Equal(
            new[] { "school:conjuration" },
            build.Character.Ticks[0].Choices.ClassFeatureChoices![WizardSchools.SpecializationFeature]);
        Assert.Equal(Enumerable.Range(0, 10), wizardCasting.SpecialtyBonusSlots.Keys.Order());
        Assert.All(wizardCasting.SpecialtyBonusSlots.Values, count => Assert.Equal(1, count));

        // Epic Spellcasting opens a separate developed-spell track at spell level 10, keyed off
        // the same caster level rather than a class progression.
        Assert.Contains("feat:epic_spellcasting", state.Feats);
        var epicCasting = state.Spellcasting["class:epic_spells_int"];
        Assert.Equal(SpellAcquisition.Developed, epicCasting.Acquisition);
        Assert.Equal(10, epicCasting.MaxSpellLevel);
        Assert.Equal(wizardCasting.CasterLevel, epicCasting.CasterLevel);

        AssertHitPointsFollowSourceRolls(build);
    }
}
