using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("D-2", "Group decals, show and hide groups")]
[EditorCommand("decals.addGroup", "Add a decal group", Story = "D-2")]
public sealed record AddDecalGroup : IRequest;
