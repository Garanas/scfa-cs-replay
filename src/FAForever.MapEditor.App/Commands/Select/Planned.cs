using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("F-11", "Select everything of the active mode")]
[EditorCommand("select.all", "Select all", Gesture = "Ctrl+A", Story = "F-11")]
public sealed record SelectAll : IRequest;

[Incomplete("F-11", "Select nothing")]
[EditorCommand("select.none", "Select none", Story = "F-11")]
public sealed record SelectNone : IRequest;

[Incomplete("F-12", "Mirror every edit: horizontally, vertically, diagonally, or around the middle for 2 to 8 players")]
[EditorCommand("select.symmetry", "Symmetry...", Story = "F-12")]
public sealed record Symmetry : IRequest;
