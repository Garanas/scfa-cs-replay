using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("K-1", "Add a named mask for World Machine, Gaea or the rules")]
[EditorCommand("paint.addMask", "Add a mask", Story = "K-1")]
public sealed record AddMask : IRequest;

[Incomplete("S-4", "Choose the terrain shader")]
[EditorCommand("paint.shader", "Terrain shader...", Story = "S-4")]
public sealed record ChooseTerrainShader : IRequest;
