using NotOnlyFiendsFeed.Contracts;
using NotOnlyFiendsStudio.PcGen;
using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsFeed.Services;

public sealed class AgentApiService
{
    private readonly ServerContentService _contentService;
    private readonly ContentRegistry _content;
    private readonly ReplayStudio _replayStudio;
    private readonly CharacterStore _characterStore;

    public AgentApiService(ServerContentService contentService, CharacterStore characterStore)
    {
        _contentService = contentService;
        _content = contentService.Registry;
        _replayStudio = contentService.ReplayStudio;
        _characterStore = characterStore;
    }

    public ApiHealthResponse GetHealth() => new()
    {
        Status = _characterStore.IsConfigured ? "ok" : "degraded",
        CharacterStoreConfigured = _characterStore.IsConfigured,
        LoadedPacks = _contentService.LoadedPacks.Select(MapPack).ToList(),
        Counts = new Dictionary<string, int>
        {
            ["races"] = _content.GetAllRaces().Count(),
            ["drivers"] = _content.GetAllDrivers().Count(),
            ["templates"] = _content.GetAllTemplates().Count(),
            ["feats"] = _content.GetAllFeats().Count(),
            ["domains"] = _content.GetAllDomains().Count(),
            ["skills"] = _content.GetAllSkills().Count(),
            ["classFeatures"] = _content.GetAllClassFeatures().Count(),
            ["spells"] = _content.GetAllSpells().Count(),
            ["equipment"] = _content.GetAllEquipment().Count()
        }
    };

    public ContentCatalogResponse GetCatalog() => new()
    {
        LoadedPacks = _contentService.LoadedPacks.Select(MapPack).ToList(),
        Races = _content.GetAllRaces()
            .OrderBy(r => r.Name)
            .Select(MapRace)
            .ToList(),
        Drivers = _content.GetAllDrivers()
            .OfType<HDDriver>()
            .OrderBy(d => d.Kind)
            .ThenBy(d => d.Name)
            .Select(MapDriver)
            .ToList(),
        Templates = _content.GetAllTemplates()
            .OrderBy(t => t.Name)
            .Select(MapTemplateSummary)
            .ToList(),
        Feats = _content.GetAllFeats()
            .OrderBy(f => f.Name)
            .Select(MapFeat)
            .ToList(),
        Domains = _content.GetAllDomains()
            .OrderBy(d => d.Name)
            .Select(d => MapSummary(d.Id, d.Name, d.Description))
            .ToList(),
        Skills = _content.GetAllSkills()
            .OrderBy(s => s.Name)
            .Select(s => MapSummary(s.Id, s.Name, s.Description))
            .ToList(),
        ClassFeatures = _content.GetAllClassFeatures()
            .OrderBy(cf => cf.Name)
            .Select(cf => MapSummary(cf.Id, cf.Name, cf.Description))
            .ToList(),
        Languages = _content.GetAllLanguages()
            .OrderBy(l => l.Name)
            .Select(MapLanguage)
            .ToList(),
        Equipment = _content.GetAllEquipment()
            .OrderBy(e => e.Category)
            .ThenBy(e => e.Name)
            .Select(MapEquipment)
            .ToList(),
        SpellCount = _content.GetAllSpells().Count()
    };

    public IEnumerable<RaceSummaryDto> GetRaces() => _content.GetAllRaces()
        .OrderBy(r => r.Name)
        .Select(MapRace);

    /// <summary>
    /// Applies the same PC-sanctioning rule the builder's picker uses, so an agent driving the API
    /// can tell a player-character race from a monster entry instead of seeing one flat list.
    /// </summary>
    private static RaceSummaryDto MapRace(RaceDefinition race) => new()
    {
        Id = race.Id,
        Name = race.Name,
        Description = race.Description,
        LevelAdjustment = race.LevelAdjustment,
        IsPcRace = RaceCatalog.IsSanctionedPcRace(race),
        AutomaticLanguages = new List<string>(race.AutomaticLanguages),
        BonusLanguages = new List<string>(race.BonusLanguages),
        BonusLanguagesAny = race.BonusLanguagesAny
    };

    private static LanguageSummaryDto MapLanguage(LanguageDefinition language) => new()
    {
        Id = language.Id,
        Name = language.Name,
        Description = language.Description,
        IsSecret = language.IsSecret
    };

