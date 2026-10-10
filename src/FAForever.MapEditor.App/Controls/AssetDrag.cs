using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// Dragging an asset from the shelf onto a view of the map (concept C4, user story F-9): what a
/// drag carries, and the handlers a view of the map attaches to take the drop. While the drag is
/// over a view, the status bar's place shows where it would land; the drop goes to
/// <see cref="MainViewModel.DropAsset"/>.
/// </summary>
public static class AssetDrag
{
    /// <summary>
    /// The asset's key, which only this editor reads: a drag never leaves the process.
    /// </summary>
    public static readonly DataFormat<string> Format = DataFormat.CreateInProcessFormat<string>("faf-map-editor-asset");

    /// <summary>
    /// What a drag of the asset carries.
    /// </summary>
    public static IDataTransfer Of(AssetEntry entry) => Of(entry.Key);

    /// <summary>
    /// What a drag of the asset with this key carries (automation's <c>drag {data}</c>).
    /// </summary>
    public static IDataTransfer Of(string key)
    {
        DataTransfer transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(Format, key));
        return transfer;
    }

    /// <summary>
    /// The key of the asset a drag carries; null for a drag of anything else.
    /// </summary>
    public static string? KeyOf(IDataTransfer? transfer) => transfer?.TryGetValue(Format);

    /// <summary>
    /// Lets a view of the map take a dragged asset.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <param name="model">The editor whose map it shows.</param>
    /// <param name="placeAt">The place of the map (in ogrids) at a point of the view; null outside the map.</param>
    /// <param name="cursor">Shows a line about the place in the status bar, as the mouse does.</param>
    public static void Attach(Control view, Func<MainViewModel?> model, Func<Point, Point?> placeAt, Action<string> cursor)
    {
        DragDrop.SetAllowDrop(view, true);
        view.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            if (Dragged(e, model) is not AssetEntry entry)
            {
                return;
            }
            Point? place = placeAt(e.GetPosition(view));
            e.DragEffects = place is null ? DragDropEffects.None : DragDropEffects.Copy;
            cursor(place is Point at ? $"Drop {entry.Name} at x {at.X:0}, z {at.Y:0}" : "");
            e.Handled = true;
        });
        view.AddHandler(DragDrop.DragLeaveEvent, (_, _) => cursor(""));
        view.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (Dragged(e, model) is not AssetEntry entry || model() is not MainViewModel editor)
            {
                return;
            }
            cursor("");
            if (placeAt(e.GetPosition(view)) is Point at)
            {
                editor.DropAsset(entry.Key, (float)at.X, (float)at.Y);
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
            e.Handled = true;
        });
    }

    private static AssetEntry? Dragged(DragEventArgs e, Func<MainViewModel?> model) =>
        KeyOf(e.DataTransfer) is string key && model() is { HasMap: true } editor ? editor.Assets.Catalog.Find(key) : null;
}
