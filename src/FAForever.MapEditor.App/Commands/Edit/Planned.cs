using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("F-11", "Copy the selected markers, props, decals or units")]
[EditorCommand("edit.copy", "Copy", Gesture = "Ctrl+C", Story = "F-11")]
public sealed record Copy : IRequest;

[Incomplete("F-11", "Paste what was copied, where the mouse is")]
[EditorCommand("edit.paste", "Paste", Gesture = "Ctrl+V", Story = "F-11")]
public sealed record Paste : IRequest;

[Incomplete("F-11", "Duplicate the selection")]
[EditorCommand("edit.duplicate", "Duplicate", Gesture = "Ctrl+D", Story = "F-11")]
public sealed record Duplicate : IRequest;

[Incomplete("F-11", "Delete the selection")]
[EditorCommand("edit.delete", "Delete", Story = "F-11")]
public sealed record Delete : IRequest;

[Incomplete("F-10", "Save the selection as a preset in the workspace")]
[EditorCommand("edit.saveAsPreset", "Save the selection as a preset...", Story = "F-10")]
public sealed record SaveAsPreset : IRequest;
