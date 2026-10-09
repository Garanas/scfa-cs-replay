using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("P-2", "Make a palette of props with weights")]
[EditorCommand("props.addPalette", "Add a palette", Story = "P-2")]
public sealed record AddPropPalette : IRequest;

[Incomplete("P-4", "Add a rule that places props from layers")]
[EditorCommand("props.addRule", "Add a rule", Story = "P-4")]
public sealed record AddPropRule : IRequest;

[Incomplete("P-5", "The mass and energy of the props, for the map and per area")]
[EditorCommand("props.reclaim", "Reclaim", Story = "P-5")]
public sealed record ShowReclaim : IRequest;
