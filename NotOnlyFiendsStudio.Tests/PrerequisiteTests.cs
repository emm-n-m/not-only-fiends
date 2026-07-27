using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class PrerequisiteTests
{
    private CharacterState CreateState()
    {
        return new CharacterState
        {
            RaceId = "race:human",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
            Alignment = Alignment.NG,
            TotalHD = 5,
            BaseBAB = 5,
            AbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            ClassLevels = new Dictionary<string, int> { { "class:fighter", 5 } },
            Feats = new List<string> { "feat:power_attack", "feat:cleave" },
            SkillHalfRanks = new Dictionary<string, int> { { "skill:intimidate", 10 } }, // 5 ranks in half-rank system
        };
    }

    [Fact]
    public void MinBAB_Met()
    {
        var state = CreateState();
        Assert.True(new MinBAB { Value = 5 }.IsMet(state));
        Assert.True(new MinBAB { Value = 1 }.IsMet(state));
    }

    [Fact]
    public void MinBAB_NotMet()
    {
        var state = CreateState();
        Assert.False(new MinBAB { Value = 6 }.IsMet(state));
    }

    [Fact]
    public void MinAbility_Met()
    {
        var state = CreateState();
        Assert.True(new MinAbility { Ability = Ability.STR, Value = 13 }.IsMet(state));
        Assert.True(new MinAbility { Ability = Ability.STR, Value = 16 }.IsMet(state));
    }

    [Fact]
    public void MinAbility_NotMet()
    {
        var state = CreateState();
        Assert.False(new MinAbility { Ability = Ability.STR, Value = 17 }.IsMet(state));
    }

    [Fact]
    public void MinSkillRanks_Met()
    {
        var state = CreateState();
        // state has 10 half-ranks = 5 ranks in intimidate
        Assert.True(new MinSkillRanks { SkillId = "skill:intimidate", Value = 5 }.IsMet(state));
        Assert.True(new MinSkillRanks { SkillId = "skill:intimidate", Value = 1 }.IsMet(state));
    }

    [Fact]
    public void MinSkillRanks_NotMet()
    {
        var state = CreateState();
        Assert.False(new MinSkillRanks { SkillId = "skill:intimidate", Value = 6 }.IsMet(state));
    }

    [Fact]
    public void MinSkillRanks_HalfRankBoundary()
    {
        // 9 half-ranks = 4.5 ranks, should not meet "5 ranks required"
        var state = CreateState();
        state.SkillHalfRanks["skill:intimidate"] = 9;
        Assert.False(new MinSkillRanks { SkillId = "skill:intimidate", Value = 5 }.IsMet(state));
    }

    [Fact]
    public void MinSkillRanks_MissingSkill()
    {
        var state = CreateState();
        Assert.False(new MinSkillRanks { SkillId = "skill:tumble", Value = 1 }.IsMet(state));
    }

    [Fact]
    public void MinClassLevel_Met()
    {
        var state = CreateState();
        Assert.True(new MinClassLevel { ClassId = "class:fighter", Value = 5 }.IsMet(state));
    }

    [Fact]
    public void MinClassLevel_NotMet()
    {
        var state = CreateState();
        Assert.False(new MinClassLevel { ClassId = "class:fighter", Value = 6 }.IsMet(state));
        Assert.False(new MinClassLevel { ClassId = "class:wizard", Value = 1 }.IsMet(state));
    }

    [Fact]
    public void HasFeat_Met()
    {
        var state = CreateState();
        Assert.True(new HasFeat { FeatId = "feat:power_attack" }.IsMet(state));
        Assert.True(new HasFeat { FeatId = "feat:cleave" }.IsMet(state));
    }

    [Fact]
    public void HasFeat_NotMet()
    {
        var state = CreateState();
        Assert.False(new HasFeat { FeatId = "feat:great_cleave" }.IsMet(state));
    }

    [Fact]
    public void HasFeat_MatchesSelectionVariant()
    {
        // Greater Spell Focus requires "feat:spell_focus" — selection stores as "spell_focus_evocation".
        var state = CreateState();
        state.Feats.Add("feat:spell_focus_evocation");
        Assert.True(new HasFeat { FeatId = "feat:spell_focus" }.IsMet(state));
    }

    [Fact]
    public void HasFeat_DoesNotFalsePositiveOnUnrelatedPrefix()
    {
        // "feat:cleave" must not match "feat:great_cleave" — prefix check uses "cleave_" separator.
        var state = new CharacterState { Feats = new List<string> { "feat:great_cleave" } };
        Assert.False(new HasFeat { FeatId = "feat:cleave" }.IsMet(state));
    }

    [Fact]
    public void AlignmentReq_Met()
    {
        var state = CreateState();
        Assert.True(new AlignmentReq { Allowed = new HashSet<Alignment> { Alignment.NG, Alignment.CG } }.IsMet(state));
    }

    [Fact]
    public void AlignmentReq_NotMet()
    {
        var state = CreateState();
        Assert.False(new AlignmentReq { Allowed = new HashSet<Alignment> { Alignment.CE, Alignment.NE } }.IsMet(state));
    }

    [Fact]
    public void MinHD_Met()
    {
        var state = CreateState();
        Assert.True(new MinHD { Value = 5 }.IsMet(state));
        Assert.True(new MinHD { Value = 1 }.IsMet(state));
    }

    [Fact]
    public void MinHD_NotMet()
    {
        var state = CreateState();
        Assert.False(new MinHD { Value = 6 }.IsMet(state));
    }

    [Fact]
    public void HasRace_Met()
    {
        var state = CreateState();
        Assert.True(new HasRace { RaceId = "race:human" }.IsMet(state));
    }

    [Fact]
    public void HasRace_NotMet()
    {
        var state = CreateState();
        Assert.False(new HasRace { RaceId = "race:elf" }.IsMet(state));
    }

    [Fact]
    public void MinCasterLevel_Met()
    {
        var state = CreateState();
        state.Spellcasting["class:sorcerer"] = new SpellcastingState { CasterLevel = 5 };
        Assert.True(new MinCasterLevel { Value = 5 }.IsMet(state));
    }

    [Fact]
    public void MinCasterLevel_NotMet_NoCasting()
    {
        var state = CreateState();
        Assert.False(new MinCasterLevel { Value = 1 }.IsMet(state));
    }

    [Fact]
    public void CanCastSpellLevel_Met()
    {
        var state = CreateState();
        state.Spellcasting["class:sorcerer"] = new SpellcastingState { MaxSpellLevel = 3 };
        Assert.True(new CanCastSpellLevel { SpellLevel = 3 }.IsMet(state));
    }

    [Fact]
    public void CanCastSpellLevel_NotMet()
    {
        var state = CreateState();
        state.Spellcasting["class:sorcerer"] = new SpellcastingState { MaxSpellLevel = 3 };
        Assert.False(new CanCastSpellLevel { SpellLevel = 4 }.IsMet(state));
    }

    [Fact]
    public void MinSave_Met()
    {
        var state = CreateState();
        state.BaseSaves = new SaveSet { Fort = 4, Ref = 1, Will = 1 };
        Assert.True(new MinSave { Save = "fort", Value = 4 }.IsMet(state));
    }

    [Fact]
    public void MinSave_NotMet()
    {
        var state = CreateState();
        state.BaseSaves = new SaveSet { Fort = 4, Ref = 1, Will = 1 };
        Assert.False(new MinSave { Save = "will", Value = 5 }.IsMet(state));
    }

    [Fact]
    public void HasAbility_Met()
    {
        var state = CreateState();
        state.Abilities.Add(new GrantedAbility { Id = "sneak_attack", Name = "Sneak Attack" });
        Assert.True(new HasAbility { AbilityId = "sneak_attack" }.IsMet(state));
    }

    [Fact]
    public void HasAbility_NotMet()
    {
        var state = CreateState();
        Assert.False(new HasAbility { AbilityId = "sneak_attack" }.IsMet(state));
    }

    [Fact]
    public void HasSpellcasting_Met()
    {
        var state = CreateState();
        state.Spellcasting["class:wizard"] = new SpellcastingState
        {
            CastingType = CastingType.Arcane, MaxSpellLevel = 3
        };
        Assert.True(new HasSpellcasting().IsMet(state));
        Assert.True(new HasSpellcasting { CastingType = CastingType.Arcane }.IsMet(state));
    }

    [Fact]
    public void HasSpellcasting_NotMet()
    {
        var state = CreateState();
        Assert.False(new HasSpellcasting().IsMet(state));

        state.Spellcasting["class:wizard"] = new SpellcastingState
        {
            CastingType = CastingType.Arcane, MaxSpellLevel = 3
        };
        Assert.False(new HasSpellcasting { CastingType = CastingType.Divine }.IsMet(state));
    }

    [Fact]
    public void HasFeatOfType_Met()
    {
        var state = CreateState();
        state.FeatTypeCounts[FeatType.ItemCreation] = 2;
        Assert.True(new HasFeatOfType { FeatType = FeatType.ItemCreation, MinCount = 2 }.IsMet(state));
        Assert.True(new HasFeatOfType { FeatType = FeatType.ItemCreation, MinCount = 1 }.IsMet(state));
    }

    [Fact]
    public void HasFeatOfType_NotMet()
    {
        var state = CreateState();
        state.FeatTypeCounts[FeatType.ItemCreation] = 1;
        Assert.False(new HasFeatOfType { FeatType = FeatType.ItemCreation, MinCount = 2 }.IsMet(state));
        Assert.False(new HasFeatOfType { FeatType = FeatType.Metamagic, MinCount = 1 }.IsMet(state));
    }

    [Fact]
    public void HasFeatWithTag_Met()
    {
        var state = CreateState();
        state.FeatTagCounts["abyssal_heritor"] = 2;
        Assert.True(new HasFeatWithTag { Tag = "abyssal_heritor", MinCount = 1 }.IsMet(state));
        Assert.True(new HasFeatWithTag { Tag = "abyssal_heritor", MinCount = 2 }.IsMet(state));
    }

    [Fact]
    public void HasFeatWithTag_NotMet()
    {
        var state = CreateState();
        Assert.False(new HasFeatWithTag { Tag = "abyssal_heritor", MinCount = 1 }.IsMet(state));

        state.FeatTagCounts["abyssal_heritor"] = 1;
        Assert.False(new HasFeatWithTag { Tag = "abyssal_heritor", MinCount = 2 }.IsMet(state));
    }

    [Fact]
    public void HasAnyRace_Met()
    {
        var state = CreateState();
        state.RaceId = "race:elf";
        Assert.True(new HasAnyRace { RaceIds = new List<string> { "race:elf", "race:half_elf" } }.IsMet(state));

        state.RaceId = "race:half_elf";
        Assert.True(new HasAnyRace { RaceIds = new List<string> { "race:elf", "race:half_elf" } }.IsMet(state));
    }

    [Fact]
    public void HasAnyRace_NotMet()
    {
        var state = CreateState();
        state.RaceId = "race:dwarf";
        Assert.False(new HasAnyRace { RaceIds = new List<string> { "race:elf", "race:half_elf" } }.IsMet(state));
    }

    [Fact]
    public void LacksTemplate_Met()
    {
        var state = CreateState();
        Assert.True(new LacksTemplate { TemplateId = "half_dragon" }.IsMet(state));
    }

    [Fact]
    public void LacksTemplate_NotMet()
    {
        var state = CreateState();
        state.TemplateIds.Add("half_dragon");
        Assert.False(new LacksTemplate { TemplateId = "half_dragon" }.IsMet(state));
    }

    [Fact]
    public void HasLanguage_Met()
    {
        var state = CreateState();
        state.Languages.Add("draconic");
        Assert.True(new HasLanguage { LanguageId = "draconic" }.IsMet(state));
    }

    [Fact]
    public void HasLanguage_NotMet()
    {
        var state = CreateState();
        Assert.False(new HasLanguage { LanguageId = "draconic" }.IsMet(state));
    }

    [Fact]
    public void MinCounter_Met()
    {
        var state = CreateState();
        state.Counters["sneak_attack_dice"] = 2;
        Assert.True(new MinCounter { CounterId = "sneak_attack_dice", Value = 2 }.IsMet(state));
    }

    [Fact]
    public void MinCounter_NotMet()
    {
        var state = CreateState();
        state.Counters["sneak_attack_dice"] = 1;
        Assert.False(new MinCounter { CounterId = "sneak_attack_dice", Value = 2 }.IsMet(state));
    }
}
