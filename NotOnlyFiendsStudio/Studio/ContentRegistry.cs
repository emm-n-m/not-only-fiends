using System.Text.Json;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Studio;

public class ContentRegistry : IContentLookup
{
    private readonly List<ContentTypeHandler> _handlers = new();
    private readonly Dictionary<string, Driver> _drivers = new();
    private readonly Dictionary<string, RaceDefinition> _races = new();
    private readonly Dictionary<string, TemplateDriver> _templates = new();
    private readonly Dictionary<string, FeatDefinition> _feats = new();
    private readonly Dictionary<string, DomainDefinition> _domains = new();
    private readonly Dictionary<string, SpellDefinition> _spells = new();
    private readonly Dictionary<string, SkillDefinition> _skills = new();
    private readonly Dictionary<string, ClassFeatureDefinition> _classFeatures = new();
    private readonly Dictionary<string, EquipmentDefinition> _equipment = new();
    private Dictionary<string, EquipmentDefinition>? _equipmentByName;

    public ConflictResolution OnConflict { get; set; } = ConflictResolution.LastWins;

    public ContentRegistry()
    {
        RegisterContentType(new ContentTypeHandler<RaceDefinition>(
            "races", race => Register(_races, race, r => r.Id)));
        RegisterContentType(new ContentTypeHandler<Driver>(
            "classes", driver => Register(_drivers, driver, d => d.Id)));
        RegisterContentType(new ContentTypeHandler<Driver>(
            "racial_hd", driver => Register(_drivers, driver, d => d.Id)));
        RegisterContentType(new ContentTypeHandler<TemplateDriver>(
            "templates", template => Register(_templates, template, t => t.Id)));
        RegisterContentType(new ContentTypeHandler<FeatDefinition>(
            "feats", feat => Register(_feats, feat, f => f.Id)));
        RegisterContentType(new ContentTypeHandler<DomainDefinition>(
            "domains", domain => Register(_domains, domain, d => d.Id)));
        RegisterContentType(new ContentTypeHandler<SpellDefinition>(
            "spells", spell => Register(_spells, spell, s => s.Id)));
        RegisterContentType(new ContentTypeHandler<SkillDefinition>(
            "skills", skill => Register(_skills, skill, sk => sk.Id)));
        RegisterContentType(new ContentTypeHandler<ClassFeatureDefinition>(
            "class_features", cf => Register(_classFeatures, cf, c => c.Id)));
        RegisterContentType(new ContentTypeHandler<EquipmentDefinition>(
            "equipment", RegisterEquipment));
    }

    private void Register<T>(Dictionary<string, T> dict, T item, Func<T, string> getId)
    {
        var id = getId(item);
        if (dict.ContainsKey(id))
        {
            switch (OnConflict)
            {
                case ConflictResolution.FirstWins:
                    return;
                case ConflictResolution.Warn:
                    _errors.Add(new ContentError(ContentErrorKind.DuplicateId,
                        $"Duplicate {typeof(T).Name} ID '{id}' — later definition used"));
                    break;
                case ConflictResolution.Error:
                    _errors.Add(new ContentError(ContentErrorKind.DuplicateId,
                        $"Duplicate {typeof(T).Name} ID '{id}' — first definition kept"));
                    return;
                case ConflictResolution.LastWins:
                default:
                    break;
            }
        }
        dict[id] = item;
    }

    public void RegisterContentType(ContentTypeHandler handler) => _handlers.Add(handler);

    // --- Direct registration (used by tests and programmatic setup) ---

