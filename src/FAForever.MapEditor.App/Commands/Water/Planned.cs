using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("W-1", "Place a wave generator with the mouse")]
[EditorCommand("water.placeWaveGenerator", "Place a wave generator...", Story = "W-1")]
public sealed record PlaceWaveGenerator : IRequest;
