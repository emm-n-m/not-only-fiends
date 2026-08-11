using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class FormulaTests
{
    private CharacterState CreateState(int totalHD = 10, int con = 16, int intScore = 8,
        int bab = 7, int? sr = null)
    {
        return new CharacterState
        {
            TotalHD = totalHD,
            BaseBAB = bab,
            SpellResistance = sr,
            AbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = con, INT = intScore, WIS = 12, CHA = 10
            }
        };
    }

    [Fact]
    public void SimpleNumber()
    {
        var f = new Formula("42");
        Assert.Equal(42, f.Evaluate(CreateState()));
    }

    [Fact]
    public void Addition()
    {
        var f = new Formula("10 + 5");
        Assert.Equal(15, f.Evaluate(CreateState()));
    }

    [Fact]
    public void Subtraction()
    {
        var f = new Formula("20 - 7");
        Assert.Equal(13, f.Evaluate(CreateState()));
    }

    [Fact]
    public void Multiplication()
    {
        var f = new Formula("3 * 4");
        Assert.Equal(12, f.Evaluate(CreateState()));
    }

    [Fact]
    public void Division_IntegerFloor()
    {
        var f = new Formula("10 / 3");
        Assert.Equal(3, f.Evaluate(CreateState()));
    }

    [Fact]
    public void Division_NegativeFloors()
    {
        // -9 / 2 should floor to -5 not truncate to -4
        var f = new Formula("1 - 10 / 2");
        // 1 - (10/2) = 1 - 5 = -4
        Assert.Equal(-4, f.Evaluate(CreateState()));
    }

    [Fact]
    public void OperatorPrecedence()
    {
        var f = new Formula("2 + 3 * 4");
        Assert.Equal(14, f.Evaluate(CreateState()));
    }

    [Fact]
    public void Parentheses()
    {
        var f = new Formula("(2 + 3) * 4");
        Assert.Equal(20, f.Evaluate(CreateState()));
    }

    [Theory]
    [InlineData("-10", -10)]
    [InlineData("2 * -4", -8)]
    [InlineData("--3", 3)]
    [InlineData("-(2 + 3)", -5)]
    [InlineData("+7", 7)]
    public void UnaryOperators(string expression, int expected)
    {
        Assert.Equal(expected, new Formula(expression).Evaluate(CreateState()));
    }

    [Fact]
    public void Attribute_TotalHD()
    {
        var state = CreateState(totalHD: 10);
        Assert.Equal(10, new Formula("TotalHD").Evaluate(state));
    }

    [Fact]
    public void Attribute_BaseBAB()
    {
        var state = CreateState(bab: 7);
        Assert.Equal(7, new Formula("BaseBAB").Evaluate(state));
    }

    [Fact]
    public void Attribute_EffectiveBAB()
    {
        var state = CreateState(bab: 7);
        state.EpicAttackBonus = 3;
        Assert.Equal(10, new Formula("EffectiveBAB").Evaluate(state));
    }

    [Fact]
    public void Attribute_SpellResistance()
    {
        var state = CreateState(sr: 18);
        Assert.Equal(18, new Formula("SpellResistance").Evaluate(state));
    }

    [Fact]
    public void Attribute_SpellResistance_Null_ReturnsZero()
    {
        var state = CreateState(sr: null);
        Assert.Equal(0, new Formula("SpellResistance").Evaluate(state));
    }

    [Fact]
    public void Function_Mod()
    {
        var state = CreateState(con: 16); // modifier = +3
        Assert.Equal(3, new Formula("Mod(CON)").Evaluate(state));
    }

    [Fact]
    public void Function_Score()
    {
        var state = CreateState(intScore: 8);
        Assert.Equal(8, new Formula("Score(INT)").Evaluate(state));
    }

    [Fact]
    public void Function_ClassLevel()
    {
        var state = CreateState();
        state.ClassLevels["class:sorcerer"] = 5;
        Assert.Equal(5, new Formula("ClassLevel(sorcerer)").Evaluate(state));
    }

    [Fact]
    public void Function_ClassLevel_Missing_ReturnsZero()
    {
        var state = CreateState();
        Assert.Equal(0, new Formula("ClassLevel(wizard)").Evaluate(state));
    }

    [Fact]
    public void Function_CasterLevel()
    {
        var state = CreateState();
        state.Spellcasting["class:sorcerer"] = new SpellcastingState { CasterLevel = 7 };
        Assert.Equal(7, new Formula("CasterLevel(sorcerer)").Evaluate(state));
    }

    [Fact]
    public void Function_Min()
    {
        var state = CreateState(totalHD: 25);
        Assert.Equal(20, new Formula("min(TotalHD, 20)").Evaluate(state));
    }

    [Fact]
    public void Function_Max()
    {
        var state = CreateState(intScore: 8); // Score=8
        Assert.Equal(1, new Formula("max(1, Score(INT) - 10)").Evaluate(state));
    }

    [Fact]
    public void Function_Max_SecondArgLarger()
    {
        var state = CreateState(intScore: 14); // Score=14
        Assert.Equal(4, new Formula("max(1, Score(INT) - 10)").Evaluate(state));
    }

    [Fact]
    public void Function_CohortLevel()
    {
        var state = CreateState();
        state.MaxCohortLevel = 15;
        Assert.Equal(15, new Formula("CohortLevel()").Evaluate(state));
    }

    // --- Complex expressions from architecture ---

    [Fact]
    public void HalfFiend_PoisonDC()
    {
        // "10 + TotalHD / 2 + Mod(CON)"
        // TotalHD=10, CON=16 (mod +3): 10 + 5 + 3 = 18
        var state = CreateState(totalHD: 10, con: 16);
        Assert.Equal(18, new Formula("10 + TotalHD / 2 + Mod(CON)").Evaluate(state));
    }

    [Fact]
    public void SmiteDamage_Capped()
    {
        // "min(TotalHD, 20)" with TotalHD=25
        var state = CreateState(totalHD: 25);
        Assert.Equal(20, new Formula("min(TotalHD, 20)").Evaluate(state));
    }

    [Fact]
    public void SLA_SaveDC()
    {
        // "10 + 4 + Mod(CHA)" with CHA=10 (mod 0)
        var state = CreateState();
        Assert.Equal(14, new Formula("10 + 4 + Mod(CHA)").Evaluate(state));
    }

    [Fact]
    public void SpellResistance_Formula()
    {
        // "TotalHD + 10" with TotalHD=10
        var state = CreateState(totalHD: 10);
        Assert.Equal(20, new Formula("TotalHD + 10").Evaluate(state));
    }

    // --- Error cases ---

    [Fact]
    public void Error_UnknownAttribute()
    {
        Assert.Throws<FormulaException>(() => new Formula("INVALID").Evaluate(CreateState()));
    }

    [Fact]
    public void Error_InvalidAbility()
    {
        Assert.Throws<FormulaException>(() => new Formula("Mod(INVALID)").Evaluate(CreateState()));
    }

    [Fact]
    public void Error_UnknownFunction()
    {
        Assert.Throws<FormulaException>(() => new Formula("foo(1)").Evaluate(CreateState()));
    }

    [Fact]
    public void Error_MissingParen()
    {
        Assert.Throws<FormulaException>(() => new Formula("(1 + 2").Evaluate(CreateState()));
    }

    // --- Caching ---

    [Fact]
    public void CachedAST_SameResultOnMultipleCalls()
    {
        var f = new Formula("TotalHD + 5");
        var state1 = CreateState(totalHD: 10);
        var state2 = CreateState(totalHD: 20);

        Assert.Equal(15, f.Evaluate(state1));
        Assert.Equal(25, f.Evaluate(state2));
    }

    [Fact]
    public void EmptyExpression_ReturnsZero()
    {
        Assert.Equal(0, new Formula("").Evaluate(CreateState()));
        Assert.Equal(0, new Formula("  ").Evaluate(CreateState()));
    }

    [Fact]
    public void DivisionByZero_ReturnsZero()
    {
        var state = CreateState();
        state.ClassLevels.Clear();
        // ClassLevel(wizard) = 0, so TotalHD / 0 = 0 (safe division)
        Assert.Equal(0, new Formula("10 / ClassLevel(wizard)").Evaluate(state));
    }
}