    public void RegisterDriver(Driver driver) => Register(_drivers, driver, d => d.Id);
    public void RegisterRace(RaceDefinition race) => Register(_races, race, r => r.Id);
    public void RegisterTemplate(TemplateDriver template) => Register(_templates, template, t => t.Id);
    public void RegisterFeat(FeatDefinition feat) => Register(_feats, feat, f => f.Id);
    public void RegisterDomain(DomainDefinition domain) => Register(_domains, domain, d => d.Id);
    public void RegisterSpell(SpellDefinition spell) => Register(_spells, spell, s => s.Id);
    public void RegisterSkill(SkillDefinition skill) => Register(_skills, skill, sk => sk.Id);
    public void RegisterClassFeature(ClassFeatureDefinition cf) => Register(_classFeatures, cf, c => c.Id);
    public void RegisterEquipment(EquipmentDefinition equipment)
    {
        Register(_equipment, equipment, e => e.Id);
        _equipmentByName = null;
    }

    // --- Lookups ---

    public Driver GetDriver(string id) =>
        _drivers.TryGetValue(id, out var driver)
            ? driver
            : throw new KeyNotFoundException($"Driver not found: {id}");

    public RaceDefinition GetRace(string id) =>
        _races.TryGetValue(id, out var race)
            ? race
            : throw new KeyNotFoundException($"Race not found: {id}");

    public TemplateDriver GetTemplate(string id) =>
        _templates.TryGetValue(id, out var template)
            ? template
            : throw new KeyNotFoundException($"Template not found: {id}");

    public FeatDefinition GetFeat(string id) =>
        _feats.TryGetValue(id, out var feat)
            ? feat
            : throw new KeyNotFoundException($"Feat not found: {id}");

    public bool TryGetFeat(string id, out FeatDefinition? feat)
    {
        if (_feats.TryGetValue(id, out feat))
            return true;

        // Match selectable variant IDs like "spell_focus_enchantment" → base "spell_focus"
        foreach (var f in _feats.Values)
        {
            if (f.SelectionRequired != null && f.Repeatable
                && id.StartsWith(f.Id + "_", StringComparison.Ordinal))
            {
                feat = f;
                return true;
            }
        }

        feat = null;
        return false;
    }

    public DomainDefinition GetDomain(string id) =>
        _domains.TryGetValue(id, out var domain)
            ? domain
            : throw new KeyNotFoundException($"Domain not found: {id}");

    public bool TryGetDomain(string id, out DomainDefinition? domain) =>
        _domains.TryGetValue(id, out domain);

    public SpellDefinition GetSpell(string id) =>
        _spells.TryGetValue(id, out var spell)
            ? spell
            : throw new KeyNotFoundException($"Spell not found: {id}");

    public bool TryGetSpell(string id, out SpellDefinition? spell) =>
        _spells.TryGetValue(id, out spell);

    public bool TryGetClassFeature(string id, out ClassFeatureDefinition? cf) =>
        _classFeatures.TryGetValue(id, out cf);

    public bool TryGetSkill(string id, out SkillDefinition? skill) =>
        _skills.TryGetValue(id, out skill);

    public EquipmentDefinition GetEquipment(string id) =>
        _equipment.TryGetValue(id, out var equipment)
            ? equipment
            : throw new KeyNotFoundException($"Equipment not found: {id}");

    public bool TryGetEquipment(string id, out EquipmentDefinition? equipment) =>
        _equipment.TryGetValue(id, out equipment);

    /// <summary>
    /// Look up equipment by display name (case-insensitive). First duplicate name wins.
    /// Used by PCGen import to resolve EQUIPNAME → catalog ID.
    /// </summary>
    public bool TryGetEquipmentByName(string name, out EquipmentDefinition? equipment)
    {
        _equipmentByName ??= _equipment.Values
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return _equipmentByName.TryGetValue(name, out equipment);
    }

    public ClassFeatureOption? GetClassFeatureOption(string featureType, string optionId)
    {
        if (!_classFeatures.TryGetValue(featureType, out var cf)) return null;
        return cf.Options.FirstOrDefault(o => o.Id == optionId);
    }

    public IEnumerable<ClassFeatureDefinition> GetAllClassFeatures() => _classFeatures.Values;

