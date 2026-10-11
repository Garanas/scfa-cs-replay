using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Map mode's panel (user stories M-1, M-3, M-4, M-5, M-6): the list of what the map has,
/// <see cref="MapPartsViewModel.Parts"/>.
/// </summary>
public sealed record MapPartsSection() : PanelSection("Map");

/// <summary>
/// The Map mode's inspector: the entry chosen in the panel's list.
/// </summary>
public sealed record MapDetailsSection() : PanelSection("Map");

/// <summary>
/// The inspector's part of the map's information (user story M-1): About, Game and Preview, with the
/// map's folder, size and armies (<c>Views/MapPanels/MapInformationPanel</c>).
/// </summary>
public sealed record MapInformationPart(MainViewModel Model);

/// <summary>
/// The inspector's part of the map's size and playable area (user story M-3): the size now, what the
/// Map size tool would make, and which area is playable (<c>Views/MapPanels/MapSizePanel</c>).
/// </summary>
public sealed record MapSizePart(MapSizeDetails Details, MapSizeTool Tool);

/// <summary>
/// The inspector's part of the map's script and the other files of its folder (user story M-6),
/// <c>Views/MapPanels/ScriptPanel</c>.
/// </summary>
public sealed record MapScriptPart(MainViewModel Model);

/// <summary>
/// The inspector's part of an army (user story M-4): the chosen army's fields
/// (<c>Views/MapPanels/ArmyPanel</c>, the view the Units mode shows for an army); choosing its entry
/// makes it <see cref="ArmiesViewModel.ChosenArmy"/>.
/// </summary>
public sealed record MapArmyPart(string Army, ArmiesViewModel Armies);

/// <summary>
/// The inspector's part of an area (user stories M-3, M-5): its name and corners, whether it is the
/// playable area, Show it in the 3D view and Remove it (<c>Views/MapPanels/MapAreaPanel</c>).
/// Choosing its entry selects it on the map, so its handles show.
/// </summary>
public sealed partial class MapAreaPart : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private Area? _area;

    /// <param name="run">Runs a command as a field does; renaming the area runs <c>map.setArea</c> through it.</param>
    public MapAreaPart(string name, Func<string, JsonObject, Task> run)
    {
        Name = name;
        _run = run;
        Area = (AreaSelection)SelectionSection.For(ObjectKind.Area, run);
    }

    /// <summary>
    /// The area's name; an entry is the area of one name (a renamed area is a new entry).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Its fields, the ones the inspector shows for one selected area.
    /// </summary>
    public AreaSelection Area { get; }

    /// <summary>
    /// E.g. <c>96, 96 to 160, 160, 64 × 64 ogrids</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Rectangle { get; private set; } = "";

    public void Refresh(MapDocument document)
    {
        if (document.Markers.Areas.FirstOrDefault(area => area.Name == Name) is not Area area)
        {
            return;
        }
        _area = area;
        Area.Refresh(document, [ObjectRef.Area(Name)]);
        Rectangle = string.Create(CultureInfo.InvariantCulture,
            $"{area.X0:0.#}, {area.Z0:0.#} to {area.X1:0.#}, {area.Z1:0.#}, {area.X1 - area.X0:0.#} × {area.Z1 - area.Z0:0.#} ogrids");
    }

    [RelayCommand]
    private Task Focus() => _area is not Area area ? Task.CompletedTask : _run("camera.focus", new JsonObject
    {
        ["x"] = (area.X0 + area.X1) / 2,
        ["z"] = (area.Z0 + area.Z1) / 2,
        ["radius"] = Math.Max(Math.Abs(area.X1 - area.X0), Math.Abs(area.Z1 - area.Z0)) / 2 + 8,
    });

    [RelayCommand]
    private Task Remove() => _run("map.removeArea", new JsonObject { ["name"] = Name });
}

/// <summary>
/// What the inspector says of the map's size and playable area (user story M-3); choosing another
/// playable area runs <c>map.setPlayableArea</c>.
/// </summary>
public sealed partial class MapSizeDetails(Func<string, JsonObject, Task> run) : ObservableObject
{
    private bool _showing;

