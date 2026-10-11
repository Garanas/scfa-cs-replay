using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// An asset dragged from the shelf and dropped on a view of the map, at a place in ogrids.
/// </summary>
public sealed class AssetDropEventArgs(AssetEntry entry, float x, float z) : EventArgs
{
    public AssetEntry Entry => entry;

    public float X => x;

    public float Z => z;

    /// <summary>
    /// Set by whoever took the drop; a drop nobody takes says so in the status line.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Dragging from the shelf onto the map (concept C4, user story F-9): the views hand the drop to
/// <see cref="DropAsset"/>, which gives it to the active tool or whoever handles <see cref="AssetDropped"/>.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Raised when an asset is dropped on a view of the map and the active tool did not take it
    /// (<see cref="IAssetDropTarget"/>): the place for a mode to place what is dropped.
    /// </summary>
    public event EventHandler<AssetDropEventArgs>? AssetDropped;

    /// <summary>
    /// Drops the asset of <paramref name="key"/> on the map at a place in ogrids: the active tool
    /// takes it if it can, else <see cref="AssetDropped"/> is raised. What nobody takes says what
    /// will take it, with its user story, in the status line.
    /// </summary>
    /// <returns>Whether something took the drop.</returns>
    /// <exception cref="ArgumentException">The workspace has no such asset.</exception>
    public bool DropAsset(string key, float x, float z)
    {
        AssetEntry entry = Assets.Find(key) ?? throw new ArgumentException($"The workspace has no asset '{key}'");
        AssetDropEventArgs drop = new AssetDropEventArgs(entry, x, z);
        if (ActiveTool?.Tool is IAssetDropTarget target && target.Accepts(entry))
        {
            target.Drop(entry, x, z);
            drop.Handled = true;
        }
        else
        {
            AssetDropped?.Invoke(this, drop);
        }
        Logger("Assets").LogDebug("Dropped {Asset} at {X}, {Z}: {Taken}", entry.Key, Math.Round(x), Math.Round(z), drop.Handled ? "taken" : "not taken");
        if (!drop.Handled)
        {
            Status = entry.Kind switch
            {
                AssetKind.Prop or AssetKind.Unit or AssetKind.Decal => $"Open a map to place {entry.Name}",
                AssetKind.Texture => $"A texture is a stratum's: choose {entry.Name} for a layer in the Paint mode's layer textures",
                _ => $"A brush is a shape of the brush tools: choose {entry.Name} on the shelf",
            };
        }
        return drop.Handled;
    }
}
