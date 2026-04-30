using System.Text.Json.Serialization;

namespace NotOnlyFiendsStudio.Models;

public class Formula
{
    public string Expression { get; set; } = string.Empty;

    [JsonIgnore]
    private FormulaNode? _cachedAST;

    public Formula() { }
    public Formula(string expression) => Expression = expression;

    public int Evaluate(CharacterState state)
    {
        if (string.IsNullOrWhiteSpace(Expression))
            return 0;

        _cachedAST ??= FormulaParser.Parse(Expression);
        return _cachedAST.Evaluate(state);
    }
}

// --- AST Nodes ---

internal abstract class FormulaNode
{
    public abstract int Evaluate(CharacterState state);
}

internal class NumberNode : FormulaNode
{
    public int Value { get; }
    public NumberNode(int value) => Value = value;
    public override int Evaluate(CharacterState state) => Value;
}

internal class AttributeNode : FormulaNode
{
    public string Name { get; }
    public AttributeNode(string name) => Name = name;

    public override int Evaluate(CharacterState state) => Name switch
    {
        "TotalHD" => state.TotalHD,
        "BaseBAB" => state.BaseBAB,
        "EffectiveBAB" => state.EffectiveBAB,
        "SpellResistance" => state.SpellResistance ?? 0,
        "MasterLevel" => state.EffectiveMasterLevel,
        "LeadershipScore" => state.LeadershipScore,
        _ => throw new FormulaException($"Unknown attribute: {Name}")
    };
}

internal class BinaryOpNode : FormulaNode
{
    public FormulaNode Left { get; }
    public FormulaNode Right { get; }
    public char Op { get; }

    public BinaryOpNode(FormulaNode left, char op, FormulaNode right)
    {
        Left = left;
        Op = op;
        Right = right;
    }

    public override int Evaluate(CharacterState state)
    {
        int l = Left.Evaluate(state);
        int r = Right.Evaluate(state);
        return Op switch
        {
            '+' => l + r,
            '-' => l - r,
            '*' => l * r,
            '/' => r == 0 ? 0 : FloorDiv(l, r),
            _ => throw new FormulaException($"Unknown operator: {Op}")
        };
    }

    private static int FloorDiv(int a, int b)
    {
        int result = a / b;
        // Adjust for floor division with negative results
        if ((a ^ b) < 0 && result * b != a)
            result--;
        return result;
    }
}

internal class FunctionNode : FormulaNode
{
    public string Name { get; }
    public List<FormulaNode> Args { get; }

    public FunctionNode(string name, List<FormulaNode> args)
    {
        Name = name;
        Args = args;
    }

    public override int Evaluate(CharacterState state)
    {
        switch (Name)
        {
            case "Mod":
                if (Args.Count != 1 || Args[0] is not AbilityRefNode abilityMod)
                    throw new FormulaException("Mod() requires exactly one ability argument");
                return AbilityScoreSet.Modifier(state.AbilityScores.GetScore(abilityMod.Ability));

            case "Score":
                if (Args.Count != 1 || Args[0] is not AbilityRefNode abilityScore)
                    throw new FormulaException("Score() requires exactly one ability argument");
                return state.AbilityScores.GetScore(abilityScore.Ability);

            case "ClassLevel":
                if (Args.Count != 1 || Args[0] is not IdentifierNode classId)
                    throw new FormulaException("ClassLevel() requires exactly one class ID argument");
                return state.ClassLevels.GetValueOrDefault($"class:{classId.Name}");

            case "CasterLevel":
                if (Args.Count != 1 || Args[0] is not IdentifierNode casterId)
                    throw new FormulaException("CasterLevel() requires exactly one class ID argument");
                return state.Spellcasting.TryGetValue($"class:{casterId.Name}", out var sc)
                    ? sc.CasterLevel : 0;

            case "RacialHD":
                if (Args.Count != 0)
                    throw new FormulaException("RacialHD() takes no arguments");
                return state.HDList.Count(id => id.StartsWith("racial_hd:"));

            case "min":
                if (Args.Count != 2)
                    throw new FormulaException("min() requires exactly two arguments");
                return Math.Min(Args[0].Evaluate(state), Args[1].Evaluate(state));

            case "max":
                if (Args.Count != 2)
                    throw new FormulaException("max() requires exactly two arguments");
                return Math.Max(Args[0].Evaluate(state), Args[1].Evaluate(state));

            default:
                throw new FormulaException($"Unknown function: {Name}");
        }
    }
}

internal class AbilityRefNode : FormulaNode
{
    public Ability Ability { get; }
    public AbilityRefNode(Ability ability) => Ability = ability;
    public override int Evaluate(CharacterState state) =>
        state.AbilityScores.GetScore(Ability);
}

internal class IdentifierNode : FormulaNode
{
    public string Name { get; }
    public IdentifierNode(string name) => Name = name;
    public override int Evaluate(CharacterState state) =>
        throw new FormulaException($"Unresolved identifier: {Name}");
}

