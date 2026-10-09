using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controllers;

/// <summary>
/// Markers, path networks, chains and adaptive maps.
/// </summary>
public sealed class MarkersController(MainViewModel model)
{
    [EditorCommand("markers.mode", "Markers mode", Menu = "Markers", Gesture = "F6", Story = "R-1")]
    public void Mode() => model.SelectMode("markers");

    [Incomplete("R-3", "Connect the path nodes of a layer, or generate them from walkability")]
    [EditorCommand("markers.connectPaths", "Connect path nodes", Menu = "Markers", Story = "R-3")]
    public void ConnectPaths()
    {
    }

    [Incomplete("R-4", "Make a chain of markers")]
    [EditorCommand("markers.addChain", "Add a chain", Menu = "Markers", Story = "R-4")]
    public void AddChain()
    {
    }

    [Incomplete("R-5", "Which markers exist for which number of players and options")]
    [EditorCommand("markers.adaptive", "Adaptive map...", Menu = "Markers", Story = "R-5")]
    public void Adaptive()
    {
    }

    [Incomplete("F-10", "Place a set of markers saved as a preset: a spawn with its deposits")]
    [EditorCommand("markers.placePreset", "Place a preset...", Menu = "Markers", Story = "F-10")]
    public void PlacePreset()
    {
    }
}

/// <summary>
/// Props: placing, painting, rules and reclaim.
/// </summary>
public sealed class PropsController(MainViewModel model)
{
    [EditorCommand("props.mode", "Props mode", Menu = "Props", Gesture = "F7", Story = "P-1")]
    public void Mode() => model.SelectMode("props");

    [Incomplete("P-2", "Make a palette of props with weights")]
    [EditorCommand("props.addPalette", "Add a palette", Menu = "Props", Story = "P-2")]
    public void AddPalette()
    {
    }

    [Incomplete("P-4", "Add a rule that places props from layers")]
    [EditorCommand("props.addRule", "Add a rule", Menu = "Props", Story = "P-4")]
    public void AddRule()
    {
    }

    [Incomplete("P-5", "The mass and energy of the props, for the map and per area")]
    [EditorCommand("props.reclaim", "Reclaim", Menu = "Props", Story = "P-5")]
    public void Reclaim()
    {
    }
}

/// <summary>
/// Decals.
/// </summary>
public sealed class DecalsController(MainViewModel model)
{
    [EditorCommand("decals.mode", "Decals mode", Menu = "Decals", Gesture = "F8", Story = "D-1")]
    public void Mode() => model.SelectMode("decals");

    [Incomplete("D-2", "Group decals, show and hide groups")]
    [EditorCommand("decals.addGroup", "Add a decal group", Menu = "Decals", Story = "D-2")]
    public void AddGroup()
    {
    }
}

/// <summary>
/// Units, their groups and presets.
/// </summary>
public sealed class UnitsController(MainViewModel model)
{
    [EditorCommand("units.mode", "Units mode", Menu = "Units", Gesture = "F9", Story = "U-2")]
    public void Mode() => model.SelectMode("units");

    [Incomplete("U-1", "Choose a unit: search, filter by faction, tech and kind")]
    [EditorCommand("units.pick", "Choose a unit...", Menu = "Units", Story = "U-1")]
    public void Pick()
    {
    }

    [Incomplete("U-3", "Add a group to the selected army or group")]
    [EditorCommand("units.addGroup", "Add a group", Menu = "Units", Story = "U-3")]
    public void AddGroup()
    {
    }

    [Incomplete("U-4", "Place a group saved as a preset, such as a civilian base")]
    [EditorCommand("units.placePreset", "Place a preset...", Menu = "Units", Story = "U-4")]
    public void PlacePreset()
    {
    }

    [Incomplete("U-5", "Place units as wrecks")]
    [EditorCommand("units.asWrecks", "Place as wrecks", Menu = "Units", Story = "U-5")]
    public void AsWrecks()
    {
    }
}
