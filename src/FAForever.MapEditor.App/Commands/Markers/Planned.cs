using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("R-3", "Connect the path nodes of a layer, or generate them from walkability")]
[EditorCommand("markers.connectPaths", "Connect path nodes", Story = "R-3")]
public sealed record ConnectPathNodes : IRequest;

[Incomplete("R-4", "Make a chain of markers")]
[EditorCommand("markers.addChain", "Add a chain", Story = "R-4")]
public sealed record AddMarkerChain : IRequest;

[Incomplete("R-5", "Which markers exist for which number of players and options")]
[EditorCommand("markers.adaptive", "Adaptive map...", Story = "R-5")]
public sealed record EditAdaptiveMap : IRequest;

[Incomplete("F-10", "Place a set of markers saved as a preset: a spawn with its deposits")]
[EditorCommand("markers.placePreset", "Place a preset...", Story = "F-10")]
public sealed record PlaceMarkerPreset : IRequest;