    /// <summary>
    /// E.g. <c>10 km, 512 by 512 ogrids</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Size { get; private set; } = "";

    /// <summary>
    /// Which area is playable and why: <c>AREA_1: 0, 0 to 512, 512, the first area</c>, or the whole map.
    /// </summary>
    [ObservableProperty]
    public partial string Playable { get; private set; } = "";

    /// <summary>
    /// The areas, to choose the playable one from.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAreas))]
    public partial IReadOnlyList<string> Areas { get; private set; } = [];

    public bool HasAreas => Areas.Count > 0;

    /// <summary>
    /// The playable area; choosing another makes it so (<c>map.setPlayableArea</c>).
    /// </summary>
    [ObservableProperty]
    public partial string? PlayableArea { get; set; }

    partial void OnPlayableAreaChanged(string? value)
    {
        if (!_showing && value is not null)
        {
            _ = run("map.setPlayableArea", new JsonObject { ["name"] = value });
        }
    }

    public void Refresh(MapDocument? document, string size)
    {
        _showing = true;
        try
        {
            Size = size;
            if (document is null)
            {
                Playable = "";
                Areas = [];
                PlayableArea = null;
                return;
            }
            string[] areas = [.. document.Markers.Areas.Select(area => area.Name)];
            if (!areas.SequenceEqual(Areas))
            {
                Areas = areas;
            }
            Area? playable = document.PlayableArea;
            PlayableArea = playable?.Name;
            Playable = playable is null
                ? "The whole map: it has no areas. Add one to make a part of it playable at the start."
                : string.Create(CultureInfo.InvariantCulture,
                    $"{playable.Name}: {playable.X0:0.#}, {playable.Z0:0.#} to {playable.X1:0.#}, {playable.Z1:0.#}, {(document.Scenario.PlayableArea is null ? "the first area" : "chosen as the playable area")}");
        }
        finally
        {
            _showing = false;
        }
    }
}

/// <summary>
/// The Map mode's list (user stories M-1, M-3, M-4, M-5, M-6): under Map the information, the size
/// and playable area, and the script and files; an entry per army under Armies and per area under
/// Areas, kept in step with the map (<see cref="PanelParts.SyncGroup"/>). The inspector edits the
/// chosen entry.
/// </summary>
public sealed class MapPartsViewModel
{
    // the ids of the Map panel's entries (view.inspectPart)
    public const string InformationPart = "map.information";
    public const string SizePart = "map.size";
    public const string ScriptPart = "map.script";
    public const string ArmyPrefix = "map.army.";
    public const string AreaPrefix = "map.area.";

    // the headings
    public const string MapGroup = "Map";
    public const string ArmiesGroup = "Armies";
    public const string AreasGroup = "Areas";

    public static string ArmyPart(string army) => ArmyPrefix + army;

    public static string AreaPart(string area) => AreaPrefix + area;

    private readonly Func<string, JsonObject, Task> _areaRun;
    private IReadOnlyList<ArmyRow>? _shownArmies;
    private System.Collections.Immutable.ImmutableArray<Area> _shownAreas;
    private string? _shownPlayable;

    /// <param name="areaRun">Runs a command as an area's fields do.</param>
    public MapPartsViewModel(MainViewModel model, ArmiesViewModel armies, Func<string, JsonObject, Task> run, Func<string, JsonObject, Task> areaRun)
    {
        Armies = armies;
        _areaRun = areaRun;
        SizeDetails = new MapSizeDetails(run);
        Parts = new PanelParts(
        [
            new PanelPart(InformationPart, "Information", MapGroup, new MapInformationPart(model),
                "What players see: the name, description, author and version, the game's settings and the preview"),
            new PanelPart(SizePart, "Size and playable area", MapGroup, new MapSizePart(SizeDetails, model.Tool<MapSizeTool>()),
                "Grow or crop the map with the Map size tool, and which area is playable at the start"),
            new PanelPart(ScriptPart, "Script and files", MapGroup, new MapScriptPart(model),
                "The map's script, to open in your own editor, and the other files of its folder"),
        ]);
    }

