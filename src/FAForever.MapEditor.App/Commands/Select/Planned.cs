using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("F-12", "Mirror every edit: horizontally, vertically, diagonally, or around the middle for 2 to 8 players")]
[EditorCommand("select.symmetry", "Symmetry...", Story = "F-12")]
public sealed record Symmetry : IRequest;
