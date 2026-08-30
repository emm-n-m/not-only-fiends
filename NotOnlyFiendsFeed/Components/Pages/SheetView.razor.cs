using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NotOnlyFiendsFeed.Services;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsFeed.Components.Pages;

public partial class SheetView
{
    private CharacterTab _activeTab = CharacterTab.Summary;

    private void SetActiveTab(CharacterTab tab) => _activeTab = tab;

    /// <summary>
    /// Same tab vocabulary as the builder. Companion display remains future work.
    /// </summary>
    private List<CharacterTabItem> VisibleTabs()
    {
        var tabs = new List<CharacterTabItem>
        {
            new(CharacterTab.Summary, "Summary"),
            new(CharacterTab.Feats, "Feats & Special Abilities"),
            new(CharacterTab.Skills, "Skills")
        };

        if (_state != null && (_state.Spellcasting.Count > 0 || _state.Domains.Count > 0))
            tabs.Add(new CharacterTabItem(CharacterTab.Spells, "Spells"));

        if (_character?.Equipment.Count > 0)
            tabs.Add(new CharacterTabItem(CharacterTab.Equipment, "Equipment"));

        return tabs;
    }

    /// <summary>
    /// Dragging the level slider below a caster's first casting level removes the Spells tab
    /// out from under the reader, so fall back rather than render a blank pane.
    /// </summary>
    private void NormalizeActiveTab()
    {
        if (!VisibleTabs().Any(t => t.Id == _activeTab))
            _activeTab = CharacterTab.Summary;
    }

    private static string FormatSigned(int value) => value >= 0 ? $"+{value}" : value.ToString();

    private string SkillName(string skillId) =>
        _registry.GetAllSkills().FirstOrDefault(s => s.Id == skillId)?.Name ?? skillId;

    // Tooltip on the Misc column: separates what content granted from what synergies added,
    // so a player can see why a skill is higher than their ranks explain.
    private string MiscBreakdown(string skillId)
    {
        var parts = new List<string>();
        var granted = _state!.SkillBonuses.GetValueOrDefault(skillId);
        var synergy = _state.SkillSynergyBonuses.GetValueOrDefault(skillId);
        if (granted != 0) parts.Add($"{FormatSigned(granted)} racial/class");
        if (synergy != 0) parts.Add($"{FormatSigned(synergy)} synergy");
        return string.Join(", ", parts);
    }

    // Size order for the wild shape matrix, smallest first — alphabetical would read
    // "Huge, Large, Medium, Small, Tiny", which is not how a druid gains them.
    private static readonly string[] _sizeOrder = { "fine", "diminutive", "tiny", "small", "medium", "large", "huge", "gargantuan", "colossal" };