    public IEnumerable<LanguageSummaryDto> GetLanguages(string? query = null)
    {
        var languages = _content.GetAllLanguages();
        if (!string.IsNullOrWhiteSpace(query))
        {
            languages = languages.Where(language =>
                language.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || language.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || language.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return languages.OrderBy(l => l.Name).Select(MapLanguage);
    }

    public IEnumerable<DriverSummaryDto> GetDrivers() => _content.GetAllDrivers()
        .OfType<HDDriver>()
        .OrderBy(d => d.Kind)
        .ThenBy(d => d.Name)
        .Select(MapDriver);

    public IEnumerable<ContentSummaryDto> GetTemplates() => _content.GetAllTemplates()
        .OrderBy(t => t.Name)
        .Select(MapTemplateSummary);

    public IEnumerable<FeatSummaryDto> GetFeats() => _content.GetAllFeats()
        .OrderBy(f => f.Name)
        .Select(MapFeat);

    public IEnumerable<ContentSummaryDto> GetDomains() => _content.GetAllDomains()
        .OrderBy(d => d.Name)
        .Select(d => MapSummary(d.Id, d.Name, d.Description));

    public IEnumerable<ContentSummaryDto> GetSkills(string? query = null)
    {
        var skills = _content.GetAllSkills();
        if (!string.IsNullOrWhiteSpace(query))
        {
            skills = skills.Where(skill =>
                skill.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || skill.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || skill.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return skills.OrderBy(s => s.Name).Select(s => MapSummary(s.Id, s.Name, s.Description));
    }

    public IEnumerable<ContentSummaryDto> GetClassFeatures() => _content.GetAllClassFeatures()
        .OrderBy(cf => cf.Name)
        .Select(cf => MapSummary(cf.Id, cf.Name, cf.Description));

    public IEnumerable<EquipmentSummaryDto> GetEquipment(EquipmentCategory? category = null, string? query = null)
    {
        var equipment = _content.GetAllEquipment();
        if (category.HasValue)
            equipment = equipment.Where(e => e.Category == category.Value);
        if (!string.IsNullOrWhiteSpace(query))
            equipment = equipment.Where(e =>
                e.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        return equipment.OrderBy(e => e.Name).Select(MapEquipment);
    }

    public EquipmentDefinition GetEquipmentById(string id) => _content.GetEquipment(id);

    public IEnumerable<SpellSummaryDto> GetSpells(string? listId = null, int? maxSpellLevel = null, string? query = null)
    {
        IEnumerable<SpellDefinition> spells = string.IsNullOrWhiteSpace(listId)
            ? _content.GetAllSpells()
            : _content.GetSpellsForList(listId, maxSpellLevel);

        if (maxSpellLevel.HasValue && string.IsNullOrWhiteSpace(listId))
        {
            spells = spells.Where(spell => spell.ClassLevels.Values.Any(level => level <= maxSpellLevel.Value));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            spells = spells.Where(spell =>
                spell.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || spell.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || spell.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return spells
            .OrderBy(spell => spell.Name)
            .Select(MapSpell);
    }

    public RaceDefinition GetRace(string id) => _content.GetRace(id);
    public Driver GetDriver(string id) => _content.GetDriver(id);
    public TemplateDriver GetTemplate(string id) => _content.GetTemplate(id);
    public FeatDefinition GetFeat(string id) => _content.GetFeat(id);
    public DomainDefinition GetDomain(string id) => _content.GetDomain(id);
    public SpellDefinition GetSpell(string id) => _content.GetSpell(id);

    public SkillDefinition GetSkill(string id) =>
        _content.GetAllSkills().FirstOrDefault(skill => skill.Id == id)
            ?? throw new KeyNotFoundException($"Skill not found: {id}");

    public ClassFeatureDefinition GetClassFeature(string id) =>
        _content.TryGetClassFeature(id, out var classFeature) && classFeature != null
            ? classFeature
            : throw new KeyNotFoundException($"Class feature not found: {id}");

    public EvaluateCharacterResponse Evaluate(EvaluateCharacterRequest request)
    {
        var state = _replayStudio.Evaluate(request.Character, request.UpToHd);
        return new EvaluateCharacterResponse
        {
            State = state,
            Sheet = CharacterSheet.FromState(state),
            PendingChoices = BuildPendingChoices(state),
            QualifiedFeats = GetQualifiedFeats(state)
        };
    }

    public ImportPcgResponse ImportPcg(ImportPcgRequest request, bool save)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("request.content is required");

        var fileName = string.IsNullOrWhiteSpace(request.FileName) ? "imported.pcg" : request.FileName;
        var data = PcgParser.ParseText(request.Content, fileName);
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper, _content);

        ResolveCompanionLinks(result.Character);

        string? id = null;
        if (save)
        {
            var state = _replayStudio.Evaluate(result.Character);
            result.Character.Sheet = CharacterSheet.FromState(state);
            id = _characterStore.Create(result.Character);
        }

        return new ImportPcgResponse
        {
            Id = id,
            Character = result.Character,
            Summary = result.Summary,
            Warnings = result.Warnings,
            DroppedFeats = result.DroppedFeats,
            DroppedSkills = result.DroppedSkills,
            DroppedClasses = result.DroppedClasses,
            DroppedTemplates = result.DroppedTemplates,
            DroppedDomains = result.DroppedDomains,
            DroppedSpells = result.DroppedSpells,
            DroppedClassAbilities = result.DroppedClassAbilities,
            DroppedEquipment = result.DroppedEquipment,
            UnsupportedCustomEquipmentModifiers = result.UnsupportedCustomEquipmentModifiers,
            IgnoredTemporaryBonuses = result.IgnoredTemporaryBonuses,
            RaceDropped = result.RaceDropped
        };
    }

    public ExportPcgResponse ExportPcg(Character character, PcgExportOptions? options = null)
    {
        var result = PcgExporter.Export(character, _content, _replayStudio,
            options ?? _contentService.PcgExportOptions);
        return new ExportPcgResponse
        {
            FileName = result.FileName,
            Content = result.Content,
            Encoding = result.Encoding,
            Status = result.Status,
            Issues = result.Issues,
        };
    }

    public ExportPcgResponse ExportPcgById(string id, PcgExportOptions? options = null) =>
        ExportPcg(_characterStore.Get(id), options);

    /// <summary>
    /// Re-points imported companion links at real store ids. PCGen names a follower; it has no
    /// concept of this app's ids, so the converter can only guess one from the name. Where the
    /// guess misses but the name identifies exactly one saved character, adopt that character's
    /// id — this is what survives a companion whose own record spells its name differently from
    /// the master's reference. Links that still cannot be resolved keep their guessed id and
    /// their <see cref="CompanionLink.SourceName"/>, so the evaluation warning can name both.
    /// </summary>
    private void ResolveCompanionLinks(Character character)
    {
        if (!_characterStore.IsConfigured)
            return;

        foreach (var link in character.CompanionLinks)
        {
            if (string.IsNullOrWhiteSpace(link.SourceName) || _characterStore.Exists(link.CompanionId))
                continue;

            var match = _characterStore.FindByName(link.SourceName!);
            if (match != null)
                link.CompanionId = match.Id;
        }
    }

    public Character LoadCharacter(string id) => _characterStore.Get(id);

    /// <summary>
    /// Evaluates and wraps a character. <paramref name="atHd"/> truncates the replay to that
    /// many ticks — the character as they were at that HD. PCGen needed a separate frozen
    /// file per life stage; the timeline makes any earlier stage a view of the one record.
    /// Equipment is current possessions, not timeline data, so it still applies in full.
    /// </summary>
    public CharacterMutationResponseDto EvaluateAndEnvelope(string id, Character character, int? atHd = null)
    {
        for (var index = 0; index < character.CompanionLinks.Count; index++)
        {
            var link = character.CompanionLinks[index];
            if (string.IsNullOrWhiteSpace(link.LinkType) || string.IsNullOrWhiteSpace(link.CompanionId))
                throw new ArgumentException(
                    $"companionLinks[{index}] requires both linkType and companionId");
        }

        var state = _replayStudio.Evaluate(character, atHd);
        var sheet = CharacterSheet.FromState(state);
        character.Sheet = sheet;
        return new CharacterMutationResponseDto
        {
            Id = id,
            Character = character,
            Sheet = sheet,
            State = state,
            PendingChoices = BuildPendingChoices(state),
            QualifiedFeats = GetQualifiedFeats(state),
            Warnings = state.Warnings.ToList()
        };
    }

    public CharacterMutationResponseDto AppendTick(string id, Tick tick)
    {
        if (string.IsNullOrWhiteSpace(tick.DriverId))
            throw new ArgumentException("tick.driverId is required");

        _ = _content.GetDriver(tick.DriverId);

        return _characterStore.Update(id, character =>
        {
            var baseline = TryEvaluateBaseline(character);
            character.Ticks.Add(tick);
            var response = EvaluateAndEnvelope(id, character);
            AnnotateTickEffects(response, baseline, tick);
            return response;
        });
    }

    public CharacterMutationResponseDto DeleteLastTick(string id)
    {
        return _characterStore.Update(id, character =>
        {
            if (character.Ticks.Count == 0)
                throw new InvalidOperationException($"Character '{id}' has no ticks to remove");

            character.Ticks.RemoveAt(character.Ticks.Count - 1);
            return EvaluateAndEnvelope(id, character);
        });
    }

    public CharacterMutationResponseDto AppendEvent(string id, PermanentEvent evt)
    {
        return _characterStore.Update(id, character =>
        {
            character.PermanentEvents.Add(evt);
            return EvaluateAndEnvelope(id, character);
        });
    }

    public NextStepResponse GetNextStepById(
        string id,
        bool includePreviews,
        OptionDetail optionDetail = OptionDetail.None,
        List<string>? candidateDriverIds = null)
    {
        var character = _characterStore.Get(id);
        var request = new NextStepRequest
        {
            Character = character,
            CandidateDriverIds = candidateDriverIds
        };
        return includePreviews
            ? GetNextStep(request, optionDetail)
            : GetNextStepLite(request);
    }

    public NextStepResponse GetNextStepLite(NextStepRequest request)
    {
        var currentState = _replayStudio.Evaluate(request.Character);
        var nextHd = currentState.TotalHD + 1;
        var candidateIds = request.CandidateDriverIds != null
            ? new HashSet<string>(request.CandidateDriverIds, StringComparer.Ordinal)
            : null;
        var abilityIncreaseDue = GetAvailableDrivers(currentState, request.Character)
            .Where(driver => candidateIds == null || candidateIds.Contains(driver.Id))
            .Any(driver => GameRules.Standard35e()
                .GrantsAbilityIncrease(nextHd - currentState.FreeMonsterClassHD, driver.Kind));
        return new NextStepResponse
        {
            NextHd = nextHd,
            AbilityIncreaseDue = abilityIncreaseDue,
            CurrentState = currentState,
            CurrentSheet = CharacterSheet.FromState(currentState),
            CurrentPendingChoices = BuildPendingChoices(currentState),
            DriverPreviews = new List<DriverPreviewDto>(),
            ExcludedDrivers = GetExcludedDrivers(currentState, request.Character, candidateIds),
            UnknownDriverIds = GetUnknownDriverIds(candidateIds),
            SkillPointAccruals = currentState.SkillPointAccruals.ToList()
        };
    }

    public CharacterMutationResponseDto SimulateTick(string id, Tick tick)
    {
        if (string.IsNullOrWhiteSpace(tick.DriverId))
            throw new ArgumentException("tick.driverId is required");

        _ = _content.GetDriver(tick.DriverId);

        var character = _characterStore.Get(id).Clone();
        var baseline = TryEvaluateBaseline(character);
        character.Ticks.Add(tick);
        var response = EvaluateAndEnvelope(id, character);
        AnnotateTickEffects(response, baseline, tick);
        return response;
    }

    /// <summary>
    /// Replaces a stored character and reports what the save changed: the full replay warnings
    /// plus the ones this version introduced over the previous one. Agents repair characters
    /// through this path (moving feats between ticks), so the response must say whether the
    /// repair produced a clean replay without a follow-up GET.
    /// </summary>
    public CharacterEnvelopeDto ReplaceCharacter(string id, Character character)
    {
        List<Warning>? baselineWarnings = null;
        try
        {
            baselineWarnings = TryEvaluateBaseline(_characterStore.Get(id))?.Warnings;
        }
        catch (CharacterStoreException)
        {
            // No previous version — every warning is new, which NewWarnings = null conveys
            // poorly, so leave it null and let Warnings carry the whole picture.
        }

        var evaluated = EvaluateAndEnvelope(id, character);
        _characterStore.Replace(id, character);
        return new CharacterEnvelopeDto
        {
            Id = id,
            Character = character,
            Warnings = evaluated.Warnings,
            NewWarnings = baselineWarnings == null
                ? null
                : DiffWarnings(baselineWarnings, evaluated.Warnings)
        };
    }

    private CharacterState? TryEvaluateBaseline(Character character)
    {
        try { return _replayStudio.Evaluate(character.Clone()); }
        catch { return null; }
    }

    private void AnnotateTickEffects(
        CharacterMutationResponseDto response, CharacterState? baseline, Tick tick)
    {
        response.NewWarnings = baseline == null
            ? response.State.Warnings.ToList()
            : DiffWarnings(baseline.Warnings, response.State.Warnings);

        if (tick.Choices.FeatIds is not { } submitted)
            return;

        var baselineCounts = CountByValue(baseline?.Feats ?? new List<string>());
        var finalCounts = CountByValue(response.State.Feats);
        var credited = new Dictionary<string, int>(StringComparer.Ordinal);
        var outcomes = new List<FeatOutcomeDto>();

        foreach (var raw in submitted)
        {
            string? canonical = null;
            if (_content.TryGetFeat(raw, out var featDef) && featDef != null)
                canonical = FeatVariantId.TryGetSelection(raw, featDef.Id, out var selection)
                    ? FeatVariantId.Canonical(featDef.Id, selection)
                    : featDef.Id;

            var applied = false;
            if (canonical != null)
            {
                var delta = finalCounts.GetValueOrDefault(canonical)
                    - baselineCounts.GetValueOrDefault(canonical);
                var used = credited.GetValueOrDefault(canonical);
                if (used < delta)
                {
                    applied = true;
                    credited[canonical] = used + 1;
                }
            }

            outcomes.Add(new FeatOutcomeDto
            {
                Submitted = raw,
                CanonicalId = canonical,
                Applied = applied,
                Reason = applied
                    ? null
                    : response.NewWarnings.FirstOrDefault(w =>
                            w.Message.Contains(canonical ?? raw, StringComparison.Ordinal)
                            || w.Message.Contains(raw, StringComparison.Ordinal))?.Message
                        ?? (canonical == null ? "unknown feat id" : "not applied — see warnings")
            });
        }

        response.FeatOutcomes = outcomes;
    }

    private static List<Warning> DiffWarnings(IEnumerable<Warning> before, IEnumerable<Warning> after)
    {
        var seen = new Dictionary<(int? Tick, string Message), int>();
        foreach (var warning in before)
        {
            var key = (warning.TickIndex, warning.Message);
            seen[key] = seen.GetValueOrDefault(key) + 1;
        }

        var fresh = new List<Warning>();
        foreach (var warning in after)
        {
            var key = (warning.TickIndex, warning.Message);
            if (seen.GetValueOrDefault(key) > 0)
                seen[key]--;
            else
                fresh.Add(warning);
        }
        return fresh;
    }

    private static Dictionary<string, int> CountByValue(IEnumerable<string> values)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var value in values)
            counts[value] = counts.GetValueOrDefault(value) + 1;
        return counts;
    }

    public CharacterMutationResponseDto ValidateCharacter(Character character) =>
        EvaluateAndEnvelope(string.Empty, character);

    public CharacterMutationResponseDto UpdateTick(string id, int index, Tick tick)
    {
        if (string.IsNullOrWhiteSpace(tick.DriverId))
            throw new ArgumentException("tick.driverId is required");

        _ = _content.GetDriver(tick.DriverId);

        return _characterStore.Update(id, character =>
        {
            if (index < 0 || index >= character.Ticks.Count)
                throw new ArgumentException(
                    $"Tick index {index} is out of range (character has {character.Ticks.Count} ticks)");

            var baseline = TryEvaluateBaseline(character);
            character.Ticks[index] = tick;
            var response = EvaluateAndEnvelope(id, character);
            AnnotateTickEffects(response, baseline, tick);
            return response;
        });
    }

    public RulesDto GetRules()
    {
        var rules = GameRules.Standard35e();
        return new RulesDto
        {
            EpicThreshold = rules.EpicThreshold,
            AbilityIncreaseInterval = rules.AbilityIncreaseInterval,
            FirstHdMaxHp = rules.FirstHDMaxHP,
            FirstHdSkillMultiplier = rules.FirstHDSkillMultiplier,
            StandardFeatHds = rules.StandardFeatHDs.OrderBy(hd => hd).ToList(),
            EpicFeatInterval = rules.EpicFeatInterval,
            EpicFeatStartHd = rules.EpicFeatStartHD
        };
    }

    /// <summary>
    /// Previews every legal next HD. <paramref name="optionDetail"/> applies only to the
    /// driver previews, which repeat their option lists once per candidate driver;
    /// <see cref="NextStepResponse.CurrentPendingChoices"/> is always fully populated
    /// because it describes a single state and is what the caller must actually fill.
    /// </summary>
    public NextStepResponse GetNextStep(NextStepRequest request, OptionDetail optionDetail = OptionDetail.Full)
    {
        var currentState = _replayStudio.Evaluate(request.Character);
        var nextHd = currentState.TotalHD + 1;
        var candidateIds = request.CandidateDriverIds != null
            ? new HashSet<string>(request.CandidateDriverIds, StringComparer.Ordinal)
            : null;

        var previews = GetAvailableDrivers(currentState, request.Character)
            .Where(driver => candidateIds == null || candidateIds.Contains(driver.Id))
            .Select(driver => BuildDriverPreview(request.Character, driver, optionDetail))
            .ToList();

        return new NextStepResponse
        {
            NextHd = nextHd,
            AbilityIncreaseDue = previews.Any(preview => preview.AbilityIncreaseDue),
            CurrentState = currentState,
            CurrentSheet = CharacterSheet.FromState(currentState),
            CurrentPendingChoices = BuildPendingChoices(currentState),
            DriverPreviews = previews,
            ExcludedDrivers = GetExcludedDrivers(currentState, request.Character, candidateIds),
            UnknownDriverIds = GetUnknownDriverIds(candidateIds),
            SkillPointAccruals = currentState.SkillPointAccruals.ToList()
        };
    }

    private DriverPreviewDto BuildDriverPreview(Character character, HDDriver driver, OptionDetail optionDetail)
    {
        var projectedCharacter = CloneCharacter(character);
        projectedCharacter.Ticks.Add(new Tick
        {
            DriverId = driver.Id,
            Choices = new TickChoices()
        });

        var projectedState = _replayStudio.Evaluate(projectedCharacter);
        return new DriverPreviewDto
        {
            Driver = MapDriver(driver),
            AbilityIncreaseDue = GameRules.Standard35e().GrantsAbilityIncrease(
                projectedState.TotalHD - projectedState.FreeMonsterClassHD, driver.Kind),
            Preview = new CharacterPreviewDto
            {
                TotalHd = projectedState.TotalHD,
                Ecl = projectedState.ECL,
                Hp = projectedState.HP,
                Bab = projectedState.EffectiveBAB,
                Saves = projectedState.EffectiveSaves,
                AbilityScores = projectedState.AbilityScores,
                ClassLevels = new Dictionary<string, int>(projectedState.ClassLevels),
                UnspentSkillPoints = projectedState.UnspentSkillPoints,
                Warnings = projectedState.Warnings.ToList()
            },
            PendingChoices = BuildPendingChoices(projectedState, optionDetail)
        };
    }

    private IEnumerable<HDDriver> GetAvailableDrivers(CharacterState state, Character character)
    {
        var takenLevels = character.Ticks
            .GroupBy(tick => tick.DriverId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return _content.GetAllDrivers()
            .OfType<HDDriver>()
            .Where(driver =>
                (!driver.MaxLevel.HasValue || takenLevels.GetValueOrDefault(driver.Id) < driver.MaxLevel.Value)
                && driver.Prerequisites.All(prereq => prereq.IsMet(state)))
            .OrderBy(driver => driver.Kind)
            .ThenBy(driver => driver.Name);
    }

    private List<DriverExclusionDto> GetExcludedDrivers(
        CharacterState state,
        Character character,
        HashSet<string>? candidateIds)
    {
        var takenLevels = character.Ticks
            .GroupBy(tick => tick.DriverId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return _content.GetAllDrivers()
            .OfType<HDDriver>()
            .Where(driver => candidateIds == null || candidateIds.Contains(driver.Id))
            .Select(driver =>
            {
                var reasons = new List<string>();
                var taken = takenLevels.GetValueOrDefault(driver.Id);
                if (driver.MaxLevel.HasValue && taken >= driver.MaxLevel.Value)
                    reasons.Add($"maximum level {driver.MaxLevel.Value} already reached");
                reasons.AddRange(driver.Prerequisites
                    .Where(prerequisite => !prerequisite.IsMet(state))
                    .Select(prerequisite => $"prerequisite unmet: {prerequisite.Description}"));
                return (driver, reasons);
            })
            .Where(entry => entry.reasons.Count > 0)
            .Select(entry => new DriverExclusionDto
            {
                Driver = MapDriver(entry.driver),
                Reasons = entry.reasons
            })
            .OrderBy(entry => entry.Driver.Kind)
            .ThenBy(entry => entry.Driver.Name)
            .ToList();
    }

    private List<string> GetUnknownDriverIds(HashSet<string>? candidateIds)
    {
        if (candidateIds == null)
            return new List<string>();

        var known = _content.GetAllDrivers().Select(driver => driver.Id).ToHashSet(StringComparer.Ordinal);
        return candidateIds.Where(id => !known.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    private PendingChoicesDto BuildPendingChoices(CharacterState state, OptionDetail optionDetail = OptionDetail.Full) => new()
    {
        FeatChoices = state.FeatSlots
            .GroupBy(slot => slot.Restriction ?? "standard")
            .Select(group => BuildFeatChoiceGroup(state, group.Key, group.Count(), optionDetail))
            .OrderBy(group => group.SlotType)
            .ToList(),
        DomainChoices = state.PendingDomainSelections
            .Where(entry => entry.Value > 0)
            .OrderBy(entry => entry.Key)
            .Select(entry => BuildDomainChoiceGroup(state, entry.Key, entry.Value, optionDetail))
            .ToList(),
        ClassFeatureChoices = state.PendingClassFeatureSelections
            .Where(entry => entry.Value > 0)
            .OrderBy(entry => entry.Key)
            .Select(entry => BuildClassFeatureChoiceGroup(state, entry.Key, entry.Value, optionDetail))
            .ToList(),
        SpellChoices = BuildSpellSelectionChoiceGroups(state, optionDetail),
        PreparedSpellChoices = BuildPreparedSpellChoiceGroups(state, optionDetail),
        CompanionTemplateChoices = BuildCompanionTemplateChoiceGroups(state),
        SpellLists = state.Spellcasting.Values
            .OrderBy(spellcasting => spellcasting.ClassId)
            .Select(spellcasting => new SpellcastingSummaryDto
            {
                ClassId = spellcasting.ClassId,
                CastingType = spellcasting.CastingType,
                CastingStat = spellcasting.CastingStat,
                Acquisition = spellcasting.Acquisition,
                CasterLevel = state.EffectiveCasterLevel(spellcasting.ClassId),
                MaxSpellLevel = spellcasting.MaxSpellLevel,
                SpellsPerDay = new Dictionary<int, int>(spellcasting.SpellsPerDay),
                SpellsKnown = spellcasting.SpellsKnown == null ? null : new Dictionary<int, int>(spellcasting.SpellsKnown),
                DomainBonusSlots = new Dictionary<int, int>(spellcasting.DomainBonusSlots),
                SpecialtyBonusSlots = new Dictionary<int, int>(spellcasting.SpecialtyBonusSlots),
                AbilityBonusSlots = new Dictionary<int, int>(spellcasting.AbilityBonusSlots)
            })
            .ToList()
    };

    private List<CompanionTemplateChoiceGroupDto> BuildCompanionTemplateChoiceGroups(CharacterState state)
    {
        if (state.ClassLevels.GetValueOrDefault("class:planar_ranger") <= 0)
            return new List<CompanionTemplateChoiceGroupDto>();

        var isGood = state.Alignment is Alignment.LG or Alignment.NG or Alignment.CG;
        var isEvil = state.Alignment is Alignment.LE or Alignment.NE or Alignment.CE;
        var options = _content.GetAllTemplates()
            .Where(template => template.Id is "template:celestial" or "template:fiendish")
            .Where(template => template.Id == "template:celestial" ? !isEvil : !isGood)
            .OrderBy(template => template.Name)
            .Select(template => new CompanionTemplateOptionDto
            {
                Id = template.Id,
                Name = template.Name,
                Description = "Apply this template to the selected normal animal companion."
            })
            .ToList();

        return state.CompanionSlots
            .Where(slot => slot.LinkType == "animal_companion")
            .GroupBy(slot => slot.LinkType, StringComparer.Ordinal)
            .Select(group => new CompanionTemplateChoiceGroupDto
            {
                LinkType = group.Key,
                ChoiceKey = $"companionTemplateChoices[{group.Key}]",
                ExistingSelection = group.Select(slot => slot.SelectedTemplateId)
                    .FirstOrDefault(selection => !string.IsNullOrEmpty(selection)),
                Options = options.Select(option => new CompanionTemplateOptionDto
                {
                    Id = option.Id,
                    Name = option.Name,
                    Description = option.Description
                }).ToList()
            })
            .ToList();
    }

    private List<SpellSelectionChoiceGroupDto> BuildSpellSelectionChoiceGroups(
        CharacterState state,
        OptionDetail optionDetail)
    {
        var groups = new List<SpellSelectionChoiceGroupDto>();

        foreach (var spellcasting in state.Spellcasting.Values
                     .Where(spellcasting => spellcasting.Acquisition == SpellAcquisition.Spellbook)
                     .OrderBy(spellcasting => spellcasting.ClassId))
        {
            var selectedByLevel = spellcasting.SelectedSpells
                .Where(selection => selection.ClassId == spellcasting.ClassId && selection.SpellLevel > 0)
                .GroupBy(selection => selection.SpellLevel)
                .ToDictionary(group => group.Key, group => group.Select(selection => selection.SpellId).ToHashSet(StringComparer.Ordinal));
            var selectedCount = selectedByLevel.Values.Sum(selectionIds => selectionIds.Count);
            var spellbookLimit = ReplayStudio.SpellbookSpellsAllowed(
                state.ClassLevels.GetValueOrDefault(spellcasting.ClassId),
                AbilityScoreSet.Modifier(state.AbilityScores.GetScore(spellcasting.CastingStat)));
            var remaining = Math.Max(0, spellbookLimit - selectedCount);

            var legalSpells = _content.GetSpellsForList(spellcasting.ClassId, spellcasting.MaxSpellLevel)
                .Where(spell => !state.IsSpellExcludedFromList(spellcasting.ClassId, spell.Id))
                .Where(spell => !WizardSchools.IsProhibited(state, spell.School))
                .Select(spell =>
                {
                    _content.TryGetSpellLevelForList(spell, spellcasting.ClassId, out var level);
                    return (Spell: spell, Level: level);
                })
                .Where(entry => entry.Level >= 1)
                .GroupBy(entry => entry.Level)
                .OrderBy(group => group.Key);

            foreach (var levelGroup in legalSpells)
            {
                var existing = selectedByLevel.GetValueOrDefault(levelGroup.Key)
                    ?? new HashSet<string>(StringComparer.Ordinal);
                var options = levelGroup
                    .Select(entry => entry.Spell)
                    .Where(spell => !existing.Contains(spell.Id))
                    .ToList();

                groups.Add(new SpellSelectionChoiceGroupDto
                {
                    ClassId = spellcasting.ClassId,
                    SpellLevel = levelGroup.Key,
                    OptionCount = options.Count,
                    ExistingSelections = existing.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                    SpellbookUsed = selectedCount,
                    SpellbookLimit = spellbookLimit,
                    SpellbookRemaining = remaining,
                    OptionIds = optionDetail == OptionDetail.Ids
                        ? options.Select(spell => spell.Id).ToList()
                        : null,
                    Options = optionDetail == OptionDetail.Full
                        ? options.Select(MapSpell).ToList()
                        : null,
                });
            }
        }

        return groups;
    }

    private List<PreparedSpellChoiceGroupDto> BuildPreparedSpellChoiceGroups(
        CharacterState state,
        OptionDetail optionDetail)
    {
        var groups = new List<PreparedSpellChoiceGroupDto>();
        foreach (var spellcasting in state.Spellcasting.Values
                     .Where(spellcasting => spellcasting.Acquisition is SpellAcquisition.FullList or SpellAcquisition.Spellbook)
                     .OrderBy(spellcasting => spellcasting.ClassId))
        {
            foreach (var spellLevel in spellcasting.SpellsPerDay.Keys.Where(level => level >= 1).OrderBy(level => level))
            {
                foreach (var slotKind in Enum.GetValues<PreparedSpellSlotKind>())
                {
                    var slotCount = slotKind switch
                    {
                        PreparedSpellSlotKind.Normal => spellcasting.SpellsPerDay.GetValueOrDefault(spellLevel)
                            + spellcasting.AbilityBonusSlots.GetValueOrDefault(spellLevel),
                        PreparedSpellSlotKind.Domain => spellcasting.DomainBonusSlots.GetValueOrDefault(spellLevel),
                        PreparedSpellSlotKind.Specialty => spellcasting.SpecialtyBonusSlots.GetValueOrDefault(spellLevel),
                        _ => 0
                    };
                    if (slotCount <= 0)
                        continue;

                    var existing = state.PreparedSpellSelections
                        .Where(selection => selection.ClassId == spellcasting.ClassId
                            && selection.SpellLevel == spellLevel
                            && selection.SlotKind == slotKind)
                        .Select(selection => selection.SpellId)
                        .ToList();
                    var options = GetPreparedSpellOptions(state, spellcasting, spellLevel, slotKind).ToList();
                    groups.Add(new PreparedSpellChoiceGroupDto
                    {
                        ClassId = spellcasting.ClassId,
                        SpellLevel = spellLevel,
                        SlotKind = slotKind,
                        SlotCount = slotCount,
                        PreparedCount = existing.Count,
                        ExistingSelections = existing,
                        OptionCount = options.Count,
                        OptionIds = optionDetail == OptionDetail.Ids
                            ? options.Select(spell => spell.Id).ToList()
                            : null,
                        Options = optionDetail == OptionDetail.Full
                            ? options.Select(MapSpell).ToList()
                            : null,
                    });
                }
            }
        }
        return groups;
    }

    private IEnumerable<SpellDefinition> GetPreparedSpellOptions(
        CharacterState state,
        SpellcastingState spellcasting,
        int spellLevel,
        PreparedSpellSlotKind slotKind)
    {
        IEnumerable<SpellDefinition> options;
        if (slotKind == PreparedSpellSlotKind.Domain)
        {
            var domainSpellIds = state.DomainOwners
                .Where(entry => entry.Value == spellcasting.ClassId)
                .SelectMany(entry => _content.TryGetDomain(entry.Key, out var domain) && domain != null
                    ? domain.BonusSpells.Where(spell => spell.Key == spellLevel).Select(spell => spell.Value)
                    : Enumerable.Empty<string>())
                .ToHashSet(StringComparer.Ordinal);
            options = _content.GetAllSpells().Where(spell => domainSpellIds.Contains(spell.Id));
        }
        else if (slotKind == PreparedSpellSlotKind.Specialty)
        {
            var specialty = WizardSchools.Specialty(state);
            options = _content.GetSpellsForList(spellcasting.ClassId, spellcasting.MaxSpellLevel)
                .Where(spell => !state.IsSpellExcludedFromList(spellcasting.ClassId, spell.Id)
                    && _content.TryGetSpellLevelForList(spell, spellcasting.ClassId, out var level)
                    && level == spellLevel
                    && string.Equals(spell.School, specialty, StringComparison.OrdinalIgnoreCase));
        }
        else if (spellcasting.Acquisition == SpellAcquisition.Spellbook)
        {
            options = spellcasting.SelectedSpells
                .Where(selection => selection.ClassId == spellcasting.ClassId && selection.SpellLevel == spellLevel)
                .Select(selection => _content.TryGetSpell(selection.SpellId, out var spell) ? spell : null)
                .Where(spell => spell != null)
                .Cast<SpellDefinition>();
        }
        else
        {
            options = _content.GetSpellsForList(spellcasting.ClassId, spellcasting.MaxSpellLevel)
                .Where(spell => !state.IsSpellExcludedFromList(spellcasting.ClassId, spell.Id)
                    && _content.TryGetSpellLevelForList(spell, spellcasting.ClassId, out var level)
                    && level == spellLevel);
        }

        return options
            .Where(spell => !WizardSchools.IsProhibited(state, spell.School))
            .OrderBy(spell => spell.Name, StringComparer.Ordinal)
            .DistinctBy(spell => spell.Id);
    }

    private DomainChoiceGroupDto BuildDomainChoiceGroup(
        CharacterState state, string ownerClassId, int count, OptionDetail optionDetail)
    {
        var options = _content.GetAllDomains();
        if (state.DomainSelectionRestrictions.TryGetValue(ownerClassId, out var allowedDomainIds)
            && allowedDomainIds.Count > 0)
        {
            options = options
                .Where(domain => allowedDomainIds.Contains(domain.Id, StringComparer.Ordinal));
        }

        var orderedOptions = options.OrderBy(domain => domain.Name).ToList();

        return new DomainChoiceGroupDto
        {
            OwnerClassId = ownerClassId,
            Count = count,
            OptionCount = orderedOptions.Count,
            OptionIds = optionDetail == OptionDetail.Ids
                ? orderedOptions.Select(domain => domain.Id).ToList()
                : null,
            Options = optionDetail == OptionDetail.Full
                ? orderedOptions.Select(domain => MapSummary(domain.Id, domain.Name, domain.Description)).ToList()
                : null
        };
    }

    private ClassFeatureChoiceGroupDto BuildClassFeatureChoiceGroup(
        CharacterState state,
        string featureType,
        int count,
        OptionDetail optionDetail)
    {
        var classFeature = GetClassFeature(featureType);
        var options = BuildClassFeatureOptions(state, classFeature);

        return new ClassFeatureChoiceGroupDto
        {
            FeatureType = featureType,
            FeatureName = classFeature.Name,
            Count = count,
            ExistingSelections = state.ClassFeatureSelections.GetValueOrDefault(featureType)?.ToList() ?? new List<string>(),
            DynamicSource = classFeature.DynamicSource == null
                ? null
                : new DynamicChoiceSourceDto
                {
                    Kind = classFeature.DynamicSource.Kind,
                    FeatType = classFeature.DynamicSource.FeatType,
                    Tag = classFeature.DynamicSource.Tag
                },
            OptionCount = options.Count,
            OptionIds = optionDetail == OptionDetail.Ids
                ? options.Select(option => option.Id).ToList()
                : null,
            Options = optionDetail == OptionDetail.Full ? options : null
        };
    }

    private List<ChoiceOptionDto> BuildClassFeatureOptions(CharacterState state, ClassFeatureDefinition classFeature)
    {
        var staticOptions = classFeature.Options.Select(option => new ChoiceOptionDto
        {
            Id = option.Id,
            Name = option.Name,
            Description = option.Description,
            SourceKind = "static",
            MinEffectiveLevel = option.MinEffectiveLevel == 0 ? null : option.MinEffectiveLevel,
            RequiredAlignment = option.RequiredAlignment,
            RequiredCasterLevel = option.RequiredCasterLevel == 0 ? null : option.RequiredCasterLevel
        });

        if (classFeature.DynamicSource?.Kind != "feat")
            return staticOptions.OrderBy(option => option.Name).ToList();

        var dynamicOptions = _content.GetAllFeats()
            .Where(feat =>
                state.Feats.Contains(feat.Id)
                && (classFeature.DynamicSource.FeatType == null
                    || string.Equals(feat.Type.ToString(), classFeature.DynamicSource.FeatType, StringComparison.OrdinalIgnoreCase))
                && (classFeature.DynamicSource.Tag == null || feat.Tags.Contains(classFeature.DynamicSource.Tag)))
            .Select(feat => new ChoiceOptionDto
            {
                Id = feat.Id,
                Name = feat.Name,
                Description = feat.Description,
                SourceKind = "feat"
            });

        return staticOptions
            .Concat(dynamicOptions)
            .OrderBy(option => option.Name)
            .ToList();
    }

    private FeatChoiceGroupDto BuildFeatChoiceGroup(
        CharacterState state,
        string slotType,
        int slotCount,
        OptionDetail optionDetail)
    {
        var options = _replayStudio
            .GetAvailableFeats(state, slotType == "standard" ? null : slotType)
            .OrderBy(feat => feat.Name)
            .ToList();

        return new FeatChoiceGroupDto
        {
            SlotType = slotType,
            Count = slotCount,
            OptionCount = options.Count,
            OptionIds = optionDetail == OptionDetail.Ids
                ? options.Select(feat => feat.Id).ToList()
                : null,
            Options = optionDetail == OptionDetail.Full
                ? options.Select(MapFeat).ToList()
                : null
        };
    }

    private List<FeatSummaryDto> GetQualifiedFeats(CharacterState state) => _replayStudio
        .GetAvailableFeats(state)
        .OrderBy(feat => feat.Name)
        .Select(MapFeat)
        .ToList();

    private static Character CloneCharacter(Character character) => character.Clone();

    private static PackSummaryDto MapPack(LoadedPack pack) => new()
    {
        Id = pack.Manifest.Id,
        Name = pack.Manifest.Name,
        Version = pack.Manifest.Version,
        Description = pack.Manifest.Description
    };

    private static ContentSummaryDto MapSummary(string id, string name, string? description) => new()
    {
        Id = id,
        Name = name,
        Description = description
    };

    private static ContentSummaryDto MapTemplateSummary(TemplateDriver template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        AcquisitionKind = template.AcquisitionKind
    };

    private static DriverSummaryDto MapDriver(HDDriver driver) => new()
    {
        Id = driver.Id,
        Name = driver.Name,
        Kind = driver.Kind,
        HitDie = driver.HitDie,
        SkillPointsPerLevel = driver.SkillPointsPerLevel,
        MaxLevel = driver.MaxLevel,
        HasSpellcasting = driver.Spellcasting != null,
        Prerequisites = driver.Prerequisites.Select(prerequisite => prerequisite.Description).ToList()
    };

    private static FeatSummaryDto MapFeat(FeatDefinition feat) => new()
    {
        Id = feat.Id,
        Name = feat.Name,
        Description = feat.Description,
        Type = feat.Type,
        Repeatable = feat.Repeatable,
        SelectionRequired = feat.SelectionRequired,
        Selection = MapSelectionGuide(feat),
        Tags = feat.Tags.ToList(),
        Prerequisites = feat.Prerequisites.Select(prerequisite => prerequisite.Description).ToList()
    };

    private static FeatSelectionGuideDto? MapSelectionGuide(FeatDefinition feat)
    {
        if (feat.SelectionRequired is not { } kind)
            return null;

        var pattern = feat.Id + ":{selection}";
        return kind switch
        {
            "skill" => new FeatSelectionGuideDto
            {
                Kind = kind,
                IdPattern = pattern,
                OptionsEndpoint = "/api/content/skills",
                Hint = $"Append ':' plus a skill id without its 'skill:' prefix, e.g. '{feat.Id}:concentration'."
            },
            "school" => new FeatSelectionGuideDto
            {
                Kind = kind,
                IdPattern = pattern,
                Options = WizardSchools.SchoolNames.ToList(),
                Hint = $"Append ':' plus a school of magic, e.g. '{feat.Id}:conjuration'."
            },
            "weapon" => new FeatSelectionGuideDto
            {
                Kind = kind,
                IdPattern = pattern,
                OptionsEndpoint = "/api/content/equipment?category=Weapon",
                Hint = $"Append ':' plus a weapon id without its 'weapon:' prefix, e.g. '{feat.Id}:longsword'."
            },
            "spell" => new FeatSelectionGuideDto
            {
                Kind = kind,
                IdPattern = pattern,
                OptionsEndpoint = "/api/content/spells",
                Hint = $"Append ':' plus a spell id without its 'spell:' prefix, e.g. '{feat.Id}:fireball'. "
                    + "Repeat the feat id once per selected spell; the takings share one feat slot."
            },
            "special_attack" => new FeatSelectionGuideDto
            {
                Kind = kind,
                IdPattern = pattern,
                OptionsEndpoint = "/api/characters/{id}/state",
                Hint = "Append ':' plus one of this character's special attack ids "
                    + "(specialAttacks[].id in the state endpoint)."
            },
            "spell_like_ability" => new FeatSelectionGuideDto
            {
                Kind = kind,
                IdPattern = pattern,
                OptionsEndpoint = "/api/characters/{id}/state",
                Hint = "Append ':' plus one of this character's spell-like ability ids or "
                    + "underscore-normalized names (the state endpoint's SLA list)."
            },
            _ => new FeatSelectionGuideDto
            {
                Kind = kind,
                IdPattern = pattern,
                Hint = $"Append ':' plus the chosen {kind}, lowercased with spaces as underscores."
            }
        };
    }

    private static SpellSummaryDto MapSpell(SpellDefinition spell) => new()
    {
        Id = spell.Id,
        Name = spell.Name,
        School = spell.School,
        ClassLevels = new Dictionary<string, int>(spell.ClassLevels),
        Description = spell.Description
    };

    private static EquipmentSummaryDto MapEquipment(EquipmentDefinition eq) => new()
    {
        Id = eq.Id,
        Name = eq.Name,
        Category = eq.Category,
        Slot = eq.Slot,
        WeightLbs = eq.WeightLbs,
        PriceCp = eq.PriceCp,
        Description = eq.Description,
        WeaponDamage = eq.Weapon?.Damage,
        EnhancementBonus = eq.EnhancementBonus,
        SpecialAbilityBonusEquivalent = eq.SpecialAbilityBonusEquivalent,
        ArmorBonus = eq.Armor?.ArmorBonus,
        IsIntelligent = eq.IntelligentItem != null,
        IntelligentItemEgo = eq.IntelligentItem?.CalculateEgo(
            eq.EnhancementBonus, eq.SpecialAbilityBonusEquivalent),
        EffectSummary = eq.GrantedPermabuffs.Select(SummarizePermabuff).ToList()
    };

    private static string SummarizePermabuff(Permabuff buff) => buff switch
    {
        GrantTypedBonus tb => $"{(tb.Value.Expression.StartsWith("-") ? "" : "+")}{tb.Value.Expression} {tb.BonusType} to {tb.Target}",
        GrantArmorProfile ap => $"{(ap.AsShield ? "Shield" : "Armor")} +{ap.Profile.ArmorBonus}",
        GrantWeaponLine w => string.IsNullOrEmpty(w.DisplayName) ? "Weapon" : w.DisplayName,
        ModifyAttribute ma => $"Modify {ma.Target} {(ma.Value >= 0 ? "+" : "")}{ma.Value}",
        _ => buff.GetType().Name
    };
}
