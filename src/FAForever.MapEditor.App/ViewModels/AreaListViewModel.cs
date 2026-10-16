using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// An area of the map (user story M-5), as <c>state.mapPanels</c> lists it: its name to type over, its rectangle, and
/// whether it is the playable area (<see cref="MapDocument.PlayableArea"/>: the one the scenario
/// names, else the first, which the map editors' scripts make playable as <c>AREA_1</c>).
/// </summary>
public sealed partial class AreaItem(Area area, bool isPlayable, Func<string, JsonObject, Task> run) : ObservableObject
{
    private string _name = area.Name;

    public Area Area => area;

    /// <summary>
    /// The name; typing another renames the area (<c>map.setArea</c>).
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            string name = value.Trim();
            if (name.Length == 0 || name == area.Name)
            {
                _name = value;
                OnPropertyChanged();
                _name = area.Name;
                OnPropertyChanged();
                return;
            }
            _ = run("map.setArea", new JsonObject { ["name"] = area.Name, ["rename"] = name });
        }
    }

    public bool IsPlayable => isPlayable;

    /// <summary>
    /// E.g. <c>0, 0 to 512, 512 (playable area)</c>.
    /// </summary>
    public string Rectangle => string.Create(CultureInfo.InvariantCulture,
        $"{area.X0:0.#}, {area.Z0:0.#} to {area.X1:0.#}, {area.Z1:0.#}{(isPlayable ? " · playable area" : "")}");

    [RelayCommand]
    private Task Select() => run("select.set", new JsonObject { ["refs"] = new JsonArray(ObjectRef.Area(area.Name).ToString()) });

    [RelayCommand]
    private Task Focus() => run("camera.focus", new JsonObject
    {
        ["x"] = (area.X0 + area.X1) / 2,
        ["z"] = (area.Z0 + area.Z1) / 2,
        ["radius"] = Math.Max(Math.Abs(area.X1 - area.X0), Math.Abs(area.Z1 - area.Z0)) / 2 + 8,
    });

    [RelayCommand]
    private Task Delete() => run("map.removeArea", new JsonObject { ["name"] = area.Name });
}

/// <summary>
/// The areas of the map (user story M-5), in their order, with the playable area marked (user story
/// M-3); the Map panel lists them as entries of its own (<see cref="MapPartsViewModel"/>).
/// Drawing, moving and resizing them is the areas tool's (<c>Tools/AreaTool</c>).
/// </summary>
public sealed partial class AreaListViewModel(Func<string, JsonObject, Task> run) : ObservableObject
{
    private System.Collections.Immutable.ImmutableArray<Area> _shown;
    private string? _shownPlayable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<AreaItem> Items { get; private set; } = [];

    public bool IsEmpty => Items.Count == 0;

    [RelayCommand]
    private Task Add() => run("map.addArea", []);

    public void Refresh(MapDocument? document)
    {
        System.Collections.Immutable.ImmutableArray<Area> areas = document?.Markers.Areas ?? [];
        string? playable = document?.PlayableArea?.Name;
        if (areas == _shown && !areas.IsDefault && playable == _shownPlayable)
        {
            return;
        }
        _shown = areas;
        _shownPlayable = playable;
        Items = [.. areas.Select(area => new AreaItem(area, area.Name == playable, run))];
    }
}