    /// <summary>
    /// Renders <c>CharacterState.Capabilities</c>, which nothing read anywhere before this.
    ///
    /// The druid's wild shape matrix alone is 14 entries of the form
    /// <c>wild_shape:&lt;kind&gt;:&lt;size&gt;</c>; listing them raw would be 14 lines of jargon for
    /// what is really three lines of "which forms can I take". So that family is grouped by kind
    /// with its sizes ordered, and anything else falls back to a title-cased reading of its
    /// segments (<c>blood_witch:minor_sacrifice</c> → "Blood Witch — Minor Sacrifice").
    /// </summary>
    private static List<(string Label, string? Detail)> GroupCapabilities(IEnumerable<string> capabilities)
    {
        var rows = new List<(string, string?)>();
        var wildShape = new List<string>();
        var other = new List<string>();

        foreach (var capability in capabilities)
        {
            if (capability.StartsWith("wild_shape:", StringComparison.Ordinal))
                wildShape.Add(capability);
            else
                other.Add(capability);
        }

        foreach (var group in wildShape
            .Select(c => c.Split(':'))
            .Where(parts => parts.Length == 3)
            .GroupBy(parts => parts[1], StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var sizes = group
                .Select(parts => parts[2])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(size => Array.IndexOf(_sizeOrder, size))
                .Select(Titleise);
            rows.Add(($"Wild Shape — {Titleise(group.Key)}", string.Join(", ", sizes)));
        }

        foreach (var capability in other.OrderBy(c => c, StringComparer.Ordinal))
        {
            var parts = capability.Split(':');
            rows.Add((string.Join(" — ", parts.Select(Titleise)), null));
        }

        return rows;
    }

    private static string Titleise(string value) =>
        string.Join(" ", value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

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
        "perfect_wight_greater_invisibility_uses" => $"Greater Invisibility {value}/day",
        "perfect_wight_improved_legerdemain_uses" => $"Improved Legerdemain {value}/day",
        "perfect_wight_incorporeal_uses" => $"Incorporeal {value}/day",
        "perfect_wight_shadow_form_uses" => $"Shadow Form {value}/day",
        _ => $"{key}: {value}"
    };

    private bool _loading = true;
    private string? _error;
    private Character? _character;
    private CharacterState? _state;
    private ReplayStudio _engine = null!;
    private ContentRegistry _registry = null!;
    private int _viewHD = 1;
    private RaceDefinition? _raceDefinition;

    [Parameter] public string? Id { get; set; }

    /// <summary>Opens the sheet already positioned at this HD — a shareable life-stage view.</summary>
    [Parameter, SupplyParameterFromQuery(Name = "atHd")] public int? AtHd { get; set; }

    private readonly List<CharacterFileInfo> _savedCharacters = new();

    // The route id the character on screen came from, and whether that load actually finished.
    // The prerender pass of the sessionStorage route deliberately loads nothing, so "same id"
    // alone is not enough to skip a load.
    private string? _loadedId;
    private bool _loaded;

    /// <summary>
    /// Blazor reuses this component instance across <c>/sheet/a</c> → <c>/sheet/b</c>, so the load
    /// lives here rather than in <see cref="OnInitializedAsync"/> — which runs once and left
    /// character A on screen after navigating to B.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        if (_loaded && Id == _loadedId)
            return;

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        _character = null;
        _state = null;
        _raceDefinition = null;
        _savedCharacters.Clear();

        try
        {
            _registry = Content.Registry;
            _engine = Content.ReplayStudio;

            if (!string.IsNullOrWhiteSpace(Id))
            {
                // Direct link: /sheet/{id} loads straight from the store. Works during
                // prerender (no JS interop needed), so no error flashes.
                if (!CharacterStore.IsConfigured)
                {
                    _error = "Character store is not configured (set CHARACTERS_PATH in .env).";
                    MarkLoaded();
                    return;
                }

                _character = CharacterStore.Get(Id);
            }
            else
            {
                // No id: the character is handed off from the Builder via sessionStorage,
                // which is only reachable once the circuit is interactive. During the
                // prerender pass, defer — this runs again when interactive.
                if (!RendererInfo.IsInteractive)
                    return;

                var sessionJson = await JS.InvokeAsync<string?>("sessionStorage.getItem", "currentCharacter");
                if (!string.IsNullOrEmpty(sessionJson))
                    _character = System.Text.Json.JsonSerializer.Deserialize<Character>(sessionJson, JsonOptions.Default);
            }

            if (_character == null)
            {
                // Not an error — offer the saved characters to pick from.
                LoadSavedCharacters();
                MarkLoaded();
                return;
            }

            // A character with no HD yet — a blank cohort or follower — still has a race and
            // templates to show, and the slider has to stay at a level it can actually render.
            _viewHD = AtHd is int requested
                ? Math.Clamp(requested, 1, Math.Max(1, _character.Ticks.Count))
                : Math.Max(1, _character.Ticks.Count);
            _state = _engine.Evaluate(_character, upToHD: _viewHD);
            _raceDefinition = _registry.GetAllRaces().FirstOrDefault(r => r.Id == _character.RaceId);
            NormalizeActiveTab();
            MarkLoaded();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            MarkLoaded();
        }
    }

    private void MarkLoaded()
    {
        _loadedId = Id;
        _loaded = true;
        _loading = false;
    }

    private void LoadSavedCharacters()
    {
        if (!CharacterStore.IsConfigured)
            return;
        try
        {
            _savedCharacters.AddRange(CharacterStore.List().OrderByDescending(c => c.ModifiedUtc));
        }
        catch
        {
            // A malformed store should just leave the picker empty, not crash the page.
        }
    }

    private void OnSliderChanged()
    {
        if (_character != null)
        {
            _state = _engine.Evaluate(_character, upToHD: _viewHD);
            NormalizeActiveTab();
        }
    }
}
