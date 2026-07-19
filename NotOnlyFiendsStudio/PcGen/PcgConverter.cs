using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.PcGen;

public class PcgConversionResult
{
    public Character Character { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> DroppedFeats { get; set; } = new();
    public List<string> DroppedSkills { get; set; } = new();
    public List<string> DroppedClasses { get; set; } = new();
    public List<string> DroppedTemplates { get; set; } = new();
    public List<string> DroppedDomains { get; set; } = new();
    public List<string> DroppedEquipment { get; set; } = new();
    public bool RaceDropped { get; set; }

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (RaceDropped) parts.Add("race unmapped");
            if (DroppedClasses.Count > 0) parts.Add($"{DroppedClasses.Count} class(es) missing");
            if (DroppedFeats.Count > 0) parts.Add($"{DroppedFeats.Count} feat(s) missing");
            if (DroppedSkills.Count > 0) parts.Add($"{DroppedSkills.Count} skill(s) missing");
            if (DroppedTemplates.Count > 0) parts.Add($"{DroppedTemplates.Count} template(s) missing");
            if (DroppedDomains.Count > 0) parts.Add($"{DroppedDomains.Count} domain(s) missing");
            if (DroppedEquipment.Count > 0) parts.Add($"{DroppedEquipment.Count} equipment item(s) missing");
            return parts.Count == 0 ? "Clean import" : string.Join(", ", parts);
        }
    }
}

public static class PcgConverter
{
    /// <summary>
    /// Convert parsed PCGen data to an engine Character.
    /// If registry is provided, validates that mapped IDs exist in content.
    /// Unmapped or missing items are skipped with warnings (never throws).
    /// </summary>
    public static PcgConversionResult Convert(PcgCharacterData data, PcgIdMapper mapper, ContentRegistry? registry = null)
    {
        var result = new PcgConversionResult();
        var alignment = Alignment.N;
        if (!string.IsNullOrEmpty(data.Alignment))
            Enum.TryParse(data.Alignment, true, out alignment);
        var character = new Character
        {
            Name = data.CharacterName,
            Alignment = alignment,
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = data.BaseStats.GetValueOrDefault("STR"),
                DEX = data.BaseStats.GetValueOrDefault("DEX"),
                CON = data.BaseStats.GetValueOrDefault("CON"),
                INT = data.BaseStats.GetValueOrDefault("INT"),
                WIS = data.BaseStats.GetValueOrDefault("WIS"),
                CHA = data.BaseStats.GetValueOrDefault("CHA"),
            },
        };

        // Map race
        var raceId = mapper.MapRace(data.Race);
        if (raceId == null)
        {
            result.Warnings.Add($"Race '{data.Race}' has no engine mapping — using 'human' as fallback");
            result.RaceDropped = true;
            character.RaceId = "human";
        }
        else if (registry != null && !registry.GetAllRaces().Any(r => r.Id == raceId))
        {
            result.Warnings.Add($"Race '{data.Race}' maps to '{raceId}' but not found in content — using 'human' as fallback");
            result.RaceDropped = true;
            character.RaceId = "human";
        }
        else
        {
            character.RaceId = raceId;
        }

        // Map templates
        foreach (var template in data.Templates.Where(t => !t.IsInternal))
        {
            var templateId = mapper.MapTemplate(template.Name);
            if (registry != null && !registry.GetAllTemplates().Any(t => t.Id == templateId))
            {
                result.Warnings.Add($"Template '{template.Name}' maps to '{templateId}' but not found in content");
                result.DroppedTemplates.Add(template.Name);
            }
            else
            {
                character.TemplateIds.Add(templateId);
            }
        }

        // Build skill purchases (distributed across ticks later, by bought-class — see below).
        var driverIds = registry != null
            ? new HashSet<string>(registry.GetAllDrivers().Select(d => d.Id))
            : null;

