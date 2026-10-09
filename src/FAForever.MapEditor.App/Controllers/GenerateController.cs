using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controllers;

/// <summary>
/// The Generate menu: what is generated runs only from here (user story F-7), never by itself.
/// </summary>
public sealed class GenerateController(MainViewModel model)
{
    public MainViewModel Model => model;

    [Incomplete("T-7", "Generate walkability from the height layers and compare it with the last time")]
    [EditorCommand("generate.walkability", "Walkability", Menu = "Generate", Story = "T-7")]
    public void Walkability()
    {
    }

    [Incomplete("P-4", "Generate the props of every rule again; props placed by hand stay")]
    [EditorCommand("generate.props", "Props from rules", Menu = "Generate", Story = "P-4")]
    public void Props()
    {
    }

    [Incomplete("K-2", "Generate masks from slope, height, distance to water or other masks")]
    [EditorCommand("generate.masks", "Masks from rules", Menu = "Generate", Story = "K-2")]
    public void Masks()
    {
    }

    [Incomplete("S-4", "Generate the normal map and the preview from the terrain")]
    [EditorCommand("generate.normalMap", "Normal map and preview", Menu = "Generate", Story = "S-4", SeparatorBefore = true)]
    public void NormalMap()
    {
    }

    [Incomplete("F-7", "Generate everything whose inputs changed")]
    [EditorCommand("generate.stale", "Everything out of date", Menu = "Generate", Story = "F-7", SeparatorBefore = true)]
    public void Stale()
    {
    }
}

/// <summary>
/// Assets: what is loaded, pickers and presets.
/// </summary>
public sealed class AssetsController(MainViewModel model)
{
    [EditorCommand("assets.show", "Show the assets", Menu = "Assets", Story = "F-8")]
    public void Show() => model.DockIndex = 2;

    [Incomplete("F-8", "Read an asset again, or all of them")]
    [EditorCommand("assets.reload", "Read the assets again", Menu = "Assets", Story = "F-8")]
    public void Reload()
    {
    }

    [Incomplete("F-9", "Choose a texture, prop or decal from a picker with thumbnails and filters")]
    [EditorCommand("assets.pick", "Find an asset...", Menu = "Assets", Gesture = "Ctrl+P", Story = "F-9")]
    public void Pick()
    {
    }
}

/// <summary>
/// The Plugins menu.
/// </summary>
public sealed class PluginsController(MainViewModel model)
{
    public MainViewModel Model => model;

    [Incomplete("F-15", "The workspace's Lua plugins: on or off, their commands, tools and panels")]
    [EditorCommand("plugins.manage", "Plugins...", Menu = "Plugins", Story = "F-15")]
    public void Manage()
    {
    }

    [Incomplete("F-14", "Run every check again, of the editor and the plugins")]
    [EditorCommand("plugins.check", "Check the map", Menu = "Plugins", Story = "F-14")]
    public void Check()
    {
    }
}
