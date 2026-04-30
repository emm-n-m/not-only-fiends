using System.Text;
using System.Text.RegularExpressions;

namespace NotOnlyFiendsStudio.PcGen;

public static class PcgParser
{
    private static readonly HashSet<string> InternalTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "Base Race Type",
        "Base Race Type ~ Humanoid",
        "Base Race Type ~ Outsider",
        "Base Race Type ~ Fey",
        "Base Race Type ~ Elemental",
        "Base Race Type ~ Magical Beast",
        "Base Race Type ~ Monstrous Humanoid",
        "Base Race Type ~ Undead",
        "Base Race Type ~ Animal",
        "Base Race Type ~ Dragon",
        "Base Race Type ~ Aberration",
        "Human Base",
        "Familiar Race Change",
        "Non-Animal Base",
        "Animal Base",
        "RighteousMightDR",
        "Unable to use Irresistible Dance",
        "Cleric ~ Bonus Languages",
        "Quarter Hitdie",
        "Half Hitdie",
    };

    /// <summary>
    /// Parse a .pcg file from disk (reads as Latin1 encoding).
    /// </summary>
    public static PcgCharacterData Parse(string filePath)
    {
        var lines = File.ReadAllLines(filePath, Encoding.Latin1);
        var data = ParseLines(lines);
        data.FileName = Path.GetFileName(filePath);
        return data;
    }

    /// <summary>
    /// Parse .pcg content from a string (for WASM where file paths aren't available).
    /// </summary>
    public static PcgCharacterData ParseText(string content, string fileName = "imported.pcg")
    {
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var data = ParseLines(lines);
        data.FileName = fileName;
        return data;
    }

    private static PcgCharacterData ParseLines(string[] lines)
    {
        var data = new PcgCharacterData();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            if (line.StartsWith("CHARACTERNAME:"))
                data.CharacterName = line["CHARACTERNAME:".Length..];
            else if (line.StartsWith("RACE:"))
                ParseRace(line, data);
            else if (line.StartsWith("ALIGN:"))
                data.Alignment = line["ALIGN:".Length..];
            else if (line.StartsWith("STAT:"))
                ParseStat(line, data);
            else if (line.StartsWith("CLASS:"))
                ParseClass(line, data);
            else if (line.StartsWith("CLASSABILITIESLEVEL:"))
                ParseLevel(line, data);
            else if (line.StartsWith("SKILL:"))
                ParseSkill(line, data);
            else if (line.StartsWith("ABILITY:FEAT|"))
                ParseFeat(line, data);
            else if (line.StartsWith("TEMPLATESAPPLIED:"))
                ParseTemplate(line, data);
            else if (line.StartsWith("SPELLNAME:"))
                ParseSpell(line, data);
            else if (line.StartsWith("DOMAIN:") && !line.StartsWith("DOMAIN:Air|DOMAIN:") /* skip DEITY domain list */)
                ParseDomain(line, data);
        }

        return data;
    }

    private static void ParseRace(string line, PcgCharacterData data)
    {
        var value = line["RACE:".Length..];
        var pipeIdx = value.IndexOf('|');
        data.Race = pipeIdx >= 0 ? value[..pipeIdx] : value;
    }

    private static void ParseStat(string line, PcgCharacterData data)
    {
        var fields = ParseFields(line);
        if (fields.TryGetValue("STAT", out var stat) && fields.TryGetValue("SCORE", out var scoreStr))
        {
            if (int.TryParse(scoreStr, out var score))
                data.BaseStats[stat] = score;
        }
    }

    private static void ParseClass(string line, PcgCharacterData data)
    {
        var fields = ParseFields(line);
        if (!fields.TryGetValue("CLASS", out var name)) return;
        if (!fields.TryGetValue("LEVEL", out var levelStr)) return;
        if (!int.TryParse(levelStr, out var level)) return;

        if (level == 0) return;

        var entry = new PcgClassEntry
        {
            Name = name,
            Level = level,
            Subclass = fields.GetValueOrDefault("SUBCLASS"),
            SpellBase = fields.GetValueOrDefault("SPELLBASE"),
        };
        data.Classes.Add(entry);
    }

    private static void ParseLevel(string line, PcgCharacterData data)
    {
        var normalized = line.Replace("&pipe;", "\x01");
        var fields = ParseFields(normalized);

        if (!fields.TryGetValue("CLASSABILITIESLEVEL", out var classLevel)) return;

        var eqIdx = classLevel.IndexOf('=');
        if (eqIdx < 0) return;

        var className = classLevel[..eqIdx];
        if (!int.TryParse(classLevel[(eqIdx + 1)..], out var lvl)) return;

        var entry = new PcgLevelEntry
        {
            ClassName = className,
            ClassLevel = lvl,
        };

        if (fields.TryGetValue("HITPOINTS", out var hpStr) && int.TryParse(hpStr, out var hp))
            entry.HitPoints = hp;

        if (fields.TryGetValue("SKILLSGAINED", out var sgStr) && int.TryParse(sgStr, out var sg))
            entry.SkillsGained = sg;

        if (fields.TryGetValue("PRESTAT", out var preStat))
        {
            var statEq = preStat.IndexOf('=');
            if (statEq >= 0)
                entry.AbilityIncrease = preStat[..statEq];
        }

        data.Levels.Add(entry);
    }

    private static void ParseSkill(string line, PcgCharacterData data)
    {
        // A single SKILL: line may contain multiple CLASSBOUGHT:[...] brackets when a skill
        // was bought from several classes/racial HD (e.g. 1 rank Sorcerer + 6 ranks Magical Beast).
        // Each bracket becomes a separate PcgSkillEntry so the converter can place the ranks on
        // the matching engine tick.
        var nameStart = "SKILL:".Length;
        var nameEnd = line.IndexOf('|', nameStart);
        if (nameEnd < 0) return;
        var skillName = line[nameStart..nameEnd];

        const string Marker = "CLASSBOUGHT:[";
        var searchFrom = 0;
        while (true)
        {
            var cbIdx = line.IndexOf(Marker, searchFrom, StringComparison.Ordinal);
            if (cbIdx < 0) return;

            var bracketStart = cbIdx + Marker.Length;
            var bracketEnd = line.IndexOf(']', bracketStart);
            if (bracketEnd < 0) return;

            var bracketContent = line[bracketStart..bracketEnd];
            var cbFields = ParseFields(bracketContent);

            if (cbFields.TryGetValue("RANKS", out var ranksStr)
                && double.TryParse(ranksStr, System.Globalization.CultureInfo.InvariantCulture, out var ranks)
                && ranks > 0)
            {
                data.Skills.Add(new PcgSkillEntry
                {
                    Name = skillName,
                    Ranks = ranks,
                    BoughtClass = cbFields.GetValueOrDefault("CLASS"),
                });
            }

            searchFrom = bracketEnd + 1;
        }
    }

    private static void ParseFeat(string line, PcgCharacterData data)
    {
        var fields = ParseFields(line);
        if (!fields.TryGetValue("KEY", out var key)) return;

        if (fields.TryGetValue("CATEGORY", out var cat) && !cat.Equals("FEAT", StringComparison.OrdinalIgnoreCase))
            return;

        var entry = new PcgFeatEntry
        {
            Key = key,
            AppliedTo = fields.GetValueOrDefault("APPLIEDTO"),
        };

        if (fields.TryGetValue("TYPE", out var typeStr))
        {
            var typeFields = line.Split('|')
                .Where(f => f.StartsWith("TYPE:"))
                .Select(f => f["TYPE:".Length..])
                .ToList();

            var featTypes = typeFields.LastOrDefault() ?? "";
            entry.Types = featTypes.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        data.Feats.Add(entry);
    }

    private static void ParseTemplate(string line, PcgCharacterData data)
    {
        var match = Regex.Match(line, @"TEMPLATESAPPLIED:\[NAME:([^\]|]+)");
        if (!match.Success) return;

        var name = match.Groups[1].Value;
        var isInternal = InternalTemplates.Contains(name) ||
                         name.StartsWith("Base Race Type", StringComparison.OrdinalIgnoreCase);

        data.Templates.Add(new PcgTemplateEntry
        {
            Name = name,
            IsInternal = isInternal,
        });
    }

    private static void ParseSpell(string line, PcgCharacterData data)
    {
        var fields = ParseFields(line);
        if (!fields.TryGetValue("SPELLNAME", out var name)) return;

        var entry = new PcgSpellEntry
        {
            Name = name,
            ClassName = fields.GetValueOrDefault("CLASS") ?? "",
            Book = fields.GetValueOrDefault("BOOK") ?? "",
        };

        if (fields.TryGetValue("SPELLLEVEL", out var slStr) && int.TryParse(slStr, out var sl))
            entry.SpellLevel = sl;

        data.Spells.Add(entry);
    }

    private static void ParseDomain(string line, PcgCharacterData data)
    {
        var fields = ParseFields(line);
        if (!fields.TryGetValue("DOMAIN", out var name)) return;

        var sourceClass = "";
        var sourceIdx = line.IndexOf("SOURCE:[");
        if (sourceIdx >= 0)
        {
            var srcBracketStart = sourceIdx + "SOURCE:[".Length;
            var srcBracketEnd = line.IndexOf(']', srcBracketStart);
            if (srcBracketEnd >= 0)
            {
                var srcContent = line[srcBracketStart..srcBracketEnd];
                var srcFields = ParseFields(srcContent);
                sourceClass = srcFields.GetValueOrDefault("NAME") ?? "";
            }
        }

        data.Domains.Add(new PcgDomainEntry
        {
            Name = name,
            SourceClass = sourceClass,
        });
    }

    private static Dictionary<string, string> ParseFields(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = line.Split('|');

        foreach (var part in parts)
        {
            var colonIdx = part.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key = part[..colonIdx];
            var value = part[(colonIdx + 1)..];
            result[key] = value;
        }

        return result;
    }
}
