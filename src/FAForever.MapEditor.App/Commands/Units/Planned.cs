using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("U-5", "Place units as wrecks")]
[EditorCommand("units.asWrecks", "Place as wrecks", Story = "U-5")]
public sealed record PlaceAsWrecks : IRequest;
