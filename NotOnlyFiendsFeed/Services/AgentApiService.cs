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
            .Select(r => MapSummary(r.Id, r.Name, r.Description))
            .ToList(),
        Drivers = _content.GetAllDrivers()
            .OfType<HDDriver>()
            .OrderBy(d => d.Kind)
            .ThenBy(d => d.Name)
            .Select(MapDriver)
            .ToList(),
        Templates = _content.GetAllTemplates()
            .OrderBy(t => t.Name)
            .Select(t => MapSummary(t.Id, t.Name, null))
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
        Equipment = _content.GetAllEquipment()
            .OrderBy(e => e.Category)
            .ThenBy(e => e.Name)
            .Select(MapEquipment)
            .ToList(),
        SpellCount = _content.GetAllSpells().Count()
    };

    public IEnumerable<ContentSummaryDto> GetRaces() => _content.GetAllRaces()
        .OrderBy(r => r.Name)
        .Select(r => MapSummary(r.Id, r.Name, r.Description));

    public IEnumerable<DriverSummaryDto> GetDrivers() => _content.GetAllDrivers()
        .OfType<HDDriver>()
        .OrderBy(d => d.Kind)
        .ThenBy(d => d.Name)
        .Select(MapDriver);

    public IEnumerable<ContentSummaryDto> GetTemplates() => _content.GetAllTemplates()
        .OrderBy(t => t.Name)
        .Select(t => MapSummary(t.Id, t.Name, null));

    public IEnumerable<FeatSummaryDto> GetFeats() => _content.GetAllFeats()
        .OrderBy(f => f.Name)
        .Select(MapFeat);

    public IEnumerable<ContentSummaryDto> GetDomains() => _content.GetAllDomains()
        .OrderBy(d => d.Name)
        .Select(d => MapSummary(d.Id, d.Name, d.Description));

    public IEnumerable<ContentSummaryDto> GetSkills() => _content.GetAllSkills()
        .OrderBy(s => s.Name)
        .Select(s => MapSummary(s.Id, s.Name, s.Description));

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

        string? id = null;
        if (save)
            id = _characterStore.Create(result.Character);

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
            RaceDropped = result.RaceDropped
        };
    }

    public Character LoadCharacter(string id) => _characterStore.Get(id);

    public CharacterMutationResponseDto EvaluateAndEnvelope(string id, Character character)
    {
        var state = _replayStudio.Evaluate(character);
        return new CharacterMutationResponseDto
        {
            Id = id,
            Character = character,
            Sheet = CharacterSheet.FromState(state),
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

        var character = _characterStore.Get(id);
        character.Ticks.Add(tick);
        _characterStore.Replace(id, character);
        return EvaluateAndEnvelope(id, character);
    }

    public CharacterMutationResponseDto DeleteLastTick(string id)
    {
        var character = _characterStore.Get(id);
        if (character.Ticks.Count == 0)
            throw new InvalidOperationException($"Character '{id}' has no ticks to remove");

        character.Ticks.RemoveAt(character.Ticks.Count - 1);
        _characterStore.Replace(id, character);
        return EvaluateAndEnvelope(id, character);
    }

    public CharacterMutationResponseDto AppendEvent(string id, PermanentEvent evt)
    {
        var character = _characterStore.Get(id);
        character.PermanentEvents.Add(evt);
        _characterStore.Replace(id, character);
        return EvaluateAndEnvelope(id, character);
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
        return new NextStepResponse
        {
            NextHd = nextHd,
            AbilityIncreaseDue = nextHd % GameRules.Standard35e().AbilityIncreaseInterval == 0,
            CurrentState = currentState,
            CurrentSheet = CharacterSheet.FromState(currentState),
            CurrentPendingChoices = BuildPendingChoices(currentState),
            DriverPreviews = new List<DriverPreviewDto>()
        };
    }

    public CharacterMutationResponseDto SimulateTick(string id, Tick tick)
    {
        if (string.IsNullOrWhiteSpace(tick.DriverId))
            throw new ArgumentException("tick.driverId is required");

        _ = _content.GetDriver(tick.DriverId);

        var character = _characterStore.Get(id).Clone();
        character.Ticks.Add(tick);
        return EvaluateAndEnvelope(id, character);
    }

    public CharacterMutationResponseDto ValidateCharacter(Character character) =>
        EvaluateAndEnvelope(string.Empty, character);

    public CharacterMutationResponseDto UpdateTick(string id, int index, Tick tick)
    {
        if (string.IsNullOrWhiteSpace(tick.DriverId))
            throw new ArgumentException("tick.driverId is required");

        _ = _content.GetDriver(tick.DriverId);

        var character = _characterStore.Get(id);
        if (index < 0 || index >= character.Ticks.Count)
            throw new ArgumentException(
                $"Tick index {index} is out of range (character has {character.Ticks.Count} ticks)");

        character.Ticks[index] = tick;
        _characterStore.Replace(id, character);
        return EvaluateAndEnvelope(id, character);
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
            AbilityIncreaseDue = nextHd % GameRules.Standard35e().AbilityIncreaseInterval == 0,
            CurrentState = currentState,
            CurrentSheet = CharacterSheet.FromState(currentState),
            CurrentPendingChoices = BuildPendingChoices(currentState),
            DriverPreviews = previews
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
            .Select(entry => BuildDomainChoiceGroup(entry.Key, entry.Value, optionDetail))
            .ToList(),
        ClassFeatureChoices = state.PendingClassFeatureSelections
            .Where(entry => entry.Value > 0)
            .OrderBy(entry => entry.Key)
            .Select(entry => BuildClassFeatureChoiceGroup(state, entry.Key, entry.Value, optionDetail))
            .ToList(),
        SpellLists = state.Spellcasting.Values
            .OrderBy(spellcasting => spellcasting.ClassId)
            .Select(spellcasting => new SpellcastingSummaryDto
            {
                ClassId = spellcasting.ClassId,
                CastingType = spellcasting.CastingType,
                CastingStat = spellcasting.CastingStat,
                CasterLevel = spellcasting.CasterLevel,
                MaxSpellLevel = spellcasting.MaxSpellLevel,
                SpellsPerDay = new Dictionary<int, int>(spellcasting.SpellsPerDay),
                SpellsKnown = spellcasting.SpellsKnown == null ? null : new Dictionary<int, int>(spellcasting.SpellsKnown),
                DomainBonusSlots = new Dictionary<int, int>(spellcasting.DomainBonusSlots)
            })
            .ToList()
    };

    private DomainChoiceGroupDto BuildDomainChoiceGroup(string ownerClassId, int count, OptionDetail optionDetail)
    {
        var options = _content.GetAllDomains().OrderBy(domain => domain.Name).ToList();

        return new DomainChoiceGroupDto
        {
            OwnerClassId = ownerClassId,
            Count = count,
            OptionCount = options.Count,
            OptionIds = optionDetail == OptionDetail.Ids
                ? options.Select(domain => domain.Id).ToList()
                : null,
            Options = optionDetail == OptionDetail.Full
                ? options.Select(domain => MapSummary(domain.Id, domain.Name, domain.Description)).ToList()
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
        Tags = feat.Tags.ToList(),
        Prerequisites = feat.Prerequisites.Select(prerequisite => prerequisite.Description).ToList()
    };

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
        ArmorBonus = eq.Armor?.ArmorBonus,
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
