using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// What the Unit presets list can be narrowed to (user story U-9).
/// </summary>
public enum PresetFilter
{
    All,
    Starred,
    Civilian,
    Wrecks,

    /// <summary>
    /// The presets the user saved: those whose file is not among the examples the editor ships.
    /// </summary>
    Mine,
}

/// <summary>
/// A chip above the Unit presets list: one way to narrow it.
/// </summary>
public sealed partial class PresetFilterChip(PresetFilter filter, bool isOn, Func<PresetFilter, Task> choose) : ObservableObject
{
    public PresetFilter Filter => filter;

    public string Title => filter.ToString();

    public bool IsOn => isOn;

    /// <summary>
    /// For screen readers and automation: <c>Show the Starred unit presets</c>.
    /// </summary>
    public string Label => filter == PresetFilter.All ? "Show all unit presets" : $"Show the {Title} unit presets";

    [RelayCommand]
    private Task Choose() => choose(filter);
}

/// <summary>
/// The Unit presets list's filters and stars (user story U-9). A star is kept as the shelf keeps
/// those of assets, in the workspace's <c>favourites.json</c> under the key <c>preset:units:name</c>.
/// </summary>
public sealed partial class PresetsViewModel
{
    private IReadOnlyList<Preset> _all = [];
    private AssetsViewModel? _stars;

    /// <summary>
    /// Takes the stars from the assets' favourites; the list follows their changes.
    /// </summary>
    public void UseStars(AssetsViewModel assets)
    {
        _stars = assets;
        assets.Favourites.Changed += () =>
        {
            if (HasFilters)
            {
                ApplyFilter();
            }
            StarsChanged?.Invoke();
        };
    }

    /// <summary>
    /// Raised after a star was set or taken away, or the stars were read.
    /// </summary>
    public event Action? StarsChanged;

    /// <summary>
    /// Whether the panel's list has filter chips: the unit presets have.
    /// </summary>
    public bool HasFilters => Kind == UnitPresets.Kind;

    [ObservableProperty]
    public partial PresetFilter Filter { get; set; }

    /// <summary>
    /// The chips of the filters, the one in force on.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<PresetFilterChip> FilterChips { get; private set; } = [];

    /// <summary>
    /// Whether the filter leaves no preset of a list that has some.
    /// </summary>
    public bool NoneMatch => HasFilters && Shown.Count == 0 && _all.Count > 0;

    partial void OnFilterChanged(PresetFilter value)
    {
        MakeChips();
        ApplyFilter();
    }

    /// <summary>
    /// Chooses the filter of the Unit presets list.
    /// </summary>
    public Task ChooseFilter(PresetFilter filter)
    {
        Filter = filter;
        return Task.CompletedTask;
    }

    private void MakeChips() =>
        FilterChips = [.. Enum.GetValues<PresetFilter>().Select(filter => new PresetFilterChip(filter, filter == Filter, ChooseFilter))];

    /// <summary>
    /// Whether a preset is starred.
    /// </summary>
    public bool IsStarred(Preset preset) => _stars?.Favourites.IsStarred(StarKey(preset.Kind, preset.Name)) == true;

    /// <summary>
    /// Stars a preset or takes its star away; what cannot be written is logged.
    /// </summary>
    /// <returns>Whether it is starred now.</returns>
    public bool SetStar(string kind, string name, bool on)
    {
        if (_stars is null)
        {
            return false;
        }
        return _stars.SetStar(StarKey(kind, name), on);
    }

    /// <summary>
    /// Turns a preset's star, as its row's star does.
    /// </summary>
    public Task ToggleStar(Preset preset) => _run("presets.star", new JsonObject { ["kind"] = preset.Kind, ["name"] = preset.Name });

    /// <summary>
    /// The key a star is kept under: the kind and the preset's name, ignoring case as the files are named.
    /// </summary>
    public static string StarKey(string kind, string name) => $"preset:{kind}:{name.Trim().ToLowerInvariant()}";

    private IReadOnlyList<Preset> Filtered(IReadOnlyList<Preset> all)
    {
        if (!HasFilters || Filter == PresetFilter.All)
        {
            return all;
        }
        return [.. all.Where(Matches)];
    }

    private void ApplyFilter()
    {
        Shown = Filtered(_all);
        OnPropertyChanged(nameof(NoneMatch));
    }

    private bool Matches(Preset preset)
    {
        switch (Filter)
        {
            case PresetFilter.Starred:
                return IsStarred(preset);
            case PresetFilter.Mine:
                return Library is PresetLibrary library && !ExampleUnitPresets.Names.Contains(System.IO.Path.GetFileName(library.FileOf(preset.Kind, preset.Name)), StringComparer.OrdinalIgnoreCase);
            case PresetFilter.Civilian or PresetFilter.Wrecks:
                try
                {
                    UnitPresetSettings settings = preset.Read<UnitPresetSettings>();
                    return Filter == PresetFilter.Wrecks ? settings.Wrecks : settings.Role == ArmyRoles.Civilian;
                }
                catch (MapProjectException exception)
                {
                    _log.LogWarning(exception, "The unit preset {Preset} cannot be read to filter the list: {Reason}", preset.Name, exception.Message);
                    return false;
                }
            default:
                return true;
        }
    }

    /// <summary>
    /// The filters and stars for automation's <c>state.presets</c>.
    /// </summary>
    public JsonObject DescribeFilters() => new JsonObject
    {
        ["filter"] = HasFilters ? Filter.ToString() : null,
        ["total"] = _all.Count,
        ["starred"] = new JsonArray([.. _all.Where(IsStarred).Select(preset => (JsonNode)JsonValue.Create(preset.Name))]),
    };
}
