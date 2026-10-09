using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("W-1", "Turn the water on or off")]
[EditorCommand("water.toggle", "Water on or off", Story = "W-1")]
public sealed record ToggleWater : IRequest;
