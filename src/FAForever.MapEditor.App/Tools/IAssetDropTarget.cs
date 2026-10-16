using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A tool that takes an asset dragged from the shelf onto a view of the map: a tool that places
/// props, units or decals (user stories P-1, U-2, D-1). The active tool is asked first
/// (<see cref="ViewModels.MainViewModel.DropAsset"/>); what it does not take goes to
/// <see cref="ViewModels.MainViewModel.AssetDropped"/>.
/// </summary>
public interface IAssetDropTarget
{
    /// <summary>
    /// Whether the tool takes this asset; the views show a drop that nothing takes as such.
    /// </summary>
    bool Accepts(AssetEntry entry);

    /// <summary>
    /// The asset was dropped at a place of the map, in ogrids.
    /// </summary>
    void Drop(AssetEntry entry, float x, float z);
}
