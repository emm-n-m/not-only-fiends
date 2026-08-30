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
    private readonly Dictionary<string, DeityDefinition> _deities = new();
    private readonly Dictionary<string, SalientDivineAbilityDefinition> _salientDivineAbilities = new();
    private readonly Dictionary<string, DomainDefinition> _domains = new();
    private readonly Dictionary<string, SpellDefinition> _spells = new();
    private readonly Dictionary<string, SkillDefinition> _skills = new();
    private readonly Dictionary<string, ClassFeatureDefinition> _classFeatures = new();
    private readonly Dictionary<string, LanguageDefinition> _languages = new();
    private readonly Dictionary<string, EquipmentDefinition> _equipment = new();
    private Dictionary<string, SpellDefinition>? _spellsByName;
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
        RegisterContentType(new ContentTypeHandler<DeityDefinition>(
            "deities", deity => Register(_deities, deity, d => d.Id)));
        RegisterContentType(new ContentTypeHandler<SalientDivineAbilityDefinition>(
            "salient_divine_abilities", ability => Register(_salientDivineAbilities, ability, a => a.Id)));
        RegisterContentType(new ContentTypeHandler<DomainDefinition>(
            "domains", domain => Register(_domains, domain, d => d.Id)));
        RegisterContentType(new ContentTypeHandler<SpellDefinition>(
            "spells", RegisterSpell));
        RegisterContentType(new ContentTypeHandler<SkillDefinition>(
            "skills", skill => Register(_skills, skill, sk => sk.Id)));
        RegisterContentType(new ContentTypeHandler<ClassFeatureDefinition>(
            "class_features", cf => Register(_classFeatures, cf, c => c.Id)));
        RegisterContentType(new ContentTypeHandler<LanguageDefinition>(
            "languages", language => Register(_languages, language, l => l.Id)));
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
                    _loadDiagnostics.Add(new ContentError(ContentErrorKind.DuplicateId,
                        $"Duplicate {typeof(T).Name} ID '{id}' — later definition used", IsWarning: true));
                    break;
                case ConflictResolution.Error:
                    _loadDiagnostics.Add(new ContentError(ContentErrorKind.DuplicateId,
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
    public void RegisterDeity(DeityDefinition deity) => Register(_deities, deity, d => d.Id);
    public void RegisterSalientDivineAbility(SalientDivineAbilityDefinition ability) =>
        Register(_salientDivineAbilities, ability, a => a.Id);
    public void RegisterDomain(DomainDefinition domain) => Register(_domains, domain, d => d.Id);
    public void RegisterSpell(SpellDefinition spell)
    {
        Register(_spells, spell, s => s.Id);
        _spellsByName = null;
    }
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

    public bool TryGetTemplate(string id, out TemplateDriver? template) =>
        _templates.TryGetValue(id, out template);

    public FeatDefinition GetFeat(string id) =>
        _feats.TryGetValue(id, out var feat)
            ? feat
            : throw new KeyNotFoundException($"Feat not found: {id}");

    public bool TryGetFeat(string id, out FeatDefinition? feat)
    {
        if (_feats.TryGetValue(id, out feat))
            return true;

        // Match selectable variant IDs like "spell_focus:enchantment" (legacy underscore
        // dialects included) → base "spell_focus"
        foreach (var f in _feats.Values)
        {
            if (f.SelectionRequired != null && f.Repeatable
                && FeatVariantId.IsVariant(id, f.Id))
            {
                feat = f;
                return true;
            }
        }

        feat = null;
        return false;
    }

    public DeityDefinition GetDeity(string id) =>
        _deities.TryGetValue(id, out var deity)
            ? deity
            : throw new KeyNotFoundException($"Deity not found: {id}");

    /// <summary>
    /// Resolves the value persisted in <see cref="Character.Deity"/>. New content may use a
    /// stable <c>deity:*</c> ID, while PCGen imports and existing saves carry a display name.
    /// </summary>
    public bool TryResolveDeity(string reference, out DeityDefinition? deity)
    {
        if (_deities.TryGetValue(reference, out deity))
            return true;

        deity = _deities.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, reference, StringComparison.OrdinalIgnoreCase));
        return deity != null;
    }

    public SalientDivineAbilityDefinition GetSalientDivineAbility(string id) =>
        _salientDivineAbilities.TryGetValue(id, out var ability)
            ? ability
            : throw new KeyNotFoundException($"Salient divine ability not found: {id}");

    public bool TryGetSalientDivineAbility(string id, out SalientDivineAbilityDefinition? ability) =>
        _salientDivineAbilities.TryGetValue(id, out ability);

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

    /// <summary>
    /// Looks up a spell by its display name. PCGen persists names rather than content IDs, and
    /// not every legacy name transforms to the catalog ID mechanically.
    /// </summary>
    public bool TryGetSpellByName(string name, out SpellDefinition? spell)
    {
        _spellsByName ??= _spells.Values
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return _spellsByName.TryGetValue(name, out spell);
    }

    public bool TryGetClassFeature(string id, out ClassFeatureDefinition? cf) =>
        _classFeatures.TryGetValue(id, out cf);

    public bool TryGetSkill(string id, out SkillDefinition? skill) =>
        _skills.TryGetValue(id, out skill);

    public bool TryGetLanguage(string id, out LanguageDefinition? language) =>
        _languages.TryGetValue(id, out language);

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
    public IEnumerable<DeityDefinition> GetAllDeities() => _deities.Values;
    public IEnumerable<SalientDivineAbilityDefinition> GetAllSalientDivineAbilities() =>
        _salientDivineAbilities.Values;
    public IEnumerable<RaceDefinition> GetAllRaces() => _races.Values;
    public IEnumerable<Driver> GetAllDrivers() => _drivers.Values;
    public IEnumerable<TemplateDriver> GetAllTemplates() => _templates.Values;
    public IEnumerable<DomainDefinition> GetAllDomains() => _domains.Values;
    public IEnumerable<SpellDefinition> GetAllSpells() => _spells.Values;
    public IEnumerable<SkillDefinition> GetAllSkills() => _skills.Values;
    public IEnumerable<LanguageDefinition> GetAllLanguages() => _languages.Values;
    public IEnumerable<EquipmentDefinition> GetAllEquipment() => _equipment.Values;
    /// <summary>
    /// The level at which a single source grants a spell. A class source reads the spell's own
    /// <see cref="SpellDefinition.ClassLevels"/>; a <c>domain:*</c> source reads the domain
    /// definition's <see cref="DomainDefinition.BonusSpells"/> (level → spell), so a caster who
    /// draws a domain as a spell-list source gets its spells at the domain's levels — matching
    /// how domain spells are actually catalogued, not requiring a redundant key on every spell.
    /// </summary>
    public bool TryGetSpellLevelForSource(SpellDefinition spell, string sourceId, out int level)
    {
        if (spell.ClassLevels.TryGetValue(sourceId, out level))
            return true;

        if (sourceId.StartsWith("domain:", StringComparison.Ordinal)
            && _domains.TryGetValue(sourceId, out var domain))
        {
            foreach (var (domainLevel, spellId) in domain.BonusSpells)
            {
                if (string.Equals(spellId, spell.Id, StringComparison.Ordinal))
                {
                    level = domainLevel;
                    return true;
                }
            }
        }

        level = 0;
        return false;
    }

    public bool TryGetSpellLevelForList(SpellDefinition spell, string spellListId, out int level)
    {
        if (IsSpellExcludedFromList(spell, spellListId))
        {
            level = 0;
            return false;
        }

        if (TryGetSpellLevelForSource(spell, spellListId, out level))
            return true;

        if (_drivers.TryGetValue(spellListId, out var driver) && driver is HDDriver hd &&
            hd.Spellcasting?.SpellListSources.Count > 0)
        {
            var levels = hd.Spellcasting.SpellListSources
                .Select(source => TryGetSpellLevelForSource(spell, source, out var l) ? (int?)l : null)
                .Where(l => l.HasValue)
                .Select(l => l!.Value)
                .ToList();
            if (levels.Count > 0)
            {
                level = levels.Min();
                return true;
            }
        }

        level = 0;
        return false;
    }

    public bool IsSpellExcludedFromList(SpellDefinition spell, string spellListId) =>
        _drivers.TryGetValue(spellListId, out var driver)
        && driver is HDDriver hd
        && hd.Spellcasting?.SpellListExclusions.Contains(spell.Id, StringComparer.Ordinal) == true;

    public IEnumerable<SpellDefinition> GetSpellsForList(string spellListId, int? maxSpellLevel = null) =>
        _spells.Values
            .Select(spell => (Spell: spell,
                HasLevel: TryGetSpellLevelForList(spell, spellListId, out var level),
                Level: level))
            .Where(item => item.HasLevel && (!maxSpellLevel.HasValue || item.Level <= maxSpellLevel.Value))
            .OrderBy(item => item.Level)
            .ThenBy(item => item.Spell.Name)
            .Select(item => item.Spell);
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
    public void LoadDeitiesFromJson(string json) => LoadJsonForDirectory("deities", json);
    public void LoadSalientDivineAbilitiesFromJson(string json) =>
        LoadJsonForDirectory("salient_divine_abilities", json);
    public void LoadDomainsFromJson(string json) => LoadJsonForDirectory("domains", json);
    public void LoadSpellsFromJson(string json) => LoadJsonForDirectory("spells", json);

    public void LoadDriverFromFile(string path) => LoadDriverFromJson(File.ReadAllText(path));
    public void LoadRaceFromFile(string path) => LoadRaceFromJson(File.ReadAllText(path));
    public void LoadTemplateFromFile(string path) => LoadTemplateFromJson(File.ReadAllText(path));
    public void LoadFeatsFromFile(string path) => LoadFeatsFromJson(File.ReadAllText(path));
    public void LoadDeitiesFromFile(string path) => LoadDeitiesFromJson(File.ReadAllText(path));
    public void LoadSalientDivineAbilitiesFromFile(string path) =>
        LoadSalientDivineAbilitiesFromJson(File.ReadAllText(path));
    public void LoadDomainsFromFile(string path) => LoadDomainsFromJson(File.ReadAllText(path));
    public void LoadSpellsFromFile(string path) => LoadSpellsFromJson(File.ReadAllText(path));

    // --- Validation ---

    private readonly List<ContentError> _loadDiagnostics = new();
    private readonly List<ContentError> _validationErrors = new();
    public IReadOnlyList<ContentError> Errors => _loadDiagnostics.Concat(_validationErrors).ToList();
    public bool HasErrors => Errors.Any(error => !error.IsWarning);
    public bool HasWarnings => Errors.Any(error => error.IsWarning);

    public void Validate()
    {
        _validationErrors.Clear();

        // Check empty IDs
        foreach (var race in _races.Values)
            if (string.IsNullOrWhiteSpace(race.Id))
                _validationErrors.Add(new ContentError(ContentErrorKind.MissingId, "Race has empty ID"));

        foreach (var driver in _drivers.Values)
            if (string.IsNullOrWhiteSpace(driver.Id))
                _validationErrors.Add(new ContentError(ContentErrorKind.MissingId, "Driver has empty ID"));

        foreach (var template in _templates.Values)
            if (string.IsNullOrWhiteSpace(template.Id))
                _validationErrors.Add(new ContentError(ContentErrorKind.MissingId, "Template has empty ID"));

        foreach (var feat in _feats.Values)
            if (string.IsNullOrWhiteSpace(feat.Id))
                _validationErrors.Add(new ContentError(ContentErrorKind.MissingId, "Feat has empty ID"));

        foreach (var deity in _deities.Values)
            if (string.IsNullOrWhiteSpace(deity.Id))
                _validationErrors.Add(new ContentError(ContentErrorKind.MissingId, "Deity has empty ID"));

        foreach (var ability in _salientDivineAbilities.Values)
        {
            if (string.IsNullOrWhiteSpace(ability.Id))
                _validationErrors.Add(new ContentError(ContentErrorKind.MissingId,
                    "Salient divine ability has empty ID"));
            if (ability.MinimumDivineRank < 1 || ability.MinimumDivineRank > 20)
                _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                    $"Salient divine ability '{ability.Id}' has invalid minimum divine rank {ability.MinimumDivineRank}"));
            ValidatePrerequisites(ability.Prerequisites, $"Salient divine ability '{ability.Id}'");
        }

        foreach (var domain in _domains.Values)
            if (string.IsNullOrWhiteSpace(domain.Id))
                _validationErrors.Add(new ContentError(ContentErrorKind.MissingId, "Domain has empty ID"));

        foreach (var cf in _classFeatures.Values)
        {
            if (string.IsNullOrWhiteSpace(cf.Id))
                _validationErrors.Add(new ContentError(ContentErrorKind.MissingId, "ClassFeature has empty ID"));
            foreach (var opt in cf.Options)
            {
                if (string.IsNullOrWhiteSpace(opt.Id))
                    _validationErrors.Add(new ContentError(ContentErrorKind.MissingId,
                        $"ClassFeature '{cf.Id}' has option with empty ID"));
                ValidatePermabuffList(opt.GrantedPermabuffs, $"ClassFeature '{cf.Id}' option '{opt.Id}'");
                foreach (var (benefitSet, buffs) in opt.AdditionalPermabuffs)
                    ValidatePermabuffList(buffs, $"ClassFeature '{cf.Id}' option '{opt.Id}' benefit set '{benefitSet}'");
            }
        }

        // Cross-reference: deity domain lists and favored weapons are the mechanical half of
        // the definition. A misspelled ID would otherwise make domain filtering incomplete or
        // silently put the War-domain rule back onto its manual fallback.
        foreach (var deity in _deities.Values)
        {
            foreach (var domainId in deity.DomainIds)
            {
                if (!_domains.ContainsKey(domainId))
                    _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                        $"Deity '{deity.Id}' references domain '{domainId}' which does not exist"));
            }

            if (deity.FavoredWeaponId != null
                && (!_equipment.TryGetValue(deity.FavoredWeaponId, out var weapon)
                    || weapon.Category != EquipmentCategory.Weapon))
                _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"Deity '{deity.Id}' references favored weapon '{deity.FavoredWeaponId}' which is not a weapon"));
        }

        foreach (var spell in _spells.Values)
            if (string.IsNullOrWhiteSpace(spell.Id))
                _validationErrors.Add(new ContentError(ContentErrorKind.MissingId, "Spell has empty ID"));

        // Cross-reference: Race → RacialHDDriverId exists as a driver
        foreach (var race in _races.Values)
        {
            if (race.RacialHDDriverId != null && !_drivers.ContainsKey(race.RacialHDDriverId))
                _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"Race '{race.Id}' references racial HD driver '{race.RacialHDDriverId}' which does not exist"));
        }

        // Cross-reference: Domain → every bonus spell ID exists. A dangling entry here is
        // invisible at runtime — the domain simply grants nothing at that level — so it has to be
        // caught at load. Eleven core domains shipped broken for months for exactly this reason.
        foreach (var domain in _domains.Values)
        {
            foreach (var (level, spellId) in domain.BonusSpells)
            {
                if (!_spells.ContainsKey(spellId))
                    _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                        $"Domain '{domain.Id}' grants bonus spell '{spellId}' at level {level} which does not exist"));
            }
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

        foreach (var domain in _domains.Values)
            ValidatePermabuffList(domain.GrantedPermabuffs, $"Domain '{domain.Id}'");

        foreach (var template in _templates.Values)
        {
            ValidatePrerequisites(template.Prerequisites, $"Template '{template.Id}'");
            ValidatePrerequisites(template.ApplicabilityPrerequisites, $"Template '{template.Id}' applicability");
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
                    _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
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
                    _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
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
                _validationErrors.Add(new ContentError(ContentErrorKind.MissingId, "Equipment has empty ID"));
            ValidatePermabuffList(eq.GrantedPermabuffs, $"Equipment '{eq.Id}'");
            ValidatePrerequisites(eq.Prerequisites, $"Equipment '{eq.Id}'");
            ValidateIntelligentItem(eq);
        }
    }

    private void ValidateIntelligentItem(EquipmentDefinition equipment)
    {
        var item = equipment.IntelligentItem;
        if (item == null) return;

        if (equipment.Category is EquipmentCategory.Potion or EquipmentCategory.Scroll
            or EquipmentCategory.Wand or EquipmentCategory.Staff or EquipmentCategory.Ammunition)
            _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                $"Equipment '{equipment.Id}' is consumed or charged and cannot be intelligent"));

        if (item.MentalAbilities.Intelligence < 1 || item.MentalAbilities.Wisdom < 1
            || item.MentalAbilities.Charisma < 1)
            _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                $"Equipment '{equipment.Id}' has an intelligent item with an invalid mental ability score"));

        if (item.Senses.RangeFt < 0)
            _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                $"Equipment '{equipment.Id}' has a negative intelligent-item sense range"));

        if (item.Powers.Any(power => string.IsNullOrWhiteSpace(power.Name)))
            _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                $"Equipment '{equipment.Id}' has an unnamed intelligent-item power"));

        if (item.Powers.GroupBy(power => power.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                $"Equipment '{equipment.Id}' repeats an intelligent-item power"));

        foreach (var languageId in item.LanguageIds)
            if (!_languages.ContainsKey(languageId))
                _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"Equipment '{equipment.Id}' has an intelligent item referencing unknown language '{languageId}'"));
        if (item.LanguageIds.Distinct(StringComparer.Ordinal).Count() != item.LanguageIds.Count)
            _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                $"Equipment '{equipment.Id}' repeats an intelligent-item language"));
        if (item.LanguageIds.Count > item.IntelligenceLanguageAllowance)
            _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                $"Equipment '{equipment.Id}' chooses {item.LanguageIds.Count} intelligent-item bonus languages but Intelligence permits {item.IntelligenceLanguageAllowance}"));

        if (item.DedicatedPower != null && item.DedicatedPower.Kind != IntelligentItemPowerKind.Dedicated)
            _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                $"Equipment '{equipment.Id}' has a dedicated power with the wrong kind"));

        if (item.HasSpecialPurpose != (item.DedicatedPower != null))
            _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                $"Equipment '{equipment.Id}' must define both a special purpose and its dedicated power"));
    }

    private void ValidatePrerequisites(List<Prerequisite> prerequisites, string context)
    {
        foreach (var prereq in prerequisites)
        {
            if (prereq is HasFeat hasFeat && !_feats.ContainsKey(hasFeat.FeatId)
                && !IsSelectableFeatVariant(hasFeat.FeatId))
                _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"{context} has HasFeat prerequisite referencing unknown feat '{hasFeat.FeatId}'"));

            if (prereq is HasFeatSelections hasFeatSel && !_feats.ContainsKey(hasFeatSel.FeatId)
                && !_feats.Keys.Any(id => id.StartsWith(hasFeatSel.FeatId + "_", StringComparison.Ordinal)))
                _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"{context} has HasFeatSelections prerequisite referencing unknown feat '{hasFeatSel.FeatId}'"));

            if (prereq is MinClassLevel minClass && !_drivers.ContainsKey(minClass.ClassId))
                _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"{context} has MinClassLevel prerequisite referencing unknown driver '{minClass.ClassId}'"));

            if (prereq is HasDivineDomain domain && !_domains.ContainsKey(domain.DomainId))
                _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"{context} references unknown divine domain '{domain.DomainId}'"));

            if (prereq is HasSalientDivineAbility salient
                && !_salientDivineAbilities.ContainsKey(salient.AbilityId))
                _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"{context} references unknown salient divine ability '{salient.AbilityId}'"));

            if (prereq is AnyOf anyOf)
                ValidatePrerequisites(anyOf.Options, context);
        }
    }

    /// <summary>
    /// Checks if a feat ID like "spell_focus:conjuration" (legacy underscore dialects
    /// included) is a valid selection of a repeatable feat with selectionRequired.
    /// </summary>
    private bool IsSelectableFeatVariant(string featId)
    {
        foreach (var feat in _feats.Values)
        {
            if (feat.SelectionRequired != null && feat.Repeatable
                && FeatVariantId.IsVariant(featId, feat.Id))
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
                _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"{context} has GrantBonusFeat referencing unknown feat '{gbf.FeatId}'"));
            if (buff is ApplyClassFeatureOptionBenefits benefits && !_classFeatures.ContainsKey(benefits.FeatureType))
                _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"{context} applies benefits from unknown class feature '{benefits.FeatureType}'"));

            var formula = buff switch
            {
                GrantEffectiveLevels effectiveLevels => effectiveLevels.BonusFormula,
                GrantRacialSpellcasting racialSpellcasting => racialSpellcasting.LevelFormula,
                GrantCompanionSlot companionSlot => companionSlot.EffectiveLevelFormula,
                GrantTypedBonus typedBonus => typedBonus.Value,
                GrantEquipmentSkillBonus skillBonus => skillBonus.Value,
                _ => null
            };
            if (formula != null)
                ValidateFormula(formula, context, buff.GetType().Name);
        }
    }

    private void ValidateFormula(Formula formula, string context, string source)
    {
        try
        {
            formula.Evaluate(new CharacterState());
        }
        catch (FormulaException ex)
        {
            _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                $"{context} has invalid {source} formula '{formula.Expression}': {ex.Message}"));
        }
    }

    private void ValidateSpell(SpellDefinition spell)
    {
        foreach (var (spellListId, level) in spell.ClassLevels)
        {
            if (!IsKnownSpellList(spellListId))
            {
                _validationErrors.Add(new ContentError(ContentErrorKind.BrokenReference,
                    $"Spell '{spell.Id}' references unknown spell list '{spellListId}'"));
            }

            if (level is < 0 or > EpicSpellcasting.SpellLevel)
            {
                _validationErrors.Add(new ContentError(ContentErrorKind.InvalidValue,
                    $"Spell '{spell.Id}' has invalid level {level} for spell list '{spellListId}'"));
            }
        }
    }

    private bool IsKnownSpellList(string spellListId)
    {
        if (EpicSpellcasting.IsSpellList(spellListId))
            return true;

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

public record ContentError(ContentErrorKind Kind, string Message, bool IsWarning = false);
