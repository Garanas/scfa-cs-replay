using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("F-15", "The workspace's Lua plugins: on or off, their commands, tools and panels")]
[EditorCommand("plugins.manage", "Plugins...", Story = "F-15")]
public sealed record ManagePlugins : IRequest;

[Incomplete("F-14", "Run every check again, of the editor and the plugins")]
[EditorCommand("plugins.check", "Check the map", Story = "F-14")]
public sealed record CheckMap : IRequest;
