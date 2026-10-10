using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("F-15", "The workspace's Lua plugins: on or off, their commands, tools and panels")]
[EditorCommand("plugins.manage", "Plugins...", Story = "F-15")]
public sealed record ManagePlugins : IRequest;
