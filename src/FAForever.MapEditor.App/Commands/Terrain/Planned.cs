using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("T-2", "Save the brush as a preset, with its stamp")]
[EditorCommand("terrain.saveBrush", "Save the brush as a preset...", Story = "T-2")]
public sealed record SaveBrushPreset : IRequest;
