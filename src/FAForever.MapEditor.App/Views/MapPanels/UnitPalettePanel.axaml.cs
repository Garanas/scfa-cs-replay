using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Views.MapPanels;

/// <summary>
/// The Units mode's palette (user story U-8). A tile dragged from the asset shelf (<see cref="AssetDrag"/>)
/// dropped on a slot puts its unit there (<c>units.setSlot</c>); while it is over a slot, the slot
/// lights up as the drop's target.
/// </summary>
public sealed partial class UnitPalettePanel : UserControl
{
    public UnitPalettePanel()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(SlotGrid, true);
        SlotGrid.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        SlotGrid.AddHandler(DragDrop.DragLeaveEvent, (_, _) => Light(null));
        SlotGrid.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>
    /// The name of the units preset a drag of the list carries, which only this editor reads.
    /// </summary>
    public static readonly DataFormat<string> PresetFormat = DataFormat.CreateInProcessFormat<string>("faf-map-editor-units-preset");

    /// <summary>
    /// What a drag of the units preset with this name carries.
    /// </summary>
    public static IDataTransfer PresetDrag(string name)
    {
        DataTransfer transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(PresetFormat, name));
        return transfer;
    }

    private Button? _lit;

    /// <summary>
    /// The slot's button under a point of the drag, and the slot it shows.
    /// </summary>
    private static (Button Button, PaletteSlotItem Slot)? SlotAt(DragEventArgs e) =>
        (e.Source as Avalonia.Visual)?.FindAncestorOfType<Button>(includeSelf: true) is Button button && button.DataContext is PaletteSlotItem slot
            ? (button, slot)
            : null;

    private void Light(Button? button)
    {
        if (_lit == button)
        {
            return;
        }
        _lit?.Classes.Remove("droptarget");
        _lit = button;
        _lit?.Classes.Add("droptarget");
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not UnitPalettesViewModel palettes)
        {
            return;
        }
        (Button Button, PaletteSlotItem Slot)? target = SlotAt(e);
        bool holds = e.DataTransfer.TryGetValue(PresetFormat) is not null
            || AssetDrag.KeyOf(e.DataTransfer) is string key && palettes.FindUnit(key) is not null;
        if (!holds && AssetDrag.KeyOf(e.DataTransfer) is null)
        {
            return;
        }
        e.DragEffects = target is not null && holds ? DragDropEffects.Copy : DragDropEffects.None;
        Light(holds ? target?.Button : null);
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        Light(null);
        if (DataContext is not UnitPalettesViewModel palettes || SlotAt(e) is not (_, PaletteSlotItem slot))
        {
            return;
        }
        if (e.DataTransfer.TryGetValue(PresetFormat) is string preset)
        {
            e.Handled = true;
            e.DragEffects = DragDropEffects.Copy;
            _ = palettes.Run("units.setSlot", new JsonObject { ["slot"] = slot.Index + 1, ["preset"] = preset });
            return;
        }
        if (AssetDrag.KeyOf(e.DataTransfer) is not string key)
        {
            return;
        }
        e.Handled = true;
        if (palettes.FindUnit(key) is not AssetEntry entry)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        _ = palettes.Run("units.setSlot", new JsonObject { ["slot"] = slot.Index + 1, ["unit"] = entry.Key });
    }
}
