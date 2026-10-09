using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("F-9", "Choose a texture, prop or decal from a picker with thumbnails and filters, for a slot or a tool")]
[EditorCommand("assets.pick", "Find an asset...", Gesture = "Ctrl+P", Story = "F-9")]
public sealed record PickAsset : IRequest;
