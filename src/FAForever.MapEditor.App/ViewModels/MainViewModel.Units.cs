using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
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

    private void InitializeUnits()
    {
        UnitPlaceTool place = Tool<UnitPlaceTool>();
        Presets.Register(UnitPreset.Of(this));
        // the tree and the inspector tell how a placed instance stands to its preset (U-9)
        Armies.PresetFingerprint = Presets.UnitPresetFingerprint;
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
        Armies.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ArmiesViewModel.ChosenGroup) && Armies.ChosenGroup is GroupNode node)
            {
                place.PlaceInto(node.Army, node.PathText);
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
