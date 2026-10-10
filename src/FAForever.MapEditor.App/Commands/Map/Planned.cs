using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("M-1", "Edit name, description, author, version, type, preview, no-rush radius, adaptive, reclaim")]
[EditorCommand("map.information", "Information...", Story = "M-1")]
public sealed record EditMapInformation : IRequest;

[Incomplete("M-5", "Add an area and draw it in a view")]
[EditorCommand("map.addArea", "Add an area", Story = "M-5")]
public sealed record AddArea : IRequest;

[Incomplete("M-3", "Grow or crop the map; set the playable area")]
[EditorCommand("map.resize", "Size and playable area...", Story = "M-3")]
public sealed record ResizeMap : IRequest;
