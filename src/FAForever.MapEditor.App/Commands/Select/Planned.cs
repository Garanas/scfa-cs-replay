using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("F-11", "Select everything of the active mode")]
[EditorCommand("select.all", "Select all", Gesture = "Ctrl+A", Story = "F-11")]
public sealed record SelectAll : IRequest;

[Incomplete("F-11", "Select nothing")]
[EditorCommand("select.none", "Select none", Story = "F-11")]
public sealed record SelectNone : IRequest;
