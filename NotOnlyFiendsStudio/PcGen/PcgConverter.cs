using System.Text.RegularExpressions;
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
    public List<string> DroppedSpells { get; set; } = new();
    public List<string> DroppedClassAbilities { get; set; } = new();
    public List<string> DroppedEquipment { get; set; } = new();
    public List<string> UnsupportedCustomEquipmentModifiers { get; set; } = new();
    public List<string> IgnoredTemporaryBonuses { get; set; } = new();
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
            if (DroppedSpells.Count > 0) parts.Add($"{DroppedSpells.Count} spell(s) missing");
            if (DroppedClassAbilities.Count > 0) parts.Add($"{DroppedClassAbilities.Count} class ability selection(s) missing");
            if (DroppedEquipment.Count > 0) parts.Add($"{DroppedEquipment.Count} equipment item(s) missing");
            if (IgnoredTemporaryBonuses.Count > 0) parts.Add($"{IgnoredTemporaryBonuses.Count} temporary modifier(s) ignored");
            return parts.Count == 0 ? "Clean import" : string.Join(", ", parts);
        }
    }
}

public static class PcgConverter
{
    private static readonly Regex DivineRankTemplate =
        new(@"^Divine Rank \((\d+)\+?\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The chooser shells PCGen writes beside the chosen rank; they carry no rank of their own.</summary>
    private static readonly HashSet<string> DivineRankChoosers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Divine Rank",
        "Innate Divine Rank",
    };

