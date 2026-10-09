using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("T-5", "Add a layer to the heightmap's stack: sculpting, curves, or an image of World Machine or Gaea")]
[EditorCommand("terrain.addLayer", "Add a height layer...", Story = "T-5")]
public sealed record AddHeightLayer : IRequest;

[Incomplete("T-5", "Bake the height layers into the heightmap")]
[EditorCommand("terrain.bake", "Bake the layers into the heightmap", Story = "T-5")]
public sealed record BakeHeightLayers : IRequest;

[Incomplete("T-3", "Import a heightmap of another size, or export one")]
[EditorCommand("terrain.importHeightmap", "Import a heightmap...", Story = "T-3")]
public sealed record ImportHeightmap : IRequest;

[Incomplete("T-2", "Save the brush as a preset, with its stamp")]
[EditorCommand("terrain.saveBrush", "Save the brush as a preset...", Story = "T-2")]
public sealed record SaveBrushPreset : IRequest;
