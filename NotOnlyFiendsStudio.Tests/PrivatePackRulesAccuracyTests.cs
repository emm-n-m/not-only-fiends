using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// Assertions from the 2026-07-27 LST audit of the private packs (see
/// {EXTRA_PACKS_PATH}/test-reports/lst_audit_2026-07-27.md). Each carries the
/// PCGen LST fragment that is its ground truth. Only runs when the private
/// packs are available.
/// </summary>
public class PrivatePackRulesAccuracyTests
{
    // PCGen witnesses:
    // - tome_of_horrors_languages.lst: Daemonic TYPE:Spoken
    // - faeries_races.lst: AUTO:LANG|Fae|Fey|Shadow
    // - pf18_race.lst: AUTO:LANG|Drow Sign Language|Dwarven|Undercommon
    [RequiresPrivatePacksFact]
    public void PrivateLanguageCatalog_ContainsTheCorpusLanguages()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var languages = registry.GetAllLanguages().Select(language => language.Id).ToHashSet();

        Assert.Superset(new HashSet<string>
        {
            "daemonic",
            "fae",
            "fey",
            "shadow",
            "drow_sign_language"
        }, languages);
    }

    // "Succubus Step Mom.pcg" EQUIPNAME witnesses, each with its PCGen CUSTOMIZATION line:
    // Lawbane        BASEITEM:Assassin's Dagger, EQMOD STEEL.PLUS_10_WEAP.BNS_AC_SCRD|+5
    //                .CHAOS_POWER_MELEE.DEFEND.UNHOLY_POWER_MELEE, COST 14653102, WT 1.
    // Rainbow Piercer BASEITEM:Longbow, EQMOD WOOD.FAERIES_SPELLTCH.FAERIES_PRISMTC.PLUS_10_WEAP
    //                .CHAOS_POWER_RANGED.KEEN.UNERRING_ACCURACY, COST 18000375, WT 3.
    // Pleasure Bringer BASEITEM:Lash of Languishing (as dagger) — enchantment_equip_weap.lst:10
    //                gives CRITRANGE:4 (17-20) and SPROP "Keen Icy Dagger +3" — EQMOD PLUS_10_WEAP
    //                .VICIOUS.WOUND…RETRN.THROW, COST 4592900, WT 4.
    // Grace's Gown   BASEITEM:Robe of Stars, EQMOD BNS_SKL_CMP|Bluff=+10|Gather Information=+10
    //                |Diplomacy=+10|Perform ~ Dance=+20, COST 158000, WT 1.
    [RequiresPrivatePacksFact]
    public void ArchDemonessItems_MatchTheirPcgCustomizationLines()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var lawbane = registry.GetEquipment("deceit:lawbane");
        Assert.Equal(10, lawbane.EnhancementBonus);
        Assert.Equal(1_465_310_200, lawbane.PriceCp);
        Assert.Equal("1d4", lawbane.Weapon!.Damage);
        Assert.Equal(19, lawbane.Weapon.CritRangeLow);
        var sacred = Assert.Single(lawbane.GrantedPermabuffs.OfType<GrantTypedBonus>());
        Assert.Equal(BonusTarget.AC, sacred.Target);
        Assert.Equal(BonusType.Sacred, sacred.BonusType);

        var piercer = registry.GetEquipment("deceit:rainbow_piercer");
        Assert.Equal(10, piercer.EnhancementBonus);
        Assert.Equal(19, piercer.Weapon!.CritRangeLow); // keen, on a longbow that normally threatens on 20.
        Assert.Equal(3, piercer.Weapon.CritMultiplier);
        Assert.True(piercer.Weapon.Ranged);

        // Keen is already folded into the printed threat range of the base weapon.
        Assert.Equal(17, registry.GetEquipment("deceit:pleasure_bringer").Weapon!.CritRangeLow);

        var gown = registry.GetEquipment("deceit:graces_gown");
        Assert.Equal(
            new[] { 10, 10, 10, 20 },
            gown.GrantedPermabuffs.OfType<GrantEquipmentSkillBonus>()
                .OrderBy(bonus => bonus.SkillId, StringComparer.Ordinal)
                .Select(bonus => bonus.Value.Evaluate(new CharacterState()))
                .ToArray());
        // The base robe of stars' own +1 luck bonus on all saves survives the tailoring.
        Assert.Single(gown.GrantedPermabuffs.OfType<GrantTypedBonus>(),
            bonus => bonus.Target == BonusTarget.AllSaves && bonus.BonusType == BonusType.Luck);
    }

    // PCGen writes a variant as "Base (Variant)" where the SRD prints "Base, Variant", and appends
    // the chooser's own record after a slash. rsrd_equip_arms_and_armor.lst:265 is the item;
    // line 304 onward are the per-creature-type chooser rows the corpus character carries.
    [RequiresPrivatePacksFact]
    public void PcgenVariantNaming_ResolvesToTheSrdItemRatherThanTheBaseOne()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var mapper = new NotOnlyFiendsStudio.PcGen.PcgIdMapper();

        Assert.Equal("ammunition:slaying_arrow_greater",
            mapper.MapEquipment("Slaying Arrow (Greater/Outsiders Slaying Arrow (Greater/lawful))", registry));
        Assert.Equal("weapon:longbow_composite", mapper.MapEquipment("Longbow (Composite)", registry));
    }

    // mongoose_publishing/encyclopaedia_divine/fey_magic/fey_skills.lst:7:
    // Knowledge (Reverie) KEYSTAT:INT USEUNTRAINED:NO.
    [RequiresPrivatePacksFact]
    public void KnowledgeReverie_MatchesTheFeyMagicSkillEntry()
    {
        var skill = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable()
            .GetAllSkills()
            .Single(skill => skill.Id == "skill:knowledge_reverie");

        Assert.Equal("Knowledge (Reverie)", skill.Name);
        Assert.Equal("int", skill.KeyAbility);
        Assert.True(skill.TrainedOnly);
        Assert.False(skill.ArmorCheckPenalty);
        Assert.Equal("skill:knowledge", skill.ParentSkill);
    }

    // Encyclopaedia Arcane: Demonology PCGen witnesses:
    // demonology_classes.lst: Demonologist, Binder, Possessed class lines and prerequisites;
    // demonology_feats.lst: Change Instruction, Dismiss Demon, Quicken Summoning,
    // Strength of Personality; demonology_skills.lst: Knowledge (Demonology).
    [RequiresPrivatePacksFact]
    public void DemonologyClassesAndPrerequisiteContent_MatchThePcGenWitnesses()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var demonologist = (HDDriver)registry.GetDriver("class:demonologist");
        Assert.Equal(4, demonologist.HitDie);
        Assert.Equal(4, demonologist.SkillPointsPerLevel);
        Assert.Equal(BABProgression.Poor, demonologist.BABProgression);
        Assert.Equal(ProgressionRate.Poor, demonologist.SaveProgression.Fort);
        Assert.Equal(ProgressionRate.Poor, demonologist.SaveProgression.Ref);
        Assert.Equal(ProgressionRate.Good, demonologist.SaveProgression.Will);
        Assert.Equal(10, demonologist.MaxLevel);
        Assert.Contains(demonologist.Prerequisites, p => p is HasFeat { FeatId: "feat:spell_mastery" });
        Assert.Contains(demonologist.Prerequisites, p => p is HasLanguage { LanguageId: "draconic" });
        Assert.Single(demonologist.PerLevelPermabuffs.OfType<AdvanceSpellcasting>());

        var binder = (HDDriver)registry.GetDriver("class:binder");
        Assert.Single(binder.PerLevelPermabuffs.OfType<AdvanceSpellcasting>());
        Assert.Contains(binder.Prerequisites, p => p is HasFeat { FeatId: "feat:craft_magic_arms_and_armor" });
        Assert.Contains(binder.Prerequisites, p => p is HasFeat { FeatId: "feat:craft_wondrous_item" });
        Assert.Equal(8, Assert.Single(binder.Prerequisites.OfType<MinSkillRanks>()).Value);
        Assert.Contains(binder.Prerequisites, p => p is HasSpellcasting { CastingType: CastingType.Arcane });

        var possessed = (HDDriver)registry.GetDriver("class:possessed");
        Assert.Contains(possessed.Prerequisites, p => p is HasFeat { FeatId: "feat:strength_of_personality" });
        Assert.Equal(11, Assert.Single(possessed.Prerequisites.OfType<MinSkillRanks>()).Value);
        Assert.Equal(6, Assert.Single(possessed.Prerequisites.OfType<AlignmentReq>()).Allowed.Count);

        Assert.True(registry.TryGetSkill("skill:knowledge_demonology", out var demonologySkill));
        Assert.NotNull(demonologySkill);
        Assert.Equal("int", demonologySkill.KeyAbility);
        Assert.True(demonologySkill.TrainedOnly);

        Assert.Equal("feat:dismiss_demon", registry.GetFeat("feat:dismiss_demon").Id);
        Assert.Equal("feat:quicken_summoning", registry.GetFeat("feat:quicken_summoning").Id);
        Assert.Equal("feat:strength_of_personality", registry.GetFeat("feat:strength_of_personality").Id);
    }

    // AEG Evil witnesses: evil_classes.lst / evil_35e/evil_classes_35e.lst, pp. 29-31, 69-71.
    // Tome of Horrors Revised witnesses: tome_of_horrors_classes_monsters_dragons.lst and
    // tome_of_horrors_classes_monsters_specific.lst, pp. 83, 162, 165; race prerequisites are
    // registered from tome_of_horrors_races_d.lst and tome_of_horrors_races_dragons.lst.
    [RequiresPrivatePacksFact]
    public void AegEvilAndTomeOfHorrorsClasses_MatchThePcGenWitnesses()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var demonSummoner = (HDDriver)registry.GetDriver("class:demon_summoner");
        Assert.Equal(4, demonSummoner.HitDie);
        Assert.Equal(2, demonSummoner.SkillPointsPerLevel);
        Assert.Contains(demonSummoner.Prerequisites, p => p is AlignmentReq a && a.Allowed.SetEquals(new[] { Alignment.LE, Alignment.NE, Alignment.CE }));
        Assert.Contains(demonSummoner.Prerequisites, p => p is AnyOf);
        Assert.Equal(6, Assert.Single(demonSummoner.Prerequisites.OfType<MinSkillRanks>(), p => p.SkillId == "skill:knowledge_demonology").Value);
        Assert.Equal(6, Assert.Single(demonSummoner.Prerequisites.OfType<MinSkillRanks>(), p => p.SkillId == "skill:spellcraft").Value);

        var bargainer = (HDDriver)registry.GetDriver("class:bargainer");
        Assert.Equal(6, bargainer.HitDie);
        Assert.Contains(bargainer.Prerequisites, p => p is HasFeat { FeatId: "feat:infernal_pact" });
        Assert.Contains(bargainer.Prerequisites, p => p is HasFeat { FeatId: "feat:iron_will" });
        Assert.Contains(bargainer.LevelPermabuffs[1], p => p is ModifyAttribute { AbilityScore: Ability.CHA, Value: 1 });
        Assert.Contains(bargainer.LevelPermabuffs[7], p => p is GrantBonusFeat { FeatId: "feat:craft_wondrous_item" });

        var bloodArcher = (HDDriver)registry.GetDriver("class:blood_archer");
        Assert.Equal(BABProgression.Good, bloodArcher.BABProgression);
        Assert.Equal(ProgressionRate.Good, bloodArcher.SaveProgression.Fort);
        Assert.Equal(ProgressionRate.Poor, bloodArcher.SaveProgression.Ref);
        Assert.Contains(bloodArcher.Prerequisites, p => p is HasFeat { FeatId: "feat:weapon_specialization" });
        Assert.Equal(6, Assert.Single(bloodArcher.Prerequisites.OfType<MinSkillRanks>(), p => p.SkillId == "skill:craft_bowyer_fletcher").Value);
        Assert.Contains(bloodArcher.ClassSkills, skill => skill == "skill:craft_bowyer_fletcher");

        var cloud = (HDDriver)registry.GetDriver("racial_hd:cloud_dragon");
        Assert.Equal(12, cloud.HitDie);
        Assert.Equal(BABProgression.Good, cloud.BABProgression);
        Assert.Equal(ProgressionRate.Good, cloud.SaveProgression.Will);
        Assert.Contains(cloud.Prerequisites, p => p is HasRace { RaceId: "race:cloud_dragon" });

        var mist = (HDDriver)registry.GetDriver("racial_hd:mist_dragon");
        var mistRaceIds = mist.Prerequisites.OfType<AnyOf>().Single().Options.OfType<HasRace>().Select(p => p.RaceId).ToHashSet();
        Assert.Equal(12, mistRaceIds.Count);
        Assert.Contains("race:mist_dragon_wyrmling", mistRaceIds);
        Assert.Contains("race:mist_dragon_great_wyrm", mistRaceIds);

        var oinodaemon = (HDDriver)registry.GetDriver("racial_hd:the_oinodaemon");
        Assert.Equal(8, oinodaemon.HitDie);
        Assert.Equal(49, oinodaemon.MaxLevel);
        Assert.Contains(oinodaemon.Prerequisites, p => p is HasRace { RaceId: "race:daemon_the_oinodaemon" });
        Assert.Contains(oinodaemon.LevelPermabuffs[1], p => p is GrantDomainSelection { Count: 3, ClassId: "class:cleric" });

        Assert.Equal("skill:craft_bowyer_fletcher", registry.GetAllSkills().Single(s => s.Name == "Craft (Bowyer/Fletcher)").Id);
        Assert.Equal("racial_hd:cloud_dragon", registry.GetRace("race:cloud_dragon").RacialHDDriverId);
        Assert.Equal("racial_hd:the_oinodaemon", registry.GetRace("race:daemon_the_oinodaemon").RacialHDDriverId);
    }

    // AEG Evil evil_35e/evil_spells_35e.lst:5, Belarosh's Law.
    [RequiresPrivatePacksFact]
    public void BelaroshLaw_MatchesThePcGenSpellWitness()
    {
        var spell = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable()
            .GetSpell("spell:belarosh_s_law");

        Assert.Equal("Belarosh's Law", spell.Name);
        Assert.Equal("abjuration", spell.School);
        Assert.Equal(3, spell.ClassLevels["class:sorcerer"]);
        Assert.Equal(3, spell.ClassLevels["class:wizard"]);
        Assert.Equal(4, spell.ClassLevels["class:cleric"]);
        Assert.True(spell.Components.Verbal);
        Assert.True(spell.Components.Somatic);
        Assert.Equal("magic circle", spell.Components.Material);
        Assert.Equal("30 minutes", spell.CastingTime);
        Assert.Equal("Touch", spell.Range);
        Assert.Equal("One magic circle of any size", spell.Target);
        Assert.Equal("24 hours", spell.Duration);
        Assert.Equal("Special", spell.SavingThrow);
        Assert.Equal("Special", spell.SpellResistance);
    }

    // silverthorne_games/.../sevenstrangespells_spells.lst:8:
    // CLASSES:Sorcerer,Wizard=1 SCHOOL:Illusion SUBSCHOOL:Pattern
    // DESCRIPTOR:Mind-Affecting COMPS:V, S, M.
    // lions_den_press/.../planarmagic_spells.lst:29:
    // CLASSES:Druid,Elemental Fire,Sorcerer,Wizard=0 SCHOOL:Evocation DESCRIPTOR:Fire.
    // malhavoc_press/.../completeeldritch_spells.lst:233:
    // CLASSES:Bard,Cleric,Sorcerer,Sorcerer (Monte Cook's),Wizard=0 SCHOOL:Transmutation.
    [RequiresPrivatePacksFact]
    public void PrivateSpellGapFixes_MatchExistingClassMappings()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var pastel = registry.GetSpell("spell:pastel_color_spray");
        Assert.Equal("illusion", pastel.School);
        Assert.Equal("pattern", pastel.Subschool);
        Assert.Contains("mind-affecting", pastel.Descriptors);
        Assert.Equal(1, pastel.ClassLevels["class:sorcerer"]);
        Assert.Equal(1, pastel.ClassLevels["class:wizard"]);
        Assert.True(pastel.Components.Verbal);
        Assert.True(pastel.Components.Somatic);
        Assert.Equal("yes", pastel.SpellResistance);

        var ember = registry.GetSpell("spell:ember");
        Assert.Equal("evocation", ember.School);
        Assert.Contains("fire", ember.Descriptors);
        Assert.Equal(0, ember.ClassLevels["class:druid"]);
        Assert.Equal(0, ember.ClassLevels["class:sorcerer"]);
        Assert.Equal(0, ember.ClassLevels["class:wizard"]);

        var transcribe = registry.GetSpell("spell:transcribe");
        Assert.Equal("transmutation", transcribe.School);
        Assert.Equal(0, transcribe.ClassLevels["class:bard"]);
        Assert.Equal(0, transcribe.ClassLevels["class:cleric"]);
        Assert.Equal(0, transcribe.ClassLevels["class:sorcerer"]);
        Assert.Equal(0, transcribe.ClassLevels["class:wizard"]);
        Assert.Equal("One piece of paper or parchment up to 1 foot square", transcribe.Target);
    }

    private static MinSkillRanks SkillPrereq(string driverId, string skillId)
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var driver = registry.GetDriver(driverId);
        return Assert.Single(driver.Prerequisites.OfType<MinSkillRanks>(), p => p.SkillId == skillId);
    }

    // deceit/epic/custom_spells_epic.lst: Mind Rape is an Epic Spells (CHA/INT/WIS)
    // level-10 enchantment (compulsion, mind-affecting) with V, S and XP components.
    [RequiresPrivatePacksFact]
    public void MindRape_MatchesTheDeceitEpicSpellList()
    {
        var spell = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable()
            .GetSpell("spell:mind_rape");

        Assert.Equal("Mind Rape", spell.Name);
        Assert.Equal("enchantment", spell.School);
        Assert.Equal("compulsion", spell.Subschool);
        Assert.Contains("mind-affecting", spell.Descriptors);
        Assert.Equal("special", spell.Range);
        Assert.Equal("special", spell.Target);
        Assert.Equal("instantaneous (24 hours for compulsion)", spell.Duration);
        Assert.Equal("Will special", spell.SavingThrow);
        Assert.Equal("yes", spell.SpellResistance);
        Assert.True(spell.Components.Verbal);
        Assert.True(spell.Components.Somatic);
        Assert.Equal(System.Text.Json.JsonValueKind.True, spell.Components.XpCost?.ValueKind);
        Assert.All(new[]
        {
            EpicSpellcasting.CharismaListId,
            EpicSpellcasting.IntelligenceListId,
            EpicSpellcasting.WisdomListId,
        }, listId => Assert.Equal(10, spell.ClassLevels[listId]));
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

    // The LST spreads the transformation across the class line (MOVE:Fly,50, SR:12,
    // DR:10/Cold Iron or Good) and the template row (NA, Listen/Spot, resistances, energy
    // drain) — enchantment_classes_35e.lst:59 / enchantment_templates.lst:8. User ruling
    // 2026-08-11: that class-side placement is a bad implementation — fly, SR and DR are
    // tanar'ri traits of the succubus form, so they live on the template the capstone
    // applies, and the class's 10th level carries only ApplyTemplate plus its own features.
    // SR is a floor, not a delta — SR from overlapping sources does not stack, so an
    // alu-fiend's higher racial SR survives the transformation. User ruling 2026-08-11: the
    // printed SR 12 is a 3.0 misport — the 3.5 succubus has SR 18 — so pack and LST both
    // carry 18. (Whether SR should scale past the vanilla succubus's is an open
    // interpretation, kept flat.) The succubus form uses the SRD succubus's Average
    // maneuverability.
    [RequiresPrivatePacksFact]
    public void DarkTemptress_CapstoneAppliesTemplate_TemplateCarriesTheTanarriTraits()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var capstone = ((HDDriver)registry.GetDriver("class:dark_temptress")).LevelPermabuffs[10];
        Assert.Contains(capstone, b => b is ApplyTemplate { TemplateId: "template:dark_temptress_succubized" });
        Assert.DoesNotContain(capstone, b => b is GrantDR or GrantMovement);
        Assert.DoesNotContain(capstone, b => b is ModifyAttribute { Target: AttributeTarget.SpellResistance });

        var succubized = registry.GetTemplate("template:dark_temptress_succubized");

        var dr = succubized.CreationPermabuffs.OfType<GrantDR>().Single();
        Assert.Equal(10, dr.Value);
        Assert.Equal("cold iron or good", dr.BypassedBy);

        var movement = succubized.CreationPermabuffs.OfType<GrantMovement>().Single();
        Assert.Equal(MovementMode.Fly, movement.Mode);
        Assert.Equal(50, movement.Speed);
        Assert.Equal(FlightManeuverability.Average, movement.FlyManeuverability);

        Assert.Equal(18, succubized.SpellResistanceFloor);
        Assert.DoesNotContain(succubized.CreationPermabuffs,
            b => b is ModifyAttribute { Target: AttributeTarget.SpellResistance });

        // BONUS:COMBAT|AC|9|TYPE=NaturalArmor is a typed, non-stacking bonus: max with the
        // class's own +5 (level 6), never a sum — the floor is how the engine says "max".
        Assert.Equal(9, succubized.NaturalArmorFloor);
        Assert.Null(succubized.NaturalArmor);
        var listenSpot = succubized
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

    // --- Fiendish Codex PDF audit, 2026-07-28 ---
    // See {EXTRA_PACKS_PATH}/test-reports/fc_pdf_audit_2026-07-28.md. Ground truth for
    // these is the owned PDF, quoted by page, rather than an LST.

    private static Character Hellbred(string? aspect)
    {
        var character = new Character
        {
            Name = "Hellbred",
            RaceId = "race:hellbred",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 12, DEX = 12, CON = 12, INT = 12, WIS = 12, CHA = 12
            },
            Ticks = new List<Tick> { new() { DriverId = "class:fighter" } }
        };
        if (aspect != null)
        {
            character.Ticks[0].Choices = new TickChoices
            {
                ClassFeatureChoices = new Dictionary<string, List<string>>
                {
                    ["class_feature:hellbred_infernal_aspect"] = new() { aspect }
                }
            };
        }
        return character;
    }

    // FC2 78: "Body (Ex): … he gains a +2 bonus to Constitution and takes a -2 penalty
    // to Intelligence." The extraction left abilityModifiers null, so neither aspect
    // applied and every hellbred was wrong by 2 points in two abilities.
    [RequiresPrivatePacksFact]
    public void Hellbred_BodyAspect_GrantsConAndIntModifiers()
    {
        var engine = new ReplayStudio(TestContentHelper.LoadBundledAndPrivatePacksIfAvailable());
        var state = engine.Evaluate(Hellbred("body"));

        Assert.Equal(14, state.AbilityScores.CON);
        Assert.Equal(10, state.AbilityScores.INT);
        Assert.Equal(12, state.AbilityScores.CHA);
    }

    // FC2 78: "Spirit (Su): … The hellbred gains a +2 bonus to Charisma and takes a -2
    // penalty to Constitution", plus "a +2 racial bonus on Sense Motive checks".
    [RequiresPrivatePacksFact]
    public void Hellbred_SpiritAspect_GrantsChaConModifiersAndSenseMotive()
    {
        var engine = new ReplayStudio(TestContentHelper.LoadBundledAndPrivatePacksIfAvailable());
        var state = engine.Evaluate(Hellbred("spirit"));

        Assert.Equal(14, state.AbilityScores.CHA);
        Assert.Equal(10, state.AbilityScores.CON);
        Assert.Equal(12, state.AbilityScores.INT);
        Assert.Equal(2, state.SkillBonuses["skill:sense_motive"]);
    }

    // The aspect is a mandatory one-time choice, so an unmade choice must stay pending
    // rather than silently defaulting to one of the two.
    [RequiresPrivatePacksFact]
    public void Hellbred_WithoutAspectChoice_LeavesSelectionPending()
    {
        var engine = new ReplayStudio(TestContentHelper.LoadBundledAndPrivatePacksIfAvailable());
        var state = engine.Evaluate(Hellbred(null));

        Assert.Equal(1, state.PendingClassFeatureSelections["class_feature:hellbred_infernal_aspect"]);
        Assert.Equal(12, state.AbilityScores.CON);
        Assert.Equal(12, state.AbilityScores.CHA);
    }

    // FC2 78: "Automatic Languages: Infernal." First content anywhere to use
    // GrantLanguage — CharacterState.Languages had no writer in any pack before this.
    [RequiresPrivatePacksFact]
    public void Hellbred_KnowsInfernal()
    {
        var engine = new ReplayStudio(TestContentHelper.LoadBundledAndPrivatePacksIfAvailable());
        var state = engine.Evaluate(Hellbred("body"));

        Assert.Contains("infernal", state.Languages);
    }

    // Fiendish Codex I prints no Level Adjustment for any creature — the string does not
    // occur in the book, and there is no "demons as characters" section. Lilitu (p43) and
    // Yochlol (p55) do read "Advancement by character class; Favored Class …", but that is
    // an NPC-advancement field, not the 3.5 PC-legality marker. The extraction had invented
    // LA 2–6 across these five. User ruling 2026-07-28: carry NO level adjustment — null,
    // not 0, since 0 would claim "playable at no cost" (Human) rather than "never priced as
    // a PC race". If one of these is ever played, house-rule that race alone.
    [RequiresPrivatePacksTheory]
    [InlineData("race:ekolid")]
    [InlineData("race:juvenile_nabassu")]
    [InlineData("race:armanite")]
    [InlineData("race:yochlol")]
    [InlineData("race:lilitu")]
    public void FiendishCodex1Races_CarryNoUnsourcedLevelAdjustment(string raceId)
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        Assert.Null(registry.GetRace(raceId).LevelAdjustment);
    }

    // Null LA is a provenance statement, not a different number: it must still contribute
    // 0 to ECL, exactly as an LA-0 race does.
    [RequiresPrivatePacksFact]
    public void NullLevelAdjustment_ContributesZeroToEcl()
    {
        var engine = new ReplayStudio(TestContentHelper.LoadBundledAndPrivatePacksIfAvailable());
        var state = engine.Evaluate(new Character
        {
            Name = "Lilitu",
            RaceId = "race:lilitu",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = new List<Tick> { new() { DriverId = "class:fighter" } }
        });

        Assert.Equal(0, state.LevelAdjustment);
        Assert.Equal(state.TotalHD, state.ECL);
    }

    // FC2 p90 Table 3-3 reads 1st +0 / 2nd +1 / 3rd +2 — average, not poor (poor gives +1
    // at 3rd). Extraction had "poor".
    [RequiresPrivatePacksFact]
    public void HellfireWarlock_HasAverageBab()
    {
        var driver = (HDDriver)TestContentHelper.LoadBundledAndPrivatePacksIfAvailable()
            .GetDriver("class:hellfire_warlock");
        Assert.Equal(BABProgression.Average, driver.BABProgression);
    }

    // FC2 p96: "Spellcasting: Ability to cast 1st-level divine spells." CanCastSpellLevel's
    // CastingType is nullable and matches ANY caster when null, so the qualifier is what
    // stops a wizard qualifying.
    [RequiresPrivatePacksFact]
    public void Soulguard_RequiresDivineCasting()
    {
        var driver = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable()
            .GetDriver("class:soulguard");
        var prereq = driver.Prerequisites.OfType<CanCastSpellLevel>().Single();
        Assert.Equal(1, prereq.SpellLevel);
        Assert.Equal(CastingType.Divine, prereq.CastingType);
    }

    // FC1 Table 4-1 (p84) lists Ordered Chaos under "General Feats", and its description
    // heading (p86) carries no [ABYSSAL HERITOR] bracket while all 13 genuine ones do. The
    // tag is load-bearing: four feats gate on HasFeatWithTag{abyssal_heritor}, so a tagged
    // Ordered Chaos unlocked them on its own and inflated every "per heritor feat" count.
    [RequiresPrivatePacksFact]
    public void OrderedChaos_IsNotAnAbyssalHeritorFeat()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        Assert.DoesNotContain("abyssal_heritor", registry.GetFeat("feat:ordered_chaos").Tags);

        // The 13 that genuinely carry the bracket still do.
        Assert.Equal(13, registry.GetAllFeats()
            .Count(f => f.Tags.Contains("abyssal_heritor")));
    }

    // FC2 p83 and Table 3-1: every divine feat requires the ability to turn or rebuke undead.
    // All five had dropped it — the LST audit's P1 pattern. Content models turning and
    // rebuking as the single ability "turn_undead" (cleric, paladin, archfiend all grant it),
    // so HasAbility on that id is the whole requirement.
    [RequiresPrivatePacksTheory]
    [InlineData("feat:divine_censure")]
    [InlineData("feat:divine_defiance")]
    [InlineData("feat:divine_justice")]
    [InlineData("feat:persistent_refusal")]
    [InlineData("feat:pious_defiance")]
    public void FiendishCodex2DivineFeats_RequireTurnOrRebukeUndead(string featId)
    {
        var feat = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable().GetFeat(featId);
        Assert.Contains(feat.Prerequisites.OfType<HasAbility>(), p => p.AbilityId == "turn_undead");
    }

    // Fiendish Codex I pp. 88-90. The extraction had invented 23 of these 54 slots,
    // substituting plausible SRD spells; every wrong slot was an SRD-spell slot while every
    // FC1-native one was right. Re-extracted 2026-07-28 from the domain blocks.
    [RequiresPrivatePacksTheory]
    [InlineData("domain:corruption", "doom,blindness_deafness,contagion,morality_undone,feeblemind,pox,insanity,befoul,despoil")]
    [InlineData("domain:demonic", "demonflesh,demoncall,demon_wings,dimensional_anchor,planar_binding_lesser,planar_binding,fiendish_clarity,planar_binding_greater,gate")]
    [InlineData("domain:entropy", "cause_fear,vision_of_entropy,ray_of_exhaustion,fear,waves_of_fatigue,disintegrate,insanity,scintillating_pattern,abyssal_rift")]
    [InlineData("domain:fury", "true_strike,bulls_strength,rage,divine_power,shout,song_of_discord,abyssal_frenzy,shout_greater,abyssal_frenzy_mass")]
    [InlineData("domain:ooze", "grease,web,poison,rusting_grasp,oozepuppet,transmute_rock_to_mud,slime_wave,befoul,implosion")]
    [InlineData("domain:temptation", "charm_person,beckoning_call,suggestion,charm_monster,dominate_person,suggestion_mass,soul_link,sympathy,dominate_monster")]
    public void FiendishCodex1Domains_HaveTheBookSpellList(string domainId, string expected)
    {
        var domain = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable().GetDomain(domainId);
        var actual = string.Join(",", Enumerable.Range(1, 9)
            .Select(lv => domain.BonusSpells[lv].Replace("spell:", "")));
        Assert.Equal(expected, actual);
    }

    // Five of the six granted powers were a different power than the book's — Corruption
    // carried the SRD Destruction domain's smite, Fury carried barbarian rage. Asserting a
    // distinguishing phrase rather than the full prose: enough to fail if the wrong power
    // returns, without pinning wording.
    [RequiresPrivatePacksTheory]
    [InlineData("domain:corruption", "hardness")]      // attack an object and ignore its hardness
    [InlineData("domain:demonic", "natural weapons")]  // +1 profane on unarmed/natural attacks
    [InlineData("domain:entropy", "sonic")]            // bolt of Abyssal entropy, half sonic
    [InlineData("domain:fury", "target of your fury")]
    [InlineData("domain:ooze", "rebuke")]
    [InlineData("domain:temptation", "gender")]
    public void FiendishCodex1Domains_GrantTheBookPower(string domainId, string phrase)
    {
        var domain = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable().GetDomain(domainId);
        var granted = domain.GrantedPermabuffs.OfType<GrantAbility>().Single().Ability;
        Assert.Contains(phrase, $"{domain.Description} {granted.Description}",
            StringComparison.OrdinalIgnoreCase);
    }

    // The seven-domain FiendishCodexDomains_ReferenceOnlyRealSpells that lived here was scoped to
    // the Fiendish Codex domains only because the public packs would have failed it: 14 slots
    // across 11 domains pointed at spell ids that do not exist. Those are fixed, so the check is
    // now ungated and covers every domain in every loaded pack — see
    // ContentIntegrityTests.EveryDomainBonusSpell_Resolves.

    // FC2 78: "Infernal Mien (Ex): … +2 racial bonus on Intimidate checks." Was prose
    // on a GrantAbility only, so it never reached computed skill totals (P3 pattern).
    [RequiresPrivatePacksFact]
    public void Hellbred_InfernalMienIsStructured()
    {
        var engine = new ReplayStudio(TestContentHelper.LoadBundledAndPrivatePacksIfAvailable());
        var state = engine.Evaluate(Hellbred("body"));

        Assert.Equal(2, state.SkillBonuses["skill:intimidate"]);
    }
}
