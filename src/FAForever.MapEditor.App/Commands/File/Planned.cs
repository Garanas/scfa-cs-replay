using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("M-2", "Make a map from scratch: size, starting height, water, biome or template")]
[EditorCommand("file.new", "New map...", Gesture = "Ctrl+N", Story = "M-2")]
public sealed record NewMap : IRequest;

[Incomplete("M-8", "Render the map's preview image")]
[EditorCommand("file.renderPreview", "Render the preview", Story = "M-8")]
public sealed record RenderPreview : IRequest;

[Incomplete("M-8", "Upload the exported map to the FAF vault")]
[EditorCommand("file.upload", "Upload to the vault...", Story = "M-8")]
public sealed record Upload : IRequest;

[Incomplete("M-6", "Open the map's script in the user's editor")]
[EditorCommand("file.openScript", "Open the script", Story = "M-6")]
public sealed record OpenScript : IRequest;