    /// <summary>Each band's lowest rank, used only when no numeric template accompanies it.</summary>
    private static readonly Dictionary<string, int> DivineRankBands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Quasideity"] = 0,
        ["Demigod"] = 1,
        ["Lesser Deity"] = 6,
        ["Intermediate Deity"] = 11,
        ["Greater Deity"] = 16,
        ["Overdeity"] = 21,
    };

    /// <summary>
    /// Convert parsed PCGen data to an engine Character.
    /// If registry is provided, validates that mapped IDs exist in content.
    /// Unmapped or missing items are skipped with warnings (never throws).
    /// </summary>
    public static PcgConversionResult Convert(PcgCharacterData data, PcgIdMapper mapper, ContentRegistry? registry = null)
    {
        var result = new PcgConversionResult();

        // An alternate class feature can decide which driver a class row resolves to, and it has
        // to be read before anything resolves a class name: ticks, skill purchases and spell rows
        // all map the class, and a druid-like bard's spells are still filed under "Bard". The
        // overrides are local to this conversion — the mapper is shared across a corpus.
        var classOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ability in data.ClassAbilities)
        {
            if (PcgIdMapper.TryGetClassSelectingAcf(ability.Key, out var pcgenClass, out var driverId))
                classOverrides[pcgenClass] = driverId;
        }

        // A substitution class says the same thing from the level row rather than an ability row.
        var unmappedSubstitutions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var level in data.Levels)
        {
            if (level.SubstitutionClass == null)
                continue;

            if (PcgIdMapper.TryGetSubstitutionClass(
                    level.SubstitutionClass, out var pcgenClass, out var driverId))
                classOverrides[pcgenClass] = driverId;
            else
                unmappedSubstitutions.Add(level.SubstitutionClass);
        }

        foreach (var substitution in unmappedSubstitutions.OrderBy(name => name, StringComparer.Ordinal))
            result.Warnings.Add(
                $"Substitution class '{substitution}' has no engine mapping — "
                + "the base class was built instead");

        string? MapClass(string? pcgenClass) =>
            pcgenClass != null && classOverrides.TryGetValue(pcgenClass, out var overridden)
                ? overridden
                : pcgenClass == null ? null : mapper.MapClass(pcgenClass);

        // Spells and "advance an existing spellcasting class" name the caster, which is not always
        // the driver the levels became — a monster class's levels are racial HD while its casting
        // keeps its own identity. See PcgIdMapper.MapCastingClass.
        string? MapCastingClass(string? pcgenClass) =>
            pcgenClass != null && classOverrides.TryGetValue(pcgenClass, out var overridden)
                ? overridden
                : pcgenClass == null ? null : mapper.MapCastingClass(pcgenClass);

        // PCGen uses TN for true neutral, while the engine calls that enum value N.
        // Keep neutral as the fallback: Enum.TryParse resets an out parameter to the
        // enum default on failure, which is LG for Alignment.
        var alignmentText = data.Alignment.Trim();
        if (alignmentText.Equals("TN", StringComparison.OrdinalIgnoreCase))
            alignmentText = nameof(Alignment.N);

        var alignment = Enum.TryParse<Alignment>(alignmentText, true, out var parsedAlignment)
            && Enum.IsDefined(parsedAlignment)
                ? parsedAlignment
                : Alignment.N;
        // PCGen writes "None" for godless characters; the engine models that as null.
        var deity = data.Deity.Trim();
        if (deity.Length == 0 || deity.Equals("None", StringComparison.OrdinalIgnoreCase))
            deity = null!;

        var character = new Character
        {
            Name = data.CharacterName,
            Alignment = alignment,
            Deity = deity,
            Gender = string.IsNullOrWhiteSpace(data.Gender) ? null : data.Gender.Trim(),
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

        // A PCG language row is a source assertion about the completed character, not
        // necessarily a creation-time Intelligence purchase. This matters for imported
        // high-level characters whose languages came from skills, classes, magic, or play.
        character.SourceLanguageIds = data.Languages
            .Select(PcgIdMapper.MapLanguage)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var follower in data.Followers)
        {
            var sourceReference = string.IsNullOrWhiteSpace(follower.File) ? follower.Name : follower.File;
            var linkType = MapCompanionLinkType(follower.Type);
            character.CompanionLinks.Add(new CompanionLink
            {
                LinkType = linkType,
                CompanionId = ToCharacterId(follower.Name, sourceReference),
                // The id above is only a guess: PCGen records a follower by name, and the saved
                // companion is filed under whatever id it was created with. Keeping the raw name
                // lets the host re-point the link once it can see the character store.
                SourceName = string.IsNullOrWhiteSpace(follower.Name) ? null : follower.Name.Trim(),
                SourceFile = string.IsNullOrWhiteSpace(follower.File) ? null : follower.File.Trim(),
                SelectedSpecies = mapper.MapRace(follower.Race),
                EffectiveLevelFormula = CompanionLevelFormula(linkType),
                FollowerLevel = linkType == "leadership_follower" ? follower.HitDice : 0,
                Notes = $"Imported from PCGen {follower.Type}; source file: {sourceReference}; source race: {follower.Race}",
            });

            if (sourceReference.Contains("..", StringComparison.Ordinal))
                result.Warnings.Add($"Companion '{follower.Name}' uses external relative file reference '{sourceReference}' — link preserved by character id");
        }

        if (data.Master != null)
        {
            character.CompanionOrigin = new CompanionOrigin
            {
                LinkType = MapCompanionLinkType(data.Master.Type),
                EffectiveMasterLevel = 0,
                MasterCharacterId = ToCharacterId(data.Master.Name, data.Master.File),
                SourceName = string.IsNullOrWhiteSpace(data.Master.Name) ? null : data.Master.Name.Trim(),
                SourceFile = string.IsNullOrWhiteSpace(data.Master.File) ? null : data.Master.File.Trim(),
            };
        }

        foreach (var temporaryBonus in data.TemporaryBonuses)
        {
            var label = temporaryBonus.Split('|')[0];
            result.IgnoredTemporaryBonuses.Add(label);
            result.Warnings.Add($"Active PCGen temporary modifier '{label}' is not a permanent character input — ignored");
        }

        // Map race
        var raceId = mapper.MapRace(data.Race);
        if (raceId == null)
        {
            result.Warnings.Add($"Race '{data.Race}' has no engine mapping — using 'human' as fallback");
            result.RaceDropped = true;
            character.RaceId = "race:human";
        }
        else if (registry != null && !registry.GetAllRaces().Any(r => r.Id == raceId))
        {
            result.Warnings.Add($"Race '{data.Race}' maps to '{raceId}' but not found in content — using 'human' as fallback");
            result.RaceDropped = true;
            character.RaceId = "race:human";
        }
        else
        {
            character.RaceId = raceId;
        }

        // Map templates. Divinity comes first: PCGen states it as templates, and the parser has
        // already marked those internal so the mapping loop below never sees them.
        ApplyDivineRankTemplates(data, character, result);
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

        // PCGen represents the familiar rules through internal companion modifiers such as
        // "Familiar Race Change" and "Non-Animal Base". Those implementation-only templates
        // are deliberately excluded above, but the MASTER record still tells us that this
        // character is a familiar. Restore the engine's universal familiar progression here;
        // Improved Familiar changes the eligible creature, not the progression or master level.
        var companionTemplateId = CompanionProgressionTemplate(character.CompanionOrigin?.LinkType);
        if (companionTemplateId != null
            && !character.TemplateIds.Contains(companionTemplateId, StringComparer.Ordinal))
        {
            character.TemplateIds.Add(companionTemplateId);
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
            var preferredDriver = skill.BoughtClass != null ? MapClass(skill.BoughtClass) : null;
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
                // "spell_focus:conjuration". Prestige classes gate on those variant ids;
                // storing the bare "spell_focus" would fail their prerequisites.
                var selection = raw.Trim();
                if (featDef?.SelectionRequired != null && selection.Length > 0)
                {
                    var suffix = featDef.SelectionRequired == "skill"
                        ? PcgIdMapper.MapSkillBare(selection)
                        : PcgIdMapper.DefaultIdTransform(selection);
                    featIds.Add(FeatVariantId.Canonical(featId, suffix));
                }
                else
                {
                    featIds.Add(featId);
                }
            }
        }

        // Build domain selections with their PCGen owner tick. A domain can be granted after HD 1
        // (e.g. Nymph Archdruid's Plant domain from Druid 2), so front-loading changes ownership
        // and can try to spend a domain slot before it exists.
        var domainSelections = new List<(string DomainId, string? SourceDriverId, int SourceLevel)>();
        foreach (var domain in data.Domains)
        {
            var domainId = mapper.MapDomain(domain.Name);
            if (registry != null && !registry.TryGetDomain(domainId, out _))
            {
                result.Warnings.Add($"Domain '{domain.Name}' maps to '{domainId}' but not found in content");
                result.DroppedDomains.Add(domain.Name);
                continue;
            }
            domainSelections.Add((
                domainId,
                string.IsNullOrWhiteSpace(domain.SourceClass) ? null : MapClass(domain.SourceClass),
                domain.SourceLevel));
        }

        var wizardClass = data.Classes.FirstOrDefault(c => MapClass(c.Name) == "class:wizard");

        // Build ticks from level entries, tracking which level-up ability increases
        // the engine will re-apply so we can subtract them from the imported STAT values.
        var appliedAbilityIncreases = new Dictionary<Ability, int>();
        const int abilityIncreaseInterval = 4;

        for (int i = 0; i < data.Levels.Count; i++)
        {
            var level = data.Levels[i];
            var driverId = MapClass(level.ClassName);

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

            if (level.HitPoints > 0)
                choices.HitPointsRolled = level.HitPoints;

            // Ability increase. In this ruleset racial ability adjustments live on the race;
            // PCGen PRESTAT rows on racial-HD levels must not become a second selectable bonus.
            // A PRESTAT on an unscheduled class level is likewise a stat *edit* PCGen recorded,
            // not the every-4-HD pick — the engine would ignore it and warn, so don't write it.
            if (level.AbilityIncrease != null &&
                !driverId.StartsWith("racial_hd:", StringComparison.Ordinal) &&
                (character.Ticks.Count + 1) % abilityIncreaseInterval == 0 &&
                Enum.TryParse<Ability>(level.AbilityIncrease, true, out var ability))
            {
                choices.AbilityIncrease = ability;
            }

            var domainsForTick = domainSelections
                .Where(domain => domain.SourceDriverId == null
                    ? character.Ticks.Count == 0
                    : domain.SourceDriverId == driverId
                      && (domain.SourceLevel > 0 ? domain.SourceLevel == level.ClassLevel : level.ClassLevel == 1))
                .Select(domain => domain.DomainId)
                .ToList();
            if (domainsForTick.Count > 0)
            {
                var consumesClassSlot = DriverGrantsDomainSlots(registry, driverId);
                AddClassFeatureChoices(choices,
                    consumesClassSlot ? "domains" : "imported_source_domains", domainsForTick);
            }

            if (driverId == "class:wizard" && level.ClassLevel == 1 && wizardClass?.Subclass != null)
            {
                var specialty = MapWizardSpecialty(wizardClass.Subclass);
                if (specialty != null)
                    AddClassFeatureChoices(choices, WizardSchools.SpecializationFeature,
                        new[] { WizardSchools.ToOptionId(specialty) });

                AddClassFeatureChoices(choices, WizardSchools.ProhibitedFeature,
                    wizardClass.ProhibitedSchools.Select(school =>
                        WizardSchools.ToOptionId(school.Trim().ToLowerInvariant())));
            }

            var spellcasterChoices = level.SpellcasterChoices
                .Select(MapCastingClass)
                .Where(id => id != null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (spellcasterChoices.Count > 0)
            {
                AddClassFeatureChoices(choices, "advance_spellcasting", spellcasterChoices);
            }

            character.Ticks.Add(new Tick { DriverId = driverId, Choices = choices });

            // Engine applies AbilityIncrease only on scheduled class ticks.
            // Record which increases will be re-applied so we can recover the rolled base.
            var cumulativeHD = character.Ticks.Count;
            if (cumulativeHD % abilityIncreaseInterval == 0 && choices.AbilityIncrease.HasValue)
            {
                var a = choices.AbilityIncrease.Value;
                appliedAbilityIncreases[a] = appliedAbilityIncreases.GetValueOrDefault(a) + 1;
            }
        }

        ApplyClassAbilitySelections(data, character, result, registry);

        // Feats go on the LAST valid tick. At the final HD, prerequisites
        // (caster level, BAB, skill ranks) are satisfied, the max-ranks cap covers
        // any legal allocation, and the accumulated feat-slot budget is available
        // (FeatSlots persist across ticks).
        if (character.Ticks.Count > 0 && featIds.Count > 0)
            character.Ticks[^1].Choices.FeatIds = featIds;

        // PCGen emits three superficially similar kinds of spell rows:
        //   - "Known Spells" for spontaneous casters: persistent build choices;
        //   - "Spellbook (...)" for wizards: persistent spellbook contents;
        //   - "Prepared Spells": a daily loadout, which the engine deliberately does not persist.
        // It also emits every spell available to a wizard as "Known Spells". Resolve the engine's
        // acquisition mode before accepting a row so those thousands of available-list entries do
        // not become spellbook choices.
        var spellSelections = new List<SpellSelection>();
        var reportedSpellSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reportedDroppedSpells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void ReportDroppedSpell(PcgSpellEntry spell, string warning)
        {
            var droppedKey = $"{spell.ClassName}: {spell.Name}";
            if (!reportedDroppedSpells.Add(droppedKey))
                return;

            result.Warnings.Add(warning);
            result.DroppedSpells.Add(spell.Name);
        }

        foreach (var spell in data.Spells)
        {
            var isKnownBook = spell.Book.Equals("Known Spells", StringComparison.OrdinalIgnoreCase);
            var isSpellbook = spell.Book.StartsWith("Spellbook", StringComparison.OrdinalIgnoreCase);
            if (!isKnownBook && !isSpellbook)
                continue;

            var classId = MapCastingClass(spell.ClassName);
            if (classId == null)
            {
                if (reportedSpellSources.Add(spell.ClassName))
                    result.Warnings.Add($"Spell source '{spell.ClassName}' has no engine mapping — spells skipped");
                ReportDroppedSpell(spell,
                    $"Spell '{spell.Name}' ({spell.ClassName}) has an unmapped source class — skipped");
                continue;
            }

            SpellAcquisition? acquisition = null;
            if (EpicSpellcasting.IsSpellList(classId))
            {
                acquisition = SpellAcquisition.Developed;
            }
            else if (registry != null)
            {
                var driver = registry.GetAllDrivers().OfType<HDDriver>()
                    .FirstOrDefault(d => d.Id == classId);
                acquisition = driver?.Spellcasting?.ResolvedAcquisition;
                if (acquisition == null)
                {
                    if (reportedSpellSources.Add(spell.ClassName))
                        result.Warnings.Add($"Spell source '{spell.ClassName}' maps to '{classId}' but has no modeled spellcasting — spells skipped");
                    ReportDroppedSpell(spell,
                        $"Spell '{spell.Name}' ({spell.ClassName}) has no modeled spellcasting source — skipped");
                    continue;
                }
            }
            else
            {
                acquisition = isSpellbook ? SpellAcquisition.Spellbook : SpellAcquisition.SpellsKnown;
            }

            if ((acquisition == SpellAcquisition.Spellbook && !isSpellbook)
                || (acquisition == SpellAcquisition.SpellsKnown && !isKnownBook)
                || (acquisition == SpellAcquisition.Developed && !isKnownBook)
                || acquisition == SpellAcquisition.FullList)
            {
                continue;
            }

            var spellId = mapper.MapSpell(spell.Name, registry, classId);
            if (spellId == null)
            {
                ReportDroppedSpell(spell,
                    $"Spell '{spell.Name}' ({spell.ClassName}) has no engine mapping — skipped");
                continue;
            }

            spellSelections.Add(new SpellSelection
            {
                ClassId = classId,
                SpellLevel = spell.SpellLevel,
                SpellId = spellId,
            });
        }

        if (character.Ticks.Count > 0 && spellSelections.Count > 0)
        {
            character.Ticks[^1].Choices.SpellSelections = spellSelections
                .DistinctBy(s => (s.ClassId, s.SpellLevel, s.SpellId))
                .OrderBy(s => s.ClassId, StringComparer.Ordinal)
                .ThenBy(s => s.SpellLevel)
                .ThenBy(s => s.SpellId, StringComparer.Ordinal)
                .ToList();
        }

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
                Quantity = raw.Quantity,
                WeightLbsOverride = raw.WeightLbs,
                PriceCpOverride = raw.PriceCp,
            };

            // Prefer a mechanically modeled base item over a name-only private-pack stub.
            // Inline PCGen customization below then layers the custom magic onto that base.
            if (registry != null
                && !string.IsNullOrWhiteSpace(raw.BaseItemName)
                && registry.TryGetEquipment(id, out var mappedDefinition)
                && mappedDefinition != null
                && !HasEquipmentMechanics(mappedDefinition))
            {
                var baseId = mapper.MapEquipment(raw.BaseItemName, registry);
                if (baseId != null
                    && registry.TryGetEquipment(baseId, out var baseDefinition)
                    && baseDefinition != null
                    && HasEquipmentMechanics(baseDefinition))
                {
                    entry.ContentId = baseId;
                }
            }

            ApplyCustomEquipmentModifiers(raw, entry, mapper, registry, result);

            if (raw.InActiveSet && raw.SlotName != null && mapper.IsWeaponSlot(raw.SlotName))
            {
                var (mh, th, doubleWeapon) = mapper.InferHand(raw.SlotName);
                entry.Slot = string.Empty;
                entry.MainHand = mh;
                entry.TwoHanded = th;
                entry.DoubleWeapon = doubleWeapon;
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

        if (registry != null)
        {
            RemoveGrantedConsequenceTemplates(character, registry);
            StampAcquiredTemplateHDs(character, registry, result);
        }

        result.Character = character;
        return result;
    }

    /// <summary>
    /// PCGen has no divine-rank field: it states divinity as a chain of templates — the
    /// <c>Divine Rank</c> chooser, the chosen <c>Divine Rank (N)</c>, and the band name
    /// (<c>Quasideity</c>, <c>Demigod</c>, …). Only the numeric one carries anything the engine
    /// cannot derive for itself — <c>DivineRankRules.Status</c> re-derives the band from the rank —
    /// so that one sets <see cref="Character.Divinity"/> and the rest are consumed.
    ///
    /// A band with no numeric template beside it still means the character is a deity. Rather than
    /// drop that, take the band's lowest rank and say so: the alternative is a sheet that quietly
    /// stops being divine.
    /// </summary>
    private static void ApplyDivineRankTemplates(
        PcgCharacterData data, Character character, PcgConversionResult result)
    {
        int? rank = null;
        string? band = null;

        foreach (var template in data.Templates)
        {
            var match = DivineRankTemplate.Match(template.Name);
            if (match.Success)
            {
                // "Divine Rank (21+)" is PCGen's overdeity row. Import it as 21 rather than
                // inventing a ceiling: the engine warns about the missing rank table itself.
                if (int.TryParse(match.Groups[1].Value, out var value))
                    rank = rank.HasValue ? Math.Max(rank.Value, value) : value;
                continue;
            }

            if (DivineRankBands.ContainsKey(template.Name))
                band ??= template.Name;
        }

        if (rank == null && band != null)
        {
            rank = DivineRankBands[band];
            result.Warnings.Add(
                $"Divine band template '{band}' has no 'Divine Rank (N)' beside it — imported as divine rank {rank}");
        }

        if (rank == null) return;

        character.Divinity = new DivinityChoices { DivineRank = rank.Value };
    }

    /// <summary>
    /// True for the templates PCGen uses to state divine rank. The engine models these as
    /// <see cref="Character.Divinity"/> rather than as templates, so the parser marks them
    /// internal and <see cref="ApplyDivineRankTemplates"/> reads the rank out of them instead.
    /// </summary>
    public static bool IsDivineRankTemplate(string name) =>
        DivineRankTemplate.IsMatch(name)
        || DivineRankChoosers.Contains(name)
        || DivineRankBands.ContainsKey(name);

    /// <summary>
    /// PCGen materializes every template a transformation drags along as its own
    /// TEMPLATESAPPLIED row — a lich sheet also carries Undead and Augmented Humanoid, and a
    /// capstone-transformed character carries the templates its class grants. The engine
    /// models those as ApplyTemplate grants (on the parent template's creation buffs, or on
    /// the class's level buffs), so the granted ids must come off the character: left in
    /// place they would apply at creation and rewrite the timeline the grant is there to
    /// keep honest.
    /// </summary>
    private static void RemoveGrantedConsequenceTemplates(Character character, ContentRegistry registry)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>();

        // Seeds: templates the character's classes grant at levels the timeline reaches…
        var levelsReached = character.Ticks
            .GroupBy(t => t.DriverId)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var hd in registry.GetAllDrivers().OfType<HDDriver>())
        {
            if (!levelsReached.TryGetValue(hd.Id, out var reached))
                continue;
            foreach (var (level, buffs) in hd.LevelPermabuffs)
                if (level <= reached)
                    foreach (var buff in buffs.OfType<ApplyTemplate>())
                    {
                        granted.Add(buff.TemplateId);
                        frontier.Enqueue(buff.TemplateId);
                    }
        }

        // …and every template already on the character starts a chain of its own.
        foreach (var templateId in character.TemplateIds)
            frontier.Enqueue(templateId);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (frontier.Count > 0)
        {
            var id = frontier.Dequeue();
            if (!visited.Add(id) || !registry.TryGetTemplate(id, out var template) || template == null)
                continue;
            var chained = template.CreationPermabuffs.OfType<ApplyTemplate>()
                .Concat(template.ScalingPermabuffs.Values.SelectMany(buffs => buffs.OfType<ApplyTemplate>()));
            foreach (var buff in chained)
            {
                granted.Add(buff.TemplateId);
                frontier.Enqueue(buff.TemplateId);
            }
        }

        character.TemplateIds.RemoveAll(granted.Contains);
        foreach (var id in granted)
            character.TemplateAcquisitionHD.Remove(id);
    }

    /// <summary>
    /// A template with acquisition prerequisites was earned mid-career, but the .pcg records
    /// nothing about when — PCGen has no acquisition level. Default to the earliest HD the
    /// prerequisites allow; the builder surfaces the value for the player to correct.
    /// </summary>
    private static void StampAcquiredTemplateHDs(Character character, ContentRegistry registry, PcgConversionResult result)
    {
        var studio = new ReplayStudio(registry);
        foreach (var templateId in character.TemplateIds.ToList())
        {
            if (character.TemplateAcquisitionHD.ContainsKey(templateId)
                || !registry.TryGetTemplate(templateId, out var template)
                || template == null
                || template.Prerequisites.Count == 0)
                continue;

            try
            {
                var acquisitionHD = studio.FindEarliestAcquisitionHD(character, templateId);
                if (acquisitionHD is > 1)
                    character.TemplateAcquisitionHD[templateId] = acquisitionHD.Value;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Could not derive an acquisition HD for template '{templateId}' — applied at creation ({ex.Message})");
            }
        }
    }

    private static bool HasEquipmentMechanics(EquipmentDefinition definition) =>
        definition.Weapon != null
        || definition.Armor != null
        || definition.EnhancementBonus != 0
        || definition.GrantedPermabuffs.Count > 0;

    private static void ApplyCustomEquipmentModifiers(
        PcgEquipmentRaw raw,
        EquipmentEntry entry,
        PcgIdMapper mapper,
        ContentRegistry? registry,
        PcgConversionResult result)
    {
        if (string.IsNullOrWhiteSpace(raw.Customization))
            return;

        var eqmod = Regex.Match(raw.Customization, @"(?:^|\$)EQMOD=(?<value>[^$]+)");
        if (!eqmod.Success)
            return;

        var text = eqmod.Groups["value"].Value;
        var supported = new HashSet<string>(StringComparer.Ordinal)
        {
            "EPIC_ABILITY_BONUS_ENHANCE",
            "EPIC_NATURAL_ARMR_ENHANCE",
            "BNS_SKL_CMP",
            "PLUS_10_WEAP",
        };

        supported.UnionWith(ApplyIntelligentItemModifiers(text, entry));

        foreach (Match match in Regex.Matches(text,
                     @"(?:^|\.)(?<key>PLUS(?<value>[1-5])[WAS])(?=\.|$)",
                     RegexOptions.IgnoreCase))
        {
            entry.EnhancementBonusOverride = int.Parse(match.Groups["value"].Value);
            supported.Add(match.Groups["key"].Value.ToUpperInvariant());
        }

        foreach (Match match in Regex.Matches(text,
                     @"EPIC_ABILITY_BONUS_ENHANCE&pipe;(?<ability>STR|DEX|CON|INT|WIS|CHA)=\+(?<value>\d+)",
                     RegexOptions.IgnoreCase))
        {
            var target = match.Groups["ability"].Value.ToUpperInvariant() switch
            {
                "STR" => BonusTarget.AbilityStr,
                "DEX" => BonusTarget.AbilityDex,
                "CON" => BonusTarget.AbilityCon,
                "INT" => BonusTarget.AbilityInt,
                "WIS" => BonusTarget.AbilityWis,
                _ => BonusTarget.AbilityCha,
            };
            entry.Permabuffs.Add(new GrantTypedBonus
            {
                Target = target,
                BonusType = BonusType.Enhancement,
                Value = new Formula(match.Groups["value"].Value),
            });
        }

        foreach (Match match in Regex.Matches(text,
                     @"EPIC_NATURAL_ARMR_ENHANCE&pipe;\+(?<value>\d+)",
                     RegexOptions.IgnoreCase))
        {
            entry.Permabuffs.Add(new GrantTypedBonus
            {
                Target = BonusTarget.AC,
                BonusType = BonusType.NaturalEnhancement,
                Value = new Formula(match.Groups["value"].Value),
            });
        }

        foreach (Match modifier in Regex.Matches(text,
                     @"BNS_SKL_CMP&pipe;(?<choices>.*?)(?=\.[A-Z][A-Z0-9_]*(?:&pipe;|\.|$)|$)",
                     RegexOptions.IgnoreCase))
        {
            foreach (var choice in modifier.Groups["choices"].Value.Split("&pipe;", StringSplitOptions.RemoveEmptyEntries))
            {
                var skillBonus = Regex.Match(choice, @"^(?<skill>.+?)=\+(?<value>\d+)$");
                if (!skillBonus.Success)
                    continue;

                var skillId = mapper.MapSkill(skillBonus.Groups["skill"].Value.Trim());
                if (registry != null && !registry.TryGetSkill(skillId, out _))
                {
                    var warning = $"{raw.Name}: BNS_SKL_CMP ({skillBonus.Groups["skill"].Value.Trim()})";
                    result.UnsupportedCustomEquipmentModifiers.Add(warning);
                    continue;
                }

                entry.Permabuffs.Add(new GrantEquipmentSkillBonus
                {
                    SkillId = skillId,
                    BonusType = BonusType.Competence,
                    Value = new Formula(skillBonus.Groups["value"].Value),
                });
            }
        }

        if (Regex.IsMatch(text, @"(?:^|\.)PLUS_10_WEAP(?:&pipe;|\.|$)", RegexOptions.IgnoreCase))
            entry.EnhancementBonusOverride = 10;

        var unsupported = Regex.Matches(text, @"(?:^|\.)(?<key>[A-Z][A-Z0-9_]+)(?=&pipe;|\.|$)")
            .Select(match => match.Groups["key"].Value)
            .Where(key => !supported.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var key in unsupported)
        {
            var warning = $"{raw.Name}: {key}";
            result.UnsupportedCustomEquipmentModifiers.Add(warning);
        }
    }

    private static readonly HashSet<string> IntelligentLesserPowerKeys = new(StringComparer.Ordinal)
    {
        "INT_ITEM_BLESS", "INT_ITEM_BLUFF", "INT_ITEM_CURE_MODERATE", "INT_ITEM_DARKNESS",
        "INT_ITEM_DAZE_MONSTER", "INT_ITEM_DEATHWATCH", "INT_ITEM_DECIPHER_SCRIPT",
        "INT_ITEM_DETECT_MGC", "INT_ITEM_DIPLOMACY", "INT_ITEM_FAERIE", "INT_ITEM_HOLD_PERSON",
        "INT_ITEM_INTIMIDATE", "INT_ITEM_KNOWLEDGE", "INT_ITEM_LISTEN", "INT_ITEM_LOCATE_OBJECT",
        "INT_ITEM_MAJOR_IMG", "INT_ITEM_MINOR_IMG", "INT_ITEM_SEARCH", "INT_ITEM_SENSE_MOTIVE",
        "INT_ITEM_SPELLCRAFT", "INT_ITEM_SPOT", "INT_ITEM_ZONE_TRUTH",
    };

    private static readonly HashSet<string> IntelligentGreaterPowerKeys = new(StringComparer.Ordinal)
    {
        "INT_ITEM_ARCANE_EYE", "INT_ITEM_CAUSE_FEAR", "INT_ITEM_CIRCLE_AGAINST_CHAOS",
        "INT_ITEM_CIRCLE_AGAINST_EVIL", "INT_ITEM_CIRCLE_AGAINST_GOOD", "INT_ITEM_CIRCLE_AGAINST_LAW",
        "INT_ITEM_CLAIR", "INT_ITEM_DAYLGT", "INT_ITEM_DEEPER_DARKNESS", "INT_ITEM_DETECT_CHAOS",
        "INT_ITEM_DETECT_EVIL", "INT_ITEM_DETECT_GOOD", "INT_ITEM_DETECT_LAW", "INT_ITEM_DETECT_SCRY",
        "INT_ITEM_DETECT_THOUGHTS", "INT_ITEM_DETECT_UNDEAD", "INT_ITEM_DIMEN_ANCHOR",
        "INT_ITEM_DISMISSAL", "INT_ITEM_FEAR", "INT_ITEM_GUST", "INT_ITEM_HASTE",
        "INT_ITEM_INVIS_PURGE", "INT_ITEM_LESS_GLOBE_INVLN", "INT_ITEM_LOCATE_CREATURE",
        "INT_ITEM_QUENCH", "INT_ITEM_SLOW", "INT_ITEM_STATUS", "INT_ITEM_WALL_FIRE",
    };

    private static readonly HashSet<string> IntelligentDedicatedPowerKeys = new(StringComparer.Ordinal)
    {
        "INT_ITEM_CONFUSION", "INT_ITEM_CONTAGION", "INT_ITEM_CRUSHING_DESPAIR",
        "INT_ITEM_DIMENSION_DOOR", "INT_ITEM_FIREBALL", "INT_ITEM_GRTR_SHOUT", "INT_ITEM_ICE_STORM",
        "INT_ITEM_LGHTNG_BOLT", "INT_ITEM_LUCK", "INT_ITEM_MASS_INFLICT_LGT_WOUNDS",
        "INT_ITEM_PHANTASMAL_KILLER", "INT_ITEM_POISON", "INT_ITEM_PRYING_EYES",
        "INT_ITEM_RUSTING_GRASP", "INT_ITEM_SONG_DISCORD", "INT_ITEM_TRUE_RESURRECTION",
        "INT_ITEM_WAVES_EXHAUSTION",
    };

    private static readonly Dictionary<string, string> IntelligentPurposes = new(StringComparer.Ordinal)
    {
        ["INT_ITEM_DEFEAT_ALL"] = "Defeat/slay all",
        ["INT_ITEM_DEFEAT_ARCANE"] = "Defeat/slay arcane spellcasters",
        ["INT_ITEM_DEFEAT_CHAOS"] = "Defeat/slay chaos",
        ["INT_ITEM_DEFEAT_DEITY"] = "Defeat/slay servants of a deity",
        ["INT_ITEM_DEFEAT_DIVINE"] = "Defeat/slay divine spellcasters",
        ["INT_ITEM_DEFEAT_EVIL"] = "Defeat/slay evil",
        ["INT_ITEM_DEFEAT_GOOD"] = "Defeat/slay good",
        ["INT_ITEM_DEFEAT_LAW"] = "Defeat/slay law",
        ["INT_ITEM_DEFEAT_NONSPELL"] = "Defeat/slay nonspellcasters",
        ["INT_ITEM_DEFEAT_RACE"] = "Defeat/slay a particular race",
        ["INT_ITEM_DEFEAT_TYPE"] = "Defeat/slay a particular creature type",
        ["INT_ITEM_DEFEND_DEITY"] = "Defend servants and interests of a deity",
        ["INT_ITEM_DEFEND_RACE"] = "Defend a particular race or kind of creature",
    };

    /// <summary>
    /// PCGen serializes an intelligent custom item as a dot-separated EQMOD program. Resolve the
    /// stable RSRD keys into the same per-instance model the builder authors; unknown keys remain
    /// visible through UnsupportedCustomEquipmentModifiers instead of being silently discarded.
    /// </summary>
    private static HashSet<string> ApplyIntelligentItemModifiers(string text, EquipmentEntry entry)
    {
        var tokens = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (!tokens.Any(token => token is "INT_ITEM" or "EPIC_INT_ITEM"
            || Regex.IsMatch(token, @"^[A-Z0-9]+_INT_ITEM_[1-8](?:&pipe;|$)")))
            return new HashSet<string>(StringComparer.Ordinal);

        var recognized = new HashSet<string>(StringComparer.Ordinal) { "INT_ITEM", "EPIC_INT_ITEM" };
        var intelligent = new IntelligentItemDefinition();
        entry.IntelligentItemOverride = intelligent;

        foreach (var token in tokens)
        {
            var key = token.Split("&pipe;", 2, StringSplitOptions.None)[0];
            if (Regex.IsMatch(key, @"^EPIC_INT_CAP_(10|11|12|14|16|18)$"))
            {
                recognized.Add(key);
                continue;
            }
            if (key.StartsWith("EPIC_INT_COMM_", StringComparison.Ordinal))
            {
                intelligent.Communication = key switch
                {
                    "EPIC_INT_COMM_SPEECH" => IntelligentItemCommunication.Speech,
                    "EPIC_INT_COMM_TEL" => IntelligentItemCommunication.Telepathy,
                    "EPIC_INT_COMM_SPEECH_TEL" => IntelligentItemCommunication.SpeechAndTelepathy,
                    _ => IntelligentItemCommunication.Empathy,
                };
                intelligent.BasePriceModifierGp += key switch
                {
                    "EPIC_INT_COMM_SEMI" => 1_000,
                    "EPIC_INT_COMM_EMPA" => 2_000,
                    "EPIC_INT_COMM_SPEECH" => 3_000,
                    "EPIC_INT_COMM_TEL" => 5_000,
                    _ => 8_000,
                };
                recognized.Add(key);
                continue;
            }
            if (key.StartsWith("EPIC_INT_PRI_", StringComparison.Ordinal)
                || key.StartsWith("EPIC_INT_EX_", StringComparison.Ordinal)
                || key.StartsWith("EPIC_INT_AW_", StringComparison.Ordinal))
            {
                var prefix = key.StartsWith("EPIC_INT_PRI_", StringComparison.Ordinal)
                    ? "EPIC_INT_PRI_"
                    : key.StartsWith("EPIC_INT_EX_", StringComparison.Ordinal)
                        ? "EPIC_INT_EX_"
                        : "EPIC_INT_AW_";
                var epicKind = prefix == "EPIC_INT_PRI_"
                    ? IntelligentItemPowerKind.Lesser
                    : IntelligentItemPowerKind.Greater;
                intelligent.Powers.Add(new IntelligentItemPower
                {
                    Kind = epicKind,
                    Name = HumanizePcgenKey(key[prefix.Length..]),
                    BasePriceModifierGp = prefix switch
                    {
                        "EPIC_INT_PRI_" => 10_000,
                        "EPIC_INT_EX_" => 35_000,
                        _ => 100_000,
                    },
                    Description = $"Imported from PCGen epic equipment modifier {key}.",
                });
                recognized.Add(key);
                continue;
            }
            if (key == "EPIC_INT_SPECIAL_PURPOSE")
            {
                intelligent.SpecialPurpose ??= "Epic special purpose";
                intelligent.BasePriceModifierGp += 50_000;
                recognized.Add(key);
                continue;
            }
            var tierMatch = Regex.Match(key, @"^[A-Z0-9]+_INT_ITEM_(?<tier>[1-8])$");
            if (tierMatch.Success)
            {
                var tier = int.Parse(tierMatch.Groups["tier"].Value);
                ApplyIntelligentCapabilityTier(intelligent, tier);
                recognized.Add(key);
                continue;
            }

            var abilityMatch = Regex.Match(key, @"^INT_ITEM_(?<ability>INT|WIS|CHA)_(?<score>\d+)$");
            if (abilityMatch.Success)
            {
                var score = int.Parse(abilityMatch.Groups["score"].Value);
                switch (abilityMatch.Groups["ability"].Value)
                {
                    case "INT": intelligent.MentalAbilities.Intelligence = score; break;
                    case "WIS": intelligent.MentalAbilities.Wisdom = score; break;
                    case "CHA": intelligent.MentalAbilities.Charisma = score; break;
                }
                recognized.Add(key);
                continue;
            }

            var alignmentMatch = Regex.Match(key, @"^INT_ITEM_ALIGN_(?<alignment>CE|CG|CN|LE|LG|LN|N|NE|NG)$");
            if (alignmentMatch.Success
                && Enum.TryParse<Alignment>(alignmentMatch.Groups["alignment"].Value, out var alignment))
            {
                intelligent.Alignment = alignment;
                recognized.Add(key);
                continue;
            }

            if (Regex.IsMatch(key, @"^INT_ITEM_LANG_[1-4]$") && token.Contains("&pipe;"))
            {
                var language = token[(token.IndexOf("&pipe;", StringComparison.Ordinal) + "&pipe;".Length)..];
                intelligent.LanguageIds.Add(PcgIdMapper.MapLanguage(language));
                recognized.Add(key);
                continue;
            }

            if (IntelligentPurposes.TryGetValue(key, out var purpose))
            {
                var choiceAt = token.IndexOf("&pipe;", StringComparison.Ordinal);
                intelligent.SpecialPurpose = choiceAt < 0
                    ? purpose
                    : $"{purpose}: {token[(choiceAt + "&pipe;".Length)..]}";
                recognized.Add(key);
                continue;
            }

            if (key == "INT_ITEM_DED_PURP")
            {
                recognized.Add(key);
                continue;
            }

            IntelligentItemPowerKind? kind = IntelligentLesserPowerKeys.Contains(key)
                ? IntelligentItemPowerKind.Lesser
                : IntelligentGreaterPowerKeys.Contains(key)
                    ? IntelligentItemPowerKind.Greater
                    : IntelligentDedicatedPowerKeys.Contains(key)
                        ? IntelligentItemPowerKind.Dedicated
                        : null;
            if (!kind.HasValue) continue;

            var power = new IntelligentItemPower
            {
                Kind = kind.Value,
                Name = IntelligentPowerName(key),
                BasePriceModifierGp = IntelligentPowerPriceGp(key),
                Description = $"Imported from PCGen equipment modifier {key}.",
            };
            if (kind == IntelligentItemPowerKind.Dedicated)
                intelligent.DedicatedPower = power;
            else
                intelligent.Powers.Add(power);
            recognized.Add(key);
        }

        return recognized;
    }

    private static void ApplyIntelligentCapabilityTier(IntelligentItemDefinition item, int tier)
    {
        item.BasePriceModifierGp = tier switch
        {
            1 => 1_000, 2 => 2_000, 3 => 4_000, 4 => 5_000,
            5 => 6_000, 6 => 9_000, 7 => 12_000, _ => 15_000,
        };
        item.Communication = tier switch
        {
            <= 2 => IntelligentItemCommunication.Empathy,
            <= 5 => IntelligentItemCommunication.Speech,
            _ => IntelligentItemCommunication.SpeechAndTelepathy,
        };
        item.Senses.RangeFt = tier switch { 1 => 30, 2 or 4 or 5 => 60, _ => 120 };
        item.Senses.Vision = tier >= 4 ? IntelligentItemVision.Darkvision : IntelligentItemVision.Vision;
        item.Senses.ReadsSpokenLanguages = tier >= 5;
        item.Senses.ReadsAllLanguages = tier >= 7;
        item.Senses.ReadsMagic = tier >= 7;
        item.Senses.Blindsense = tier >= 7;
    }

    private static string IntelligentPowerName(string key)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["INT_ITEM_CLAIR"] = "Clairvoyance",
            ["INT_ITEM_CURE_MODERATE"] = "Cure moderate wounds",
            ["INT_ITEM_DAYLGT"] = "Daylight",
            ["INT_ITEM_DETECT_MGC"] = "Detect magic",
            ["INT_ITEM_DIMEN_ANCHOR"] = "Dimensional anchor",
            ["INT_ITEM_FAERIE"] = "Faerie fire",
            ["INT_ITEM_GRTR_SHOUT"] = "Greater shout",
            ["INT_ITEM_GUST"] = "Gust of wind",
            ["INT_ITEM_INVIS_PURGE"] = "Invisibility purge",
            ["INT_ITEM_LESS_GLOBE_INVLN"] = "Lesser globe of invulnerability",
            ["INT_ITEM_LGHTNG_BOLT"] = "Lightning bolt",
            ["INT_ITEM_MAJOR_IMG"] = "Major image",
            ["INT_ITEM_MASS_INFLICT_LGT_WOUNDS"] = "Mass inflict light wounds",
            ["INT_ITEM_MINOR_IMG"] = "Minor image",
            ["INT_ITEM_WALL_FIRE"] = "Wall of fire",
            ["INT_ITEM_WAVES_EXHAUSTION"] = "Waves of exhaustion",
        };
        if (overrides.TryGetValue(key, out var name)) return name;
        var words = key["INT_ITEM_".Length..].Split('_').Select(word => word.ToLowerInvariant());
        var text = string.Join(' ', words);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string HumanizePcgenKey(string key)
    {
        var text = string.Join(' ', key.Split('_').Select(word => word.ToLowerInvariant()));
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static int IntelligentPowerPriceGp(string key) => key switch
    {
        "INT_ITEM_BLESS" => 1_000,
        "INT_ITEM_FAERIE" => 1_100,
        "INT_ITEM_MINOR_IMG" => 2_200,
        "INT_ITEM_DEATHWATCH" => 2_700,
        "INT_ITEM_DETECT_MGC" => 3_600,
        "INT_ITEM_MAJOR_IMG" => 5_400,
        "INT_ITEM_BLUFF" or "INT_ITEM_DECIPHER_SCRIPT" or "INT_ITEM_DIPLOMACY"
            or "INT_ITEM_INTIMIDATE" or "INT_ITEM_KNOWLEDGE" or "INT_ITEM_LISTEN"
            or "INT_ITEM_SEARCH" or "INT_ITEM_SENSE_MOTIVE" or "INT_ITEM_SPELLCRAFT"
            or "INT_ITEM_SPOT" => 5_000,
        "INT_ITEM_CURE_MODERATE" or "INT_ITEM_DARKNESS" or "INT_ITEM_DAZE_MONSTER"
            or "INT_ITEM_HOLD_PERSON" or "INT_ITEM_LOCATE_OBJECT" or "INT_ITEM_ZONE_TRUTH" => 6_500,
        "INT_ITEM_CAUSE_FEAR" or "INT_ITEM_DETECT_CHAOS" or "INT_ITEM_DETECT_EVIL"
            or "INT_ITEM_DETECT_GOOD" or "INT_ITEM_DETECT_LAW" or "INT_ITEM_DETECT_UNDEAD" => 7_200,
        "INT_ITEM_ARCANE_EYE" or "INT_ITEM_DETECT_SCRY" or "INT_ITEM_DIMEN_ANCHOR"
            or "INT_ITEM_DISMISSAL" or "INT_ITEM_LESS_GLOBE_INVLN" or "INT_ITEM_WALL_FIRE" => 10_000,
        "INT_ITEM_GUST" or "INT_ITEM_STATUS" => 11_000,
        "INT_ITEM_CIRCLE_AGAINST_CHAOS" or "INT_ITEM_CIRCLE_AGAINST_EVIL"
            or "INT_ITEM_CIRCLE_AGAINST_GOOD" or "INT_ITEM_CIRCLE_AGAINST_LAW"
            or "INT_ITEM_CLAIR" or "INT_ITEM_DAYLGT" or "INT_ITEM_DEEPER_DARKNESS"
            or "INT_ITEM_HASTE" or "INT_ITEM_INVIS_PURGE" or "INT_ITEM_QUENCH" or "INT_ITEM_SLOW" => 16_000,
        "INT_ITEM_FEAR" or "INT_ITEM_LOCATE_CREATURE" => 30_000,
        "INT_ITEM_DETECT_THOUGHTS" => 44_000,
        "INT_ITEM_CONFUSION" or "INT_ITEM_CRUSHING_DESPAIR" or "INT_ITEM_ICE_STORM"
            or "INT_ITEM_PHANTASMAL_KILLER" => 50_000,
        "INT_ITEM_CONTAGION" or "INT_ITEM_POISON" or "INT_ITEM_RUSTING_GRASP" => 56_000,
        "INT_ITEM_FIREBALL" or "INT_ITEM_LGHTNG_BOLT" => 60_000,
        "INT_ITEM_LUCK" => 80_000,
        "INT_ITEM_MASS_INFLICT_LGT_WOUNDS" or "INT_ITEM_PRYING_EYES" or "INT_ITEM_SONG_DISCORD" => 81_000,
        "INT_ITEM_GRTR_SHOUT" => 130_000,
        "INT_ITEM_WAVES_EXHAUSTION" => 164_000,
        "INT_ITEM_TRUE_RESURRECTION" => 200_000,
        _ => 0,
    };

    private static bool DriverGrantsDomainSlots(ContentRegistry? registry, string driverId)
    {
        if (registry == null)
            return driverId is "class:cleric" or "class:cloistered_cleric";

        HDDriver? driver;
        try
        {
            driver = registry.GetDriver(driverId) as HDDriver;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }

        return driver != null && (driver.PerLevelPermabuffs.OfType<GrantDomainSelection>().Any()
            || driver.LevelPermabuffs.Values.SelectMany(value => value)
                .OfType<GrantDomainSelection>().Any());
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

    private static void ApplyClassAbilitySelections(
        PcgCharacterData data,
        Character character,
        PcgConversionResult result,
        ContentRegistry? registry)
    {
        if (data.ClassAbilities.Count == 0)
            return;

        if (registry == null)
        {
            foreach (var ability in data.ClassAbilities)
            {
                if (IsAnimalTrick(ability) || IsMarkerConsumedElsewhere(ability))
                    continue;
                result.DroppedClassAbilities.Add(ability.Key);
                result.Warnings.Add($"Class ability '{ability.Key}' was not resolved because content validation was unavailable");
            }
            return;
        }

        var grantTicks = BuildClassFeatureGrantTicks(character.Ticks, registry);
        var usedGrantTicks = new HashSet<(string FeatureType, int TickIndex)>();

        foreach (var ability in data.ClassAbilities)
        {
            // PCGen stores trained animal tricks in the class-ability section even
            // though they are creature properties, not class-feature choices. The
            // engine does not model the trick capacity yet, so deliberately ignore
            // these entries instead of reporting harmless import noise as a missing
            // class feature.
            if (IsAnimalTrick(ability))
                continue;

            // Already consumed at the top of Convert, where it chose the class's driver.
            // There is no per-tick selection left to make.
            if (PcgIdMapper.IsClassSelectingAcf(ability.Key))
                continue;

            // Markers whose information imports through another path entirely:
            // *LANGBONUS rows duplicate the LANGUAGE list (and the campaign's LST files
            // were mass-edited to LANGBONUS:any, so they never match an authored bonus
            // list), and "Epic Spellcaster (X Spellstat)" records the stat that the
            // engine already takes from the epic spell selections' classId
            // (class:epic_spells_cha). Dropping them as missing selections is noise.
            if (IsMarkerConsumedElsewhere(ability))
                continue;

            var mapped = MapClassAbility(ability, registry);
            if (mapped == null)
            {
                result.DroppedClassAbilities.Add(ability.Key);
                result.Warnings.Add($"Class ability '{ability.Key}' selection '{ability.AppliedTo}' has no matching class-feature option");
                continue;
            }

            // A dynamic feat-sourced pool records all its takings in one APPLIEDTO list
            // ("Enlarge Spell,Extend Spell,…"); each pick consumes its own grant tick.
            // Static features keep the one-entry-one-pick semantics.
            var optionIds = new List<string> { mapped.Value.OptionId };
            if (!string.IsNullOrWhiteSpace(ability.AppliedTo)
                && registry.TryGetClassFeature(mapped.Value.FeatureType, out var featureDef)
                && featureDef?.DynamicSource?.Kind == "feat")
            {
                foreach (var extra in ability.AppliedTo.Split(',').Skip(1))
                {
                    var extraKey = PcgIdMapper.DefaultIdTransform(extra.Trim());
                    var extraOption = MatchDynamicFeatOption(registry, featureDef, extraKey);
                    if (extraOption != null)
                    {
                        optionIds.Add(extraOption);
                    }
                    else
                    {
                        result.DroppedClassAbilities.Add($"{ability.Key} ~ {extra.Trim()}");
                        result.Warnings.Add($"Class ability '{ability.Key}' selection '{extra.Trim()}' has no matching class-feature option");
                    }
                }
            }

            foreach (var optionId in optionIds)
            {
                var grant = grantTicks
                    .Where(candidate => candidate.FeatureType == mapped.Value.FeatureType
                        && !usedGrantTicks.Contains((candidate.FeatureType, candidate.TickIndex))
                        && (string.IsNullOrWhiteSpace(ability.ClassName)
                            || candidate.DriverName.Equals(ability.ClassName, StringComparison.OrdinalIgnoreCase)
                            || candidate.DriverId.Equals(ability.ClassName, StringComparison.OrdinalIgnoreCase))
                        && (ability.ClassLevel <= 0 || candidate.DriverLevel == ability.ClassLevel))
                    .OrderBy(candidate => candidate.TickIndex)
                    .FirstOrDefault();

                if (grant == null)
                {
                    result.DroppedClassAbilities.Add(ability.Key);
                    result.Warnings.Add($"Class ability '{ability.Key}' selection '{optionId}' has no matching pending tick");
                    continue;
                }

                AddClassFeatureChoices(character.Ticks[grant.TickIndex].Choices,
                    mapped.Value.FeatureType, new[] { optionId });
                usedGrantTicks.Add((grant.FeatureType, grant.TickIndex));
            }
        }
    }

    private static bool IsAnimalTrick(PcgClassAbilityEntry ability) =>
        ability.Key.StartsWith("Animal Trick", StringComparison.OrdinalIgnoreCase)
        || ability.AppliedTo?.StartsWith("Animal Trick", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsMarkerConsumedElsewhere(PcgClassAbilityEntry ability) =>
        ability.Key.Equals("*LANGBONUS", StringComparison.OrdinalIgnoreCase)
        || ability.Key.StartsWith("Epic Spellcaster (", StringComparison.OrdinalIgnoreCase);

    private static List<ClassFeatureGrantTick> BuildClassFeatureGrantTicks(
        List<Tick> ticks,
        ContentRegistry registry)
    {
        var levelsByDriver = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<ClassFeatureGrantTick>();

        for (var tickIndex = 0; tickIndex < ticks.Count; tickIndex++)
        {
            Driver driver;
            try
            {
                driver = registry.GetDriver(ticks[tickIndex].DriverId);
            }
            catch (KeyNotFoundException)
            {
                continue;
            }

            if (driver is not HDDriver hd)
                continue;

            var driverLevel = levelsByDriver.GetValueOrDefault(hd.Id) + 1;
            levelsByDriver[hd.Id] = driverLevel;
            if (!hd.LevelPermabuffs.TryGetValue(driverLevel, out var permabuffs))
                continue;

            foreach (var grant in permabuffs.OfType<GrantClassFeatureSelection>())
            {
                result.Add(new ClassFeatureGrantTick
                {
                    FeatureType = grant.FeatureType,
                    TickIndex = tickIndex,
                    DriverId = hd.Id,
                    DriverName = hd.Name,
                    DriverLevel = driverLevel,
                });
            }
        }

        return result;
    }

    private static (string FeatureType, string OptionId)? MapClassAbility(
        PcgClassAbilityEntry ability,
        ContentRegistry registry)
    {
        var applied = string.IsNullOrWhiteSpace(ability.AppliedTo)
            ? null
            : PcgIdMapper.DefaultIdTransform(ability.AppliedTo.Split(',')[0].Trim());

        // A key with no tilde carries its choice in APPLIEDTO, or is itself the choice.
        var wholeKey = PcgIdMapper.DefaultIdTransform(ability.Key);
        var match = MatchFeatureOption(registry, wholeKey, applied ?? wholeKey);
        if (match != null)
            return match;

        // PCGen also writes a per-level pick as a compound "parent ~ choice" key —
        // "Loremaster Secret ~ Weapon Trick", "High Arcana ~ Arcane Fire" — where APPLIEDTO is
        // not the choice at all (the archmage rows hold the advanced spellcasting class there).
        // Splitting is the fallback, not the first move: content is free to name a feature with
        // a tilde in it, as the Favored Soul pools do, and that whole-name match must win.
        var tilde = ability.Key.IndexOf('~');
        if (tilde < 0)
            return null;

        var parentKey = PcgIdMapper.DefaultIdTransform(ability.Key[..tilde].Trim());
        var choice = PcgIdMapper.DefaultIdTransform(ability.Key[(tilde + 1)..].Trim());
        return MatchFeatureOption(registry, parentKey, choice)
            ?? (applied == null ? null : MatchFeatureOption(registry, parentKey, applied));
    }

    private static (string FeatureType, string OptionId)? MatchFeatureOption(
        ContentRegistry registry,
        string featureKey,
        string optionKey)
    {
        foreach (var feature in registry.GetAllClassFeatures())
        {
            var featureId = feature.Id[(feature.Id.IndexOf(':') + 1)..];
            var featureName = PcgIdMapper.DefaultIdTransform(feature.Name);
            if (!string.Equals(featureKey, featureName, StringComparison.Ordinal)
                && !string.Equals(featureKey, featureId, StringComparison.Ordinal))
                continue;

            var option = feature.Options.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, optionKey, StringComparison.Ordinal)
                || string.Equals(PcgIdMapper.DefaultIdTransform(candidate.Name), optionKey, StringComparison.Ordinal));
            if (option != null)
                return (feature.Id, option.Id);

            // Features whose options are the character's own feats (blood witch's Blood
            // Enhancement selects among known metamagic feats) have no static option list —
            // the selection maps to the feat's content id.
            var dynamicOption = MatchDynamicFeatOption(registry, feature, optionKey);
            if (dynamicOption != null)
                return (feature.Id, dynamicOption);
        }

        return null;
    }

    private static string? MatchDynamicFeatOption(
        ContentRegistry registry,
        ClassFeatureDefinition feature,
        string optionKey)
    {
        if (feature.DynamicSource?.Kind != "feat")
            return null;

        var candidate = "feat:" + optionKey;
        if (!registry.TryGetFeat(candidate, out var featDef) || featDef == null)
            return null;

        if (feature.DynamicSource.FeatType != null
            && !string.Equals(featDef.Type.ToString(), feature.DynamicSource.FeatType, StringComparison.OrdinalIgnoreCase))
            return null;
        if (feature.DynamicSource.Tag != null && !featDef.Tags.Contains(feature.DynamicSource.Tag))
            return null;

        return candidate;
    }

    private sealed class ClassFeatureGrantTick
    {
        public string FeatureType { get; init; } = string.Empty;
        public int TickIndex { get; init; }
        public string DriverId { get; init; } = string.Empty;
        public string DriverName { get; init; } = string.Empty;
        public int DriverLevel { get; init; }
    }

    private static void AddClassFeatureChoices(TickChoices choices, string key, IEnumerable<string> values)
    {
        var additions = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (additions.Count == 0) return;

        choices.ClassFeatureChoices ??= new Dictionary<string, List<string>>();
        if (!choices.ClassFeatureChoices.TryGetValue(key, out var existing))
        {
            existing = new List<string>();
            choices.ClassFeatureChoices[key] = existing;
        }

        foreach (var value in additions)
        {
            if (!existing.Contains(value, StringComparer.Ordinal))
                existing.Add(value);
        }
    }

    private static string? MapWizardSpecialty(string subclass) => subclass.Trim().ToLowerInvariant() switch
    {
        "abjurer" => "abjuration",
        "conjurer" => "conjuration",
        "diviner" => "divination",
        "enchanter" => "enchantment",
        "evoker" => "evocation",
        "illusionist" => "illusion",
        "necromancer" => "necromancy",
        "transmuter" => "transmutation",
        _ => null,
    };

    private static string MapCompanionLinkType(string pcgenType) => pcgenType.Trim().ToLowerInvariant() switch
    {
        "animal companion" => "animal_companion",
        "familiar" => "familiar",
        "improved familiar" => "improved_familiar",
        "shadow companion" => "shadow_companion",
        "cohort" => "leadership_cohort",
        "follower" => "leadership_follower",
        _ => PcgIdMapper.DefaultIdTransform(pcgenType),
    };

    private static Formula CompanionLevelFormula(string linkType) => linkType switch
    {
        "animal_companion" => new Formula(CompanionResolver.AnimalCompanionLevelExpression),
        "familiar" or "improved_familiar" => new Formula("ClassLevel(wizard) + ClassLevel(sorcerer)"),
        "leadership_cohort" => new Formula("min(TotalHD - 2, LeadershipScore - 2)"),
        _ => new Formula("TotalHD"),
    };

    private static bool IsFamiliarLinkType(string? linkType) =>
        linkType is "familiar" or "improved_familiar";

    private static string? CompanionProgressionTemplate(string? linkType) => linkType switch
    {
        "animal_companion" => "template:animal_companion_standard",
        "familiar" or "improved_familiar" => "template:familiar_standard",
        "special_mount" => "template:special_mount_standard",
        _ => null,
    };

    private static string ToCharacterId(string name, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(fallback.Replace('\\', '/'))
            : name.Trim();
        return new string(source.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : (c is ' ' or '-' or '_' ? '_' : '-'))
            .ToArray())
            .Trim('-', '_');
    }
}
