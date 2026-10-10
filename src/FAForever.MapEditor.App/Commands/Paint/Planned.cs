using MediatR;

namespace FAForever.MapEditor.App.Commands;


[Incomplete("S-4", "Choose the terrain shader")]
[EditorCommand("paint.shader", "Terrain shader...", Story = "S-4")]
public sealed record ChooseTerrainShader : IRequest;
