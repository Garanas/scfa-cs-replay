using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("R-3", "Generate the path nodes of a layer and their links from walkability")]
[EditorCommand("markers.connectPaths", "Generate path nodes", Story = "R-3")]
public sealed record ConnectPathNodes : IRequest;

[Incomplete("F-10", "Place a set of markers saved as a preset: a spawn with its deposits")]
[EditorCommand("markers.placePreset", "Place a preset...", Story = "F-10")]
public sealed record PlaceMarkerPreset : IRequest;