        // Each entry is one CLASSBOUGHT bracket from PCGen: (skillId, preferredDriverId, halfRanks).
        // Multiple buys of the same skill from different classes produce multiple entries.
        // A skill that doesn't map still only generates one warning/drop per unique name —
        // multiple brackets for the same unmapped skill would otherwise double-count.
        var skillBuys = new List<(string SkillId, string? PreferredDriverId, int HalfRanks)>();
        var reportedDrops = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in data.Skills)
        {
            var skillId = mapper.MapSkill(skill.Name);
            if (registry != null && !registry.GetAllSkills().Any(s => s.Id == skillId))
            {
                if (reportedDrops.Add(skill.Name))
                {
                    result.Warnings.Add($"Skill '{skill.Name}' maps to '{skillId}' but not found in content");
                    result.DroppedSkills.Add(skill.Name);
                }
                continue;
            }
            var preferredDriver = skill.BoughtClass != null ? mapper.MapClass(skill.BoughtClass) : null;
            skillBuys.Add((skillId, preferredDriver, (int)(skill.Ranks * 2)));
        }

        // Build feat list (applied on the last tick — see below)
        var featIds = new List<string>();
        foreach (var feat in data.Feats)
        {
            var featId = mapper.MapFeat(feat.Key);
            FeatDefinition? featDef = null;
            if (registry != null && !registry.TryGetFeat(featId, out featDef))
            {
                result.Warnings.Add($"Feat '{feat.Key}' maps to '{featId}' but not found in content");
                result.DroppedFeats.Add(feat.Key);
                continue;
            }

            // Repeatable feats: PCGen stores each taking as a comma-separated APPLIEDTO value.
            // One entry per element (an empty element still counts as a taking, e.g. "APPLIEDTO:,,"
            // means the feat was taken three times with no selection).
            var selections = string.IsNullOrEmpty(feat.AppliedTo)
                ? new[] { string.Empty }
                : feat.AppliedTo.Split(',');

            foreach (var raw in selections)
            {
                // For feats with a selection (Spell Focus → school, Skill Focus → skill,
                // Weapon Focus → weapon) encode the choice into the id, e.g.
                // "spell_focus_conjuration". Prestige classes gate on those variant ids;
                // storing the bare "spell_focus" would fail their prerequisites.
                var selection = raw.Trim();
                if (featDef?.SelectionRequired != null && selection.Length > 0)
                {
                    var suffix = featDef.SelectionRequired == "skill"
                        ? mapper.MapSkill(selection)
                        : PcgIdMapper.DefaultIdTransform(selection);
                    featIds.Add($"{featId}_{suffix}");
                }
                else
                {
                    featIds.Add(featId);
                }
            }
        }

        // Build domain selections
        var domainSelections = new List<string>();
        foreach (var domain in data.Domains)
        {
            var domainId = mapper.MapDomain(domain.Name);
            if (registry != null && !registry.TryGetDomain(domainId, out _))
            {
                result.Warnings.Add($"Domain '{domain.Name}' maps to '{domainId}' but not found in content");
                result.DroppedDomains.Add(domain.Name);
                continue;
            }
            domainSelections.Add(domainId);
        }

        // Build ticks from level entries, tracking which level-up ability increases
        // the engine will re-apply so we can subtract them from the imported STAT values.
        var appliedAbilityIncreases = new Dictionary<Ability, int>();
        const int abilityIncreaseInterval = 4;

        for (int i = 0; i < data.Levels.Count; i++)
        {
            var level = data.Levels[i];
            var driverId = mapper.MapClass(level.ClassName);

            if (driverId == null)
            {
                result.Warnings.Add($"Class '{level.ClassName}' level {level.ClassLevel} has no engine mapping — tick skipped");
                result.DroppedClasses.Add(level.ClassName);
                continue;
            }

            if (driverIds != null && !driverIds.Contains(driverId))
            {
                result.Warnings.Add($"Class '{level.ClassName}' maps to '{driverId}' but not found in content — tick skipped");
                result.DroppedClasses.Add(level.ClassName);
                continue;
            }

            var choices = new TickChoices();

            // Ability increase
            if (level.AbilityIncrease != null &&
                Enum.TryParse<Ability>(level.AbilityIncrease, true, out var ability))
            {
                choices.AbilityIncrease = ability;
            }

            // Front-load domains onto the first valid tick so they resolve against
            // the class that grants them (e.g., cleric taken at level 1).
            if (character.Ticks.Count == 0 && domainSelections.Count > 0)
            {
                choices.ClassFeatureChoices = new Dictionary<string, List<string>>
                {
                    ["domains"] = domainSelections
                };
            }

            character.Ticks.Add(new Tick { DriverId = driverId, Choices = choices });

            // Engine applies AbilityIncrease only when cumulative HD % interval == 0.
            // Record which increases will be re-applied so we can recover the rolled base.
            var cumulativeHD = character.Ticks.Count;
            if (cumulativeHD % abilityIncreaseInterval == 0 && choices.AbilityIncrease.HasValue)
            {
                var a = choices.AbilityIncrease.Value;
                appliedAbilityIncreases[a] = appliedAbilityIncreases.GetValueOrDefault(a) + 1;
            }
        }

        // Feats go on the LAST valid tick. At the final HD, prerequisites
        // (caster level, BAB, skill ranks) are satisfied, the max-ranks cap covers
        // any legal allocation, and the accumulated feat-slot budget is available
        // (FeatSlots persist across ticks).
        if (character.Ticks.Count > 0 && featIds.Count > 0)
            character.Ticks[^1].Choices.FeatIds = featIds;

        // Skill allocations are distributed across ticks by bought-class. PCGen records
        // CLASSBOUGHT:[CLASS:X|RANKS:Y] per skill rank, telling us which class/racial HD
        // paid for each rank. Placing the allocation on a matching-class tick lets the
        // engine's CurrentTickClassSkills resolve the correct cost (class vs cross-class)
        // naturally — especially important for racial class skills that aren't class
        // skills for subsequent class ticks (e.g. Aranea's Climb during Sorcerer levels).
        if (character.Ticks.Count > 0 && skillBuys.Count > 0)
        {
            // tickIndex -> skillId -> cumulative halfRanks
            var perTick = new Dictionary<int, Dictionary<string, int>>();
            foreach (var (skillId, preferredDriverId, halfRanks) in skillBuys)
            {
                var tickIdx = ResolveTickForBuy(character.Ticks, preferredDriverId);
                if (!perTick.TryGetValue(tickIdx, out var map))
                {
                    map = new Dictionary<string, int>();
                    perTick[tickIdx] = map;
                }
                map[skillId] = map.GetValueOrDefault(skillId) + halfRanks;
            }

            foreach (var (tickIdx, map) in perTick)
            {
                character.Ticks[tickIdx].Choices.SkillAllocations = map
                    .Select(kv => new SkillAllocation { SkillId = kv.Key, HalfRanks = kv.Value })
                    .OrderBy(a => a.SkillId, StringComparer.Ordinal)
                    .ToList();
            }
        }

        // PCGen's STAT:X|SCORE is the base score before racial/template/equipment
        // modifiers, but it already includes level-up ability increases. The engine
        // re-applies those increases via AbilityIncrease ticks, so we subtract them
        // here to recover the true rolled base. (Race/template mods are *not* baked
        // into STAT and are applied on top of BaseAbilityScores by the engine.)
        character.BaseAbilityScores.STR -= appliedAbilityIncreases.GetValueOrDefault(Ability.STR);
        character.BaseAbilityScores.DEX -= appliedAbilityIncreases.GetValueOrDefault(Ability.DEX);
        character.BaseAbilityScores.CON -= appliedAbilityIncreases.GetValueOrDefault(Ability.CON);
        character.BaseAbilityScores.INT -= appliedAbilityIncreases.GetValueOrDefault(Ability.INT);
        character.BaseAbilityScores.WIS -= appliedAbilityIncreases.GetValueOrDefault(Ability.WIS);
        character.BaseAbilityScores.CHA -= appliedAbilityIncreases.GetValueOrDefault(Ability.CHA);

        // Equipment: active-set items get their PCGen slot translated to the engine vocabulary;
        // weapon-slot items go in MainHand/OffHand/TwoHanded. Items in non-active sets (alternate
        // loadouts) are imported as "carried" so the user keeps them on the character but they
        // don't contribute to AC/attack math. Unmapped names are warned and skipped — the
        // regression report surfaces them so PcgIdMapper.EquipmentOverrides or the catalog can grow.
        foreach (var raw in data.Equipment)
        {
            var id = mapper.MapEquipment(raw.Name, registry);
            if (id == null)
            {
                result.Warnings.Add($"Equipment '{raw.Name}' has no engine mapping — skipped");
                result.DroppedEquipment.Add(raw.Name);
                continue;
            }

            var entry = new EquipmentEntry
            {
                ItemId = raw.Name,
                ContentId = id,
            };

            if (raw.InActiveSet && raw.SlotName != null && mapper.IsWeaponSlot(raw.SlotName))
            {
                var (mh, th) = mapper.InferHand(raw.SlotName);
                entry.Slot = string.Empty;
                entry.MainHand = mh;
                entry.TwoHanded = th;
            }
            else if (raw.InActiveSet && raw.SlotName != null)
            {
                entry.Slot = mapper.MapSlot(raw.SlotName);
            }
            else
            {
                entry.Slot = "carried";
            }

            character.Equipment.Add(entry);
        }

        result.Character = character;
        return result;
    }

    /// <summary>
    /// Picks the engine tick that should own a skill purchase. Prefers the latest tick whose
    /// driver matches the PCGen class that bought the rank; falls back to the final tick if
    /// no matching driver is present (e.g., the class was dropped during conversion).
    /// </summary>
    private static int ResolveTickForBuy(List<Tick> ticks, string? preferredDriverId)
    {
        if (!string.IsNullOrEmpty(preferredDriverId))
        {
            for (int i = ticks.Count - 1; i >= 0; i--)
            {
                if (ticks[i].DriverId == preferredDriverId)
                    return i;
            }
        }
        return ticks.Count - 1;
    }
}
