using System.ComponentModel;
using FAForever.MapEditor.App.Tools;
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

    private void InitializeDecals()
    {
        DocumentChanged += (_, _) => Decals.Refresh(ShownDocument);
        MapChanged += (_, _) => Decals.Clear();
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
        if (e.PropertyName != nameof(AssetsViewModel.Shown) || Assets.Shown is not { Kind: AssetKind.Decal } decal)
        {
            return;
        }
        DecalPlaceTool.Decal = decal.Key;
        if (SelectedMode?.Definition.Id == "decals" && ActiveTool?.Definition.Id != "decals.place")
        {
            SelectTool("decals.place");
        }
    }
}
