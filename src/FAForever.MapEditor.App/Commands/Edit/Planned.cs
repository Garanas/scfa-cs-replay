using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("F-10", "Save the selection as a preset in the workspace")]
[EditorCommand("edit.saveAsPreset", "Save the selection as a preset...", Story = "F-10")]
public sealed record SaveAsPreset : IRequest;
