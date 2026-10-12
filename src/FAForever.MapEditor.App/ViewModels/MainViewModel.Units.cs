using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The units' and armies' side of the core (user stories U-2, U-3, U-4, M-4): the armies with
/// their trees of groups, the place tool following the shelf and the tree, and units presets.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The armies of the open map, the Units mode's tree of groups and the Map mode's list of armies.
    /// </summary>
    public ArmiesViewModel Armies { get; }

    /// <summary>
    /// Whether the selection holds only structures, which turn in quarter turns (Q and E).
    /// </summary>
    public bool SelectionTurnsInQuarters => ShownDocument is MapDocument document && Selection.HasAny
        && Selection.Items.All(thing => thing.Kind == ObjectKind.Unit
            && document.Find(thing) is PlacedUnit unit && Assets.Catalog.Find(unit.Unit.Blueprint) is { IsStructure: true });

    private string? _placedInto;

    private void InitializeUnits()
    {
        UnitPlaceTool place = Tool<UnitPlaceTool>();
        Presets.Register(UnitPreset.Of(this));
        // the tree and the inspector tell how a placed instance stands to its preset (U-9)
        Armies.PresetFingerprint = Presets.UnitPresetFingerprint;
        // another map: no army, group or expanded node of the one before stays
        AnotherMap += (_, _) =>
        {
            _placedInto = null;
            place.ForgetMap();
            Armies.ForgetMap();
            place.Refresh(ShownDocument);
            Armies.Refresh(ShownDocument);
        };
        DocumentChanged += (_, _) =>
        {
            using Performance.Span measure = Performance.Measure("model.units");
            // the tool first: the tree's chosen group goes to the tool, which must know the group
            place.Refresh(ShownDocument);
            Armies.Refresh(ShownDocument);
            Presets.CountInstances(ShownDocument);
        };
        // the presets changed on disk (or one was saved): every instance's state again, and the check
        Presets.Changed += (_, _) =>
        {
            Armies.RefreshInstances();
            // a preset deleted is no longer what a click places
            if (place.Preset is string preset && Presets.Library is not null && !Presets.HasUnitsPreset(preset))
            {
                place.ClearPreset();
            }
            Presets.CountInstances(ShownDocument, again: true);
            Problems.Schedule();
        };

        // a unit chosen on the shelf or in the picker is what the place tool places
        Assets.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AssetsViewModel.Shown) && Assets.Shown is { Kind: AssetKind.Unit } unit)
            {
                place.Unit = unit;
            }
        };

        // a unit dropped on a view while another tool is active is placed by the place tool
        AssetDropped += (_, drop) =>
        {
            if (!drop.Handled && drop.Entry.Kind == AssetKind.Unit && HasMap)
            {
                SelectTool("units.place");
                place.Drop(drop.Entry, drop.X, drop.Z);
                drop.Handled = true;
            }
        };

        // the group chosen in the tree is where the place tool places
        // (a tree made again keeps its choice in a new node of the same key: that is no new choice, and must not
        // take the tool back from the army or group chosen in its row since)
        Armies.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ArmiesViewModel.ChosenGroup) && Armies.ChosenGroup is GroupNode node && node.Key != _placedInto)
            {
                _placedInto = node.Key;
                place.PlaceInto(node.Army, node.PathText);
            }
        };
        // and the row's Group combo chooses the group in the tree: one choice, shown twice
        place.GroupPicked += (army, path) =>
        {
            if (Armies.ChosenGroup?.Key != $"{army}:{path}" && !Armies.Choose(army, UnitGroupEdits.ParsePath(path)))
            {
                // the army has no such group yet (INITIAL is made by placing): its root group stands for it in the tree,
                // and the tool keeps the group it has
                _placedInto = $"{army}:";
                Armies.Choose(army, []);
            }
        };
    }

    /// <summary>
    /// A unit chosen in the Units mode's tree: it is selected, and the selection has the inspector
    /// (also when it was selected already).
    /// </summary>
    public void InspectUnit(UnitNode unit)
    {
        if (Assets.HasSelection)
        {
            Assets.Select((AssetTile?)null);
        }
        Selection.Set([ObjectRef.Unit(unit.Name)]);
        InspectTheSelection();
    }
}
