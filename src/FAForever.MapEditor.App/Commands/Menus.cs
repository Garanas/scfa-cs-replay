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

    /// <summary>
    /// The menu of the plugins' commands (user story F-15), after the editor's own.
    /// </summary>
    public const string PluginsMenu = "Plugins";

    public static IReadOnlyList<MenuLayout> Bar { get; } =
    [
        new MenuLayout("File",
        [
            "file.new", "file.open", "file.import", Separator,
            "file.save", "file.saveAs", "file.saveAsTemplate", "file.commit", "file.push", "file.pull", "file.history", Separator,
            "file.export", "file.upload", Separator,
            "file.openScript", Separator,
            "file.workspaceSettings",
        ]),
        new MenuLayout("Edit",
        [
            "edit.undo", "edit.redo", Separator,
            "edit.copy", "edit.paste", "edit.duplicate", "edit.delete", "edit.rotate", "edit.scale", "edit.mirror", Separator,
            "edit.saveAsPreset", "edit.saveSelectionPreset", "edit.placePreset",
        ]),
        new MenuLayout("Select", ["select.all", "select.none", "select.similar", "select.invert", "select.insideArea", "select.isolate", Separator, "select.symmetry", "select.mirrorEdits", "select.symmetryPartners", "select.handles"]),
        new MenuLayout("View",
        [
            "view.top", "view.3d", "view.sideBySide", "view.fit", "camera.home", "camera.focusSelection", "view.frame", Separator,
            "view.palette", Separator,
            "view.problems", "view.history", "view.assets", "view.console", Separator,
            "view.modePanel", "view.inspector", "view.dock", "view.toolbar", "view.resetLayout", Separator,
            .. EditorOverlays.All.Select(overlay => overlay.CommandId),
            "view.gridSpacing",
        ]),
        new MenuLayout("Map",
        [
            "map.mode", Separator,
            "map.information", "map.armies", "map.addArea", Separator,
            "map.addArmy", Separator,
            "map.resize", "map.addFiles", Separator,
            .. ToolsOf("map"),
        ]),
        new MenuLayout("Modes",
        [
            Mode("terrain", "terrain.addLayer", "terrain.bake", "terrain.importHeightmap", "terrain.exportHeightmap", "terrain.saveBrush", Separator, "terrain.switchHeights", "terrain.writeDraft", "terrain.useAsFinalBase"),
            Mode("paint", "paint.chooseTexture", "paint.addStratum", "paint.shader"),
            Mode("masks", "paint.addMask", "paint.renameMask", "paint.duplicateMask", "paint.deleteMask", "layers.add", "paint.maskRules"),
            Mode("water", "water.toggle", "water.addWaveGenerator", "water.placeWaveGenerator"),
            Mode("lighting", "lighting.preset", "lighting.chooseCubemap", "lighting.importSkybox"),
            Mode("markers", "markers.addChain", "markers.adaptive", "markers.placePreset"),
            Mode("props", "props.choose", "props.addToPalette", "props.clearPalette", "props.restorePalette", "props.followTerrain", Separator, "props.alignToTerrain", "props.upright", "props.dropToGround", "props.randomize", "props.replace", "props.randomVariant", Separator, "props.reclaim", "props.addRule", "props.keepFromRules"),
            Mode("decals", "decals.choose", "decals.addGroup", Separator, "decals.forward", "decals.backward", "decals.toFront", "decals.toBack"),
            Mode("units", "units.pick", "units.addGroup", "units.selectGroup", "units.placePreset", "units.asWrecks"),
            Mode("procedural"),
            new MenuLayout("Assets", ["assets.browse", "assets.pick", "assets.star", "assets.reload", "assets.reloadShown"]),
        ]),
        new MenuLayout("Generate",
        [
            "generate.walkability", "generate.navigation", "markers.connectPaths", "generate.props", "generate.masks", Separator,
            "generate.normalMap", "decals.generateNormal", "decals.generateShadow", "file.renderPreview", "generate.skyCube", Separator,
            "generate.stale", "generate.all", Separator,
            "generate.automatic", "generate.useAsWorkspaceDefault",
        ]),
        new MenuLayout(PluginsMenu, ["plugins.manage", "plugins.reload", Separator, "plugins.check"]),
        new MenuLayout("Help",
        [
            "help.shortcuts", Separator,
            new MenuLayout("Folders",
            [
                "help.workspaceFolder", "help.projectsFolder", "help.projectFolder", Separator,
                "help.presetsFolder", "assets.brushesFolder", "plugins.showFolder", "help.assetsFolder", Separator,
                "help.gameFolder", "help.fafDataFolder", "help.exportFolder", Separator,
                "help.diagnosticsFolder",
            ]),
            Separator,
            "help.diagnostics", Separator,
            "help.reportProblem",
        ]),
    ];

    /// <summary>
    /// The menu bar with the commands the plugins registered (<see cref="EditorCommand.Owner"/>) at
    /// the end of the Plugins menu, by title.
    /// </summary>
    public static IReadOnlyList<MenuLayout> WithPlugins(IEnumerable<EditorCommand> commands)
    {
        string[] plugins = [.. commands
            .Where(command => command.Owner is not null && command.Menu == PluginsMenu)
            .OrderBy(command => command.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(command => command.Id)];
        return plugins.Length == 0
            ? Bar
            : [.. Bar.Select(menu => menu.Title == PluginsMenu ? menu with { Items = [.. menu.Items, Separator, .. plugins] } : menu)];
    }

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