    public ArmiesViewModel Armies { get; }

    /// <summary>
    /// What the Map panel lists; the inspector edits the chosen entry.
    /// </summary>
    public PanelParts Parts { get; }

    public MapSizeDetails SizeDetails { get; }

    /// <summary>
    /// The army an entry is of, or null for another entry.
    /// </summary>
    public static string? ArmyOf(PanelPart part) => part.Content is MapArmyPart army ? army.Army : null;

    /// <summary>
    /// The area an entry is of, or null for another entry.
    /// </summary>
    public static string? AreaOf(PanelPart part) => part.Content is MapAreaPart area ? area.Name : null;

    /// <summary>
    /// The short line beside an army: its team (<c>team 1</c>), <c>playable</c> without one, or
    /// <c>extra · 42 units</c> for an army no player takes.
    /// </summary>
    public static string DetailOf(ArmyRow army) =>
        army.Playable ? army.Team is null ? "playable" : $"team {army.Team}" : $"extra · {MainViewModel.Units(army.Units)}";

    /// <summary>
    /// The short line beside an area: <c>playable</c>, or its size (<c>96 × 64</c>).
    /// </summary>
    public static string DetailOf(Area area, bool playable) =>
        playable ? "playable" : string.Create(CultureInfo.InvariantCulture, $"{area.X1 - area.X0:0.#} × {area.Z1 - area.Z0:0.#}");

    /// <summary>
    /// Shows a version of the document: the details of the fixed entries, and the armies' and areas'
    /// entries, made again only when the armies or areas changed (a dab of a brush costs nothing).
    /// </summary>
    /// <returns>Whether the armies' or areas' entries changed.</returns>
    public bool Refresh(MapDocument? document, MapInformationViewModel information, ScriptFilesViewModel files)
    {
        Parts.Find(InformationPart)!.Detail = information.Summary;
        Parts.Find(ScriptPart)!.Detail = document is null ? ""
            : $"{(document.Script is null ? "no script" : "script.lua")} · {(document.Files.Count == 1 ? "1 file" : $"{document.Files.Count.ToString(CultureInfo.InvariantCulture)} files")}";
        Parts.Find(SizePart)!.Detail = document is null ? ""
            : string.Create(CultureInfo.InvariantCulture, $"{(document.Width == document.Height ? $"{document.Width}" : $"{document.Width} × {document.Height}")} · {document.PlayableArea?.Name ?? "whole map"}");
        SizeDetails.Refresh(document, information.Size);

        bool changed = false;
        if (!ReferenceEquals(Armies.Rows, _shownArmies))
        {
            _shownArmies = Armies.Rows;
            changed |= Parts.SyncGroup(ArmiesGroup, [.. Armies.Rows.Select(row => new PanelPartEntry(ArmyPart(row.Name), row.Name,
                () => new MapArmyPart(row.Name, Armies), DetailOf(row), "An army: whether players take it, its team, factions and alliances"))], MapGroup);
        }
        System.Collections.Immutable.ImmutableArray<Area> areas = document?.Markers.Areas ?? [];
        string? playable = document?.PlayableArea?.Name;
        if (areas != _shownAreas || areas.IsDefault || playable != _shownPlayable)
        {
            _shownAreas = areas;
            _shownPlayable = playable;
            changed |= Parts.SyncGroup(AreasGroup, [.. areas.Select(area => new PanelPartEntry(AreaPart(area.Name), area.Name,
                () => new MapAreaPart(area.Name, _areaRun), DetailOf(area, area.Name == playable), "An area: choosing it selects it on the map, so its handles show"))], ArmiesGroup);
            if (document is not null)
            {
                foreach (PanelPart part in Parts.Items)
                {
                    (part.Content as MapAreaPart)?.Refresh(document);
                }
            }
        }
        return changed;
    }
}
