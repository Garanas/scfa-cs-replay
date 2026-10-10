using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Plugins;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using Lua;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The plugins' tools (user story F-15): a plugin's <c>plugin.tool{...}</c> joins a mode's tools,
/// chosen like any (<c>tool.plugin.&lt;folder&gt;.&lt;id&gt;</c>, the Plugins menu), and the left
/// mouse in the views calls its handlers. A stroke, from press to release, is one step of undo:
/// its edits go into the stroke the views show while the mouse is down. Each call has the time
/// limit of a check (<see cref="CallLimit"/>) and may not wait; a call that fails throws the whole
/// stroke away.
/// </summary>
public sealed partial class PluginsViewModel
{
    /// <summary>
    /// Puts a plugin's tool into its mode and its command into the registry.
    /// </summary>
    private void AddTool(LuaPlugin plugin, PluginToolSpec spec)
    {
        IReadOnlyList<Setting> settings = [.. spec.Fields
            .Select(key => plugin.Fields.First(field => field.Key == key))
            .Select(field => FieldSetting(plugin, field))];
        PluginTool tool = new PluginTool(this, plugin, spec, settings);
        tool.Refresh(_model.Map?.Document);
        ToolDefinition definition = new ToolDefinition(spec.Id, spec.Title, "F-15");
        if (_model.Commands.Find(definition.CommandId) is not null)
        {
            plugin.AddError($"There is a command {definition.CommandId} already");
            return;
        }
        try
        {
            _model.AddPluginTool(spec.Mode, definition, tool);
        }
        catch (ArgumentException exception)
        {
            plugin.AddError(exception.Message);
            return;
        }
        _model.Commands.Register(new EditorCommand
        {
            Id = definition.CommandId,
            Title = spec.Title,
            Story = "F-15",
            Owner = plugin.Id,
            Menu = Menus.PluginsMenu,
            MakeRequest = _ => new ChooseTool(spec.Id),
        });
        plugin.MadeTools.Add(tool);
    }

    /// <summary>
    /// Takes a plugin's tool out of its mode and the registry; a stroke it holds is thrown away.
    /// </summary>
    private void RemoveTool(LuaPlugin plugin, PluginTool tool)
    {
        if (tool.Stroke is EditTransaction stroke)
        {
            tool.Stroke = null;
            tool.Run = null;
            _model.EndStroke(stroke, keep: false);
        }
        _model.RemovePluginTool(tool.Spec.Id);
        string command = $"tool.{tool.Spec.Id}";
        if (_model.Commands.Find(command) is { Owner: string owner } && owner == plugin.Id)
        {
            _model.Commands.Unregister(command);
        }
    }

    /// <summary>
    /// The mouse pressed with a plugin's tool: the stroke starts and <c>press</c> is called.
    /// </summary>
    internal void ToolPress(PluginTool tool, float x, float z, ToolInput input)
    {
        if (tool.Stroke is not null)
        {
            ToolRelease(tool);
        }
        LuaPlugin plugin = tool.Plugin;
        if (plugin.State is null || !plugin.IsLoaded)
        {
            _model.Status = $"The plugin {plugin.Manifest.Name} is not loaded";
            return;
        }
        if (plugin.IsRunning)
        {
            _model.Status = $"The plugin {plugin.Manifest.Name} is running";
            return;
        }
        if (_model.Map is not EditedMap map)
        {
            return;
        }
        string title = $"{plugin.Manifest.Name}: {tool.Title}";
        tool.Before = map.Session.Document;
        tool.Stroke = _model.StartStroke(title);
        tool.Run = new PluginRun(tool.Stroke, title);
        tool.Last = (x, z);
        Call(tool, tool.Spec.Press, x, z, input);
    }

    /// <summary>
    /// The mouse dragged with a plugin's tool: <c>drag</c> is called at the new place.
    /// </summary>
    internal void ToolDrag(PluginTool tool, float x, float z, ToolInput input)
    {
        if (tool.Run is null)
        {
            return;
        }
        tool.Last = (x, z);
        Call(tool, tool.Spec.Drag, x, z, input);
    }

    /// <summary>
    /// The mouse let go with a plugin's tool: <c>release</c> is called, and the stroke becomes one
    /// step of undo (none when nothing changed).
    /// </summary>
    internal void ToolRelease(PluginTool tool)
    {
        if (tool.Stroke is null)
        {
            return;
        }
        Call(tool, tool.Spec.Release, tool.Last.X, tool.Last.Z, ToolInput.Default);
        if (tool.Stroke is not EditTransaction stroke)
        {
            // release failed: the stroke is gone already
            return;
        }
        tool.Stroke = null;
        tool.Run = null;
        _model.EndStroke(stroke);
        if (_model.Map is EditedMap map)
        {
            _model.Selection.Prune(map.Document);
            bool changed = !ReferenceEquals(map.Session.Document, tool.Before);
            Log.LogInformation("The plugin {Plugin}'s tool {Tool} ended a stroke{Changed}", tool.Plugin.Id, tool.Spec.Id, changed ? " that changed the map" : "");
        }
        tool.Before = null;
    }

    /// <summary>
    /// Calls a handler of a plugin's tool with the place and the keys held, at once: the stroke's
    /// run lets it edit; a failure throws the stroke away and is the plugin's error.
    /// </summary>
    private void Call(PluginTool tool, LuaFunction? handler, float x, float z, ToolInput input)
    {
        if (handler is null || tool.Run is not PluginRun run)
        {
            return;
        }
        LuaPlugin plugin = tool.Plugin;
        using Performance.Span measure = Performance.Measure("plugins.tool");
        using PluginClock clock = new PluginClock(CallLimit);
        LuaTable keys = new LuaTable(0, 4);
        keys["shift"] = input.Modifiers.HasFlag(ToolModifiers.Shift);
        keys["control"] = input.Modifiers.HasFlag(ToolModifiers.Control);
        keys["alt"] = input.Modifiers.HasFlag(ToolModifiers.Alt);
        if (input.Picked is ObjectRef picked)
        {
            keys["picked"] = picked.ToString();
        }
        if (CallNow(plugin, new PluginContext($"The tool {tool.Title}", () => run.Document, clock, run), $"the tool {tool.Title}",
            () => plugin.State!.CallAsync(handler, [x, z, keys], clock.Token), out _))
        {
            _model.ShowStroke();
            return;
        }
        // the map shows as it was at once; what the mouse does until it is let go does nothing
        if (tool.Stroke is EditTransaction stroke)
        {
            tool.Stroke = null;
            tool.Run = null;
            _model.EndStroke(stroke, keep: false);
        }
        _model.Status = $"{plugin.Errors[^1]}; nothing was changed";
    }
}
