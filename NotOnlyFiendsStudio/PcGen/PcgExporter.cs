using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.PcGen;

public enum PcgExportStatus
{
    Exact,
    Partial,
    Blocked,
}

public enum PcgExportIssueSeverity
{
    Warning,
    Error,
}

public sealed class PcgExportIssue
{
    public PcgExportIssueSeverity Severity { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class PcgExportOptions
{
    public string PcgenVersion { get; set; } = "6.08.00 RC10";
    public string GameMode { get; set; } = "35e";
    public List<string> Campaigns { get; set; } = new()
    {
        "3.5 RSRD Basics",
        "3.5 RSRD Divine",
        "3.5 RSRD Epic",
        "3.5 RSRD Monsters",
        "Unearthed Arcana",
    };
}

public sealed class PcgExportResult
{
    public string FileName { get; init; } = "character.pcg";
    public string Content { get; init; } = string.Empty;
    public string Encoding { get; init; } = "utf-8";
    public List<PcgExportIssue> Issues { get; init; } = new();
    public PcgExportStatus Status => Issues.Any(issue => issue.Severity == PcgExportIssueSeverity.Error)
        ? PcgExportStatus.Blocked
        : Issues.Count > 0 ? PcgExportStatus.Partial : PcgExportStatus.Exact;
}

/// <summary>
/// Writes the decision-bearing subset of PCGen's version-2 save format. PCGen recalculates
/// derived sheet values from the campaigns named in the file; exporting the engine's cached
/// CharacterSheet would therefore be both redundant and misleading.
/// </summary>
public static class PcgExporter
{
    public static PcgExportResult Export(
        Character character,
        ContentRegistry registry,
        ReplayStudio? replayStudio = null,
        PcgExportOptions? options = null)
    {
        options ??= new PcgExportOptions();
        replayStudio ??= new ReplayStudio(registry);
        var mapper = new PcgIdMapper();
        var issues = new List<PcgExportIssue>();

        CharacterState state;
        try
        {
            state = replayStudio.Evaluate(character);
        }
        catch (Exception ex)
        {
            AddError(issues, "evaluation_failed", "character", $"Character evaluation failed: {ex.Message}");
            return Result(character, string.Empty, issues);
        }

        var race = registry.GetAllRaces().FirstOrDefault(candidate => candidate.Id == character.RaceId);
        if (race == null)
        {
            AddError(issues, "unmapped_race", "raceId", $"Race '{character.RaceId}' is not loaded.");
            return Result(character, string.Empty, issues);
        }

        var raceName = mapper.ToPcgenRace(character.RaceId);
        if (raceName == null)
        {
            raceName = race.Name;
            AddWarning(issues, "assumed_race_key", "raceId",
                $"Using display name '{raceName}' as the PCGen race key; verify the target dataset uses that key.");
        }

        var driverLevels = new Dictionary<string, int>(StringComparer.Ordinal);
        var tickRows = new List<TickRow>();
        var classGroups = new List<ClassGroup>();
        var classByKey = new Dictionary<string, ClassGroup>(StringComparer.Ordinal);
        var previousUnspent = 0;

        for (var index = 0; index < character.Ticks.Count; index++)
        {
            var tick = character.Ticks[index];
            if (registry.GetAllDrivers().FirstOrDefault(candidate => candidate.Id == tick.DriverId) is not HDDriver driver)
            {
                AddError(issues, "unmapped_driver", $"ticks[{index}].driverId",
                    $"Driver '{tick.DriverId}' is not a loaded HD driver.");
                continue;
            }

            var className = mapper.ToPcgenClass(tick.DriverId, character.RaceId);
            if (className == null)
            {
                className = driver.Name;
                AddWarning(issues, "assumed_class_key", $"ticks[{index}].driverId",
                    $"Using display name '{className}' as the PCGen class key; verify the target dataset uses that key.");
            }

            var driverLevel = driverLevels.GetValueOrDefault(tick.DriverId) + 1;
            driverLevels[tick.DriverId] = driverLevel;

            if (!classByKey.TryGetValue(className, out var group))
            {
                group = new ClassGroup(className, tick.DriverId, driver);
                classByKey[className] = group;
                classGroups.Add(group);
            }
            group.Level++;

            var tickState = replayStudio.Evaluate(character, index + 1);
            var spent = SkillPointsSpent(tick, tickState);
            var gained = Math.Max(0, tickState.UnspentSkillPoints - previousUnspent + spent);
            previousUnspent = tickState.UnspentSkillPoints;
            var hitDie = tickState.HitDice.LastOrDefault();
            var hpRoll = tick.Choices.HitPointsRolled
                ?? (hitDie == null ? 0 : index == 0 ? hitDie.DieSize : hitDie.DieSize / 2 + 1);

            tickRows.Add(new TickRow(index, tick, driver, className, driverLevel, hpRoll, gained,
                tickState.UnspentSkillPoints, tickState));
        }

        if (issues.Any(issue => issue.Severity == PcgExportIssueSeverity.Error))
            return Result(character, string.Empty, issues);

        foreach (var warning in state.Warnings)
        {
            var path = warning.TickIndex.HasValue && warning.TickIndex.Value > 0
                ? $"ticks[{warning.TickIndex.Value - 1}]"
                : "character";
            AddWarning(issues, "character_evaluation_warning", path, warning.Message);
        }
        ReportKnownLimitations(character, issues);

        var writer = new PcgWriter();
        WriteSystem(writer, options);
        WriteBio(writer, character);
        WriteAttributes(writer, character, raceName);
        WriteClasses(writer, character, state, classGroups, tickRows, mapper, registry, issues);
        WriteExperience(writer, state);
        WriteTemplates(writer, character, registry, issues);
        WriteSkills(writer, tickRows, registry, issues);
        WriteLanguages(writer, state, registry);
        WriteFeatsAndAbilities(writer, character, tickRows, registry, mapper, issues);
        WriteEquipment(writer, character, registry, issues);
        WriteDomains(writer, character, tickRows, registry, mapper, issues);
        WriteSpells(writer, character, state, registry, mapper, issues);
        WriteTail(writer, character, registry, mapper, issues);

        return Result(character, writer.ToString(), issues);
    }

    private static void WriteSystem(PcgWriter writer, PcgExportOptions options)
    {
        writer.Line("PCGVERSION:2.0");
        writer.Section("System Information");
        if (options.Campaigns.Count > 0)
            writer.Line(string.Join('|', options.Campaigns.Distinct(StringComparer.Ordinal)
                .Select(campaign => $"CAMPAIGN:{PcgWriter.Encode(campaign)}")));
        writer.Line($"VERSION:{PcgWriter.Encode(options.PcgenVersion)}");
        writer.Line("ROLLMETHOD:3|EXPRESSION:roll(4,6,top(3))");
        writer.Line("PURCHASEPOINTS:N");
        writer.Line("CHARACTERTYPE:PC");
        writer.Line("PREVIEWSHEET:Standard.htm.ftl");
        writer.Line("POOLPOINTS:0");
        writer.Line("POOLPOINTSAVAIL:-1");
        writer.Line($"GAMEMODE:{PcgWriter.Encode(options.GameMode)}");
        writer.Line("TABLABEL:0");
        writer.Line("AUTOSPELLS:Y");
        writer.Line("USEHIGHERKNOWN:N");
        writer.Line("USEHIGHERPREPPED:N");
        writer.Line("LOADCOMPANIONS:N");
        writer.Line("USETEMPMODS:Y");
        writer.Line("AUTOSORTGEAR:Y");
        writer.Line("SKILLSOUTPUTORDER:0");
        writer.Line("SKILLFILTER:2");
        writer.Line("IGNORECOST:N");
        writer.Line("ALLOWDEBT:N");
        writer.Line("AUTORESIZEGEAR:Y");
    }

    private static void WriteBio(PcgWriter writer, Character character)
    {
        writer.Section("Character Bio");
        writer.Line($"CHARACTERNAME:{PcgWriter.Encode(character.Name)}");
        foreach (var tag in new[] { "TABNAME", "PLAYERNAME" })
            writer.Line($"{tag}:");
        if (!string.IsNullOrWhiteSpace(character.Gender))
            writer.Line($"GENDER:{PcgWriter.Encode(character.Gender)}");
        writer.Line("HANDED:Right");
        foreach (var tag in new[]
                 {
                     "SKINCOLOR", "EYECOLOR", "HAIRCOLOR", "HAIRSTYLE", "LOCATION", "CITY",
                     "BIRTHDAY", "BIRTHPLACE", "PERSONALITYTRAIT1", "PERSONALITYTRAIT2",
                     "SPEECHPATTERN", "PHOBIAS", "INTERESTS", "CATCHPHRASE", "PORTRAIT"
                 })
            writer.Line($"{tag}:");
    }

    private static void WriteAttributes(PcgWriter writer, Character character, string raceName)
    {
        writer.Section("Character Attributes");
        var scores = new AbilityScoreSet
        {
            STR = character.BaseAbilityScores.STR,
            DEX = character.BaseAbilityScores.DEX,
            CON = character.BaseAbilityScores.CON,
            INT = character.BaseAbilityScores.INT,
            WIS = character.BaseAbilityScores.WIS,
            CHA = character.BaseAbilityScores.CHA,
        };
        foreach (var choice in character.Ticks.Select(tick => tick.Choices.AbilityIncrease).Where(choice => choice.HasValue))
            scores.SetScore(choice!.Value, scores.GetScore(choice.Value) + 1);

        foreach (var ability in Enum.GetValues<Ability>())
            writer.Line($"STAT:{ability}|SCORE:{scores.GetScore(ability)}");
        writer.Line($"ALIGN:{(character.Alignment == Alignment.N ? "TN" : character.Alignment)}");
        var raceLine = $"RACE:{PcgWriter.Encode(raceName)}";
        var addition = PcgIdMapper.ToPcgenRaceAddition(character.RaceId);
        if (addition != null) raceLine += '|' + addition;
        writer.Line(raceLine);
    }

    private static void WriteClasses(
        PcgWriter writer,
        Character character,
        CharacterState state,
        List<ClassGroup> groups,
        List<TickRow> ticks,
        PcgIdMapper mapper,
        ContentRegistry registry,
        List<PcgExportIssue> issues)
    {
        writer.Section("Character Class(es)");
        var specialty = WizardSchools.Specialty(state);
        var prohibited = WizardSchools.ProhibitedSchools(state);

        foreach (var group in groups)
        {
            var line = new StringBuilder($"CLASS:{PcgWriter.Encode(group.Name)}");
            if (group.Name == "Wizard" && specialty != null)
                line.Append("|SUBCLASS:").Append(PcgWriter.Encode(SpecialistName(specialty)));
            line.Append("|LEVEL:").Append(group.Level).Append("|SKILLPOOL:0");

            var casting = state.Spellcasting.GetValueOrDefault(group.DriverId)
                ?? state.Spellcasting.Values.FirstOrDefault(candidate =>
                    string.Equals(mapper.ToPcgenClass(candidate.ClassId, character.RaceId), group.Name,
                        StringComparison.Ordinal));
            if (casting != null)
            {
                line.Append("|SPELLBASE:").Append(casting.CastingStat);
                line.Append("|CANCASTPERDAY:");
                if (casting.SpellsPerDay.Count > 0)
                {
                    line.Append(string.Join(',', Enumerable.Range(0, casting.SpellsPerDay.Keys.Max() + 1)
                        .Select(level => casting.TotalSlotsAt(level))));
                }
            }
            else
            {
                line.Append("|SPELLBASE:None|CANCASTPERDAY:");
            }

            if (group.Name == "Wizard" && prohibited.Count > 0)
                line.Append("|PROHIBITED:").Append(string.Join(',', prohibited.Select(Humanize)));
            writer.Line(line.ToString());
        }

        foreach (var row in ticks)
        {
            var line = new StringBuilder($"CLASSABILITIESLEVEL:{PcgWriter.Encode(row.ClassName)}={row.DriverLevel}");
            var substitution = PcgIdMapper.ToPcgenSubstitutionLevel(row.Tick.DriverId);
            if (substitution != null)
                line.Append("|SUBSTITUTIONLEVEL:").Append(PcgWriter.Encode(substitution));
            line.Append("|HITPOINTS:").Append(row.HitPoints);
            if (row.ClassName == "Wizard" && row.DriverLevel == 1 && specialty != null)
                line.Append("|SPECIALTIES:[SPECIALTY:").Append(PcgWriter.Encode(Humanize(specialty))).Append(']');
            if (row.Tick.Choices.AbilityIncrease.HasValue)
                line.Append("|PRESTAT:").Append(row.Tick.Choices.AbilityIncrease.Value).Append("=1");

            if (row.Tick.Choices.ClassFeatureChoices?.TryGetValue("advance_spellcasting", out var advances) == true)
            {
                foreach (var classId in advances)
                {
                    var target = mapper.ToPcgenClass(classId, character.RaceId)
                        ?? registry.GetAllDrivers().FirstOrDefault(driver => driver.Id == classId)?.Name;
                    if (target == null)
                    {
                        AddWarning(issues, "unmapped_spellcasting_choice",
                            $"ticks[{row.Index}].choices.classFeatureChoices.advance_spellcasting",
                            $"Spellcasting target '{classId}' has no PCGen class key and was omitted.");
                        continue;
                    }
                    line.Append("|ADD:[SPELLCASTER:").Append(PcgWriter.Encode(row.ClassName))
                        .Append("|CHOICE:").Append(PcgWriter.Encode(target)).Append(']');
                }
            }

            line.Append("|SKILLSGAINED:").Append(row.SkillPointsGained)
                .Append("|SKILLSREMAINING:").Append(row.SkillPointsRemaining);
            writer.Line(line.ToString());
        }
    }

    private static void WriteExperience(PcgWriter writer, CharacterState state)
    {
        writer.Section("Character Experience");
        var effectiveLevel = Math.Max(1, state.ECL);
        var experience = 1000L * effectiveLevel * (effectiveLevel - 1) / 2;
        writer.Line($"EXPERIENCE:{experience}");
        writer.Line("EXPERIENCETABLE:Default");
    }

    private static void WriteTemplates(
        PcgWriter writer,
        Character character,
        ContentRegistry registry,
        List<PcgExportIssue> issues)
    {
        writer.Section("Character Templates");
        foreach (var (templateId, index) in character.TemplateIds.Select((id, index) => (id, index)))
        {
            if (!registry.TryGetTemplate(templateId, out var template) || template == null)
            {
                AddWarning(issues, "unmapped_template", $"templateIds[{index}]",
                    $"Template '{templateId}' is not loaded and was omitted.");
                continue;
            }
            if (template.AcquisitionKind == TemplateAcquisitionKind.Internal)
                continue; // Engine implementation detail; PCGen rebuilds its own internal templates.
            writer.Line($"TEMPLATESAPPLIED:[NAME:{PcgWriter.Encode(template.Name)}]");
        }
        writer.Section("Character Region");
        writer.Line("REGION:None");
    }

    private static void WriteSkills(
        PcgWriter writer,
        List<TickRow> ticks,
        ContentRegistry registry,
        List<PcgExportIssue> issues)
    {
        writer.Section("Character Skills");
        var buys = new Dictionary<string, Dictionary<(string ClassName, bool IsClassSkill), int>>(StringComparer.Ordinal);
        foreach (var row in ticks)
        {
            foreach (var allocation in row.Tick.Choices.SkillAllocations ?? Enumerable.Empty<SkillAllocation>())
            {
                if (!registry.TryGetSkill(allocation.SkillId, out var skill) || skill == null)
                {
                    AddWarning(issues, "unmapped_skill", $"ticks[{row.Index}].choices.skillAllocations",
                        $"Skill '{allocation.SkillId}' is not loaded and was omitted.");
                    continue;
                }
                if (!buys.TryGetValue(skill.Id, out var byClass))
                    buys[skill.Id] = byClass = new Dictionary<(string, bool), int>();
                var key = (row.ClassName, row.State.CurrentTickClassSkills.Contains(allocation.SkillId));
                byClass[key] = byClass.GetValueOrDefault(key) + allocation.HalfRanks;
            }
        }

        foreach (var (skillId, byClass) in buys.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var skill = registry.GetAllSkills().First(candidate => candidate.Id == skillId);
            var line = new StringBuilder($"SKILL:{PcgWriter.Encode(skill.Name)}");
            foreach (var (key, halfRanks) in byClass
                         .OrderBy(entry => entry.Key.ClassName, StringComparer.Ordinal)
                         .ThenByDescending(entry => entry.Key.IsClassSkill))
            {
                line.Append("|CLASSBOUGHT:[CLASS:").Append(PcgWriter.Encode(key.ClassName))
                    .Append("|RANKS:").Append((halfRanks / 2.0).ToString("0.0", CultureInfo.InvariantCulture))
                    .Append(key.IsClassSkill ? "|COST:1|CLASSSKILL:Y]" : "|COST:2|CLASSSKILL:N]");
            }
            writer.Line(line.ToString());
        }
    }

    private static void WriteLanguages(PcgWriter writer, CharacterState state, ContentRegistry registry)
    {
        writer.Section("Character Languages");
        var names = state.Languages
            .Select(id => registry.TryGetLanguage(id, out var language) && language != null ? language.Name : Humanize(id))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        if (names.Count > 0)
            writer.Line(string.Join('|', names.Select(name => $"LANGUAGE:{PcgWriter.Encode(name)}")));
    }

    private static void WriteFeatsAndAbilities(
        PcgWriter writer,
        Character character,
        List<TickRow> ticks,
        ContentRegistry registry,
        PcgIdMapper mapper,
        List<PcgExportIssue> issues)
    {
        writer.Section("Character Feats");
        writer.Line("FEATPOOL:0.0");
        writer.Section("Character Abilities");

        var variantAbilities = character.Ticks.Select(tick => PcgIdMapper.ToPcgenVariantAbility(tick.DriverId))
            .Where(name => name != null).Distinct(StringComparer.Ordinal).Cast<string>();
        foreach (var ability in variantAbilities)
            writer.Line($"ABILITY:Special Ability|TYPE:NORMAL|CATEGORY:Special Ability|KEY:{PcgWriter.Encode(ability)}");

        var availableRestrictedFeatSlots = new List<string>();
        foreach (var row in ticks)
        {
            availableRestrictedFeatSlots.AddRange(row.Driver.PerLevelPermabuffs.OfType<GrantFeatSlot>()
                .Where(slot => slot.Restriction != null).Select(slot => slot.Restriction!));
            if (row.Driver.LevelPermabuffs.TryGetValue(row.DriverLevel, out var levelBuffs))
            {
                availableRestrictedFeatSlots.AddRange(levelBuffs.OfType<GrantFeatSlot>()
                    .Where(slot => slot.Restriction != null).Select(slot => slot.Restriction!));
            }
            foreach (var featId in row.Tick.Choices.FeatIds ?? Enumerable.Empty<string>())
            {
                var feat = ResolveFeat(registry, featId, out var selection);
                if (feat == null)
                {
                    AddWarning(issues, "unmapped_feat", $"ticks[{row.Index}].choices.featIds",
                        $"Feat '{featId}' is not loaded and was omitted.");
                    continue;
                }

                var category = FeatPoolForChoice(feat, availableRestrictedFeatSlots);
                var line = new StringBuilder($"ABILITY:{PcgWriter.Encode(category)}|TYPE:NORMAL|CATEGORY:FEAT|KEY:{PcgWriter.Encode(feat.Name)}");
                if (selection != null)
                    line.Append("|APPLIEDTO:").Append(PcgWriter.Encode(SelectionName(feat.SelectionRequired, selection, registry)));
                line.Append("|TYPE:").Append(PcgWriter.Encode(feat.Type.ToString()));
                writer.Line(line.ToString());
            }

            foreach (var (featureId, selections) in row.Tick.Choices.ClassFeatureChoices
                         ?? new Dictionary<string, List<string>>())
            {
                if (featureId is "domains" or "imported_source_domains" or "advance_spellcasting"
                    || featureId == WizardSchools.SpecializationFeature
                    || featureId == WizardSchools.ProhibitedFeature)
                    continue;
                if (!registry.TryGetClassFeature(featureId, out var feature) || feature == null)
                {
                    AddWarning(issues, "unmapped_class_feature", $"ticks[{row.Index}].choices.classFeatureChoices.{featureId}",
                        $"Class feature '{featureId}' is not loaded and was omitted.");
                    continue;
                }
                foreach (var selection in selections)
                {
                    var optionName = FeatureOptionName(feature, selection, registry);
                    writer.Line($"ABILITY:{PcgWriter.Encode(feature.Name)}|TYPE:NORMAL|CATEGORY:{PcgWriter.Encode(feature.Name)}"
                        + $"|KEY:{PcgWriter.Encode(feature.Name + " ~ " + optionName)}"
                        + $"|APPLIEDTO:{PcgWriter.Encode(optionName)}|CLASS:{PcgWriter.Encode(row.ClassName)}|LEVEL:{row.DriverLevel}");
                    AddWarning(issues, "assumed_class_feature_encoding",
                        $"ticks[{row.Index}].choices.classFeatureChoices.{featureId}",
                        $"Exported '{feature.Name}: {optionName}' using PCGen's conventional ability-key form; verify the target dataset uses that category and key.");
                }
            }
        }

        writer.Section("Character Weapon proficiencies");
    }

    private static void WriteEquipment(
        PcgWriter writer,
        Character character,
        ContentRegistry registry,
        List<PcgExportIssue> issues)
    {
        writer.Section("Character Equipment");
        writer.Line("MONEY:0.00");
        var exportedItems = new List<(EquipmentEntry Entry, string Name)>();
        foreach (var (entry, index) in character.Equipment.Select((entry, index) => (entry, index)))
        {
            EquipmentDefinition? definition = null;
            if (entry.ContentId != null)
                registry.TryGetEquipment(entry.ContentId, out definition);
            var name = !string.IsNullOrWhiteSpace(entry.ItemId) ? entry.ItemId : definition?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                AddWarning(issues, "unmapped_equipment", $"equipment[{index}]", "Equipment has no PCGen item name and was omitted.");
                continue;
            }
            exportedItems.Add((entry, name));
            var priceCp = entry.PriceCpOverride ?? definition?.PriceCp ?? 0;
            var weight = entry.WeightLbsOverride ?? definition?.WeightLbs ?? 0;
            writer.Line($"EQUIPNAME:{PcgWriter.Encode(name)}|OUTPUTORDER:{index + 1}"
                + $"|COST:{(priceCp / 100m).ToString("0.##", CultureInfo.InvariantCulture)}"
                + $"|WT:{weight.ToString("0.##", CultureInfo.InvariantCulture)}"
                + $"|QUANTITY:{entry.Quantity.ToString("0.0###", CultureInfo.InvariantCulture)}|NOTE:");

            if (entry.Permabuffs.Count > 0 || entry.EnhancementBonusOverride.HasValue
                || entry.SpecialAbilityBonusEquivalentOverride.HasValue || entry.IntelligentItemOverride != null)
            {
                AddWarning(issues, "unsupported_equipment_customization", $"equipment[{index}]",
                    $"Custom mechanics on '{name}' cannot be encoded as PCGen EQMOD data; the base item was exported.");
            }
        }

        writer.Line("EQUIPSET:Default Set|ID:0.1|USETEMPMODS:Y");
        for (var index = 0; index < exportedItems.Count; index++)
        {
            var (entry, name) = exportedItems[index];
            var slot = PcgenSlot(entry);
            writer.Line($"EQUIPSET:{PcgWriter.Encode(slot)}|ID:0.1.{index + 1:00}|VALUE:{PcgWriter.Encode(name)}"
                + $"|QUANTITY:{entry.Quantity.ToString("0.0###", CultureInfo.InvariantCulture)}|USETEMPMODS:Y");
        }
        writer.Line("CALCEQUIPSET:0.1");
        writer.Section("Temporary Bonuses");
        writer.Section("EquipSet Temp Bonuses");
    }

    private static void WriteDomains(
        PcgWriter writer,
        Character character,
        List<TickRow> ticks,
        ContentRegistry registry,
        PcgIdMapper mapper,
        List<PcgExportIssue> issues)
    {
        writer.Section("Character Deity/Domain");
        if (!string.IsNullOrWhiteSpace(character.Deity))
        {
            var deityName = registry.TryResolveDeity(character.Deity, out var deity) && deity != null
                ? deity.Name
                : character.Deity;
            writer.Line($"DEITY:{PcgWriter.Encode(deityName)}");
        }
        foreach (var row in ticks)
        {
            foreach (var feature in new[] { "domains", "imported_source_domains" })
            {
                if (row.Tick.Choices.ClassFeatureChoices?.TryGetValue(feature, out var domains) != true)
                    continue;
                foreach (var domainId in domains!)
                {
                    if (!registry.TryGetDomain(domainId, out var domain) || domain == null)
                    {
                        AddWarning(issues, "unmapped_domain", $"ticks[{row.Index}].choices.classFeatureChoices.{feature}",
                            $"Domain '{domainId}' is not loaded and was omitted.");
                        continue;
                    }
                    writer.Line($"DOMAIN:{PcgWriter.Encode(domain.Name)}|SOURCE:[TYPE:CLASS|NAME:{PcgWriter.Encode(row.ClassName)}|LEVEL:{row.DriverLevel}]");
                }
            }
        }
    }

    private static void WriteSpells(
        PcgWriter writer,
        Character character,
        CharacterState state,
        ContentRegistry registry,
        PcgIdMapper mapper,
        List<PcgExportIssue> issues)
    {
        writer.Section("Character Spells Information");
        var all = character.Ticks.SelectMany((tick, tickIndex) =>
                (tick.Choices.SpellSelections ?? new List<SpellSelection>()).Select(selection => (selection, tickIndex)))
            .ToList();
        var hasSpellbook = all.Any(item => Acquisition(item.selection.ClassId, state, registry) == SpellAcquisition.Spellbook);
        if (hasSpellbook)
            writer.Line("SPELLBOOK:Spellbook (Wizard's/Blank)|TYPE:3");
        if (character.PreparedSpellSelections.Count > 0)
            writer.Line("SPELLBOOK:Prepared Spells|TYPE:2");

        foreach (var (selection, tickIndex) in all)
            WriteSpell(writer, selection, tickIndex, Acquisition(selection.ClassId, state, registry), registry, mapper, issues);
        foreach (var selection in character.PreparedSpellSelections)
            WriteSpell(writer, new SpellSelection
            {
                ClassId = selection.ClassId,
                SpellId = selection.SpellId,
                SpellLevel = selection.SpellLevel,
            }, null, null, registry, mapper, issues, "Prepared Spells");
    }

    private static void WriteSpell(
        PcgWriter writer,
        SpellSelection selection,
        int? tickIndex,
        SpellAcquisition? acquisition,
        ContentRegistry registry,
        PcgIdMapper mapper,
        List<PcgExportIssue> issues,
        string? forcedBook = null)
    {
        if (!registry.TryGetSpell(selection.SpellId, out var spell) || spell == null)
        {
            AddWarning(issues, "unmapped_spell", tickIndex.HasValue ? $"ticks[{tickIndex}].choices.spellSelections" : "preparedSpellSelections",
                $"Spell '{selection.SpellId}' is not loaded and was omitted.");
            return;
        }
        var className = mapper.ToPcgenClass(selection.ClassId)
            ?? registry.GetAllDrivers().FirstOrDefault(driver => driver.Id == selection.ClassId)?.Name
            ?? selection.ClassId;
        var book = forcedBook ?? (acquisition == SpellAcquisition.Spellbook
            ? "Spellbook (Wizard's/Blank)"
            : "Known Spells");
        writer.Line($"SPELLNAME:{PcgWriter.Encode(spell.Name)}|TIMES:1|CLASS:{PcgWriter.Encode(className)}"
            + $"|BOOK:{PcgWriter.Encode(book)}|SPELLLEVEL:{selection.SpellLevel}|SOURCE:[TYPE:CLASS|NAME:{PcgWriter.Encode(className)}]");
    }

    private static void WriteTail(
        PcgWriter writer,
        Character character,
        ContentRegistry registry,
        PcgIdMapper mapper,
        List<PcgExportIssue> issues)
    {
        writer.Section("Character Description/Bio/History");
        foreach (var tag in new[] { "CHARACTERBIO", "CHARACTERDESC", "CHARACTERCOMP", "CHARACTERASSET", "CHARACTERMAGIC", "CHARACTERDMNOTES" })
            writer.Line($"{tag}:");
        writer.Section("Kits");
        writer.Section("Character Master/Follower");

        if (character.CompanionOrigin != null)
        {
            var origin = character.CompanionOrigin;
            var name = origin.SourceName ?? Humanize(origin.MasterCharacterId ?? "master");
            var file = origin.SourceFile ?? $"{origin.MasterCharacterId ?? "master"}.pcg";
            writer.Line($"MASTER:{PcgWriter.Encode(name)}|TYPE:{PcgWriter.Encode(PcgenLinkType(origin.LinkType))}"
                + $"|HITDICE:{origin.EffectiveMasterLevel}|FILE:{PcgWriter.Encode(file)}|ADJUSTMENT:0");
        }

        foreach (var (link, index) in character.CompanionLinks.Select((link, index) => (link, index)))
        {
            var name = link.SourceName ?? Humanize(link.CompanionId);
            var file = link.SourceFile ?? $"{link.CompanionId}.pcg";
            var raceName = link.SelectedSpecies == null ? string.Empty : mapper.ToPcgenRace(link.SelectedSpecies)
                ?? registry.GetAllRaces().FirstOrDefault(race => race.Id == link.SelectedSpecies)?.Name ?? string.Empty;
            writer.Line($"FOLLOWER:{PcgWriter.Encode(name)}|TYPE:{PcgWriter.Encode(PcgenLinkType(link.LinkType))}"
                + (raceName.Length == 0 ? string.Empty : $"|RACE:{PcgWriter.Encode(raceName)}")
                + $"|HITDICE:{link.FollowerLevel}|FILE:{PcgWriter.Encode(file)}");
            if (link.SourceFile == null)
                AddWarning(issues, "assumed_companion_file", $"companionLinks[{index}]",
                    $"Companion file name was inferred as '{file}'. Keep both PCG files in the same directory.");
        }

        writer.Section("Character Notes Tab");
        writer.Section("Age Set Selections");
        writer.Line("AGESET:1:0:0:0:0:0:0:0:0:0");
        writer.Section("Campaign History");
        writer.Section("Suppressed Biography Fields");
        writer.Line("SUPPRESSBIOFIELDS:");
    }

    private static int SkillPointsSpent(Tick tick, CharacterState state)
    {
        var total = 0;
        foreach (var allocation in tick.Choices.SkillAllocations ?? Enumerable.Empty<SkillAllocation>())
            total += state.CurrentTickClassSkills.Contains(allocation.SkillId) ? allocation.HalfRanks / 2 : allocation.HalfRanks;
        return total;
    }

    private static FeatDefinition? ResolveFeat(ContentRegistry registry, string featId, out string? selection)
    {
        selection = null;
        foreach (var feat in registry.GetAllFeats())
        {
            if (feat.Id == featId) return feat;
            if (FeatVariantId.TryGetSelection(featId, feat.Id, out var selected))
            {
                selection = FeatVariantId.NormalizeSelection(selected);
                return feat;
            }
        }
        return null;
    }

    private static string FeatPoolForChoice(FeatDefinition feat, List<string> restrictedSlots)
    {
        var index = restrictedSlots.FindIndex(restriction => ReplayStudio.FeatMatchesRestriction(feat, restriction));
        if (index < 0) return "FEAT";
        var restriction = restrictedSlots[index];
        restrictedSlots.RemoveAt(index);
        if (restriction == "fighter_bonus") return "Fighter Feat";
        if (restriction == "wizard_bonus") return "Wizard Feat";
        return "FEAT";
    }

    private static string SelectionName(string? kind, string selection, ContentRegistry registry)
    {
        if (kind == "skill" && registry.TryGetSkill("skill:" + selection, out var skill) && skill != null)
            return skill.Name;
        if (kind == "spell" && registry.TryGetSpell("spell:" + selection, out var spell) && spell != null)
            return spell.Name;
        if (kind == "weapon")
        {
            var equipment = registry.GetAllEquipment().FirstOrDefault(item =>
                item.Id == "weapon:" + selection || item.Id.EndsWith(':' + selection, StringComparison.Ordinal));
            if (equipment != null) return PcgenWeaponChoiceName(equipment.Name);
        }
        return Humanize(selection);
    }

    // The SRD catalog uses natural display names such as "Flail, Heavy", while PCGen's
    // weapon chooser stores the same key as "Flail (Heavy)". A comma cannot be emitted here:
    // APPLIEDTO uses commas to separate repeated feat selections, so doing so would turn one
    // Weapon Focus choice into two feats when PCGen (or our importer) reads the file.
    private static string PcgenWeaponChoiceName(string name)
    {
        var separator = name.IndexOf(", ", StringComparison.Ordinal);
        return separator < 0
            ? name
            : $"{name[..separator]} ({name[(separator + 2)..]})";
    }

    private static string FeatureOptionName(ClassFeatureDefinition feature, string optionId, ContentRegistry registry)
    {
        var option = feature.Options.FirstOrDefault(candidate => candidate.Id == optionId);
        if (option != null) return option.Name;
        if (feature.DynamicSource?.Kind == "feat" && registry.TryGetFeat(optionId, out var feat) && feat != null)
            return feat.Name;
        return Humanize(optionId[(optionId.LastIndexOf(':') + 1)..]);
    }

    private static SpellAcquisition? Acquisition(string classId, CharacterState state, ContentRegistry registry)
    {
        if (state.Spellcasting.TryGetValue(classId, out var casting)) return casting.Acquisition;
        return registry.GetAllDrivers().OfType<HDDriver>().FirstOrDefault(driver => driver.Id == classId)
            ?.Spellcasting?.ResolvedAcquisition;
    }

    private static string PcgenSlot(EquipmentEntry entry)
    {
        if (entry.TwoHanded) return "Both Hands";
        if (entry.DoubleWeapon) return "Double Weapon";
        if (entry.MainHand && string.IsNullOrWhiteSpace(entry.Slot)) return "Primary Hand";
        if (!entry.MainHand && string.IsNullOrWhiteSpace(entry.Slot)) return "Secondary Hand";
        return entry.Slot.ToLowerInvariant() switch
        {
            "head" => "Head", "eyes" => "Eyes", "neck" => "Neck", "shoulders" => "Shoulders",
            "body" => "Body", "torso" => "Torso", "wrists" => "Arms", "hands" => "Hands",
            "ring" => "Fingers", "waist" => "Waist", "feet" => "Feet", _ => "Carried",
        };
    }

    private static string PcgenLinkType(string type) => type switch
    {
        "animal_companion" => "Animal Companion",
        "familiar" => "Familiar",
        "improved_familiar" => "Improved Familiar",
        "shadow_companion" => "Shadow Companion",
        "leadership_cohort" => "Cohort",
        "leadership_follower" => "Follower",
        "special_mount" => "Special Mount",
        "wild_cohort" => "Wild Cohort",
        _ => Humanize(type),
    };

    private static string SpecialistName(string school) => school.ToLowerInvariant() switch
    {
        "abjuration" => "Abjurer", "conjuration" => "Conjurer", "divination" => "Diviner",
        "enchantment" => "Enchanter", "evocation" => "Evoker", "illusion" => "Illusionist",
        "necromancy" => "Necromancer", "transmutation" => "Transmuter", _ => Humanize(school),
    };

    private static string Humanize(string value) => string.Join(' ', value
        .Replace('~', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private static void ReportKnownLimitations(Character character, List<PcgExportIssue> issues)
    {
        foreach (var (templateId, hd) in character.TemplateAcquisitionHD.Where(entry => entry.Value > 1))
            AddWarning(issues, "template_timing_lost", $"templateAcquisitionHD.{templateId}",
                $"PCGen applies template '{templateId}' to the saved character without preserving acquisition at HD {hd}.");
        if (character.PermanentEvents.Count > 0)
            AddWarning(issues, "unsupported_permanent_events", "permanentEvents",
                $"{character.PermanentEvents.Count} permanent event(s) cannot be represented in PCGen and were omitted.");
        var leadership = character.LeadershipModifiers;
        if (leadership.GreatRenown || leadership.FairnessAndGenerosity || leadership.SpecialPower
            || leadership.Failure || leadership.Aloofness || leadership.Cruelty
            || leadership.CohortDeathsCaused != 0 || leadership.HasStronghold
            || leadership.MovesAroundALot || leadership.CausedFollowerDeaths)
            AddWarning(issues, "unsupported_leadership_modifiers", "leadershipModifiers",
                "Campaign-specific Leadership modifiers cannot be represented in this PCGen export.");
    }

    private static PcgExportResult Result(Character character, string content, List<PcgExportIssue> issues)
    {
        var stem = Regex.Replace(character.Name.Trim().ToLowerInvariant(), @"[^a-z0-9._-]+", "_").Trim('_');
        if (stem.Length == 0) stem = "character";
        return new PcgExportResult { FileName = stem + ".pcg", Content = content, Issues = issues };
    }

    private static void AddWarning(List<PcgExportIssue> issues, string code, string path, string message) =>
        issues.Add(new PcgExportIssue { Severity = PcgExportIssueSeverity.Warning, Code = code, Path = path, Message = message });

    private static void AddError(List<PcgExportIssue> issues, string code, string path, string message) =>
        issues.Add(new PcgExportIssue { Severity = PcgExportIssueSeverity.Error, Code = code, Path = path, Message = message });

    private sealed record ClassGroup(string Name, string DriverId, HDDriver Driver)
    {
        public int Level { get; set; }
    }

    private sealed record TickRow(
        int Index,
        Tick Tick,
        HDDriver Driver,
        string ClassName,
        int DriverLevel,
        int HitPoints,
        int SkillPointsGained,
        int SkillPointsRemaining,
        CharacterState State);
}

internal sealed class PcgWriter
{
    private readonly StringBuilder _buffer = new();

    public void Line(string line) => _buffer.Append(line).Append('\n');

    public void Section(string name)
    {
        Line(string.Empty);
        Line("# " + name);
    }

    public override string ToString() => _buffer.ToString();

    public static string Encode(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            result.Append(character switch
            {
                '\\' => "\\",
                '\n' => "&nl;",
                '\r' => "&cr;",
                '\f' => "&lf;",
                ':' => "&colon;",
                '|' => "&pipe;",
                '[' => "&lbracket;",
                ']' => "&rbracket;",
                '&' => "&amp;",
                _ => character.ToString(),
            });
        }
        return result.ToString();
    }
}
