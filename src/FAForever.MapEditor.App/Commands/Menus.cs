using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// A menu of the menu bar, or a submenu.
/// </summary>
/// <param name="Items">In order: a command's id, <see cref="Menus.Separator"/>, or a submenu.</param>
public sealed record MenuLayout(string Title, IReadOnlyList<object> Items);

/// <summary>
/// Where the commands show: the menu bar, each menu with its commands in order. A request says
/// what a command is; this says where it is. A command in no menu is for automation and plugins.
/// </summary>
public static class Menus
{
    public const string Separator = "-";

    public static IReadOnlyList<MenuLayout> Bar { get; } =
    [
        new MenuLayout("File",
        [
            "file.new", "file.open", "file.import", Separator,
            "file.save", "file.saveAs", "file.commit", Separator,
            "file.export", "file.renderPreview", "file.upload", Separator,
            "file.openScript",
        ]),
        new MenuLayout("Edit",
        [
            "edit.undo", "edit.redo", Separator,
            "edit.copy", "edit.paste", "edit.duplicate", "edit.delete", Separator,
            "edit.saveAsPreset",
        ]),
        new MenuLayout("Select", ["select.all", "select.none", Separator, "select.symmetry"]),
        new MenuLayout("View",
        [
            "view.top", "view.3d", "view.sideBySide", "view.fit", "camera.home", Separator,
            "view.palette", Separator,
            "view.problems", "view.history", "view.assets", "view.console", Separator,
            .. EditorOverlays.All.Select(overlay => overlay.CommandId),
        ]),
        new MenuLayout("Map",
        [
            "map.mode", Separator,
            "map.information", "map.armies", "map.addArea", Separator,
            "map.resize", Separator,
            .. ToolsOf("map"),
        ]),
        new MenuLayout("Modes",
        [
            Mode("terrain", "terrain.addLayer", "terrain.bake", "terrain.importHeightmap", "terrain.saveBrush"),
            Mode("paint", "paint.chooseTexture", "paint.addStratum", "paint.addMask", "paint.shader"),
            Mode("water", "water.toggle"),
            Mode("lighting", "lighting.preset"),
            Mode("markers", "markers.connectPaths", "markers.addChain", "markers.adaptive", "markers.placePreset"),
            Mode("props", "props.addPalette", "props.addRule", "props.reclaim"),
            Mode("decals", "decals.addGroup"),
            Mode("units", "units.pick", "units.addGroup", "units.placePreset", "units.asWrecks"),
            new MenuLayout("Assets", ["assets.browse", "assets.pick", "assets.reload", Separator, "assets.brushesFolder"]),
        ]),
        new MenuLayout("Generate",
        [
            "generate.walkability", "generate.props", "generate.masks", Separator,
            "generate.normalMap", Separator,
            "generate.stale",
        ]),
        new MenuLayout("Plugins", ["plugins.manage", "plugins.check"]),
        new MenuLayout("Help",
        [
            "help.workspaceFolder", Separator,
            "help.diagnostics", "help.diagnosticsFolder", Separator,
            "help.reportProblem",
        ]),
    ];

    /// <summary>
    /// A mode's submenu: the command that chooses it, its own commands, then its tools.
    /// </summary>
    private static MenuLayout Mode(string id, params string[] commands)
    {
        ModeDefinition mode = EditorModes.All.Single(m => m.Id == id);
        return new MenuLayout(mode.Title, [mode.CommandId, .. commands, Separator, .. ToolsOf(id)]);
    }

    private static IEnumerable<string> ToolsOf(string mode) => EditorModes.All.Single(m => m.Id == mode).Tools.Select(tool => tool.CommandId);
}
