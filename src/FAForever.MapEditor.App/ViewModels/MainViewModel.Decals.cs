using System.ComponentModel;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Decals mode's side of the core (user stories D-1, D-2): the decal groups and which are
/// hidden, the decal the place tool places, and decals dropped on the map.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The decal groups as the Decals panel lists them, and which the views hide.
    /// </summary>
    public DecalsViewModel Decals { get; }

    /// <summary>
    /// The Decals mode's place tool, which holds the decal to place.
    /// </summary>
    public DecalPlaceTool DecalPlaceTool => Tool<DecalPlaceTool>();

    /// <summary>
    /// Where the decal sets' masks are read from (the map's own files, else the workspace's assets),
    /// for the decal-masks layers that follow the decals.
    /// </summary>
    public IDecalMaskPictures DecalPictures { get; private set; } = null!;

    private void InitializeDecals()
    {
        DecalPictures = new GameMaskPictures(() => Setup.Workspace);
        DocumentChanged += (_, _) => Decals.Refresh(ShownDocument);
        // another map shows every group again; the same map resized (or its size undone) keeps what it hid
        AnotherMap += (_, _) =>
        {
            Decals.Clear();
            DecalPlaceTool.ForgetMapsDecal();
            DecalsAlong.ForgetMapsDecal();
        };
        MapChanged += (_, _) =>
        {
            if (Map is not null)
            {
                Map.Session.DecalPictures = DecalPictures;
            }
        };
        Selection.Changed += (_, _) => Decals.SelectionChanged(Selection.Items.Any(thing => thing.Kind == ObjectKind.Decal));
        Decals.HiddenChanged += (_, _) =>
        {
            // what is hidden cannot stay selected
            if (Selection.Items.Any(Decals.IsHidden))
            {
                Selection.Set(Selection.Items.Where(Decals.IsShown));
            }
            OverlaysChanged?.Invoke(this, EventArgs.Empty);
        };
        Assets.PropertyChanged += OnDecalChosen;
        AssetDropped += (_, drop) =>
        {
            // a decal dropped while another tool is active is placed with the place tool's settings
            if (!drop.Handled && drop.Entry.Kind == AssetKind.Decal)
            {
                DecalPlaceTool.Drop(drop.Entry, drop.X, drop.Z);
                drop.Handled = true;
            }
        };
    }

    /// <summary>
    /// A decal chosen on the shelf or with the picker becomes the one the place tool places; in the
    /// Decals mode the place tool becomes active, so the next click places it.
    /// </summary>
    private void OnDecalChosen(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AssetsViewModel.Shown) or nameof(AssetsViewModel.ShownSet)) || Assets.Shown is not { Kind: AssetKind.Decal } decal)
        {
            return;
        }
        // a decal set's tile places the whole set (D-1), one of its textures that texture alone
        DecalPlaceTool.Decal = Assets.ShownSet?.Name ?? decal.Key;
        if (SelectedMode?.Definition.Id == "decals" && ActiveTool?.Definition.Id != "decals.place")
        {
            SelectTool("decals.place");
        }
    }
}