    public IEnumerable<FeatDefinition> GetAllFeats() => _feats.Values;
    public IEnumerable<RaceDefinition> GetAllRaces() => _races.Values;
    public IEnumerable<Driver> GetAllDrivers() => _drivers.Values;
    public IEnumerable<TemplateDriver> GetAllTemplates() => _templates.Values;
    public IEnumerable<DomainDefinition> GetAllDomains() => _domains.Values;
    public IEnumerable<SpellDefinition> GetAllSpells() => _spells.Values;
    public IEnumerable<SkillDefinition> GetAllSkills() => _skills.Values;
    public IEnumerable<EquipmentDefinition> GetAllEquipment() => _equipment.Values;
    public IEnumerable<SpellDefinition> GetSpellsForList(string spellListId, int? maxSpellLevel = null) =>
        _spells.Values
            .Where(s => s.ClassLevels.TryGetValue(spellListId, out var level)
                && (!maxSpellLevel.HasValue || level <= maxSpellLevel.Value))
            .OrderBy(s => s.ClassLevels[spellListId])
            .ThenBy(s => s.Name);
    public IEnumerable<SpellDefinition> GetSpellsForClass(string classId, int? maxSpellLevel = null) =>
        GetSpellsForList(classId, maxSpellLevel);

    // --- JSON Loading via handlers ---

    public void LoadJsonForDirectory(string directoryName, string json)
    {
        var handler = _handlers.FirstOrDefault(h => h.DirectoryName == directoryName)
            ?? throw new InvalidOperationException($"No handler for content directory: {directoryName}");
        handler.LoadFromJson(json, JsonOptions.Default);
    }

    public void LoadContentDirectory(string basePath)
    {
        foreach (var handler in _handlers)
            handler.LoadFromDirectory(basePath, JsonOptions.Default);
    }

    public void LoadContent(params string[] basePaths)
    {
        foreach (var basePath in basePaths)
            LoadContentDirectory(basePath);
    }

    public void LoadPacks(string packsRoot, PackConfig? config = null)
    {
        var loader = new PackLoader();
        loader.LoadPacks(this, packsRoot, config);
    }

    // --- Legacy convenience methods (delegate to handlers) ---

    public void LoadDriverFromJson(string json) => LoadJsonForDirectory("classes", json);
    public void LoadRaceFromJson(string json) => LoadJsonForDirectory("races", json);
    public void LoadTemplateFromJson(string json) => LoadJsonForDirectory("templates", json);
    public void LoadFeatsFromJson(string json) => LoadJsonForDirectory("feats", json);
    public void LoadDomainsFromJson(string json) => LoadJsonForDirectory("domains", json);
    public void LoadSpellsFromJson(string json) => LoadJsonForDirectory("spells", json);

    public void LoadDriverFromFile(string path) => LoadDriverFromJson(File.ReadAllText(path));
    public void LoadRaceFromFile(string path) => LoadRaceFromJson(File.ReadAllText(path));
    public void LoadTemplateFromFile(string path) => LoadTemplateFromJson(File.ReadAllText(path));
    public void LoadFeatsFromFile(string path) => LoadFeatsFromJson(File.ReadAllText(path));
    public void LoadDomainsFromFile(string path) => LoadDomainsFromJson(File.ReadAllText(path));
    public void LoadSpellsFromFile(string path) => LoadSpellsFromJson(File.ReadAllText(path));

    // --- Validation ---

    private readonly List<ContentError> _errors = new();
    public IReadOnlyList<ContentError> Errors => _errors;
    public bool HasErrors => _errors.Count > 0;

