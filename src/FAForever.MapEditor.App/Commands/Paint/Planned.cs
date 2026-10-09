using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("S-2", "Choose a layer's albedo and normal texture from the picker")]
[EditorCommand("paint.chooseTexture", "Choose the layer's texture...", Story = "S-2")]
public sealed record ChooseLayerTexture : IRequest;

[Incomplete("S-2", "Add a stratum, or put the strata in another order")]
[EditorCommand("paint.addStratum", "Add a stratum", Story = "S-2")]
public sealed record AddStratum : IRequest;

[Incomplete("K-1", "Add a named mask for World Machine, Gaea or the rules")]
[EditorCommand("paint.addMask", "Add a mask", Story = "K-1")]
public sealed record AddMask : IRequest;

[Incomplete("S-4", "Choose the terrain shader")]
[EditorCommand("paint.shader", "Terrain shader...", Story = "S-4")]
public sealed record ChooseTerrainShader : IRequest;
