using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("F-8", "Read an asset again, or all of them")]
[EditorCommand("assets.reload", "Read the assets again", Story = "F-8")]
public sealed record ReloadAssets : IRequest;

[Incomplete("F-9", "Choose a texture, prop or decal from a picker with thumbnails and filters")]
[EditorCommand("assets.pick", "Find an asset...", Gesture = "Ctrl+P", Story = "F-9")]
public sealed record PickAsset : IRequest;
