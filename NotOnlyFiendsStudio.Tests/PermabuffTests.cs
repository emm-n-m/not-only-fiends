using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class PermabuffTests
{
    private CharacterState CreateState(int totalHD = 1, int con = 10, int intScore = 10)
    {
        return new CharacterState
        {
            TotalHD = totalHD,
            AbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = con, INT = intScore, WIS = 10, CHA = 10
            }
        };
    }

    // --- AddHitDie ---

    [Fact]
    public void AddHitDie_FirstHD_MaxRoll()
    {
        var state = CreateState(totalHD: 1, con: 14); // CON mod +2
        new AddHitDie(10).Apply(state);
        Assert.Equal(12, state.HP); // 10 (max) + 2 (CON)
    }

    [Fact]
    public void AddHitDie_SubsequentHD_AverageRoll()
    {
        var state = CreateState(totalHD: 2, con: 14);
        new AddHitDie(10).Apply(state);
        Assert.Equal(8, state.HP); // 6 (10/2+1) + 2 (CON)
    }

    [Fact]
    public void AddHitDie_D6_Average()
    {
        var state = CreateState(totalHD: 2, con: 10);
        new AddHitDie(6).Apply(state);
        Assert.Equal(4, state.HP); // 4 (6/2+1) + 0 (CON)
    }

    [Fact]
    public void AddHitDie_NegativeCON_MinimumOneHP()
    {
        var state = CreateState(totalHD: 2, con: 3); // CON mod -4
        new AddHitDie(4).Apply(state);
        // 3 (4/2+1) + (-4) = -1, but min 1
        Assert.Equal(1, state.HP);
    }

    // --- AddBAB ---

    [Theory]
    [InlineData(BABProgression.Good, 1, 1)]   // Fighter 1: +1
    [InlineData(BABProgression.Good, 2, 2)]   // Fighter 2: +2
    [InlineData(BABProgression.Good, 5, 5)]   // Fighter 5: +5
    [InlineData(BABProgression.Average, 1, 0)] // Cleric/Rogue 1: floor(0.75) = 0
    [InlineData(BABProgression.Average, 2, 1)] // Cleric/Rogue 2: floor(1.5) = 1
    [InlineData(BABProgression.Average, 4, 3)] // Cleric/Rogue 4: floor(3) = 3
    [InlineData(BABProgression.Poor, 1, 0)]    // Wizard 1: floor(0.5) = 0
    [InlineData(BABProgression.Poor, 2, 1)]    // Wizard 2: floor(1) = 1
    [InlineData(BABProgression.Poor, 5, 2)]    // Wizard 5: floor(2.5) = 2
    public void AddBAB_CumulativeProgression(BABProgression prog, int levels, int expectedTotalBAB)
    {
        var state = CreateState();
        // Simulate taking multiple levels by applying incremental BAB
        for (int lvl = 1; lvl <= levels; lvl++)
        {
            new AddBAB(prog, lvl).Apply(state);
        }
        Assert.Equal(expectedTotalBAB, state.BaseBAB);
    }

    [Fact]
    public void AddBAB_Fighter1to5_Correct()
    {
        var state = CreateState();
        for (int lvl = 1; lvl <= 5; lvl++)
            new AddBAB(BABProgression.Good, lvl).Apply(state);
        Assert.Equal(5, state.BaseBAB);
    }

    [Fact]
    public void AddBAB_Wizard1to5_Correct()
    {
        var state = CreateState();
        for (int lvl = 1; lvl <= 5; lvl++)
            new AddBAB(BABProgression.Poor, lvl).Apply(state);
        Assert.Equal(2, state.BaseBAB);
    }

    // --- AddSaves ---

    [Fact]
    public void AddSaves_Fighter1to5()
    {
        // Fighter: Fort good, Ref poor, Will poor
        var prog = new SaveProgression
        {
            Fort = ProgressionRate.Good,
            Ref = ProgressionRate.Poor,
            Will = ProgressionRate.Poor
        };
        var state = CreateState();
        for (int lvl = 1; lvl <= 5; lvl++)
            new AddSaves(prog, lvl).Apply(state);

        // Good Fort at 5: 2 + 5/2 = 4
        Assert.Equal(4, state.BaseSaves.Fort);
        // Poor Ref at 5: 5/3 = 1
        Assert.Equal(1, state.BaseSaves.Ref);
        // Poor Will at 5: 5/3 = 1
        Assert.Equal(1, state.BaseSaves.Will);
    }

    [Fact]
    public void AddSaves_Wizard1to5()
    {
        // Wizard: Fort poor, Ref poor, Will good
        var prog = new SaveProgression
        {
            Fort = ProgressionRate.Poor,
            Ref = ProgressionRate.Poor,
            Will = ProgressionRate.Good
        };
        var state = CreateState();
        for (int lvl = 1; lvl <= 5; lvl++)
            new AddSaves(prog, lvl).Apply(state);

        Assert.Equal(1, state.BaseSaves.Fort);
        Assert.Equal(1, state.BaseSaves.Ref);
        Assert.Equal(4, state.BaseSaves.Will);
    }

    [Fact]
    public void AddSaves_GoodProgression_Level1_Is2()
    {
        // Good save at level 1 = 2 + 1/2 = 2
        var state = CreateState();
        new AddSaves(new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Poor, Will = ProgressionRate.Poor }, 1).Apply(state);
        Assert.Equal(2, state.BaseSaves.Fort);
    }

    // --- GrantSkillPoints ---

    [Fact]
    public void GrantSkillPoints_FirstHD_Quadrupled()
    {
        var state = CreateState(totalHD: 1, intScore: 14); // INT mod +2
        new GrantSkillPoints(2).Apply(state);
        // (2 + 2) * 4 = 16
        Assert.Equal(16, state.UnspentSkillPoints);
    }

    [Fact]
    public void GrantSkillPoints_SubsequentHD_Normal()
    {
        var state = CreateState(totalHD: 2, intScore: 14);
        new GrantSkillPoints(2).Apply(state);
        // 2 + 2 = 4
        Assert.Equal(4, state.UnspentSkillPoints);
    }

    [Fact]
    public void GrantSkillPoints_MinimumOne()
    {
        var state = CreateState(totalHD: 2, intScore: 3); // INT mod -4
        new GrantSkillPoints(2).Apply(state);
        // 2 + (-4) = -2, but min 1
        Assert.Equal(1, state.UnspentSkillPoints);
    }

    [Fact]
    public void GrantSkillPoints_MinimumOne_FirstHD_StillQuadrupled()
    {
        var state = CreateState(totalHD: 1, intScore: 3);
        new GrantSkillPoints(2).Apply(state);
        // max(1, 2 + (-4)) = 1, * 4 = 4
        Assert.Equal(4, state.UnspentSkillPoints);
    }

    // --- AddClassSkills ---

    [Fact]
    public void AddClassSkills_AddsToHashSet()
    {
        var state = CreateState();
        new AddClassSkills(new List<string> { "skill:climb", "skill:swim", "skill:jump" }).Apply(state);
        Assert.Contains("skill:climb", state.ClassSkills);
        Assert.Contains("skill:swim", state.ClassSkills);
        Assert.Contains("skill:jump", state.ClassSkills);
    }

    [Fact]
    public void AddClassSkills_NoDuplicates()
    {
        var state = CreateState();
        new AddClassSkills(new List<string> { "skill:climb" }).Apply(state);
        new AddClassSkills(new List<string> { "skill:climb", "skill:swim" }).Apply(state);
        Assert.Equal(2, state.ClassSkills.Count);
    }

    // --- GrantAbility / RevokeAbility ---

    [Fact]
    public void GrantAbility_AddsToList()
    {
        var state = CreateState();
        new GrantAbility { Ability = new GrantedAbility { Id = "spell:rage", Name = "Rage" } }.Apply(state);
        Assert.Single(state.Abilities);
        Assert.Equal("spell:rage", state.Abilities[0].Id);
    }

    [Fact]
    public void RevokeAbility_RemovesById()
    {
        var state = CreateState();
        state.Abilities.Add(new GrantedAbility { Id = "spell:rage", Name = "Rage" });
        state.Abilities.Add(new GrantedAbility { Id = "fast_movement", Name = "Fast Movement" });

        new RevokeAbility { AbilityId = "spell:rage" }.Apply(state);
        Assert.Single(state.Abilities);
        Assert.Equal("fast_movement", state.Abilities[0].Id);
    }

    // --- ModifyCounter ---

    [Fact]
    public void ModifyCounter_IncrementsValue()
    {
        var state = CreateState();
        new ModifyCounter { CounterId = "sneak_attack_dice", Value = 1 }.Apply(state);
        Assert.Equal(1, state.Counters["sneak_attack_dice"]);
    }

    [Fact]
    public void ModifyCounter_StacksMultipleTimes()
    {
        var state = CreateState();
        new ModifyCounter { CounterId = "sneak_attack_dice" }.Apply(state);
        new ModifyCounter { CounterId = "sneak_attack_dice" }.Apply(state);
        new ModifyCounter { CounterId = "sneak_attack_dice" }.Apply(state);
        Assert.Equal(3, state.Counters["sneak_attack_dice"]);
    }

    [Fact]
    public void ModifyCounter_DefaultValueIsOne()
    {
        var state = CreateState();
        new ModifyCounter { CounterId = "rage_uses" }.Apply(state);
        Assert.Equal(1, state.Counters["rage_uses"]);
    }

    [Fact]
    public void ModifyCounter_CustomValue()
    {
        var state = CreateState();
        new ModifyCounter { CounterId = "shadow_jump_distance", Value = 20 }.Apply(state);
        new ModifyCounter { CounterId = "shadow_jump_distance", Value = 20 }.Apply(state);
        Assert.Equal(40, state.Counters["shadow_jump_distance"]);
    }

    // --- GrantSLA / RevokeSLA ---

    [Fact]
    public void GrantSLA_AddsToList()
    {
        var state = CreateState();
        new GrantSLA { SLA = new SLA { Id = "spell:darkness", Name = "Darkness", UsesPerDay = "3" } }.Apply(state);
        Assert.Single(state.SLAs);
        Assert.Equal("spell:darkness", state.SLAs[0].Id);
    }

    [Fact]
    public void RevokeSLA_RemovesById()
    {
        var state = CreateState();
        state.SLAs.Add(new SLA { Id = "spell:darkness", Name = "Darkness" });
        state.SLAs.Add(new SLA { Id = "spell:desecrate", Name = "Desecrate" });

        new RevokeSLA { SLAId = "spell:darkness" }.Apply(state);
        Assert.Single(state.SLAs);
        Assert.Equal("spell:desecrate", state.SLAs[0].Id);
    }

    // --- GrantBonusFeat ---

    [Fact]
    public void GrantBonusFeat_AddsFeatToList()
    {
        var state = CreateState();
        new GrantBonusFeat { FeatId = "feat:improved_initiative" }.Apply(state);
        Assert.Contains("feat:improved_initiative", state.Feats);
    }

    [Fact]
    public void GrantSaveBonus_UsesHighestBonusOfEachType()
    {
        var state = CreateState();
        new GrantSaveBonus { Target = SaveTarget.Will, BonusType = BonusType.Racial, Value = 2 }.Apply(state);
        new GrantSaveBonus { Target = SaveTarget.Will, BonusType = BonusType.Racial, Value = 1 }.Apply(state);
        new GrantSaveBonus { Target = SaveTarget.Will, BonusType = BonusType.Untyped, Value = 2 }.Apply(state);

        Assert.Equal(4 + AbilityScoreSet.Modifier(state.AbilityScores.WIS), state.EffectiveSaves.Will);
    }

    // --- ModifyAttribute ---

    [Fact]
    public void ModifyAttribute_NaturalArmor()
    {
        var state = CreateState();
        state.NaturalArmor = 2;
        new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 3 }.Apply(state);
        Assert.Equal(5, state.NaturalArmor);
    }

    [Fact]
    public void ModifyAttribute_Resistance()
    {
        var state = CreateState();
        new ModifyAttribute { Target = AttributeTarget.Resistance, ResistanceElement = "fire", Value = 10 }.Apply(state);
        Assert.Equal(10, state.Resistances["fire"]);
    }

    [Fact]
    public void ModifyAttribute_AbilityScore()
    {
        var state = CreateState();
        state.AbilityScores.STR = 16;
        new ModifyAttribute { Target = AttributeTarget.AbilityScore, AbilityScore = Ability.STR, Value = 4 }.Apply(state);
        Assert.Equal(20, state.AbilityScores.STR);
    }

    [Fact]
    public void ModifyAttribute_SpellResistance()
    {
        var state = CreateState();
        new ModifyAttribute { Target = AttributeTarget.SpellResistance, Value = 15 }.Apply(state);
        Assert.Equal(15, state.SpellResistance);
    }

    // --- SetAttribute ---

    [Fact]
    public void SetAttribute_NaturalArmor_Overwrites()
    {
        var state = CreateState();
        state.NaturalArmor = 5;
        new SetAttribute(AttributeTarget.NaturalArmor, 10).Apply(state);
        Assert.Equal(10, state.NaturalArmor);
    }

    [Fact]
    public void SetAttribute_SpellResistance()
    {
        var state = CreateState();
        new SetAttribute(AttributeTarget.SpellResistance, 18).Apply(state);
        Assert.Equal(18, state.SpellResistance);
    }

    [Fact]
    public void SetAttribute_Resistance()
    {
        var state = CreateState();
        state.Resistances["fire"] = 5;
        new SetAttribute(AttributeTarget.Resistance, 10, "fire").Apply(state);
        Assert.Equal(10, state.Resistances["fire"]);
    }

    [Fact]
    public void SetAttribute_AbilityScore()
    {
        var state = CreateState();
        state.AbilityScores.DEX = 14;
        new SetAttribute(AttributeTarget.AbilityScore, 20, abilityScore: Ability.DEX).Apply(state);
        Assert.Equal(20, state.AbilityScores.DEX);
    }

    [Fact]
    public void ModifyAttribute_LevelAdjustment()
    {
        var state = CreateState();
        state.LevelAdjustment = 1;
        new ModifyAttribute { Target = AttributeTarget.LevelAdjustment, Value = 2 }.Apply(state);
        Assert.Equal(3, state.LevelAdjustment);
    }

    [Fact]
    public void ModifyAttribute_Resistance_StacksOnExisting()
    {
        var state = CreateState();
        state.Resistances["fire"] = 5;
        new ModifyAttribute { Target = AttributeTarget.Resistance, ResistanceElement = "fire", Value = 10 }.Apply(state);
        Assert.Equal(15, state.Resistances["fire"]);
    }

    // --- GrantFeatSlot ---

    [Fact]
    public void GrantFeatSlot_NoRestriction_IncrementsPendingSlots()
    {
        var state = CreateState();
        new GrantFeatSlot().Apply(state);
        Assert.Equal(1, state.PendingFeatSlots);
    }

    [Fact]
    public void GrantFeatSlot_WithRestriction_IncrementsBonusSlots()
    {
        var state = CreateState();
        new GrantFeatSlot { Restriction = "fighter_bonus" }.Apply(state);
        Assert.Equal(1, state.PendingBonusFeatSlots);
        Assert.Equal(0, state.PendingFeatSlots);
    }

    // --- GrantImmunity ---

    [Fact]
    public void GrantImmunity_AddsToHashSet()
    {
        var state = CreateState();
        new GrantImmunity { Immunity = "electricity" }.Apply(state);
        new GrantImmunity { Immunity = "spell:poison" }.Apply(state);
        Assert.Contains("electricity", state.Immunities);
        Assert.Contains("spell:poison", state.Immunities);
    }

    [Fact]
    public void GrantImmunity_NoDuplicates()
    {
        var state = CreateState();
        new GrantImmunity { Immunity = "fire" }.Apply(state);
        new GrantImmunity { Immunity = "fire" }.Apply(state);
        Assert.Single(state.Immunities);
    }

    // --- GrantDR ---

    [Fact]
    public void GrantDR_AddsEntry()
    {
        var state = CreateState();
        new GrantDR { Value = 10, BypassedBy = "cold iron" }.Apply(state);
        Assert.Single(state.DamageReduction);
        Assert.Equal(10, state.DamageReduction[0].Value);
        Assert.Equal("cold iron", state.DamageReduction[0].BypassedBy);
    }

    [Fact]
    public void GrantDR_MultipleEntries_Stack()
    {
        var state = CreateState();
        new GrantDR { Value = 10, BypassedBy = "cold iron" }.Apply(state);
        new GrantDR { Value = 5, BypassedBy = "good" }.Apply(state);
        Assert.Equal(2, state.DamageReduction.Count);
    }

    [Fact]
    public void GrantDR_SameBypassConditionKeepsTheHigherValue()
    {
        var state = CreateState();
        new GrantDR { Value = 10, BypassedBy = "silver" }.Apply(state);
        new GrantDR { Value = 5, BypassedBy = "SILVER" }.Apply(state);
        new GrantDR { Value = 15, BypassedBy = "silver" }.Apply(state);

        var dr = Assert.Single(state.DamageReduction);
        Assert.Equal(15, dr.Value);
        Assert.Equal("silver", dr.BypassedBy);
    }

    // --- GrantSkillBonus ---

    [Fact]
    public void GrantSkillBonus_AddsBonus()
    {
        var state = CreateState();
        new GrantSkillBonus { SkillId = "skill:listen", Value = 8 }.Apply(state);
        Assert.Equal(8, state.SkillBonuses["skill:listen"]);
    }

    [Fact]
    public void GrantSkillBonus_StacksAdditively()
    {
        var state = CreateState();
        new GrantSkillBonus { SkillId = "skill:spot", Value = 4 }.Apply(state);
        new GrantSkillBonus { SkillId = "skill:spot", Value = 8 }.Apply(state);
        Assert.Equal(12, state.SkillBonuses["skill:spot"]);
    }

    // --- BAB/Save multiclass stacking ---

    [Fact]
    public void BABAndSaves_MulticlassStacking_Fighter2Rogue1()
    {
        var state = CreateState();
        // Fighter: Good BAB, Fort good, Ref poor, Will poor
        var fighterSaves = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Poor, Will = ProgressionRate.Poor };
        // Rogue: Average BAB, Fort poor, Ref good, Will poor
        var rogueSaves = new SaveProgression { Fort = ProgressionRate.Poor, Ref = ProgressionRate.Good, Will = ProgressionRate.Poor };

        // Fighter 1
        new AddBAB(BABProgression.Good, 1).Apply(state);
        new AddSaves(fighterSaves, 1).Apply(state);
        // Fighter 2
        new AddBAB(BABProgression.Good, 2).Apply(state);
        new AddSaves(fighterSaves, 2).Apply(state);
        // Rogue 1
        new AddBAB(BABProgression.Average, 1).Apply(state);
        new AddSaves(rogueSaves, 1).Apply(state);

        // BAB: Fighter 2 (+2) + Rogue 1 (floor(0.75)=0) = 2
        Assert.Equal(2, state.BaseBAB);
        // Fort: Fighter good 2 (2+2/2=3) + Rogue poor 1 (1/3=0) = 3
        Assert.Equal(3, state.BaseSaves.Fort);
        // Ref: Fighter poor 2 (2/3=0) + Rogue good 1 (2+1/2=2) = 2
        Assert.Equal(2, state.BaseSaves.Ref);
        // Will: Fighter poor 2 (2/3=0) + Rogue poor 1 (1/3=0) = 0
        Assert.Equal(0, state.BaseSaves.Will);
    }
}
