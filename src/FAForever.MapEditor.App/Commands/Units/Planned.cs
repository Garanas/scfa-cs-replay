using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("U-1", "Choose a unit: search, filter by faction, tech and kind")]
[EditorCommand("units.pick", "Choose a unit...", Story = "U-1")]
public sealed record PickUnit : IRequest;

[Incomplete("U-3", "Add a group to the selected army or group")]
[EditorCommand("units.addGroup", "Add a group", Story = "U-3")]
public sealed record AddUnitGroup : IRequest;

[Incomplete("U-4", "Place a group saved as a preset, such as a civilian base")]
[EditorCommand("units.placePreset", "Place a preset...", Story = "U-4")]
public sealed record PlaceUnitPreset : IRequest;

[Incomplete("U-5", "Place units as wrecks")]
[EditorCommand("units.asWrecks", "Place as wrecks", Story = "U-5")]
public sealed record PlaceAsWrecks : IRequest;
