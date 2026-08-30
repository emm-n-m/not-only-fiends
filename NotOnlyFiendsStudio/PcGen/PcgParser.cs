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
    /// Parse a .pcg file from disk. PCGen 6.08+ writes UTF-8; a strict decode failure falls back
    /// to Latin1 for older character archives.
    /// </summary>
    public static PcgCharacterData Parse(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        string content;
        try
        {
            content = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            content = Encoding.Latin1.GetString(bytes);
        }
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
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

        // Equipment needs deferred resolution: CALCEQUIPSET appears after the EQUIPSET lines,
        // which in turn appear after the EQUIPNAME lines. Buffer the slot assignments and
        // join them against the items + active-set ID at end-of-file.
        var slotAssignments = new List<(string SetId, string Slot, string ItemName)>();
        string? activeSetId = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            if (line.StartsWith("CHARACTERNAME:"))
                data.CharacterName = Decode(line["CHARACTERNAME:".Length..]);
            else if (line.StartsWith("RACE:"))
                ParseRace(line, data);
            else if (line.StartsWith("ALIGN:"))
                data.Alignment = Decode(line["ALIGN:".Length..]);
            else if (line.StartsWith("DEITY:"))
                // First pipe segment only — the rest is domain/weapon/alignment noise.
                data.Deity = Decode(line["DEITY:".Length..].Split('|')[0].Trim());
            else if (line.StartsWith("GENDER:"))
                data.Gender = Decode(line["GENDER:".Length..].Split('|')[0].Trim());
            else if (line.StartsWith("STAT:"))
                ParseStat(line, data);
            else if (line.StartsWith("CLASS:"))
                ParseClass(line, data);
            else if (line.StartsWith("CLASSABILITIESLEVEL:"))
                ParseLevel(line, data);
            else if (line.StartsWith("SKILL:"))
                ParseSkill(line, data);
            else if (line.StartsWith("ABILITY:"))
                ParseAbility(line, data);
            else if (line.StartsWith("TEMPLATESAPPLIED:"))
                ParseTemplate(line, data);
            else if (line.StartsWith("SPELLNAME:"))
                ParseSpell(line, data);
            else if (line.StartsWith("DOMAIN:") && !line.StartsWith("DOMAIN:Air|DOMAIN:") /* skip DEITY domain list */)
                ParseDomain(line, data);
            else if (line.StartsWith("LANGUAGE:"))
                ParseLanguages(line, data);
            else if (line.StartsWith("EQUIPNAME:"))
                ParseEquipmentItem(line, data);
            else if (line.StartsWith("EQUIPSET:"))
                ParseEquipmentSet(line, slotAssignments);
            else if (line.StartsWith("CALCEQUIPSET:"))
                activeSetId = line["CALCEQUIPSET:".Length..].Trim();
            else if (line.StartsWith("FOLLOWER:"))
                ParseFollower(line, data);
            else if (line.StartsWith("MASTER:"))
                ParseMaster(line, data);
            else if (line.StartsWith("TEMPBONUS:"))
                data.TemporaryBonuses.Add(line["TEMPBONUS:".Length..]);
        }

        ResolveEquipmentSlots(data, slotAssignments, activeSetId);

        return data;
    }

    private static void ParseRace(string line, PcgCharacterData data)
    {
        var value = line["RACE:".Length..];
        var pipeIdx = value.IndexOf('|');
        data.Race = Decode(pipeIdx >= 0 ? value[..pipeIdx] : value);
    }

    /// <summary>
    /// PCGen writes every language on one pipe-delimited line, repeating the tag:
    /// <c>LANGUAGE:Abyssal|LANGUAGE:Auran|LANGUAGE:Common|…</c>. Splitting on the pipe and
    /// stripping each tag handles both that and the degenerate single-language form.
    /// </summary>
    private static void ParseLanguages(string line, PcgCharacterData data)
    {
        foreach (var field in line.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = field.Trim();
            if (!trimmed.StartsWith("LANGUAGE:", StringComparison.Ordinal))
                continue;

            var name = Decode(trimmed["LANGUAGE:".Length..].Trim());
            if (name.Length > 0 && !data.Languages.Contains(name, StringComparer.OrdinalIgnoreCase))
                data.Languages.Add(name);
        }
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
            ProhibitedSchools = fields.GetValueOrDefault("PROHIBITED")
                ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList() ?? new List<string>(),
            SpellBase = fields.GetValueOrDefault("SPELLBASE"),
        };
        data.Classes.Add(entry);
    }

    private static void ParseLevel(string line, PcgCharacterData data)
    {
        var fields = ParseFields(line);

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

        if (fields.TryGetValue("SUBSTITUTIONLEVEL", out var substitution)
            && !string.IsNullOrWhiteSpace(substitution))
            entry.SubstitutionClass = substitution.Trim();

        if (fields.TryGetValue("PRESTAT", out var preStat))
        {
            var statEq = preStat.IndexOf('=');
            if (statEq >= 0)
                entry.AbilityIncrease = preStat[..statEq];
        }

        foreach (Match match in Regex.Matches(
                     line,
                     @"ADD:\[SPELLCASTER:[^|\]]+\|CHOICE:([^\]]+)\]",
                     RegexOptions.IgnoreCase))
        {
            var choice = Decode(match.Groups[1].Value.Trim());
            if (choice.Length > 0)
                entry.SpellcasterChoices.Add(choice);
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
        var skillName = Decode(line[nameStart..nameEnd]);

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

    /// <summary>
    /// Routes an ABILITY row by its CATEGORY, not by the tag that opens the line. A feat the
    /// character chose is written as <c>ABILITY:FEAT|…</c>, but one a class handed out keeps the
    /// granting pool in that first field — <c>ABILITY:Wizard Feat|…|CATEGORY:FEAT|KEY:Extend
    /// Spell</c>. Both are feats and both must land in <see cref="PcgCharacterData.Feats"/>;
    /// matching on the opening tag dropped every class bonus feat, taking with it any later feat
    /// that named one as a prerequisite.
    /// </summary>
    private static void ParseAbility(string line, PcgCharacterData data)
    {
        var category = ParseFields(line).GetValueOrDefault("CATEGORY")
            ?? Decode(line["ABILITY:".Length..].Split('|')[0].Trim());
        if (category.StartsWith("CATEGORY=", StringComparison.OrdinalIgnoreCase))
            category = category["CATEGORY=".Length..];

        if (category.Equals("FEAT", StringComparison.OrdinalIgnoreCase))
            ParseFeat(line, data);
        else
            ParseClassAbility(line, data);
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
                .Select(f => Decode(f["TYPE:".Length..]))
                .ToList();

            var featTypes = typeFields.LastOrDefault() ?? "";
            entry.Types = featTypes.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        data.Feats.Add(entry);
    }

    private static void ParseClassAbility(string line, PcgCharacterData data)
    {
        var fields = ParseFields(line);
        var category = fields.GetValueOrDefault("CATEGORY") ??
            Decode(line["ABILITY:".Length..].Split('|')[0].Trim());

        if (category.StartsWith("CATEGORY=", StringComparison.OrdinalIgnoreCase))
            category = category["CATEGORY=".Length..];
        if (category.Equals("FEAT", StringComparison.OrdinalIgnoreCase))
            return;

        var key = fields.GetValueOrDefault("KEY");
        if (string.IsNullOrWhiteSpace(key))
            key = Decode(line["ABILITY:".Length..].Split('|')[0].Trim());
        if (string.IsNullOrWhiteSpace(key) || key.Contains('='))
            return;

        var entry = new PcgClassAbilityEntry
        {
            Category = category,
            Key = key,
            AppliedTo = fields.GetValueOrDefault("APPLIEDTO"),
            ClassName = fields.GetValueOrDefault("CLASS") ?? fields.GetValueOrDefault("SOURCECLASS"),
        };
        var classLevel = 0;
        if (fields.TryGetValue("LEVEL", out var levelText))
            int.TryParse(levelText, out classLevel);
        entry.ClassLevel = classLevel;

        data.ClassAbilities.Add(entry);
    }

    private static void ParseTemplate(string line, PcgCharacterData data)
    {
        var match = Regex.Match(line, @"TEMPLATESAPPLIED:\[NAME:([^\]|]+)");
        if (!match.Success) return;

        var name = Decode(match.Groups[1].Value);
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
        var sourceLevel = 0;
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
                if (srcFields.TryGetValue("LEVEL", out var levelText))
                    int.TryParse(levelText, out sourceLevel);
            }
        }

        data.Domains.Add(new PcgDomainEntry
        {
            Name = name,
            SourceClass = sourceClass,
            SourceLevel = sourceLevel,
        });
    }

    private static void ParseFollower(string line, PcgCharacterData data)
    {
        var fields = ParseFields(line);
        if (!fields.TryGetValue("FOLLOWER", out var name)) return;
        var hitDice = 0;
        if (fields.TryGetValue("HITDICE", out var hitDiceText))
            int.TryParse(hitDiceText, out hitDice);
        data.Followers.Add(new PcgFollowerEntry
        {
            Name = name,
            Type = fields.GetValueOrDefault("TYPE") ?? "",
            Race = fields.GetValueOrDefault("RACE") ?? "",
            File = fields.GetValueOrDefault("FILE") ?? "",
            HitDice = hitDice,
        });
    }

    private static void ParseMaster(string line, PcgCharacterData data)
    {
        var fields = ParseFields(line);
        if (!fields.TryGetValue("MASTER", out var name)) return;
        var entry = new PcgMasterEntry
        {
            Name = name,
            Type = fields.GetValueOrDefault("TYPE") ?? "",
            File = fields.GetValueOrDefault("FILE") ?? "",
        };
        var parsedHitDice = 0;
        var parsedAdjustment = 0;
        if (fields.TryGetValue("HITDICE", out var hitDice))
            int.TryParse(hitDice, out parsedHitDice);
        if (fields.TryGetValue("ADJUSTMENT", out var adjustment))
            int.TryParse(adjustment, out parsedAdjustment);
        entry.HitDice = parsedHitDice;
        entry.Adjustment = parsedAdjustment;
        data.Master = entry;
    }

    // PCGen models natural attacks (Bite, Claw, Sting, Tail Slap, Wing…) as auto-equipped
    // EQUIPNAME rows. In our engine they live on the race/template, not the equipment list,
    // so these rows would always be unmappable. Skip them at parse time rather than warning.
    private static readonly Regex NaturalAttackSuffix =
        new(@"\(Natural/(Primary|Secondary)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ParseEquipmentItem(string line, PcgCharacterData data)
    {
        var rest = line["EQUIPNAME:".Length..];
        var firstPipe = rest.IndexOf('|');
        var name = Decode(firstPipe >= 0 ? rest[..firstPipe] : rest);

        if (NaturalAttackSuffix.IsMatch(name))
            return;

        var fields = ParseFields(line);

        var raw = new PcgEquipmentRaw { Name = name };
        if (fields.TryGetValue("QUANTITY", out var q)
            && double.TryParse(q, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var qv))
            raw.Quantity = qv;
        if (fields.TryGetValue("WT", out var w)
            && double.TryParse(w, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var wv))
            raw.WeightLbs = wv;
        // PCGen COST is gold pieces; engine stores copper pieces (1 gp = 100 cp).
        if (fields.TryGetValue("COST", out var c)
            && double.TryParse(c, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cv))
            raw.PriceCp = (long)(cv * 100);
        // CUSTOMIZATION is the final field, but it contains an internal literal pipe
        // (`BASEITEM:...|DATA:...`), so the generic pipe-delimited parser truncates it.
        // Preserve the entire tail verbatim for the custom-item modifier importer.
        const string customizationMarker = "|CUSTOMIZATION:";
        var customizationIndex = line.IndexOf(customizationMarker, StringComparison.Ordinal);
        if (customizationIndex >= 0)
        {
            var customization = line[(customizationIndex + customizationMarker.Length)..];
            raw.Customization = customization;
            var baseItem = Regex.Match(customization, @"BASEITEM:(?<name>[^|$\]]+)");
            if (baseItem.Success)
                raw.BaseItemName = baseItem.Groups["name"].Value.Trim();
        }

        data.Equipment.Add(raw);
    }

    private static void ParseEquipmentSet(string line, List<(string SetId, string Slot, string ItemName)> assignments)
    {
        // Format: EQUIPSET:<slotLabel>|ID:<id>|VALUE:<itemName>|...
        // Set headers (e.g. "EQUIPSET:Default Set|ID:0.1|USETEMPMODS:Y") have no VALUE — skip them.
        var rest = line["EQUIPSET:".Length..];
        var firstPipe = rest.IndexOf('|');
        if (firstPipe < 0) return;
        var slot = Decode(rest[..firstPipe]);

        var fields = ParseFields(line);
        if (!fields.TryGetValue("ID", out var id)) return;
        if (!fields.TryGetValue("VALUE", out var value)) return;

        assignments.Add((id, slot, value));
    }

    private static void ResolveEquipmentSlots(
        PcgCharacterData data,
        List<(string SetId, string Slot, string ItemName)> assignments,
        string? activeSetId)
    {
        // Sort so active-set assignments are tried first. An EQUIPNAME row is one physical item;
        // each EQUIPSET row claims one. If the same name appears in multiple sets, the active set
        // wins the assignment for that physical item.
        var sorted = assignments
            .OrderByDescending(a => IsInActiveSet(a.SetId, activeSetId))
            .ToList();

        foreach (var (setId, slot, itemName) in sorted)
        {
            var item = data.Equipment.FirstOrDefault(
                e => e.SlotName == null
                  && string.Equals(e.Name, itemName, StringComparison.Ordinal));
            if (item == null) continue;

            item.SlotName = slot;
            item.InActiveSet = IsInActiveSet(setId, activeSetId);
        }
    }

    private static bool IsInActiveSet(string setId, string? activeSetId)
    {
        if (string.IsNullOrEmpty(activeSetId)) return false;
        return setId == activeSetId
            || setId.StartsWith(activeSetId + ".", StringComparison.Ordinal);
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
            result[key] = Decode(value);
        }

        return result;
    }

    private static string Decode(string value) => value
        .Replace("&nl;", "\n", StringComparison.Ordinal)
        .Replace("&cr;", "\r", StringComparison.Ordinal)
        .Replace("&lf;", "\f", StringComparison.Ordinal)
        .Replace("&colon;", ":", StringComparison.Ordinal)
        .Replace("&pipe;", "|", StringComparison.Ordinal)
        .Replace("&lbracket;", "[", StringComparison.Ordinal)
        .Replace("&rbracket;", "]", StringComparison.Ordinal)
        .Replace("&amp;", "&", StringComparison.Ordinal);
}
