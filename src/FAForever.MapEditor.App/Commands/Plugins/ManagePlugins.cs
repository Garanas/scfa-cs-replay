using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows the Plugins page (user story F-15): the workspace's Lua plugins, each on or off, what it
/// registered, its errors and its panel.
/// </summary>
[EditorCommand("plugins.manage", "Plugins...", Story = "F-15")]
public sealed record ManagePlugins : IRequest;

public sealed class ManagePluginsHandler(PluginsViewModel plugins) : IRequestHandler<ManagePlugins>
{
    public Task Handle(ManagePlugins request, CancellationToken cancellationToken)
    {
        plugins.Show();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Reads every plugin of the workspace again (user story F-15); a plugin whose files change is
/// read again by itself.
/// </summary>
[EditorCommand("plugins.reload", "Reload the plugins", Story = "F-15", Needs = Needs.Workspace | Needs.NoStroke)]
public sealed record ReloadPlugins : IRequest<string>;

public sealed class ReloadPluginsHandler(MainViewModel model, PluginsViewModel plugins) : IRequestHandler<ReloadPlugins, string>
{
    public Task<string> Handle(ReloadPlugins request, CancellationToken cancellationToken)
    {
        if (plugins.Items.Any(plugin => plugin.IsRunning))
        {
            throw new InvalidOperationException("A plugin is running: reload when it is done");
        }
        plugins.ReloadAll();
        model.Status = $"Reloaded the plugins: {plugins.Summary}";
        return Task.FromResult(plugins.Summary);
    }
}

/// <summary>
/// Shows the workspace's plugins folder in the system's file manager (user story F-15).
/// </summary>
[EditorCommand("plugins.showFolder", "Show the plugins folder", Story = "F-15", Needs = Needs.Workspace)]
public sealed record ShowPluginsFolder : IRequest;

public sealed class ShowPluginsFolderHandler(MainViewModel model, PluginsViewModel plugins) : IRequestHandler<ShowPluginsFolder>
{
    public Task Handle(ShowPluginsFolder request, CancellationToken cancellationToken)
    {
        if (plugins.Folder is string folder)
        {
            Directory.CreateDirectory(folder);
            model.ShowFolder(folder);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Turns a plugin on or off (user story F-15), as its check box on the Plugins page does; the user's
/// settings remember it. Off, its commands and checks are gone.
/// </summary>
/// <param name="Plugin">The plugin's folder name.</param>
/// <param name="On">Left out, it turns over.</param>
[EditorCommand("plugins.enable", "Turn a plugin on or off", Story = "F-15", Needs = Needs.NoStroke)]
public sealed record EnablePlugin(string Plugin, bool? On = null) : IRequest;

public sealed class EnablePluginHandler(MainViewModel model, PluginsViewModel plugins) : IRequestHandler<EnablePlugin>
{
    public Task Handle(EnablePlugin request, CancellationToken cancellationToken)
    {
        bool on = request.On ?? !(plugins.Find(request.Plugin)?.IsOn ?? false);
        plugins.SetOn(request.Plugin, on);
        model.Status = $"The plugin {request.Plugin} is {(on ? "on" : "off")}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sets a field of a plugin's panel (user story F-15), as the field on the Plugins page does; the
/// user's settings remember it.
/// </summary>
/// <param name="Field">The field's key.</param>
/// <param name="Value">A number, text, true or false, or one of a choice's texts.</param>
[EditorCommand("plugins.setField", "Set a field of a plugin", Story = "F-15")]
public sealed record SetPluginField(string Plugin, string Field, JsonNode? Value) : IRequest;

public sealed class SetPluginFieldHandler(PluginsViewModel plugins) : IRequestHandler<SetPluginField>
{
    public Task Handle(SetPluginField request, CancellationToken cancellationToken)
    {
        plugins.SetValue(request.Plugin, request.Field, request.Value);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The request of every command a plugin registered (<c>plugin.&lt;folder&gt;.&lt;id&gt;</c>): runs
/// its Lua function with the arguments as a table, one step of undo (user story F-15). It has no
/// id of its own; the registry makes it for each plugin command.
/// </summary>
public sealed record RunPluginCommand(string Id, JsonObject Arguments) : IRequest<JsonNode?>;

public sealed class RunPluginCommandHandler(PluginsViewModel plugins) : IRequestHandler<RunPluginCommand, JsonNode?>
{
    public Task<JsonNode?> Handle(RunPluginCommand request, CancellationToken cancellationToken) => plugins.RunAsync(request.Id, request.Arguments);
}
