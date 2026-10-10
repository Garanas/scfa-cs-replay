using System.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The selection's side of the core (user story F-11): what a click or a box picks, which kinds it
/// may pick, and the overlays of the things that can be picked in the top view.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// What is selected on the map, what was copied, and the inspector's view of it.
    /// </summary>
    public SelectionViewModel Selection { get; }

    /// <summary>
    /// The place under the mouse in a view, in ogrids; null while the mouse is outside the views.
    /// Paste puts what was copied here.
    /// </summary>
    public (float X, float Z)? Pointer { get; set; }

    /// <summary>
    /// Whether the views show the props: as dots in the top view, as their models in 3D.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowProps { get; set; } = true;

    /// <summary>
    /// Whether the views show the units: as squares in the top view, as their models in 3D.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowUnits { get; set; } = true;

    /// <summary>
    /// Whether the top view shows the areas, as dashed rectangles with their names.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowAreas { get; set; } = true;

    partial void OnShowPropsChanged(bool value) => RefreshOverlays();

    partial void OnShowUnitsChanged(bool value) => RefreshOverlays();

    partial void OnShowAreasChanged(bool value) => RefreshOverlays();

    /// <summary>
    /// The kinds the views show, so a click can pick them: hidden things are not picked. Decals are
    /// always on the terrain.
    /// </summary>
    public ObjectKinds ShownKinds =>
        (ShowMarkers ? ObjectKinds.Markers : ObjectKinds.None)
        | (ShowPathNodes ? ObjectKinds.PathNodes : ObjectKinds.None)
        | (ShowProps ? ObjectKinds.Props : ObjectKinds.None)
        | (ShowUnits ? ObjectKinds.Units : ObjectKinds.None)
        | (ShowAreas ? ObjectKinds.Areas : ObjectKinds.None)
        | (ShowDecals ? ObjectKinds.Decals : ObjectKinds.None);

    /// <summary>
    /// The kinds the selected mode works on: the Markers mode its markers and areas, the Props mode
    /// props, and so on; a mode without things of its own, every kind.
    /// </summary>
    public ObjectKinds ModeKinds => SelectedMode?.Definition.Id switch
    {
        "markers" => ObjectKinds.Markers | ObjectKinds.PathNodes | ObjectKinds.Areas,
        "props" => ObjectKinds.Props,
        "decals" => ObjectKinds.Decals,
        "units" => ObjectKinds.Units,
        "map" => ObjectKinds.Areas,
        _ => ObjectKinds.All,
    };

    /// <summary>
    /// What a click, a box or Select all may pick now: the active select tool's kinds (else the
    /// mode's) that the views show and that are not locked (<see cref="LockedKinds"/>).
    /// </summary>
    public ObjectKinds SelectableKinds => ((ActiveTool?.Tool as SelectTool)?.Kinds ?? ModeKinds) & ShownKinds & ~LockedKinds;

    /// <summary>
    /// What a box or Select all takes: the kinds that can be picked, without areas unless areas are
    /// all that can be (the Map mode), so that a box over markers never takes the playable area
    /// along (a click still picks an area by its edge).
    /// </summary>
    public ObjectKinds BoxKinds => SelectableKinds == ObjectKinds.Areas ? ObjectKinds.Areas : SelectableKinds & ~ObjectKinds.Areas;

    private void InitializeSelection()
    {
        // selecting on the map takes the inspector from the shelf's asset, and choosing an asset gives it back
        Selection.Changed += (_, _) =>
        {
            if (Selection.HasAny && Assets.HasSelection)
            {
                Assets.Select((AssetTile?)null);
            }
            Selection.Inspector.Refresh(ShownDocument);
            Commands.NotifyCanRunChanged();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        Assets.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AssetsViewModel.HasSelection) && Assets.HasSelection)
            {
                Selection.Clear();
            }
        };
        DocumentChanged += (_, _) =>
        {
            Selection.Prune(ShownDocument);
            Selection.Inspector.Refresh(ShownDocument);
        };
        MapChanged += (_, _) =>
        {
            Selection.Clear();
        };
    }

    /// <summary>
    /// Raised when the selection changes: the views draw its outlines again.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// The thing a click at a place picks (<see cref="MapObjects.Nearest"/>) among the kinds given,
    /// or by default those that can be picked now (<see cref="SelectableKinds"/>); never a decal of
    /// a hidden group (<see cref="DecalsViewModel"/>).
    /// </summary>
    /// <param name="ogridsPerPixel">How large a pixel of the view is on the map.</param>
    public ObjectRef? PickAt(float x, float z, float ogridsPerPixel, ObjectKinds? kinds = null) =>
        ShownDocument?.Nearest(x, z, ogridsPerPixel, kinds ?? SelectableKinds, Decals.HiddenDecals.Count > 0 || Isolated is not null ? IsPickable : null, ShownMarkerKinds);

    /// <summary>
    /// Selects what a click at a place picks; a click on nothing clears the selection unless it adds.
    /// </summary>
    /// <returns>What it picked, or null.</returns>
    public ObjectRef? SelectAt(float x, float z, float ogridsPerPixel, SelectionChange change, ObjectKinds? kinds = null)
    {
        ObjectRef? picked = PickAt(x, z, ogridsPerPixel, kinds);
        if (picked is ObjectRef thing)
        {
            Selection.Apply([thing], change);
        }
        else if (change == SelectionChange.Replace)
        {
            Selection.Clear();
        }
        return picked;
    }

    /// <summary>
    /// Selects what lies in a box (corners in ogrids, any order), as dragging on empty ground does.
    /// </summary>
    public void SelectInside(float x0, float z0, float x1, float z1, SelectionChange change, ObjectKinds? kinds = null)
    {
        if (ShownDocument is MapDocument document)
        {
            Selection.Apply(document.Inside(x0, z0, x1, z1, kinds ?? BoxKinds, ShownMarkerKinds).Where(IsPickable), change);
        }
    }

    /// <summary>
    /// Selects what shows inside a rectangle of a view's pixels: each thing of the kinds that can be
    /// picked whose place (at its own height) <paramref name="toScreen"/> puts inside it. The 3D
    /// view's box.
    /// </summary>
    /// <param name="from">A corner, in the view's pixels; <paramref name="to"/> the other, in any order.</param>
    /// <param name="toScreen">Where a place shows in the view; null behind the camera.</param>
    public void SelectOnScreen(System.Numerics.Vector2 from, System.Numerics.Vector2 to, Func<System.Numerics.Vector3, System.Numerics.Vector2?> toScreen, SelectionChange change, ObjectKinds? kinds = null)
    {
        if (ShownDocument is not MapDocument document)
        {
            return;
        }
        System.Numerics.Vector2 min = System.Numerics.Vector2.Min(from, to);
        System.Numerics.Vector2 max = System.Numerics.Vector2.Max(from, to);
        List<ObjectRef> inside = [];
        using (Performance.Measure("model.select-on-screen"))
        {
            foreach (ObjectRef thing in document.All(kinds ?? BoxKinds, ShownMarkerKinds).Where(IsPickable))
            {
                if (document.PlaceOf(thing) is System.Numerics.Vector3 place && toScreen(place) is System.Numerics.Vector2 at
                    && at.X >= min.X && at.Y >= min.Y && at.X <= max.X && at.Y <= max.Y)
                {
                    inside.Add(thing);
                }
            }
        }
        Selection.Apply(inside, change);
    }

    /// <summary>
    /// Selects every thing of the kinds that can be picked now.
    /// </summary>
    public void SelectAll(ObjectKinds? kinds = null)
    {
        if (ShownDocument is MapDocument document)
        {
            Selection.Set(document.All(kinds ?? BoxKinds, ShownMarkerKinds).Where(IsPickable));
        }
    }

    /// <summary>
    /// Runs a command with arguments as a key or a field of the window does: what goes wrong shows
    /// in the status line.
    /// </summary>
    public Task RunWithAsync(string id, JsonObject arguments) => TryAsync(() => Commands.RunAsync(id, arguments, interactive: true));
}
