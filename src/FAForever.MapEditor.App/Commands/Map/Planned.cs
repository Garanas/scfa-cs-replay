using MediatR;

namespace FAForever.MapEditor.App.Commands;


[Incomplete("M-3", "Grow or crop the map; set the playable area")]
[EditorCommand("map.resize", "Size and playable area...", Story = "M-3")]
public sealed record ResizeMap : IRequest;
