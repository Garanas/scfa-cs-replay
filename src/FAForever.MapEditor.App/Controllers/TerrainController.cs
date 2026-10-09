using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controllers;

/// <summary>
/// The terrain: height layers, curves, brushes, terrain types and walkability.
/// </summary>
public sealed class TerrainController(MainViewModel model)
{
    [EditorCommand("terrain.mode", "Terrain mode", Menu = "Terrain", Gesture = "F2", Story = "T-1")]
    public void Mode() => model.SelectMode("terrain");

    [Incomplete("T-5", "Add a layer to the heightmap's stack: sculpting, curves, or an image of World Machine or Gaea")]
    [EditorCommand("terrain.addLayer", "Add a height layer...", Menu = "Terrain", Story = "T-5")]
    public void AddLayer()
    {
    }

    [Incomplete("T-5", "Bake the height layers into the heightmap")]
    [EditorCommand("terrain.bake", "Bake the layers into the heightmap", Menu = "Terrain", Story = "T-5")]
    public void Bake()
    {
    }

    [Incomplete("T-3", "Import a heightmap of another size, or export one")]
    [EditorCommand("terrain.importHeightmap", "Import a heightmap...", Menu = "Terrain", Story = "T-3")]
    public void ImportHeightmap()
    {
    }

    [Incomplete("T-2", "Save the brush as a preset, with its stamp")]
    [EditorCommand("terrain.saveBrush", "Save the brush as a preset...", Menu = "Terrain", Story = "T-2")]
    public void SaveBrush()
    {
    }
}

/// <summary>
/// Painting: the strata and the masks for tools.
/// </summary>
public sealed class PaintController(MainViewModel model)
{
    [EditorCommand("paint.mode", "Paint mode", Menu = "Paint", Gesture = "F3", Story = "S-1")]
    public void Mode() => model.SelectMode("paint");

    [Incomplete("S-2", "Choose a layer's albedo and normal texture from the picker")]
    [EditorCommand("paint.chooseTexture", "Choose the layer's texture...", Menu = "Paint", Story = "S-2")]
    public void ChooseTexture()
    {
    }

    [Incomplete("S-2", "Add a stratum, or put the strata in another order")]
    [EditorCommand("paint.addStratum", "Add a stratum", Menu = "Paint", Story = "S-2")]
    public void AddStratum()
    {
    }

    [Incomplete("K-1", "Add a named mask for World Machine, Gaea or the rules")]
    [EditorCommand("paint.addMask", "Add a mask", Menu = "Paint", Story = "K-1")]
    public void AddMask()
    {
    }

    [Incomplete("S-4", "Choose the terrain shader")]
    [EditorCommand("paint.shader", "Terrain shader...", Menu = "Paint", Story = "S-4")]
    public void Shader()
    {
    }
}

/// <summary>
/// Water and lighting.
/// </summary>
public sealed class AtmosphereController(MainViewModel model)
{
    [EditorCommand("water.mode", "Water mode", Menu = "Water", Gesture = "F4", Story = "W-1")]
    public void WaterMode() => model.SelectMode("water");

    [Incomplete("W-1", "Turn the water on or off")]
    [EditorCommand("water.toggle", "Water on or off", Menu = "Water", Story = "W-1")]
    public void ToggleWater()
    {
    }

    [EditorCommand("lighting.mode", "Lighting mode", Menu = "Lighting", Gesture = "F5", Story = "L-1")]
    public void LightingMode() => model.SelectMode("lighting");

    [Incomplete("L-2", "Apply the lighting of a biome")]
    [EditorCommand("lighting.preset", "Apply a lighting preset...", Menu = "Lighting", Story = "L-2")]
    public void ApplyPreset()
    {
    }
}
