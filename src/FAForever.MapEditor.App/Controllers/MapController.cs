using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controllers;

/// <summary>
/// The Map menu and mode: the map's information, armies, areas and size.
/// </summary>
public sealed class MapController(MainViewModel model)
{
    [EditorCommand("map.mode", "Map mode", Menu = "Map", Gesture = "F1", Story = "M-1")]
    public void Mode() => model.SelectMode("map");

    [Incomplete("M-1", "Edit name, description, author, version, type, preview, no-rush radius, adaptive, reclaim")]
    [EditorCommand("map.information", "Information...", Menu = "Map", Story = "M-1", SeparatorBefore = true)]
    public void Information()
    {
    }

    [Incomplete("M-4", "Add, remove and rename armies; playable or extra; factions; alliances; teams")]
    [EditorCommand("map.armies", "Armies and teams...", Menu = "Map", Story = "M-4")]
    public void Armies()
    {
    }

    [Incomplete("M-5", "Add an area and draw it in a view")]
    [EditorCommand("map.addArea", "Add an area", Menu = "Map", Story = "M-5")]
    public void AddArea()
    {
    }

    [Incomplete("M-3", "Grow or crop the map; set the playable area")]
    [EditorCommand("map.resize", "Size and playable area...", Menu = "Map", Story = "M-3", SeparatorBefore = true)]
    public void Resize()
    {
    }
}