// --- Parser ---

internal static class FormulaParser
{
    public static FormulaNode Parse(string expression)
    {
        var tokens = Tokenize(expression);
        var pos = 0;
        var result = ParseExpr(tokens, ref pos);
        if (pos < tokens.Count)
            throw new FormulaException($"Unexpected token at position {pos}: {tokens[pos]}");
        return result;
    }

    private static List<string> Tokenize(string expr)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < expr.Length)
        {
            if (char.IsWhiteSpace(expr[i]))
            {
                i++;
                continue;
            }

            if (char.IsDigit(expr[i]))
            {
                int start = i;
                while (i < expr.Length && char.IsDigit(expr[i]))
                    i++;
                tokens.Add(expr[start..i]);
                continue;
            }

            if (char.IsLetter(expr[i]) || expr[i] == '_')
            {
                int start = i;
                while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_'))
                    i++;
                tokens.Add(expr[start..i]);
                continue;
            }

            if ("+-*/(),".Contains(expr[i]))
            {
                tokens.Add(expr[i].ToString());
                i++;
                continue;
            }

            throw new FormulaException($"Unexpected character: '{expr[i]}' at position {i}");
        }
        return tokens;
    }

    private static FormulaNode ParseExpr(List<string> tokens, ref int pos)
    {
        var left = ParseTerm(tokens, ref pos);
        while (pos < tokens.Count && (tokens[pos] == "+" || tokens[pos] == "-"))
        {
            var op = tokens[pos][0];
            pos++;
            var right = ParseTerm(tokens, ref pos);
            left = new BinaryOpNode(left, op, right);
        }
        return left;
    }

    private static FormulaNode ParseTerm(List<string> tokens, ref int pos)
    {
        var left = ParseFactor(tokens, ref pos);
        while (pos < tokens.Count && (tokens[pos] == "*" || tokens[pos] == "/"))
        {
            var op = tokens[pos][0];
            pos++;
            var right = ParseFactor(tokens, ref pos);
            left = new BinaryOpNode(left, op, right);
        }
        return left;
    }

    private static FormulaNode ParseFactor(List<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count)
            throw new FormulaException("Unexpected end of expression");

        var token = tokens[pos];

        // Number
        if (int.TryParse(token, out var num))
        {
            pos++;
            return new NumberNode(num);
        }

        // Parenthesized expression
        if (token == "(")
        {
            pos++;
            var inner = ParseExpr(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos] != ")")
                throw new FormulaException("Missing closing parenthesis");
            pos++;
            return inner;
        }

        // Identifier or function
        if (char.IsLetter(token[0]) || token[0] == '_')
        {
            pos++;

            // Check if it's a function call
            if (pos < tokens.Count && tokens[pos] == "(")
            {
                pos++; // consume '('
                var args = new List<FormulaNode>();

                if (pos < tokens.Count && tokens[pos] != ")")
                {
                    args.Add(ParseArgument(token, tokens, ref pos));
                    while (pos < tokens.Count && tokens[pos] == ",")
                    {
                        pos++; // consume ','
                        args.Add(ParseArgument(token, tokens, ref pos));
                    }
                }

                if (pos >= tokens.Count || tokens[pos] != ")")
                    throw new FormulaException($"Missing closing parenthesis for function {token}");
                pos++; // consume ')'

                return new FunctionNode(token, args);
            }

            // It's an attribute
            return ResolveIdentifier(token);
        }

        throw new FormulaException($"Unexpected token: {token}");
    }

    private static FormulaNode ParseArgument(string funcName, List<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count)
            throw new FormulaException($"Expected argument for function {funcName}");

        // For Mod/Score: argument is an ability name
        if (funcName is "Mod" or "Score")
        {
            var argToken = tokens[pos];
            if (Enum.TryParse<Ability>(argToken, true, out var ability))
            {
                pos++;
                return new AbilityRefNode(ability);
            }
            throw new FormulaException($"Invalid ability name for {funcName}: {argToken}");
        }

        // For ClassLevel/CasterLevel: argument is an identifier
        if (funcName is "ClassLevel" or "CasterLevel")
        {
            var argToken = tokens[pos];
            if (char.IsLetter(argToken[0]))
            {
                pos++;
                return new IdentifierNode(argToken);
            }
            throw new FormulaException($"Expected class ID for {funcName}, got: {argToken}");
        }

        // For min/max: arguments are expressions
        return ParseExpr(tokens, ref pos);
    }

    private static FormulaNode ResolveIdentifier(string name)
    {
        // Check if it's a known attribute
        return name switch
        {
            "TotalHD" or "BaseBAB" or "EffectiveBAB" or "SpellResistance"
                or "MasterLevel" or "LeadershipScore" => new AttributeNode(name),
            _ => throw new FormulaException($"Unknown identifier: {name}")
        };
    }
}

public class FormulaException : Exception
{
    public FormulaException(string message) : base(message) { }
}
