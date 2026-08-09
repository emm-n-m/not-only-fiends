using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NotOnlyFiendsFeed.Contracts;
using NotOnlyFiendsFeed.Services;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsFeed.Components.Pages;

public partial class BuilderView
{
    private string FormatCounter(string key, int value) => key switch
    {
        "sneak_attack_dice" => $"Sneak Attack +{value}d6",
        "trap_sense_bonus" => $"Trap Sense +{value}",
        "rage_uses" => $"Rage {value}/day",
        "smite_evil_uses" => $"Smite Evil {value}/day",
        "smite_good_uses" => $"Smite Good {value}/day",
        "remove_disease_uses" => $"Remove Disease {value}/week",
        "impromptu_sneak_attack_uses" => $"Impromptu Sneak Attack {value}/day",
        "wild_shape_uses" => $"Wild Shape {value}/day",
        "bardic_music_uses" => $"Bardic Music {value}/day",
        "shadow_jump_distance" => $"Shadow Jump {value} ft.",
        _ => $"{key}: {value}"
    };

    [Parameter] public string? Id { get; set; }

    private bool _loading = true;
    private string? _error;
    private Character _character = new()
    {
        Name = "New Character",
        RaceId = "race:human",
        BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 }
    };
    private CharacterState? _state;
    private List<CompanionBuild>? _companionBuilds;
    private ReplayStudio _engine = null!;
    private List<RaceDefinition> _races = new();
    private List<LanguageDefinition> _languages = new();
    // Monster, companion and creature races the source never priced as PC options are hidden by
    // default — they were previously offered as character choices with nothing marking them, which
    // is a discoverability trap on the first step of character creation. Not removed outright:
    // the builder is also used to construct companions and monsters.
    private bool _showNonPcRaces;
    private int _nonPcRaceCount;
    private List<TemplateDriver> _templates = new();
    private List<Driver> _drivers = new();
    private Dictionary<string, string> _driverNames = new();

    // Driver previews ("what does the next HD buy me") — computed on demand from the
    // same in-process engine the REST API uses, so the builder and agents agree.
    private bool _showDriverPreviews;
    private List<DriverPreviewDto> _driverPreviews = new();
    private int _previewNextHd;
    private List<FeatDefinition> _feats = new();
    private List<SpellDefinition> _spells = new();
    private List<SkillDefinition> _skills = new();
    private readonly Dictionary<int, List<TickSpellSummary>> _tickSpellSummaries = new();
    private readonly Dictionary<int, TickSkillInfo> _tickSkillInfos = new();
    private readonly Dictionary<int, List<FeatDefinition>> _tickAvailableFeats = new();
    private readonly Dictionary<int, TickCasterInfo> _tickCasterInfos = new();
    private readonly Dictionary<int, List<CompanionSlotState>> _tickNewCompanionSlots = new();

    // How many feat slots each tick hands out. Lets the Feats tab list the ticks that owe a
    // choice, not just the ones that already have feats recorded.
    private readonly Dictionary<int, int> _tickFeatGrants = new();
    private readonly Dictionary<int, TickWizardSchoolPicks> _tickWizardSchoolPicks = new();
    private readonly Dictionary<int, List<TickClassFeaturePick>> _tickClassFeaturePicks = new();
    // Schools given up as of each tick, so the spell dropdowns can exclude them.
    private readonly Dictionary<int, HashSet<string>> _tickProhibitedSchools = new();
    // 0-level spells a spellbook caster holds for free at each tick — "all of them" only for a
    // universalist; a specialist loses the cantrips of its given-up schools too.
    private readonly Dictionary<int, int> _tickAutomaticCantrips = new();
    private List<DomainDefinition> _domains = new();
    private List<ClassFeatureDefinition> _classFeatures = new();
    private List<EquipmentDefinition> _equipmentCatalog = new();
    private string _equipmentPickerCategory = "weapon";
    private string _equipmentPickerSelection = "";
    private CharacterTab _activeTab = CharacterTab.Summary;

    // One expansion set, not three. Each facet now owns a tab, so the old nested collapses
    // (a skill panel inside an expanded tick inside the timeline) had nothing left to hide.
    // It also fixes a real bug: RemoveTick only ever re-indexed the HD set, so the skill and
    // spell sets silently pointed at the wrong ticks after a removal.
    private readonly HashSet<int> _expandedTicks = new();
    private string _selectedTemplate = "";
    // Keyed by tick index, not single fields: the Feats tab renders every tick's picker at
    // once, so one shared field would let typing into HD 7's box fill HD 3's and let HD 3's
    // "Add" button commit HD 7's feat.
    private readonly Dictionary<int, string> _featInputs = new();
    private readonly Dictionary<int, string> _featSelectionInputs = new();
    private readonly Dictionary<int, string> _domainInputs = new();

    private string FeatInput(int index) => _featInputs.GetValueOrDefault(index, "");
    private string FeatSelectionInput(int index) => _featSelectionInputs.GetValueOrDefault(index, "");
    private string DomainInput(int index) => _domainInputs.GetValueOrDefault(index, "");

    private void SetFeatSelectionInput(int index, ChangeEventArgs e) =>
        _featSelectionInputs[index] = e.Value?.ToString() ?? string.Empty;

    /// <summary>Drops every per-tick picker draft. Call whenever the tick list is reshaped.</summary>
    private void ClearPickerInputs()
    {
        _featInputs.Clear();
        _featSelectionInputs.Clear();
        _domainInputs.Clear();
    }
    private string? _characterStoreId;
    // The store id last loaded via the /builder/{id} route. Tracked so navigating between
    // characters (rail / roster links) reloads even though the component instance is reused.
    private string? _loadedId;
    private bool _catalogsLoaded;
    private List<CharacterFileInfo> _availableCharacters = new();

    // Which stored characters list this one as their companion. Cached rather than computed
    // in markup: CharacterStore.FindMasters deserializes every character JSON on disk, and
    // this value is read during render and gates whether the Companions tab is offered.
    // Only _characterStoreId can change the answer, so refresh where that is assigned.
    private CharacterFileInfo[] _masterLinks = Array.Empty<CharacterFileInfo>();
    private readonly Dictionary<string, string> _linkCandidateByLinkType = new();
    private int _leadershipFollowerLevel = 1;
    private string _leadershipFollowerCandidate = string.Empty;

    private static readonly string[] _spellSchools = { "Abjuration", "Conjuration", "Divination", "Enchantment", "Evocation", "Illusion", "Necromancy", "Transmutation" };
    private int? _pendingWishEvent;
    private string? _statusMessage;
    private bool _statusIsError;
    private bool _showOpenPicker;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var registry = Content.Registry;
            _engine = Content.ReplayStudio;
            _races = registry.GetAllRaces().ToList();
            _languages = registry.GetAllLanguages().ToList();
            _nonPcRaceCount = _races.Count(r => !RaceCatalog.IsSanctionedPcRace(r));
            _templates = registry.GetAllTemplates().ToList();
            _drivers = registry.GetAllDrivers().ToList();
            _driverNames = _drivers
                .GroupBy(d => d.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
            _addHdDriverId = _drivers.FirstOrDefault()?.Id ?? "class:fighter";
            _feats = registry.GetAllFeats().OrderBy(f => f.Name).ToList();
            _spells = registry.GetAllSpells().OrderBy(s => s.Name).ToList();
            _skills = registry.GetAllSkills().OrderBy(s => s.Name).ToList();
            _domains = registry.GetAllDomains().OrderBy(d => d.Name).ToList();
            _classFeatures = registry.GetAllClassFeatures().ToList();
            _equipmentCatalog = registry.GetAllEquipment().OrderBy(e => e.Category).ThenBy(e => e.Name).ToList();
            RefreshAvailableCharacters();

            // Route param: /builder/{id} loads an existing character from the store.
            if (!string.IsNullOrWhiteSpace(Id) && CharacterStore.IsConfigured)
            {
                try
                {
                    _character = CharacterStore.Get(Id);
                    _characterStoreId = Id;
                    _loadedId = Id;
                    ResetForLoadedCharacter();
                }
                catch (CharacterStoreException ex)
                {
                    _error = ex.Message;
                }
            }

            // Check for imported character from /import page
            var uri = new Uri(Navigation.Uri);
            if (uri.Query.Contains("imported=true"))
            {
                var importedJson = await JS.InvokeAsync<string?>("sessionStorage.getItem", "importedCharacter");
                if (!string.IsNullOrEmpty(importedJson))
                {
                    var imported = System.Text.Json.JsonSerializer.Deserialize<Character>(importedJson, JsonOptions.Default);
                    if (imported != null)
                    {
                        _character = imported;
                        _characterStoreId = null;
                        ResetForLoadedCharacter();
                        await JS.InvokeVoidAsync("sessionStorage.removeItem", "importedCharacter");
                        SetStatus($"Imported \"{imported.Name}\" from PCGen.", false);
                    }
                }
            }

            OnCharacterChanged();
            _catalogsLoaded = true;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Reloads the character when the <c>/builder/{id}</c> route parameter changes. Blazor reuses
    /// this component instance across such navigations, so <see cref="OnInitializedAsync"/> — which
    /// loads the catalogs and the first character — does not run again. Without this, clicking a
    /// different character in the rail or roster while already on the builder did nothing.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (!_catalogsLoaded)
            return; // first load is handled by OnInitializedAsync
        if (string.IsNullOrWhiteSpace(Id) || Id == _loadedId)
            return;
        if (!CharacterStore.IsConfigured)
            return;

        _error = null;
        try
        {
            _character = CharacterStore.Get(Id);
            _characterStoreId = Id;
            _loadedId = Id;
            ResetForLoadedCharacter();
            OnCharacterChanged();
        }
        catch (CharacterStoreException ex)
        {
            _error = ex.Message;
        }
    }

    private void OnCharacterChanged()
    {
        RefreshPerTickData();
        EvaluateCharacter();
        NormalizeActiveTab();
    }

    private void RefreshPerTickData()
    {
        _tickSpellSummaries.Clear();
        _tickSkillInfos.Clear();
        _tickAvailableFeats.Clear();
        _tickCasterInfos.Clear();
        _tickNewCompanionSlots.Clear();
        _tickWizardSchoolPicks.Clear();
        _tickClassFeaturePicks.Clear();
        _tickProhibitedSchools.Clear();
        _tickAutomaticCantrips.Clear();
        _tickFeatGrants.Clear();

        if (_engine == null || !_character.Ticks.Any())
            return;

        var prevSlotKeys = new HashSet<(string, string)>();
        var prevPendingFeats = 0;
        for (int i = 0; i < _character.Ticks.Count; i++)
        {
            CharacterState state;
            try
            {
                state = _engine.Evaluate(_character, upToHD: i + 1);
            }
            catch
            {
                continue;
            }

            var summaries = new List<TickSpellSummary>();
            foreach (var (spellListId, sc) in state.Spellcasting.OrderBy(kv => kv.Key))
            {
                var availableCount = _spells.Count(spell =>
                    Content.Registry.TryGetSpellLevelForList(spell, spellListId, out var level)
                    && level <= sc.MaxSpellLevel);
                summaries.Add(new TickSpellSummary(spellListId, sc.MaxSpellLevel, availableCount, IsDomainList: false));
            }
            foreach (var domainId in state.Domains.OrderBy(d => d))
            {
                var domainMaxSpellLevel = 0;
                if (state.DomainOwners.TryGetValue(domainId, out var ownerClassId)
                    && state.Spellcasting.TryGetValue(ownerClassId, out var ownerSc))
                    domainMaxSpellLevel = ownerSc.MaxSpellLevel;
                var availableCount = _spells.Count(spell =>
                    spell.ClassLevels.TryGetValue(domainId, out var level)
                    && level <= domainMaxSpellLevel);
                summaries.Add(new TickSpellSummary(domainId, domainMaxSpellLevel, availableCount, IsDomainList: true));
            }
            if (summaries.Count > 0)
                _tickSpellSummaries[i] = summaries;

            _tickSkillInfos[i] = new TickSkillInfo(
                state.UnspentSkillPoints,
                state.MaxHalfRanks,
                new HashSet<string>(state.CurrentTickClassSkills),
                new HashSet<string>(state.ClassSkills),
                new Dictionary<string, int>(state.SkillHalfRanks));

            _tickAvailableFeats[i] = _engine.GetAvailableFeats(state)
                .OrderBy(f => f.Name).ToList();

            // Feats granted here = the rise in outstanding slots since the previous tick, plus
            // whatever was already spent on this tick (spending lowers the pending count again).
            var pendingNow = state.PendingFeatSlots + state.PendingBonusFeatSlots;
            _tickFeatGrants[i] = pendingNow - prevPendingFeats
                + (_character.Ticks[i].Choices.FeatIds?.Count ?? 0);
            prevPendingFeats = pendingNow;

            // Wizard school pickers belong on the tick that granted them. Once a school is chosen
            // the pending count drops to zero, so the tick's own recorded choices have to be added
            // back in or the dropdowns would vanish the moment they were used.
            var chosenHere = _character.Ticks[i].Choices.ClassFeatureChoices;
            var specialisationPending = state.PendingClassFeatureSelections
                .GetValueOrDefault(WizardSchools.SpecializationFeature);
            var prohibitedPending = state.PendingClassFeatureSelections
                .GetValueOrDefault(WizardSchools.ProhibitedFeature);
            var specialisationHere = chosenHere?.GetValueOrDefault(WizardSchools.SpecializationFeature)?.Count ?? 0;
            var prohibitedHere = chosenHere?.GetValueOrDefault(WizardSchools.ProhibitedFeature)?.Count ?? 0;

            if (specialisationPending + specialisationHere > 0 || prohibitedPending + prohibitedHere > 0)
                _tickWizardSchoolPicks[i] = new TickWizardSchoolPicks(
                    PendingSpecialization: specialisationPending + specialisationHere > 0,
                    PendingProhibited: prohibitedPending + prohibitedHere);

            var staticFeaturePicks = _classFeatures
                .Where(f => f.Id != WizardSchools.SpecializationFeature
                    && f.Id != WizardSchools.ProhibitedFeature
                    && !state.CompanionSlots.Any(slot => slot.ClassFeatureType == f.Id)
                    && f.Options.Count > 0)
                .Select(f => new TickClassFeaturePick(
                    f,
                    state.PendingClassFeatureSelections.GetValueOrDefault(f.Id)
                        + (chosenHere?.GetValueOrDefault(f.Id)?.Count ?? 0)))
                .Where(p => p.SlotCount > 0)
                .ToList();
            if (staticFeaturePicks.Count > 0)
                _tickClassFeaturePicks[i] = staticFeaturePicks;

            _tickProhibitedSchools[i] = WizardSchools.ProhibitedSchools(state).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _tickAutomaticCantrips[i] = state.Spellcasting
                .Where(kv => kv.Value.Acquisition == SpellAcquisition.Spellbook)
                .Sum(kv => WizardSchools.AutomaticCantrips(_spells, kv.Key, state).Count());

            // Companion slots granted at this tick = slots present now that weren't
            // present in the previous tick's snapshot.
            var newSlots = state.CompanionSlots
                .Where(s => prevSlotKeys.Add((s.LinkType, s.Granter)))
                .ToList();
            if (newSlots.Count > 0)
                _tickNewCompanionSlots[i] = newSlots;

            var advanceWarning = state.Warnings.FirstOrDefault(w => w.Message.Contains("AdvanceSpellcasting") && w.Message.Contains("multiple matching"));
            var pendingTotal = state.PendingDomainSelections.Values.Sum();
            if (pendingTotal > 0 || state.Spellcasting.Any() || advanceWarning != null)
            {
                _tickCasterInfos[i] = new TickCasterInfo(
                    pendingTotal,
                    new List<string>(state.Domains),
                    new Dictionary<string, string>(state.DomainOwners),
                    state.Spellcasting.ToDictionary(
                        kv => kv.Key,
                        kv => new TickSpellcastingInfo(
                            kv.Value.CastingType,
                            kv.Value.CastingStat,
                            kv.Value.CasterLevel,
                            kv.Value.MaxSpellLevel,
                            new Dictionary<int, int>(kv.Value.SpellsPerDay),
                            kv.Value.SpellsKnown != null ? new Dictionary<int, int>(kv.Value.SpellsKnown) : null,
                            new Dictionary<int, int>(kv.Value.DomainBonusSlots),
                            new Dictionary<int, int>(kv.Value.SpecialtyBonusSlots),
                            new Dictionary<int, int>(kv.Value.AbilityBonusSlots),
                            kv.Value.Acquisition,
                            ReplayStudio.SpellbookSpellsAllowed(
                                state.ClassLevels.GetValueOrDefault(kv.Key),
                                AbilityScoreSet.Modifier(state.AbilityScores.GetScore(kv.Value.CastingStat))))),
                    NeedsAdvanceSpellcastingChoice: advanceWarning != null);
            }
        }
    }

    private void AddTemplate()
    {
        if (!string.IsNullOrEmpty(_selectedTemplate) && !_character.TemplateIds.Contains(_selectedTemplate))
        {
            _character.TemplateIds.Add(_selectedTemplate);
            OnCharacterChanged();
        }
    }

    private void RemoveTemplate(string templateId)
    {
        if (_character.TemplateIds.Remove(templateId))
            OnCharacterChanged();
    }

    private void SetAbilityScore(Ability ability, ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var val))
        {
            _character.BaseAbilityScores.SetScore(ability, val);
            OnCharacterChanged();
        }
    }

    private int _addHdCount = 1;
    private string _addHdDriverId = "";

    private void AddTick()
    {
        var driverId = string.IsNullOrEmpty(_addHdDriverId)
            ? _drivers.FirstOrDefault()?.Id ?? "class:fighter"
            : _addHdDriverId;
        var count = Math.Clamp(_addHdCount, 1, 20);
        for (int n = 0; n < count; n++)
        {
            var tickIndex = _character.Ticks.Count;
            _character.Ticks.Add(new Tick { DriverId = driverId });
            _expandedTicks.Add(tickIndex);
        }
        _addHdCount = 1;
        OnCharacterChanged();
    }

    /// <summary>
    /// Toggles the driver-preview grid. Opening it replays the current character one
    /// hit die forward against every legal driver via <see cref="AgentApiService.GetNextStep"/>
    /// (option lists omitted — the grid only shows counts and deltas).
    /// </summary>
    private void ToggleDriverPreviews()
    {
        _showDriverPreviews = !_showDriverPreviews;
        if (!_showDriverPreviews)
            return;

        var response = Api.GetNextStep(
            new NextStepRequest { Character = _character },
            OptionDetail.None);
        _driverPreviews = response.DriverPreviews;
        _previewNextHd = response.NextHd;
    }

    /// <summary>Commits a single hit die of the chosen driver, then closes the preview grid.</summary>
    private void ChooseDriverFromPreview(string driverId)
    {
        _addHdDriverId = driverId;
        _addHdCount = 1;
        AddTick();
        _showDriverPreviews = false;
    }

    private void RemoveTick(int index)
    {
        if (index >= 0 && index < _character.Ticks.Count)
        {
            _character.Ticks.RemoveAt(index);
            // Drafts are keyed by tick index, so removing a tick shifts every key above it.
            // Dropping them all is simpler than re-indexing and loses only unsubmitted text.
            ClearPickerInputs();
            var updated = new HashSet<int>();
            foreach (var i in _expandedTicks)
            {
                if (i < index) updated.Add(i);
                else if (i > index) updated.Add(i - 1);
            }
            _expandedTicks.Clear();
            foreach (var i in updated) _expandedTicks.Add(i);
            OnCharacterChanged();
        }
    }

    private void SetAbilityIncrease(int index, ChangeEventArgs e)
    {
        var val = e.Value?.ToString();
        if (string.IsNullOrEmpty(val))
            _character.Ticks[index].Choices.AbilityIncrease = null;
        else if (Enum.TryParse<Ability>(val, out var ability))
            _character.Ticks[index].Choices.AbilityIncrease = ability;
        OnCharacterChanged();
    }

    private void SetTickDriver(int index, string? driverId)
    {
        if (string.IsNullOrEmpty(driverId)) return;
        _character.Ticks[index].DriverId = driverId;
        if (!IsAbilityIncreaseDue(index))
            _character.Ticks[index].Choices.AbilityIncrease = null;
        OnCharacterChanged();
    }

    private bool IsAbilityIncreaseDue(int index)
    {
        if (index < 0 || index >= _character.Ticks.Count) return false;
        var driver = _drivers.FirstOrDefault(d => d.Id == _character.Ticks[index].DriverId) as HDDriver;
        return driver != null && GameRules.Standard35e().GrantsAbilityIncrease(index + 1, driver.Kind);
    }

    private void AddFeatToTick(int index)
    {
        var input = FeatInput(index);
        if (string.IsNullOrWhiteSpace(input)) return;
        var featDef = _feats.FirstOrDefault(f => f.Id == input);
        var featId = input.Trim();

        if (featDef?.SelectionRequired != null)
        {
            var selection = FeatSelectionInput(index);
            if (string.IsNullOrWhiteSpace(selection)) return;
            var suffix = selection.Trim().ToLowerInvariant().Replace(' ', '_');
            featId = $"{featId}_{suffix}";
        }

        _character.Ticks[index].Choices.FeatIds ??= new List<string>();
        _character.Ticks[index].Choices.FeatIds!.Add(featId);
        _featInputs.Remove(index);
        _featSelectionInputs.Remove(index);
        OnCharacterChanged();
    }

    private void RemoveFeatFromTick(int index, string featId)
    {
        var ids = _character.Ticks[index].Choices.FeatIds;
        if (ids == null) return;
        ids.Remove(featId);
        OnCharacterChanged();
    }

    private void EvaluateCharacter()
    {
        if (_engine == null) return;
        try
        {
            if (_character.CompanionLinks.Count > 0 && CharacterStore.IsConfigured)
            {
                var resolver = new CompanionResolver(_engine, TryLoadCompanion, TryLoadCompanionByName);
                var composite = resolver.Build(_character);
                _state = composite.MasterState;
                _companionBuilds = composite.Companions;
            }
            else
            {
                _state = _engine.Evaluate(_character);
                _companionBuilds = null;
            }
            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _state = null;
            _companionBuilds = null;
        }
    }

    private Character? TryLoadCompanion(string id)
    {
        try { return CharacterStore.Get(id); }
        catch (CharacterStoreException) { return null; }
    }

    /// Repair path for a companion link whose id no longer resolves — see CompanionResolver.
    /// Returns null when the name matches no character or more than one.
    private Character? TryLoadCompanionByName(string name)
    {
        var match = CharacterStore.FindByName(name);
        return match == null ? null : TryLoadCompanion(match.Id);
    }

    private void RefreshAvailableCharacters()
    {
        _availableCharacters = CharacterStore.IsConfigured
            ? CharacterStore.List().OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : new();
    }

    private async Task ViewSheet()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(_character, JsonOptions.Default);
        await JS.InvokeVoidAsync("sessionStorage.setItem", "currentCharacter", json);
        Navigation.NavigateTo("/sheet");
    }

    private void SaveCharacter()
    {
        try
        {
            if (_state != null)
                _character.Sheet = CharacterSheet.FromState(_state);
            if (CharacterStore.IsConfigured)
            {
                var id = string.IsNullOrEmpty(_characterStoreId)
                    ? CharacterStore.DeriveId(_character)
                    : _characterStoreId;
                CharacterStore.Replace(id, _character);
                _characterStoreId = id;
                RefreshMasterLinks();
                SetStatus($"Saved \"{_character.Name}\" to server.", false);
            }
            else
            {
                SetStatus("Character storage not configured on server.", true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}", true);
        }
    }

    private async Task DownloadCharacter()
    {
        try
        {
            if (_state != null)
                _character.Sheet = CharacterSheet.FromState(_state);
            await FileService.DownloadCharacterAsync(_character);
            SetStatus($"Downloaded \"{_character.Name}\".", false);
        }
        catch (Exception ex)
        {
            SetStatus($"Download failed: {ex.Message}", true);
        }
    }

    private async Task OpenFile()
    {
        try
        {
            var loaded = await FileService.OpenCharacterAsync();
            if (loaded != null)
            {
                _character = loaded;
                _characterStoreId = null;
                _error = null;
                _showOpenPicker = false;
                ResetForLoadedCharacter();
                OnCharacterChanged();
                SetStatus($"Opened \"{loaded.Name}\".", false);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Open failed: {ex.Message}", true);
        }
    }

    private void ToggleOpenPicker()
    {
        _showOpenPicker = !_showOpenPicker;
        if (_showOpenPicker)
            RefreshAvailableCharacters();
    }

    private void OpenFromStore(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return;
        try
        {
            _character = CharacterStore.Get(id);
            _characterStoreId = id;
            _error = null;
            _showOpenPicker = false;
            ResetForLoadedCharacter();
            OnCharacterChanged();
            // Keep the URL in sync so a refresh reloads the same character. Same-component
            // navigation only updates the route parameter; state is already set above.
            Navigation.NavigateTo($"/builder/{Uri.EscapeDataString(id)}");
            SetStatus($"Opened \"{_character.Name}\" from server.", false);
        }
        catch (Exception ex)
        {
            SetStatus($"Open failed: {ex.Message}", true);
        }
    }

    private void SetStatus(string message, bool isError)
    {
        _statusMessage = message;
        _statusIsError = isError;
    }

    private int GetRaceAbilityModifier(Ability ability)
    {
        var race = _races.FirstOrDefault(r => r.Id == _character.RaceId);
        return race?.AbilityModifiers?.GetScore(ability) ?? 0;
    }

    /// <summary>
    /// Intelligence at creation — base plus racial and template modifiers, deliberately excluding
    /// level-up increases, because 3.5 prices bonus languages once from starting Int. Mirrors what
    /// <c>ReplayStudio</c> sees when it spends the picks.
    /// </summary>
    private int GetStartingIntelligence() =>
        _character.BaseAbilityScores.INT
        + GetRaceAbilityModifier(Ability.INT)
        + GetTemplateAbilityModifier(Ability.INT);

    private string LanguageName(string id) =>
        _languages.FirstOrDefault(l => l.Id == id)?.Name ?? id;

    private RaceDefinition? SelectedRace() =>
        _races.FirstOrDefault(r => r.Id == _character.RaceId);

    private List<LanguageDefinition> OfferedBonusLanguages() =>
        LanguageCatalog.OfferedBonusLanguages(SelectedRace(), _languages).ToList();

    /// <summary>
    /// Imported languages that the race offers as bonus picks, capped at the starting-Intelligence
    /// allowance. A PCG import lists the finished character's languages without saying which were
    /// creation-time Intelligence purchases, so the builder credits these against the allowance
    /// instead of re-prompting for picks the character has visibly already made.
    /// </summary>
    private HashSet<string> ImportedBonusLanguages()
    {
        if (_character.SourceLanguageIds.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var offered = OfferedBonusLanguages().Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        return _character.SourceLanguageIds
            .Where(offered.Contains)
            .Distinct(StringComparer.Ordinal)
            .Take(LanguageCatalog.Allowance(GetStartingIntelligence()))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// Bonus picks accounted for: the imported ones plus any the user chose on top of them.
    private int LanguagePicksUsed()
    {
        var imported = ImportedBonusLanguages();
        return imported.Count + _character.BonusLanguageIds.Count(id => !imported.Contains(id));
    }

    /// Language slots granted outright (a raven familiar's speech), and what granted them.
    private int GrantedLanguageSlots() => _state?.GrantedLanguageSlots ?? 0;

    private List<string> GrantedLanguageSources() =>
        _state?.GrantedLanguageSources ?? new List<string>();

    /// <summary>
    /// A granted slot is "one language of its master's choice", so it is not restricted to the
    /// race's bonus-language list the way an Intelligence pick is — only secret languages are off
    /// the table, and anything already known would waste the slot.
    /// </summary>
    private List<LanguageDefinition> OfferedGrantedLanguages()
    {
        var automatic = (SelectedRace()?.AutomaticLanguages ?? new List<string>())
            .ToHashSet(StringComparer.Ordinal);
        return _languages
            .Where(language => !language.IsSecret && !automatic.Contains(language.Id))
            .OrderBy(language => language.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ToggleGrantedLanguage(string languageId)
    {
        if (!_character.GrantedLanguageIds.Remove(languageId))
        {
            if (_character.GrantedLanguageIds.Count >= GrantedLanguageSlots())
                return;
            _character.GrantedLanguageIds.Add(languageId);
        }
        OnCharacterChanged();
    }

    private void ToggleBonusLanguage(string languageId)
    {
        if (!_character.BonusLanguageIds.Remove(languageId))
        {
            if (LanguagePicksUsed() >= LanguageCatalog.Allowance(GetStartingIntelligence()))
                return;
            _character.BonusLanguageIds.Add(languageId);
        }
        OnCharacterChanged();
    }

    private int GetTemplateAbilityModifier(Ability ability) =>
        _character.TemplateIds
            .Select(id => _templates.FirstOrDefault(t => t.Id == id))
            .Where(template => template?.AbilityModifiers != null)
            .Sum(template => template!.AbilityModifiers!.GetScore(ability));

    private static string FormatSigned(int value) => value >= 0 ? $"+{value}" : value.ToString();

    /// <summary>
    /// "Race · Class levels · Alignment" line shown under the character name in the
    /// builder header. Class ids resolve to driver display names via <c>_drivers</c>.
    /// </summary>
    private string HeaderSubtitle()
    {
        if (_state == null)
            return "";

        var parts = new List<string>();

        var race = _races.FirstOrDefault(r => r.Id == _character.RaceId)?.Name;
        if (!string.IsNullOrEmpty(race))
            parts.Add(race);

        var classes = string.Join(" / ", _state.ClassLevels
            .Where(kv => kv.Value > 0)
            .Select(kv => $"{DriverName(kv.Key)} {kv.Value}"));
        if (!string.IsNullOrEmpty(classes))
            parts.Add(classes);

        parts.Add(FormatAlignment(_character.Alignment));

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Display name for a driver (class or racial HD) id, falling back to the id stripped of its
    /// <c>class:</c> prefix when the driver is not loaded.
    /// </summary>
    private string DriverName(string driverId) =>
        _drivers.FirstOrDefault(d => d.Id == driverId)?.Name
        ?? driverId.Replace("class:", "");

    private string FormatFeatLabel(string featId, FeatDefinition? featDef)
    {
        if (featDef != null)
            return featDef.Name;

        foreach (var f in _feats)
        {
            if (f.SelectionRequired != null && featId.StartsWith(f.Id + "_", StringComparison.Ordinal))
            {
                var suffix = featId[(f.Id.Length + 1)..];
                var selection = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(suffix.Replace('_', ' '));
                return $"{f.Name} ({selection})";
            }
        }
        return featId;
    }

    // How many spells a full-list caster has available at its current maximum spell level. Shown
    // instead of a picker, so the panel still says something useful about the class's reach.
    private int AvailableSpellCount(string spellListId, int maxSpellLevel) =>
        _spells.Count(s => Content.Registry.TryGetSpellLevelForList(s, spellListId, out var lvl)
            && lvl <= maxSpellLevel);

    private static string PreparedSpellSlotLabel(PreparedSpellSlotKind slotKind) => slotKind switch
    {
        PreparedSpellSlotKind.Normal => "Normal",
        PreparedSpellSlotKind.Domain => "Domain",
        PreparedSpellSlotKind.Specialty => "Specialty",
        _ => slotKind.ToString()
    };

    private static int PreparedSpellSlotCount(
        TickSpellcastingInfo spellcasting,
        int spellLevel,
        PreparedSpellSlotKind slotKind) => slotKind switch
        {
            PreparedSpellSlotKind.Normal => spellcasting.SpellsPerDay.GetValueOrDefault(spellLevel)
                + spellcasting.AbilityBonusSlots.GetValueOrDefault(spellLevel),
            PreparedSpellSlotKind.Domain => spellcasting.DomainBonusSlots.GetValueOrDefault(spellLevel),
            PreparedSpellSlotKind.Specialty => spellcasting.SpecialtyBonusSlots.GetValueOrDefault(spellLevel),
            _ => 0
        };

    private List<PreparedSpellSelection> PreparedSelections(
        string classId,
        int spellLevel,
        PreparedSpellSlotKind slotKind) =>
        _character.PreparedSpellSelections
            .Where(selection => selection.ClassId == classId
                && selection.SpellLevel == spellLevel
                && selection.SlotKind == slotKind)
            .ToList();

    private IEnumerable<SpellDefinition> PreparedSpellOptions(
        string classId,
        TickSpellcastingInfo tickSpellcasting,
        int spellLevel,
        PreparedSpellSlotKind slotKind)
    {
        if (_state == null || !_state.Spellcasting.TryGetValue(classId, out var spellcasting))
            return Enumerable.Empty<SpellDefinition>();

        IEnumerable<SpellDefinition> options;
        if (slotKind == PreparedSpellSlotKind.Domain)
        {
            var domainSpellIds = _state.DomainOwners
                .Where(entry => entry.Value == classId)
                .SelectMany(entry => _domains.FirstOrDefault(domain => domain.Id == entry.Key)?.BonusSpells
                    .Where(spell => spell.Key == spellLevel)
                    .Select(spell => spell.Value)
                    ?? Enumerable.Empty<string>())
                .ToHashSet(StringComparer.Ordinal);
            options = _spells.Where(spell => domainSpellIds.Contains(spell.Id));
        }
        else if (slotKind == PreparedSpellSlotKind.Specialty)
        {
            var specialty = WizardSchools.Specialty(_state);
            options = _spells.Where(spell =>
                Content.Registry.TryGetSpellLevelForList(spell, classId, out var level)
                && level == spellLevel
                && string.Equals(spell.School, specialty, StringComparison.OrdinalIgnoreCase));
        }
        else if (spellcasting.Acquisition == SpellAcquisition.Spellbook)
        {
            options = spellcasting.SelectedSpells
                .Where(selection => selection.ClassId == classId && selection.SpellLevel == spellLevel)
                .Select(selection => _spells.FirstOrDefault(spell => spell.Id == selection.SpellId))
                .Where(spell => spell != null)
                .Cast<SpellDefinition>();
        }
        else
        {
            options = _spells.Where(spell =>
                Content.Registry.TryGetSpellLevelForList(spell, classId, out var level)
                && level == spellLevel);
        }

        return options
            .Where(spell => !WizardSchools.IsProhibited(_state, spell.School))
            .OrderBy(spell => spell.Name, StringComparer.Ordinal)
            .DistinctBy(spell => spell.Id);
    }

    private void AddPreparedSpellSelection(
        string classId,
        int spellLevel,
        PreparedSpellSlotKind slotKind,
        string spellId)
    {
        if (string.IsNullOrWhiteSpace(spellId))
            return;

        _character.PreparedSpellSelections.Add(new PreparedSpellSelection
        {
            ClassId = classId,
            SpellLevel = spellLevel,
            SpellId = spellId,
            SlotKind = slotKind,
        });
        OnCharacterChanged();
    }

    private void RemovePreparedSpellSelection(PreparedSpellSelection selection)
    {
        _character.PreparedSpellSelections.Remove(selection);
        OnCharacterChanged();
    }

    // --- Specialist wizard schools ---

    private List<ClassFeatureOption> SchoolOptions(string featureType) =>
        _classFeatures.FirstOrDefault(f => f.Id == featureType)?.Options ?? new List<ClassFeatureOption>();

    private static string? SelectedSchool(Tick tick, string featureType, int slot)
    {
        var picks = tick.Choices.ClassFeatureChoices?.GetValueOrDefault(featureType);
        return picks != null && slot < picks.Count ? picks[slot] : null;
    }

    /// <summary>
    /// The schools a given prohibited-school slot may offer: never the wizard's own specialty, and
    /// never one already given up in another slot. (Divination is absent from the option pool in
    /// content, since it can never be given up at all.)
    /// </summary>
    private List<ClassFeatureOption> ProhibitedSchoolOptions(Tick tick, string? specialtyPick, int slot)
    {
        var picks = tick.Choices.ClassFeatureChoices?.GetValueOrDefault(WizardSchools.ProhibitedFeature);
        var takenElsewhere = picks is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : picks.Where((_, i) => i != slot).Where(p => !string.IsNullOrEmpty(p))
                   .ToHashSet(StringComparer.Ordinal);

        return SchoolOptions(WizardSchools.ProhibitedFeature)
            .Where(o => o.Id != specialtyPick && !takenElsewhere.Contains(o.Id))
            .ToList();
    }

    /// <summary>How many of the required prohibited slots actually hold a school.</summary>
    private static int ChosenProhibitedCount(Tick tick, int required)
    {
        var picks = tick.Choices.ClassFeatureChoices?.GetValueOrDefault(WizardSchools.ProhibitedFeature);
        if (picks is null) return 0;
        return picks.Take(required).Count(p => !string.IsNullOrEmpty(p));
    }

    /// <summary>
    /// Writes one school slot. Slots are positional so the two prohibited-school dropdowns stay
    /// independent; clearing one leaves the other in place. Empty entries are dropped, so an
    /// unmade choice never reaches the engine as a selection.
    /// </summary>
    private void SetWizardSchool(int tickIndex, string featureType, int slot, string? optionId)
    {
        var tick = _character.Ticks[tickIndex];
        tick.Choices.ClassFeatureChoices ??= new Dictionary<string, List<string>>();
        var picks = tick.Choices.ClassFeatureChoices.TryGetValue(featureType, out var existing)
            ? new List<string>(existing)
            : new List<string>();

        while (picks.Count <= slot)
            picks.Add(string.Empty);
        picks[slot] = optionId ?? string.Empty;

        var cleaned = picks.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (cleaned.Count > 0)
            tick.Choices.ClassFeatureChoices[featureType] = cleaned;
        else
            tick.Choices.ClassFeatureChoices.Remove(featureType);

        // Changing the specialty changes how many schools are owed, and which are legal. Switching
        // to divination (2 → 1) or to universalist (→ 0) would otherwise strand picks that the
        // picker no longer shows, leaving an illegal character that looked correct on screen.
        if (featureType == WizardSchools.SpecializationFeature)
            TrimProhibitedSchools(tick, optionId);

        OnCharacterChanged();
    }

    private static void TrimProhibitedSchools(Tick tick, string? specialtyPick)
    {
        var choices = tick.Choices.ClassFeatureChoices;
        if (choices is null || !choices.TryGetValue(WizardSchools.ProhibitedFeature, out var picks))
            return;

        var specialtyName = string.IsNullOrEmpty(specialtyPick)
            ? null
            : WizardSchools.ToSchoolName(specialtyPick);

        var kept = picks
            .Where(p => !string.IsNullOrEmpty(p) && p != specialtyPick)
            .Take(WizardSchools.RequiredProhibitedCount(specialtyName))
            .ToList();

        if (kept.Count > 0)
            choices[WizardSchools.ProhibitedFeature] = kept;
        else
            choices.Remove(WizardSchools.ProhibitedFeature);
    }

    private sealed record TickWizardSchoolPicks(bool PendingSpecialization, int PendingProhibited);
    private sealed record TickClassFeaturePick(ClassFeatureDefinition Feature, int SlotCount);

    private static string? SelectedClassFeatureOption(Tick tick, string featureType, int slot)
    {
        var picks = tick.Choices.ClassFeatureChoices?.GetValueOrDefault(featureType);
        return picks != null && slot < picks.Count ? picks[slot] : null;
    }

    private List<ClassFeatureOption> ClassFeatureOptionsForTick(int tickIndex, string featureType, int slot)
    {
        var selectedElsewhere = _character.Ticks
            .SelectMany((tick, index) => tick.Choices.ClassFeatureChoices?.GetValueOrDefault(featureType)
                ?.Where((_, pickIndex) => index != tickIndex || pickIndex != slot)
                ?? Enumerable.Empty<string>())
            .ToHashSet(StringComparer.Ordinal);

        return _classFeatures.FirstOrDefault(f => f.Id == featureType)?.Options
            .Where(option => !selectedElsewhere.Contains(option.Id))
            .ToList() ?? new List<ClassFeatureOption>();
    }

    private void SetClassFeatureChoice(int tickIndex, string featureType, int slot, string? optionId)
    {
        var tick = _character.Ticks[tickIndex];
        tick.Choices.ClassFeatureChoices ??= new Dictionary<string, List<string>>();
        var picks = tick.Choices.ClassFeatureChoices.TryGetValue(featureType, out var existing)
            ? new List<string>(existing)
            : new List<string>();

        while (picks.Count <= slot)
            picks.Add(string.Empty);
        picks[slot] = optionId ?? string.Empty;

        var cleaned = picks.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (cleaned.Count > 0)
            tick.Choices.ClassFeatureChoices[featureType] = cleaned;
        else
            tick.Choices.ClassFeatureChoices.Remove(featureType);

        OnCharacterChanged();
    }

    // Purely a display toggle — it widens the picker's list and does not touch the character,
    // so it deliberately does not call OnCharacterChanged.
    private void OnShowNonPcRacesChanged(ChangeEventArgs e) =>
        _showNonPcRaces = e.Value is true;

    private void OnAlignmentChanged(ChangeEventArgs e)
    {
        if (Enum.TryParse<Alignment>(e.Value?.ToString(), out var a))
        {
            _character.Alignment = a;
            OnCharacterChanged();
        }
    }

    private static string FormatAlignment(Alignment a) => a switch
    {
        Alignment.LG => "Lawful Good",
        Alignment.LN => "Lawful Neutral",
        Alignment.LE => "Lawful Evil",
        Alignment.NG => "Neutral Good",
        Alignment.N  => "True Neutral",
        Alignment.NE => "Neutral Evil",
        Alignment.CG => "Chaotic Good",
        Alignment.CN => "Chaotic Neutral",
        Alignment.CE => "Chaotic Evil",
        _ => a.ToString()
    };

    private IReadOnlyList<TickSpellSummary> GetTickSpellSummaries(int index) =>
        _tickSpellSummaries.TryGetValue(index, out var summaries)
            ? summaries
            : Array.Empty<TickSpellSummary>();

    private static string FormatSpellSummary(TickSpellSummary summary)
    {
        var name = summary.SpellListId.Replace("class:", "").Replace("domain:", "");
        var listType = summary.IsDomainList ? "domain" : "class";
        return $"{name} {listType} L{summary.MaxSpellLevel} ({summary.AvailableSpellCount} spells)";
    }

    private static string FormatSpellSelection(SpellSelection selection)
    {
        var listId = selection.ClassId.Replace("class:", "").Replace("domain:", "");
        return $"{listId} {selection.SpellLevel}th: {selection.SpellId}";
    }

    private static string SpellTooltip(SpellDefinition spell) => Tooltips.ForSpell(spell);

    private string SpellTooltip(string spellId) =>
        Content.Registry.TryGetSpell(spellId, out var spell) && spell != null
            ? Tooltips.ForSpell(spell)
            : spellId;

    private string SlaTooltip(SLA sla) => Tooltips.ForSla(sla, Content.Registry);

    private List<FeatDefinition> GetTickFeats(int index) =>
        _tickAvailableFeats.TryGetValue(index, out var feats) ? feats : _feats;

    private TickSkillInfo? GetTickSkillInfo(int index) =>
        _tickSkillInfos.TryGetValue(index, out var info) ? info : null;

    private void AddSkillAllocation(int tickIndex, string skillId, bool isClassSkill)
    {
        var tick = _character.Ticks[tickIndex];
        tick.Choices.SkillAllocations ??= new List<SkillAllocation>();

        var halfRanks = isClassSkill ? 2 : 1;
        var existing = tick.Choices.SkillAllocations.FirstOrDefault(a => a.SkillId == skillId);
        if (existing != null)
            existing.HalfRanks += halfRanks;
        else
            tick.Choices.SkillAllocations.Add(new SkillAllocation { SkillId = skillId, HalfRanks = halfRanks });

        OnCharacterChanged();
    }

    private void RemoveSkillAllocation(int tickIndex, string skillId, bool isClassSkill)
    {
        var tick = _character.Ticks[tickIndex];
        if (tick.Choices.SkillAllocations == null) return;

        var existing = tick.Choices.SkillAllocations.FirstOrDefault(a => a.SkillId == skillId);
        if (existing == null) return;

        var halfRanks = isClassSkill ? 2 : 1;
        existing.HalfRanks -= halfRanks;
        if (existing.HalfRanks <= 0)
            tick.Choices.SkillAllocations.Remove(existing);

        OnCharacterChanged();
    }

    private void ToggleAllHdTicks()
    {
        if (_expandedTicks.Count == _character.Ticks.Count)
            _expandedTicks.Clear();
        else
            ExpandAllHdTicks();
    }

    private void ExpandAllHdTicks()
    {
        _expandedTicks.Clear();
        for (int i = 0; i < _character.Ticks.Count; i++)
            _expandedTicks.Add(i);
    }

    /// <summary>
    /// Per-load UI reset. Distinct from <see cref="ExpandAllHdTicks"/>, which is also the
    /// "Expand All" button and must not discard a draft the user is part-way through typing.
    /// </summary>
    private void ResetForLoadedCharacter()
    {
        ClearPickerInputs();
        RefreshMasterLinks();

        // Only auto-expand short characters. The spell panel rescans the whole spell catalogue
        // per spell level per tick, and it re-renders on every SearchSelect keystroke, so
        // opening a 20-HD caster fully expanded makes the Spells tab crawl. Longer characters
        // start collapsed and lean on the per-facet collapsed summary line instead.
        if (_character.Ticks.Count <= 5)
            ExpandAllHdTicks();
        else
            _expandedTicks.Clear();
    }

    // ---------------------------------------------------------------- tabs

    private void SetActiveTab(CharacterTab tab) => _activeTab = tab;

    /// <summary>
    /// Which tabs to offer. Spells and Companions are hidden for characters that have no
    /// casting or no companion slots rather than shown empty.
    /// </summary>
    private List<CharacterTabItem> VisibleTabs()
    {
        var tabs = new List<CharacterTabItem>
        {
            new(CharacterTab.Summary, "Summary & Progression"),
            new(CharacterTab.Feats, "Feats & Special Abilities", PendingFeatBadge(), "bg-warning text-dark"),
            new(CharacterTab.Skills, "Skills", PendingSkillBadge(), "bg-warning text-dark")
        };

        if (HasSpellcasting())
            tabs.Add(new CharacterTabItem(CharacterTab.Spells, "Spells"));

        tabs.Add(new CharacterTabItem(CharacterTab.Equipment, "Equipment"));

        if (HasCompanions())
            tabs.Add(new CharacterTabItem(CharacterTab.Companions, "Companions"));

        return tabs;
    }

    private bool HasSpellcasting() =>
        _tickCasterInfos.Values.Any(ci => ci.Spellcasting.Count > 0 || ci.PendingDomainSelections > 0)
        || _state?.Spellcasting.Count > 0;

    // Unions four sources deliberately: _tickNewCompanionSlots is populated before the first
    // Evaluate, so a freshly-added druid level must not lose its species picker while _state
    // is still stale.
    private bool HasCompanions() =>
        _tickNewCompanionSlots.Count > 0
        || _state?.CompanionSlots.Count > 0
        || _character.CompanionLinks.Count > 0
        || _masterLinks.Length > 0;

    private string? PendingFeatBadge()
    {
        var pending = (_state?.PendingFeatSlots ?? 0) + (_state?.PendingBonusFeatSlots ?? 0);
        return pending > 0 ? pending.ToString() : null;
    }

    private string? PendingSkillBadge() =>
        _state?.UnspentSkillPoints > 0 ? _state.UnspentSkillPoints.ToString() : null;

    /// <summary>
    /// Drops back to Summary when the active tab is no longer offered — evaluating a character
    /// can remove casting or companions out from under whichever tab the user is standing on.
    /// </summary>
    private void NormalizeActiveTab()
    {
        if (!VisibleTabs().Any(t => t.Id == _activeTab))
            _activeTab = CharacterTab.Summary;
    }

    // ------------------------------------------------- HD timeline facets

    /// <summary>Every tab except Equipment renders the tick list, each showing only its facet.</summary>
    private static bool IsTimelineTab(CharacterTab tab) => tab != CharacterTab.Equipment;

    private bool _showAllTicks;

    private void ToggleShowAllTicks() => _showAllTicks = !_showAllTicks;

    /// <summary>
    /// Whether a tick has anything to show on the given facet. The conditions are the same ones
    /// that already gated each panel inside the expanded tick body, hoisted so the list can skip
    /// ticks outright instead of rendering an empty shell for them.
    /// </summary>
    private bool TickShowsFacet(int index, CharacterTab tab)
    {
        if (index < 0 || index >= _character.Ticks.Count) return false;
        var tick = _character.Ticks[index];

        return tab switch
        {
            // Progression is the spine: every tick has a driver, and may need removing.
            CharacterTab.Summary => true,

            CharacterTab.Feats => tick.Choices.FeatIds?.Any() == true || IsFeatDue(index),

            CharacterTab.Skills => _skills.Any()
                && _tickSkillInfos.TryGetValue(index, out var skillInfo)
                && (skillInfo.UnspentPoints > 0 || tick.Choices.SkillAllocations?.Any() == true),

            CharacterTab.Spells => _tickCasterInfos.TryGetValue(index, out var caster)
                && (caster.Spellcasting.Count > 0 || caster.PendingDomainSelections > 0
                    || tick.Choices.SpellSelections?.Any() == true),

            CharacterTab.Companions => _tickNewCompanionSlots.ContainsKey(index),

            _ => false
        };
    }

    /// <summary>Does this HD hand out a feat slot — from the standard schedule or a class bonus?</summary>
    private bool IsFeatDue(int index)
    {
        if (_tickFeatGrants.GetValueOrDefault(index) > 0) return true;
        var hd = index + 1;
        var rules = GameRules.Standard35e();
        return rules.GrantsStandardFeat(hd) || rules.GrantsEpicFeat(hd);
    }

    private int VisibleTickCount(CharacterTab tab)
    {
        var count = 0;
        for (var i = 0; i < _character.Ticks.Count; i++)
            if (TickShowsFacet(i, tab)) count++;
        return count;
    }

    private static string TimelineHeading(CharacterTab tab) => tab switch
    {
        CharacterTab.Feats => "Feats by HD",
        CharacterTab.Skills => "Skills by HD",
        CharacterTab.Spells => "Spells by HD",
        CharacterTab.Companions => "Companion slots by HD",
        _ => "HD Timeline"
    };

    private static string EmptyFacetMessage(CharacterTab tab) => tab switch
    {
        CharacterTab.Feats => "No HD grants a feat yet.",
        CharacterTab.Skills => "No HD has skill points to spend.",
        CharacterTab.Spells => "No HD grants spellcasting.",
        CharacterTab.Companions => "No HD grants a companion slot.",
        _ => "No HD yet — use \"+ Add HD\" to start."
    };

    /// <summary>
    /// The one-line summary shown for a collapsed tick, phrased for the facet being viewed so
    /// that collapsing stays useful rather than hiding the only thing the tab is about.
    /// </summary>
    private string CollapsedSummary(int index, CharacterTab tab)
    {
        var tick = _character.Ticks[index];
        var driverName = _drivers.FirstOrDefault(d => d.Id == tick.DriverId)?.Name ?? tick.DriverId;
        var parts = new List<string> { driverName };

        switch (tab)
        {
            case CharacterTab.Feats:
                var feats = tick.Choices.FeatIds ?? new List<string>();
                parts.AddRange(feats.Select(id =>
                    FormatFeatLabel(id, _feats.FirstOrDefault(f => f.Id == id))));
                var owed = _tickFeatGrants.GetValueOrDefault(index) - feats.Count;
                if (owed > 0) parts.Add($"{owed} to choose");
                break;

            case CharacterTab.Skills:
                var halfRanks = tick.Choices.SkillAllocations?.Sum(a => a.HalfRanks) ?? 0;
                if (halfRanks > 0) parts.Add($"{halfRanks / 2.0} ranks allocated");
                if (_tickSkillInfos.TryGetValue(index, out var si) && si.UnspentPoints > 0)
                    parts.Add($"{si.UnspentPoints} pts left");
                break;

            case CharacterTab.Spells:
                var selected = tick.Choices.SpellSelections?.Count ?? 0;
                if (selected > 0) parts.Add($"{selected} spell(s) selected");
                if (_tickCasterInfos.TryGetValue(index, out var ci) && ci.PendingDomainSelections > 0)
                    parts.Add($"{ci.PendingDomainSelections} domain(s) to choose");
                break;

            case CharacterTab.Companions:
                if (_tickNewCompanionSlots.TryGetValue(index, out var slots))
                    parts.AddRange(slots.Select(s => s.LinkType));
                break;

            default:
                if (tick.Choices.AbilityIncrease != null)
                    parts.Add($"+1 {tick.Choices.AbilityIncrease}");
                if (tick.Choices.FeatIds?.Any() == true)
                    parts.Add($"{tick.Choices.FeatIds.Count} feat(s)");
                break;
        }

        return string.Join(" | ", parts);
    }

    private void RefreshMasterLinks()
    {
        _masterLinks = CharacterStore.IsConfigured && !string.IsNullOrEmpty(_characterStoreId)
            ? CharacterStore.FindMasters(_characterStoreId).ToArray()
            : Array.Empty<CharacterFileInfo>();
    }

    private void ToggleHdTick(int index)
    {
        if (!_expandedTicks.Add(index))
            _expandedTicks.Remove(index);
    }


    private int GetTickSkillAllocation(int tickIndex, string skillId)
    {
        var allocs = _character.Ticks[tickIndex].Choices.SkillAllocations;
        return allocs?.FirstOrDefault(a => a.SkillId == skillId)?.HalfRanks ?? 0;
    }

    private TickCasterInfo? GetTickCasterInfo(int index) =>
        _tickCasterInfos.TryGetValue(index, out var info) ? info : null;

    private void AddDomainToTick(int tickIndex, string domainId)
    {
        if (string.IsNullOrEmpty(domainId)) return;
        var tick = _character.Ticks[tickIndex];
        tick.Choices.ClassFeatureChoices ??= new Dictionary<string, List<string>>();
        if (!tick.Choices.ClassFeatureChoices.ContainsKey("domains"))
            tick.Choices.ClassFeatureChoices["domains"] = new List<string>();
        var domains = tick.Choices.ClassFeatureChoices["domains"];
        if (!domains.Contains(domainId))
        {
            domains.Add(domainId);
            _domainInputs.Remove(tickIndex);
            OnCharacterChanged();
        }
    }

    private void RemoveDomainFromTick(int tickIndex, string domainId, string choiceKey = "domains")
    {
        var tick = _character.Ticks[tickIndex];
        var domains = tick.Choices.ClassFeatureChoices?.GetValueOrDefault(choiceKey);
        if (domains == null) return;
        domains.Remove(domainId);
        OnCharacterChanged();
    }

    private void SetWarFavoredWeapon(int tickIndex, string? weaponId)
    {
        var choices = _character.Ticks[tickIndex].Choices;
        choices.ClassFeatureChoices ??= new Dictionary<string, List<string>>();
        choices.ClassFeatureChoices[GrantWarDomainWeaponFeats.ChoiceKey] =
            string.IsNullOrWhiteSpace(weaponId) ? new List<string>() : new List<string> { weaponId };
        OnCharacterChanged();
    }


    private void AddSpellSelection(int tickIndex, string classId, int spellLevel, string spellId)
    {
        if (string.IsNullOrEmpty(spellId)) return;
        var tick = _character.Ticks[tickIndex];
        tick.Choices.SpellSelections ??= new List<SpellSelection>();
        tick.Choices.SpellSelections.Add(new SpellSelection
        {
            ClassId = classId,
            SpellLevel = spellLevel,
            SpellId = spellId
        });
        OnCharacterChanged();
    }

    private void LearnAllWizardSpells(int tickIndex, string classId)
    {
        if (tickIndex < 0 || tickIndex >= _character.Ticks.Count
            || !_tickCasterInfos.TryGetValue(tickIndex, out var casterInfo)
            || !casterInfo.Spellcasting.TryGetValue(classId, out var spellcasting)
            || spellcasting.Acquisition != SpellAcquisition.Spellbook)
            return;

        var selected = _character.Ticks
            .SelectMany(tick => tick.Choices.SpellSelections ?? Enumerable.Empty<SpellSelection>())
            .Where(selection => selection.ClassId == classId && selection.SpellLevel > 0)
            .Select(selection => selection.SpellId)
            .ToHashSet(StringComparer.Ordinal);
        var remaining = Math.Max(0, spellcasting.SpellbookLimit - selected.Count);
        if (remaining == 0)
            return;

        var prohibited = _tickProhibitedSchools.GetValueOrDefault(tickIndex)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var additions = _spells
            .Select(spell =>
            {
                Content.Registry.TryGetSpellLevelForList(spell, classId, out var level);
                return (Spell: spell, Level: level);
            })
            .Where(entry => entry.Level >= 1
                && entry.Level <= spellcasting.MaxSpellLevel
                && !prohibited.Contains(entry.Spell.School)
                && !selected.Contains(entry.Spell.Id))
            .OrderBy(entry => entry.Level)
            .ThenBy(entry => entry.Spell.Name)
            .Take(remaining)
            .ToList();
        if (additions.Count == 0)
            return;

        var choices = _character.Ticks[tickIndex].Choices;
        choices.SpellSelections ??= new List<SpellSelection>();
        choices.SpellSelections.AddRange(additions.Select(entry => new SpellSelection
        {
            ClassId = classId,
            SpellLevel = entry.Level,
            SpellId = entry.Spell.Id,
        }));
        OnCharacterChanged();
    }

    private void RemoveSpellSelection(int tickIndex, SpellSelection selection)
    {
        var tick = _character.Ticks[tickIndex];
        tick.Choices.SpellSelections?.Remove(selection);
        OnCharacterChanged();
    }

    private void SetAdvanceSpellcastingChoice(int tickIndex, string classId)
    {
        var tick = _character.Ticks[tickIndex];
        tick.Choices.ClassFeatureChoices ??= new Dictionary<string, List<string>>();
        tick.Choices.ClassFeatureChoices["advance_spellcasting"] = new List<string> { classId };
        OnCharacterChanged();
    }

    private void SetCompanionSpeciesOnTick(int tickIndex, string classFeatureType, string raceId)
    {
        var tick = _character.Ticks[tickIndex];
        tick.Choices.ClassFeatureChoices ??= new Dictionary<string, List<string>>();
        tick.Choices.ClassFeatureChoices[classFeatureType] = new List<string> { raceId };
        OnCharacterChanged();
    }

    private static readonly Dictionary<string, string> CompanionDefaultTemplateByLinkType = new()
    {
        ["animal_companion"] = "template:animal_companion_standard",
        ["familiar"] = "template:familiar_standard",
        ["improved_familiar"] = "template:familiar_standard",
        ["special_mount"] = "template:special_mount_standard",
    };

    private static readonly Dictionary<string, string> CompanionRacialHdByLinkType = new()
    {
        ["shadow_companion"] = "racial_hd:undead",
    };

    private void CreateCompanionFromSlot(CompanionSlotState slot)
    {
        if (!CharacterStore.IsConfigured)
        {
            SetStatus("Character storage not configured on server.", true);
            return;
        }
        if (string.IsNullOrEmpty(_characterStoreId))
        {
            SetStatus("Save the master character first so the companion can link back.", true);
            return;
        }

        var species = slot.SelectedSpecies;
        var raceDef = string.IsNullOrEmpty(species) ? null : _races.FirstOrDefault(r => r.Id == species);
        var displayName = raceDef?.Name ?? species ?? slot.LinkType.Replace('_', ' ');

        var companion = new Character
        {
            Name = $"{_character.Name}'s {displayName}",
            Alignment = _character.Alignment,
            RaceId = species ?? string.Empty,
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 }
        };

        if (CompanionDefaultTemplateByLinkType.TryGetValue(slot.LinkType, out var templateId)
            && _templates.Any(t => t.Id == templateId))
        {
            companion.TemplateIds.Add(templateId);
        }

        if (CompanionRacialHdByLinkType.TryGetValue(slot.LinkType, out var racialHdDriverId))
        {
            for (var hd = 0; hd < Math.Max(0, slot.EffectiveLevel); hd++)
                companion.Ticks.Add(new Tick { DriverId = racialHdDriverId });
        }

        var baseId = CharacterStore.DeriveId(companion);
        var id = baseId;
        var suffix = 1;
        while (CharacterStore.Exists(id))
        {
            suffix++;
            id = $"{baseId}_{suffix}";
        }

        CharacterStore.Replace(id, companion);

        _character.CompanionLinks.Add(new CompanionLink
        {
            LinkType = slot.LinkType,
            CompanionId = id,
            SelectedSpecies = species,
            EffectiveLevelFormula = slot.EffectiveLevelFormula
        });
        CharacterStore.Replace(_characterStoreId!, _character);
        RefreshAvailableCharacters();

        Navigation.NavigateTo($"/builder/{id}");
    }

    private void LinkExistingCompanion(CompanionSlotState slot)
    {
        if (!CharacterStore.IsConfigured)
        {
            SetStatus("Character storage not configured on server.", true);
            return;
        }
        if (string.IsNullOrEmpty(_characterStoreId))
        {
            SetStatus("Save the master character first so it can own the link.", true);
            return;
        }
        if (!_linkCandidateByLinkType.TryGetValue(slot.LinkType, out var candidateId)
            || string.IsNullOrEmpty(candidateId))
        {
            SetStatus("Pick a character to link first.", true);
            return;
        }
        if (candidateId == _characterStoreId)
        {
            SetStatus("A character cannot be linked to itself.", true);
            return;
        }

        Character candidate;
        try { candidate = CharacterStore.Get(candidateId); }
        catch (CharacterStoreException ex) { SetStatus(ex.Message, true); return; }

        _character.CompanionLinks.Add(new CompanionLink
        {
            LinkType = slot.LinkType,
            CompanionId = candidateId,
            SelectedSpecies = string.IsNullOrEmpty(candidate.RaceId) ? null : candidate.RaceId,
            EffectiveLevelFormula = slot.EffectiveLevelFormula
        });
        CharacterStore.Replace(_characterStoreId, _character);
        _linkCandidateByLinkType.Remove(slot.LinkType);
        SetStatus($"Linked \"{candidate.Name}\" as {slot.LinkType}.", false);
        OnCharacterChanged();
    }

    private int FollowerCapacity(int level) => level switch
    {
        1 => _state?.Followers.Level1 ?? 0,
        2 => _state?.Followers.Level2 ?? 0,
        3 => _state?.Followers.Level3 ?? 0,
        4 => _state?.Followers.Level4 ?? 0,
        5 => _state?.Followers.Level5 ?? 0,
        6 => _state?.Followers.Level6 ?? 0,
        _ => 0,
    };

    private void CreateLeadershipFollower(int level)
    {
        if (!CharacterStore.IsConfigured)
        {
            SetStatus("Character storage not configured on server.", true);
            return;
        }
        if (string.IsNullOrEmpty(_characterStoreId))
        {
            SetStatus("Save the leader first so the follower can link back.", true);
            return;
        }

        var follower = new Character
        {
            Name = $"{_character.Name}'s level {level} follower",
            Alignment = _character.Alignment,
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
        };
        var baseId = CharacterStore.DeriveId(follower);
        var id = baseId;
        for (var suffix = 2; CharacterStore.Exists(id); suffix++)
            id = $"{baseId}_{suffix}";

        CharacterStore.Replace(id, follower);
        _character.CompanionLinks.Add(new CompanionLink
        {
            LinkType = "leadership_follower",
            CompanionId = id,
            FollowerLevel = level,
            EffectiveLevelFormula = new Formula(level.ToString()),
        });
        CharacterStore.Replace(_characterStoreId, _character);
        RefreshAvailableCharacters();
        Navigation.NavigateTo($"/builder/{id}");
    }

    private void LinkExistingLeadershipFollower()
    {
        if (!CharacterStore.IsConfigured || string.IsNullOrEmpty(_characterStoreId))
        {
            SetStatus("Save the leader first so the follower can link back.", true);
            return;
        }
        if (string.IsNullOrEmpty(_leadershipFollowerCandidate)
            || _leadershipFollowerCandidate == _characterStoreId)
        {
            SetStatus("Pick another character to link first.", true);
            return;
        }

        Character candidate;
        try { candidate = CharacterStore.Get(_leadershipFollowerCandidate); }
        catch (CharacterStoreException ex) { SetStatus(ex.Message, true); return; }

        _character.CompanionLinks.Add(new CompanionLink
        {
            LinkType = "leadership_follower",
            CompanionId = _leadershipFollowerCandidate,
            SelectedSpecies = string.IsNullOrEmpty(candidate.RaceId) ? null : candidate.RaceId,
            FollowerLevel = _leadershipFollowerLevel,
            EffectiveLevelFormula = new Formula(_leadershipFollowerLevel.ToString()),
        });
        CharacterStore.Replace(_characterStoreId, _character);
        _leadershipFollowerCandidate = string.Empty;
        SetStatus($"Linked \"{candidate.Name}\" as a level {_leadershipFollowerLevel} Leadership follower.", false);
        OnCharacterChanged();
    }

    private void UnlinkCompanion(CompanionLink link)
    {
        _character.CompanionLinks.Remove(link);
        if (!string.IsNullOrEmpty(_characterStoreId) && CharacterStore.IsConfigured)
            CharacterStore.Replace(_characterStoreId, _character);
        OnCharacterChanged();
    }

    private void AddPermanentEvent()
    {
        _character.PermanentEvents.Add(new PermanentEvent
        {
            BeforeTick = _character.Ticks.Count
        });
    }

    private void RemovePermanentEvent(int index)
    {
        _character.PermanentEvents.RemoveAt(index);
        OnCharacterChanged();
    }

    private void AddPresetToEvent(int evtIndex, string preset)
    {
        var evt = _character.PermanentEvents[evtIndex];

        if (preset.StartsWith("tome_"))
        {
            var parts = preset.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out var bonus) && Enum.TryParse<Ability>(parts[2], out var ab))
            {
                AddAbilityModToEvent(evtIndex, ab, bonus);
            }
        }
        else if (preset == "wish_inherent")
        {
            _pendingWishEvent = evtIndex;
        }
        else if (preset.StartsWith("nat_armor_"))
        {
            if (int.TryParse(preset.Replace("nat_armor_", ""), out var val))
            {
                evt.Permabuffs.Add(new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = val });
                OnCharacterChanged();
            }
        }
        else if (preset.StartsWith("sr_"))
        {
            if (int.TryParse(preset.Replace("sr_", ""), out var val))
            {
                evt.Permabuffs.Add(new ModifyAttribute { Target = AttributeTarget.SpellResistance, Value = val });
                OnCharacterChanged();
            }
        }
    }

    private void AddAbilityModToEvent(int evtIndex, Ability ability, int value)
    {
        var evt = _character.PermanentEvents[evtIndex];
        evt.Permabuffs.Add(new ModifyAttribute
        {
            Target = AttributeTarget.AbilityScore,
            AbilityScore = ability,
            Value = value
        });
        OnCharacterChanged();
    }

    private void AddCustomEquipmentEntry()
    {
        _character.Equipment.Add(new EquipmentEntry { ItemId = "New Item", Slot = "slotless" });
        OnCharacterChanged();
    }

    private void AddCatalogItem()
    {
        if (string.IsNullOrEmpty(_equipmentPickerSelection)) return;
        var def = _equipmentCatalog.FirstOrDefault(e => e.Id == _equipmentPickerSelection);
        if (def == null) return;
        _character.Equipment.Add(new EquipmentEntry
        {
            ContentId = def.Id,
            ItemId = def.Name,
            Slot = def.Slot ?? string.Empty,
            MainHand = def.Category == EquipmentCategory.Weapon,
            TwoHanded = def.Weapon?.TwoHanded ?? false
        });
        _equipmentPickerSelection = "";
        OnCharacterChanged();
    }

    private void ResetEquipmentSelection()
    {
        _equipmentPickerSelection = "";
    }

    private void RemoveEquipmentEntry(int index)
    {
        _character.Equipment.RemoveAt(index);
        OnCharacterChanged();
    }

    private void AddEquipmentEffect(int eqIndex, string effect)
    {
        var item = _character.Equipment[eqIndex];

        if (effect.StartsWith("ability_"))
        {
            var parts = effect.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out var bonus) && Enum.TryParse<Ability>(parts[2], out var ab))
            {
                item.Permabuffs.Add(new ModifyAttribute
                {
                    Target = AttributeTarget.AbilityScore,
                    AbilityScore = ab,
                    Value = bonus
                });
                OnCharacterChanged();
            }
        }
        else if (effect.StartsWith("nat_armor_"))
        {
            if (int.TryParse(effect.Replace("nat_armor_", ""), out var val))
            {
                item.Permabuffs.Add(new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = val });
                OnCharacterChanged();
            }
        }
        else if (effect.StartsWith("save_resist_"))
        {
            if (int.TryParse(effect.Replace("save_resist_", ""), out var val))
            {
                item.Permabuffs.Add(new ModifyAttribute { Target = AttributeTarget.AllSaves, Value = val });
                OnCharacterChanged();
            }
        }
        else if (effect.StartsWith("sr_"))
        {
            if (int.TryParse(effect.Replace("sr_", ""), out var val))
            {
                item.Permabuffs.Add(new SetAttribute { Target = AttributeTarget.SpellResistance, Value = val });
                OnCharacterChanged();
            }
        }
    }

    private static string FormatPermabuff(Permabuff buff)
    {
        return buff switch
        {
            ModifyAttribute ma => ma.Target switch
            {
                AttributeTarget.AbilityScore => $"{(ma.Value >= 0 ? "+" : "")}{ma.Value} {ma.AbilityScore}",
                AttributeTarget.NaturalArmor => $"+{ma.Value} Natural Armor",
                AttributeTarget.SpellResistance => $"+{ma.Value} SR",
                AttributeTarget.LevelAdjustment => $"+{ma.Value} LA",
                AttributeTarget.Resistance => $"+{ma.Value} {ma.ResistanceElement} Resistance",
                _ => $"Modify {ma.Target} {ma.Value}"
            },
            SetAttribute sa => sa.Target switch
            {
                AttributeTarget.SpellResistance => $"SR {sa.Value}",
                AttributeTarget.AbilityScore => $"Set {sa.AbilityScore} = {sa.Value}",
                _ => $"Set {sa.Target} = {sa.Value}"
            },
            GrantTypedBonus tb => FormatTypedBonus(tb),
            GrantArmorProfile ap => $"{(ap.AsShield ? "Shield" : "Armor")} +{ap.Profile.ArmorBonus}{(ap.Profile.MaxDex.HasValue ? $" (max dex {ap.Profile.MaxDex})" : "")}",
            GrantWeaponLine w => string.IsNullOrEmpty(w.DisplayName) ? w.Profile.Damage : $"{w.DisplayName} ({w.Profile.Damage})",
            GrantMovement movement => $"{movement.Mode} {movement.Speed} ft.{(movement.Mode == MovementMode.Fly && movement.FlyManeuverability.HasValue ? $" ({movement.FlyManeuverability})" : "")}",
            _ => buff.GetType().Name
        };
    }

    private static string FormatTypedBonus(GrantTypedBonus tb)
    {
        var sign = tb.Value.Expression.StartsWith("-") ? "" : "+";
        var target = tb.Target switch
        {
            BonusTarget.AC => "AC",
            BonusTarget.AllSaves => "Saves",
            BonusTarget.SaveFort => "Fort",
            BonusTarget.SaveRef => "Ref",
            BonusTarget.SaveWill => "Will",
            BonusTarget.AbilityStr => "Str",
            BonusTarget.AbilityDex => "Dex",
            BonusTarget.AbilityCon => "Con",
            BonusTarget.AbilityInt => "Int",
            BonusTarget.AbilityWis => "Wis",
            BonusTarget.AbilityCha => "Cha",
            BonusTarget.NaturalArmor => "Natural Armor",
            BonusTarget.SR => "SR",
            _ => tb.Target.ToString()
        };
        return $"{sign}{tb.Value.Expression} {tb.BonusType} to {target}";
    }

    private static string Titleise(string value) =>
        string.Join(" ", value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private sealed record TickSpellSummary(
        string SpellListId,
        int MaxSpellLevel,
        int AvailableSpellCount,
        bool IsDomainList);

    private sealed record TickSkillInfo(
        int UnspentPoints,
        int MaxHalfRanks,
        HashSet<string> CurrentTickClassSkills,
        HashSet<string> ClassSkills,
        Dictionary<string, int> SkillHalfRanks);

    private sealed record TickCasterInfo(
        int PendingDomainSelections,
        List<string> Domains,
        Dictionary<string, string> DomainOwners,
        Dictionary<string, TickSpellcastingInfo> Spellcasting,
        bool NeedsAdvanceSpellcastingChoice = false);

    private sealed record TickSpellcastingInfo(
        CastingType CastingType,
        Ability CastingStat,
        int CasterLevel,
        int MaxSpellLevel,
        Dictionary<int, int> SpellsPerDay,
        Dictionary<int, int>? SpellsKnown,
        Dictionary<int, int> DomainBonusSlots,
        Dictionary<int, int> SpecialtyBonusSlots,
        Dictionary<int, int> AbilityBonusSlots,
        SpellAcquisition Acquisition,
        // Spellbook casters only: how many spells of 1st level or higher the book may hold.
        int SpellbookLimit);
}