    public void Validate()
    {
        _errors.Clear();

        // Check empty IDs
        foreach (var race in _races.Values)
            if (string.IsNullOrWhiteSpace(race.Id))
                _errors.Add(new ContentError(ContentErrorKind.MissingId, "Race has empty ID"));

        foreach (var driver in _drivers.Values)
            if (string.IsNullOrWhiteSpace(driver.Id))
                _errors.Add(new ContentError(ContentErrorKind.MissingId, "Driver has empty ID"));

        foreach (var template in _templates.Values)
            if (string.IsNullOrWhiteSpace(template.Id))
                _errors.Add(new ContentError(ContentErrorKind.MissingId, "Template has empty ID"));

        foreach (var feat in _feats.Values)
            if (string.IsNullOrWhiteSpace(feat.Id))
                _errors.Add(new ContentError(ContentErrorKind.MissingId, "Feat has empty ID"));

        foreach (var domain in _domains.Values)
            if (string.IsNullOrWhiteSpace(domain.Id))
                _errors.Add(new ContentError(ContentErrorKind.MissingId, "Domain has empty ID"));

        foreach (var cf in _classFeatures.Values)
        {
            if (string.IsNullOrWhiteSpace(cf.Id))
                _errors.Add(new ContentError(ContentErrorKind.MissingId, "ClassFeature has empty ID"));
            foreach (var opt in cf.Options)
            {
                if (string.IsNullOrWhiteSpace(opt.Id))
                    _errors.Add(new ContentError(ContentErrorKind.MissingId,
                        $"ClassFeature '{cf.Id}' has option with empty ID"));
                ValidatePermabuffList(opt.GrantedPermabuffs, $"ClassFeature '{cf.Id}' option '{opt.Id}'");
            }
        }

        foreach (var spell in _spells.Values)
            if (string.IsNullOrWhiteSpace(spell.Id))
                _errors.Add(new ContentError(ContentErrorKind.MissingId, "Spell has empty ID"));

        // Cross-reference: Race → RacialHDDriverId exists as a driver
        foreach (var race in _races.Values)
        {
            if (race.RacialHDDriverId != null && !_drivers.ContainsKey(race.RacialHDDriverId))
                _errors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"Race '{race.Id}' references racial HD driver '{race.RacialHDDriverId}' which does not exist"));
        }

        // Cross-reference: HasFeat prerequisites → feat ID exists
        foreach (var feat in _feats.Values)
            ValidatePrerequisites(feat.Prerequisites, $"Feat '{feat.Id}'");

        foreach (var driver in _drivers.Values)
            ValidatePrerequisites(driver.Prerequisites, $"Driver '{driver.Id}'");

        // Cross-reference: GrantBonusFeat → feat ID exists
        foreach (var driver in _drivers.Values)
            ValidatePermabuffs(driver, $"Driver '{driver.Id}'");

        foreach (var feat in _feats.Values)
            ValidatePermabuffList(feat.GrantedPermabuffs, $"Feat '{feat.Id}'");

        foreach (var template in _templates.Values)
        {
            ValidatePermabuffList(template.CreationPermabuffs, $"Template '{template.Id}'");
            foreach (var (hd, buffs) in template.ScalingPermabuffs)
                ValidatePermabuffList(buffs, $"Template '{template.Id}' HD {hd}");
            foreach (var (masterLevel, buffs) in template.CompanionScalingPermabuffs)
                ValidatePermabuffList(buffs, $"Template '{template.Id}' master level {masterLevel}");
        }

        // Validate race permabuffs and scaling formulas
        foreach (var race in _races.Values)
        {
            ValidatePermabuffList(race.RacialPermabuffs, $"Race '{race.Id}'");
            foreach (var sf in race.ScalingFormulas)
            {
                try
                {
                    sf.Formula.Evaluate(new CharacterState());
                }
                catch (FormulaException ex)
                {
                    _errors.Add(new ContentError(ContentErrorKind.InvalidValue,
                        $"Race '{race.Id}' has invalid scaling formula '{sf.Formula.Expression}': {ex.Message}"));
                }
            }
        }

        // Validate template scaling formulas
        foreach (var template in _templates.Values)
        {
            foreach (var sf in template.ScalingFormulas)
            {
                try
                {
                    sf.Formula.Evaluate(new CharacterState());
                }
                catch (FormulaException ex)
                {
                    _errors.Add(new ContentError(ContentErrorKind.InvalidValue,
                        $"Template '{template.Id}' has invalid scaling formula '{sf.Formula.Expression}': {ex.Message}"));
                }
            }
        }

        foreach (var spell in _spells.Values)
            ValidateSpell(spell);

        // Equipment validation: empty IDs, broken permabuff refs, slot category sanity
        foreach (var eq in _equipment.Values)
        {
            if (string.IsNullOrWhiteSpace(eq.Id))
                _errors.Add(new ContentError(ContentErrorKind.MissingId, "Equipment has empty ID"));
            ValidatePermabuffList(eq.GrantedPermabuffs, $"Equipment '{eq.Id}'");
            ValidatePrerequisites(eq.Prerequisites, $"Equipment '{eq.Id}'");
        }
    }

    private void ValidatePrerequisites(List<Prerequisite> prerequisites, string context)
    {
        foreach (var prereq in prerequisites)
        {
            if (prereq is HasFeat hasFeat && !_feats.ContainsKey(hasFeat.FeatId)
                && !IsSelectableFeatVariant(hasFeat.FeatId))
                _errors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"{context} has HasFeat prerequisite referencing unknown feat '{hasFeat.FeatId}'"));

            if (prereq is HasFeatSelections hasFeatSel && !_feats.ContainsKey(hasFeatSel.FeatId)
                && !_feats.Keys.Any(id => id.StartsWith(hasFeatSel.FeatId + "_", StringComparison.Ordinal)))
                _errors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"{context} has HasFeatSelections prerequisite referencing unknown feat '{hasFeatSel.FeatId}'"));

            if (prereq is MinClassLevel minClass && !_drivers.ContainsKey(minClass.ClassId))
                _errors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"{context} has MinClassLevel prerequisite referencing unknown driver '{minClass.ClassId}'"));
        }
    }

    /// <summary>
    /// Checks if a feat ID like "spell_focus_conjuration" is a valid selection
    /// of a repeatable feat with selectionRequired (e.g., "spell_focus").
    /// </summary>
    private bool IsSelectableFeatVariant(string featId)
    {
        foreach (var feat in _feats.Values)
        {
            if (feat.SelectionRequired != null && feat.Repeatable
                && featId.StartsWith(feat.Id + "_", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private void ValidatePermabuffs(Driver driver, string context)
    {
        if (driver is HDDriver hd)
        {
            ValidatePermabuffList(hd.PerLevelPermabuffs, context);
            foreach (var (level, buffs) in hd.LevelPermabuffs)
                ValidatePermabuffList(buffs, $"{context} level {level}");
        }
    }

    private void ValidatePermabuffList(List<Permabuff> permabuffs, string context)
    {
        foreach (var buff in permabuffs)
        {
            if (buff is GrantBonusFeat gbf && !_feats.ContainsKey(gbf.FeatId))
                _errors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"{context} has GrantBonusFeat referencing unknown feat '{gbf.FeatId}'"));
        }
    }

    private void ValidateSpell(SpellDefinition spell)
    {
        foreach (var (spellListId, level) in spell.ClassLevels)
        {
            if (!IsKnownSpellList(spellListId))
            {
                _errors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"Spell '{spell.Id}' references unknown spell list '{spellListId}'"));
            }

            if (level < 0)
            {
                _errors.Add(new ContentError(ContentErrorKind.InvalidValue,
                    $"Spell '{spell.Id}' has invalid level {level} for spell list '{spellListId}'"));
            }
        }
    }

    private bool IsKnownSpellList(string spellListId)
    {
        if (_domains.ContainsKey(spellListId))
            return true;

        return _drivers.TryGetValue(spellListId, out var driver)
            && driver is HDDriver hd
            && hd.Kind == DriverKind.Class;
    }
}

public enum ConflictResolution
{
    LastWins,
    FirstWins,
    Warn,
    Error
}

public enum ContentErrorKind
{
    MissingId,
    DuplicateId,
    BrokenReference,
    InvalidValue
}

public record ContentError(ContentErrorKind Kind, string Message);
