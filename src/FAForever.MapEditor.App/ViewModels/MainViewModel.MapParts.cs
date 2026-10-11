using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Map mode as "the panel lists, the inspector edits" (user stories M-1, M-3, M-4, M-5, M-6):
/// the list of <see cref="MapPartsViewModel"/> kept in step with the map, and what choosing an entry
/// does besides showing it: the size chooses the Map size tool, an army becomes the chosen army, an
/// area is selected on the map (its handles show) while the inspector keeps the entry.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The Map mode's list: the information, size, script, armies and areas.
    /// </summary>
    public MapPartsViewModel MapParts { get; private set; } = null!;

    // set while the list follows the map, the selection or the chosen army: such a choice selects nothing
    private bool _followingMapParts;

    private void InitializeMapParts()
    {
        MapParts = new MapPartsViewModel(this, Armies, RunWithAsync, RunAreaFieldAsync);
        PanelParts parts = MapParts.Parts;
        parts.SetFooter(MapPartsViewModel.ArmiesGroup, new PanelPartFooter(
        [
            new PanelPartAction("Add army", new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => AddArmyFromPanelAsync(extra: false)),
                Label: "Add a playable army", Help: "With a spawn in the middle of the map"),
            new PanelPartAction("Add extra", new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => AddArmyFromPanelAsync(extra: true)),
                Label: "Add an extra army", Help: "An army no player takes, such as civilians"),
        ], "No armies.", MapPartsViewModel.MapGroup));
        parts.SetFooter(MapPartsViewModel.AreasGroup, new PanelPartFooter(
        [
            new PanelPartAction("Add an area", new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(AddAreaFromPanelAsync),
                Help: "A square in the middle of the map, selected with its handles"),
            new PanelPartAction("Draw", RunCommand, "tool.map.areas", Label: "Draw areas with the areas tool",
                Help: "Drag a rectangle in a view; drag the selected area's corners or edges to resize it"),
        ], "No areas yet. The first area is the playable area.", MapPartsViewModel.ArmiesGroup));

        DocumentChanged += (_, _) => RefreshMapParts();
        MapChanged += (_, _) => RefreshMapParts();

        parts.Chose += (_, _) =>
        {
            if (_followingMapParts)
            {
                return;
            }
            PanelPart chosen = parts.Chosen;
            if (MapPartsViewModel.ArmyOf(chosen) is string army)
            {
                if (Armies.ChosenArmy?.Name != army && Armies.Rows.FirstOrDefault(row => row.Name == army) is ArmyRow row)
                {
                    Armies.ChosenArmy = row;
                }
            }
            else if (MapPartsViewModel.AreaOf(chosen) is string area && SelectedMode?.Definition.Id == "map")
            {
                // the area chosen is selected, so its handles show; the entry keeps the inspector
                // (InspectTheMode follows the choice)
                if (Selection.Items.Length != 1 || !Selection.Contains(ObjectRef.Area(area)))
                {
                    Selection.Set([ObjectRef.Area(area)]);
                }
            }
            else if (chosen.Id == MapPartsViewModel.SizePart && SelectedMode?.Definition.Id == "map" && ActiveTool?.Definition.Id != "map.size")
            {
                SelectTool("map.size");
            }
        };

        // and back: the Map size tool shows its entry, one area selected its entry, the army chosen
        // elsewhere (the Units mode's tree, a command) the army's entry while an army is shown
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActiveTool) && ActiveTool?.Definition.Id == "map.size" && parts.Chosen.Id != MapPartsViewModel.SizePart)
            {
                parts.Choose(MapPartsViewModel.SizePart);
            }
        };
        Selection.Changed += (_, _) => FollowSelectedArea();
        Armies.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ArmiesViewModel.ChosenArmy))
            {
                FollowChosenArmy();
            }
        };
        RefreshMapParts();
    }

    /// <summary>
    /// Keeps the Map panel's list in step with the shown document; cheap when the armies and areas
    /// did not change (span <c>model.map-parts</c>).
    /// </summary>
    private void RefreshMapParts()
    {
        using Performance.Span measure = Performance.Measure("model.map-parts");
        _followingMapParts = true;
        try
        {
            if (MapParts.Refresh(ShownDocument, MapInformation, ScriptFiles))
            {
                FollowChosenArmy();
                FollowSelectedArea();
            }
        }
        finally
        {
            _followingMapParts = false;
        }
    }

    // an army chosen elsewhere is the army entry the list shows, while it shows an army
    private void FollowChosenArmy()
    {
        PanelParts parts = MapParts.Parts;
        if (Armies.ChosenArmy is ArmyRow row && MapPartsViewModel.ArmyOf(parts.Chosen) is string shown && shown != row.Name
            && parts.Find(MapPartsViewModel.ArmyPart(row.Name)) is not null)
        {
            Follow(() => parts.Choose(MapPartsViewModel.ArmyPart(row.Name)));
        }
    }

    // one area selected on the map is the area entry the Map panel's list shows; the selection keeps the inspector
    private void FollowSelectedArea()
    {
        if (SelectedMode?.Definition.Id != "map" || Selection.Items.Length != 1 || Selection.Items[0] is not { Kind: ObjectKind.Area, Name: string area })
        {
            return;
        }
        string id = MapPartsViewModel.AreaPart(area);
        if (MapParts.Parts.Chosen.Id != id && MapParts.Parts.Find(id) is not null)
        {
            Follow(() => MapParts.Parts.Choose(id));
        }
    }

    private void Follow(Action choose)
    {
        bool following = _followingMapParts;
        _followingMapParts = true;
        try
        {
            choose();
        }
        finally
        {
            _followingMapParts = following;
        }
    }

    /// <summary>
    /// Runs a command for an area's fields in the Map mode's inspector: a renamed area's entry is
    /// shown again under its new name (the selection that follows the rename would take the inspector).
    /// </summary>
    private async Task RunAreaFieldAsync(string id, JsonObject arguments)
    {
        string? name = arguments["name"]?.GetValue<string>();
        string? rename = id == "map.setArea" ? arguments["rename"]?.GetValue<string>()?.Trim() : null;
        bool shown = name is not null && MapParts.Parts.Chosen.Id == MapPartsViewModel.AreaPart(name) && InspectorOwner == InspectorOwner.Part;
        await RunWithAsync(id, arguments);
        if (shown && rename is { Length: > 0 } && MapParts.Parts.Find(MapPartsViewModel.AreaPart(rename)) is not null)
        {
            InspectPart(MapPartsViewModel.AreaPart(rename));
        }
    }

    // the panel's Add army and Add extra: the new army's entry is shown
    private async Task AddArmyFromPanelAsync(bool extra)
    {
        CommandRun run = await RunAndAnswerAsync("map.addArmy", new JsonObject { ["extra"] = extra });
        if (run.Result is string army && MapParts.Parts.Find(MapPartsViewModel.ArmyPart(army)) is not null)
        {
            InspectPart(MapPartsViewModel.ArmyPart(army));
        }
    }

    // the panel's Add an area: the new area is selected with its handles, and its entry shown
    private async Task AddAreaFromPanelAsync()
    {
        CommandRun run = await RunAndAnswerAsync("map.addArea", []);
        if (run.Result is string area && MapParts.Parts.Find(MapPartsViewModel.AreaPart(area)) is not null)
        {
            InspectPart(MapPartsViewModel.AreaPart(area));
        }
    }

    private async Task<CommandRun> RunAndAnswerAsync(string id, JsonObject arguments)
    {
        CommandRun run = new CommandRun(false, null);
        await TryAsync(async () => run = await Commands.RunAsync(id, arguments, interactive: true));
        return run;
    }
}
