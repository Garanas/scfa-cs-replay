# Agent guide: FAForever.MapEditor.App

The map editor's window (`faf-map-editor`, Avalonia 12, Fluent theme, dark). The repository-wide
guide is [`../../AGENTS.md`](../../AGENTS.md); everything about maps (the document, the project
format, undo, brushes, saving and reloading) is in the library and its guide,
[`../FAForever.MapEditor.Project/AGENTS.md`](../FAForever.MapEditor.Project/AGENTS.md). Keep this
project thin: what the menu does, the library does.

```sh
dotnet run --project src/FAForever.MapEditor.App -- [folder] [--automation 5371]
```

The folder is a project (it holds `map.json`) or a map folder of the game, which is imported
once the workspace is ready.

## Every feature: the checklist

The editor is built to be checked and used by agents as well as people. A feature is done when it
keeps that up; go through this list with every change to the window, not only at the end:

1. **A command**: every operation a person can do is a request with `[EditorCommand]` and its
   handler, in one file under `Commands/<menu>/` (the controller, below), with its shortcut, user
   story and `Needs`, and its id in its menu in `Commands/Menus.cs`. It then is in the menus, the
   palette, the shortcuts and automation (`run id=... ...arguments`) at once. Not built yet? A
   request without a handler, marked `[Incomplete(story, what it is to do)]`, in its menu's
   `Planned.cs`. A tool is a `ToolDefinition` of its mode (`ViewModels/EditorModes.cs`), with
   `Missing` until it works and then `Make`, its class in `Tools/` (below) and a `DataTemplate`
   for its settings in `Views/ToolOptions.axaml` (a `controls:ToolRow` of them, most used first:
   what does not fit goes behind More); a part of a panel not built yet a
   `PlannedSection`. What the feature shows or holds is in `state` (or its own automation
   command when it is large).
2. **Labels**: every new control one can read or use gets `AutomationProperties.Name` (and
   `HelpText` when its use is not obvious), unless its own text says what it is. A new kind of
   control (one `UiTree` does not know as usable, or whose text it cannot read) gets a line in
   `Automation/UiTree.cs`, so `ui` describes it.
3. **Tests**: the operation in `MainViewModelTest` (or the library's tests), and its input in
   `MainWindowTest` (headless: the mouse, keys, what the view then shows). A view drawn on the
   graphics card is checked with automation in a real editor instead (`screenshot gpu=true`).
4. **Performance**: work that grows with the map, or runs per frame or per mouse move, gets a
   span (`Performance.Measure`); check `perf` before and after on the 81 km and the decal-heavy
   test maps. Nothing per frame touches the disk; a hidden view does no work.
5. **Diagnostics**: what can go wrong is logged (`model.Logger(category)`, a message template
   with named values, never a value glued into the text), at the level it deserves: what the
   user asked that failed is an error, what the editor worked around a warning. A new kind of
   operation that takes time is measured (`EditorMetrics`). Section "Diagnostics" below.
6. **Check it running**: in a test editor on port 5372 with `FAF_MAP_EDITOR_SETTINGS` pointing
   at a settings file of its own, read `ui` and `state`, and look at a screenshot.
7. **Look**: the views use tokens and style classes, never loose values (UI rules below); a
   change of look updates the snapshots on purpose (`UPDATE_SNAPSHOTS=1`), checked by eye.
8. **This guide**: what the feature does and its rules, where a later reader looks for them.

The project skill `.claude/skills/editor-ui` holds the same rules in short, for agents.

## The shell: concept A, and the controller

The window follows concept A of the UI design (the canvas "Map editor UI concepts"): a rail of
modes on the left (`ModeRail`), the mode's tools and sections (`ModePanel`), the active tool's
settings above the views (`ToolOptions`), the view tabs with the overlay chips, the views, the
dock under them (`Dock`: Problems, History, Assets, Console), the inspector on the right
(`Inspector`), the status bar, and the command palette over everything (`CommandPalette`,
Ctrl+K). Where the user stories go in it is on the design's anatomy board.

- **The controller** (`Commands/`, user story F-1) holds every command, sent through MediatR.
  A command is a request, a record whose properties are its arguments, with
  `[EditorCommand(id, title, Gesture, Story, Needs)]`, and its handler in the same file, in a
  folder per menu (`Commands/File/Save.cs`). The handler calls the view model, which owns the
  state and the document; it never sends another command. All of them share the namespace
  `FAForever.MapEditor.App.Commands` (a namespace `...Commands.File` would hide `System.IO.File`).
  - `CommandRegistry` is the catalogue: it finds the requests with `[EditorCommand]`, makes a
    request from arguments by name (JSON: an argument the request does not take, or a missing one
    it needs, is an error), checks `Needs` (`Map`, `Idle`, `Undo`, `Redo`, `Workspace`, `Project`,
    `NoStroke`, answered by `MainViewModel.Meets`), sends it, counts it (`editor.commands`) and
    logs it. Commands made from data are registered by the view model with a request that takes
    the id: `<mode>.mode` (`ChooseMode`, shortcut and story from `EditorModes`), `tool.<id>`
    (`ChooseTool`), `view.overlay.<id>` (`ToggleOverlay`, from `EditorOverlays`).
  - `CommandServices` puts it together: the one container of the app, holding MediatR, the
    handlers of the assembly, the pipeline and what the handlers take (`MainViewModel`, the
    dialogs, `CurrentEdit`); `MainViewModel` makes it. MediatR runs without a licence key, which a
    desktop application needs not set; it would log only that, and its logging is off. The
    project uses MediatR under Lucky Penny's community licence (accepted 2026-10-09); a key, when
    there is one, goes in the environment as `MEDIATR_LICENSE_KEY`, never in the repository.
  - `Commands/Menus.cs` is the menu bar: each menu's command ids in order, separators, the modes'
    submenus (the mode, its commands, its tools). A request says what a command is, the layout
    where it shows. A command in no menu is for automation and plugins, and the palette leaves
    it out: its control is on a page or panel (`workspace.setUp`, `workspace.convert`,
    `file.enableVersionControl`, `diagnostics.set`, `assets.showFile`, `paint.chooseMask`, `presets.apply`,
    `presets.delete`, `view.walkability`, `problems.show`, `markers.showKind`, `markers.set`,
    `markers.setChain`, `markers.renameChain`, `markers.deleteChain`, `markers.setWeather`,
    `markers.addOptionalGroup`, `file.showCommit`, `file.restoreCommit`, `view.togglePanel`,
    `view.resizePanel`, `view.setToolbar`, `view.addToToolbar`, `view.removeFromToolbar`), it selects what one
    clicks (`assets.select`) or drops what one drags (`assets.drop`), it is what a tool's click does
    (`markers.place`, `markers.link`), it is the mouse's or a view's own key in
    the views (`camera.look`, `camera.focus`, `view.zoomTo`, `brush.set`, `edit.goTo`), or it is
    for finding faults (`view.gpuDebug`, `diagnostics.flush`, `diagnostics.upload`). The window makes the menus and the
    shortcuts from it (`MainWindow.BuildMenusAndShortcuts`); the palette searches the commands in
    menus, the ones that work first.
  - **When the menus ask again** whether their commands can run: on `CommandRegistry.CanRunChanged`
    only (span `window.commands`). The core raises it wherever what `Needs` asks for changes:
    `Refresh` (a map opened, busy, a step done or undone, the workspace ready), `StartStroke` and
    `EndStroke`. A new kind of `Needs` raises it where its answer changes.
  - Run without a person (automation, Lua plugins: `RunAsync(id, arguments, interactive: false)`),
    a command asks nothing: `DialogGuard` refuses the question with what argument to give. The
    menus, shortcuts, palette and buttons run `MainViewModel.RunAsync(id)` (`RunCommand` in XAML),
    which shows what went wrong in the status line. A command made at run time (a plugin's)
    is taken away with `Unregister`; one in the menu bar cannot be.
  - **One undo step**: a request with `[UndoStep]` runs inside a transaction named after the
    command (`UndoStepBehavior`, the pipeline), kept when the handler returns and thrown away when
    it fails; the handler edits through `CurrentEdit.Transaction`. Transactions do not nest: such a
    command needs a map and no brush stroke going on (the registry adds `Map | NoStroke`).
    `water.toggle {on?}` (`Commands/Water/ToggleWater.cs`) is the example.
- **Not built yet** is visible and traceable: a command with `[Incomplete(story, missing)]`
  only says what it is to do (status bar, Console, `run` answers `ran: false`) and shows in
  italics in the menus; a tool with `Missing` shows it in the tool options and does nothing in
  the views (it has no class, so the left mouse goes nowhere); a `PlannedSection`
  shows a dashed box with its story. `CommandRegistryTest` checks that every story named
  anywhere exists in `USER_STORIES.md`, that every request has a handler unless it is
  `[Incomplete]` (and then none), and that the menus hold each command once.
- **Modes and tools** are data (`EditorModes`): a mode's id, title, icon (`Styles/Icons.axaml`),
  tools, panel sections and inspector sections. A mode remembers its last tool.
- **Overlays** are data too (`EditorOverlays`): each is a chip above the views and a command in
  the View menu (`view.overlay.<id>`). One with `Panel` is also a check box in that mode's panel
  (`OverlaySection`: the Markers panel's "Shown in the views"), which runs the same command.
- **The views' bar** is one row of `ToolbarHeight`, as the tool's options are: the view tabs, then
  a `controls:ToolRow` (`ViewOptions`) holding the symmetry and a chip per overlay, which
  `MainWindow.BuildOverlayChips` makes from `MainViewModel.Overlays`: a `ToggleButton.chip` that runs
  the overlay's command and is checked while it shows (`ui` reads it; `state.show` lists every
  overlay), with a swatch of the colour it tints the terrain in when its definition names one
  (`OverlayDefinition.Swatch`: walkability, mask, areas). What does not fit at the window's width
  lies behind the row's More, over the views (the bar has `ZIndex` 1, the tool's options 2, so their
  More lies over this bar). At 1280 by 800 the top view is 464 pixels high (`TheViewsBarIsOneRow...`).
  Walkability and a mask tint the terrain in turn: turning one on turns the other off.
- **What the views draw again, by event**: `DocumentChanged` when the shown document changes
  (an edit, undo, reload, a dab, a stroke thrown away): the top view updates the tiles that
  changed, the 3D view uploads them. `OverlaysChanged` (an overlay turned on or off),
  `CameraMoved` and `CursorChanged`: only what lies over the terrain. `ShownMask`, a property:
  the top view's picture with that layer's mask. `MapChanged`: everything, for another map.
- **Tabs are enums**: `MapViewTab` (the top view, the 3D view, both side by side) and `DockTab` (the dock), declared in
  the order of their `TabItem`s and bound through `Controls/EnumIndexConverter`. A new tab is a
  value there, its `TabItem` in that place and its command; `TheTabsAreInTheOrderOfTheirEnums`
  checks the order.
- **What a tool does is one class** in `Tools/`, made once per editor by its definition's `Make`,
  so its settings last while the person switches tools (`ToolItem.Tool`; `MainViewModel.Tool<T>()`
  finds one by class). It implements `IMapTool`: `Press`, `Drag` and `Release` of the left mouse,
  in ogrids, and the radius of the circle drawn around the mouse. The views keep the rest of the
  mouse (moving, zooming, alt and a click for the camera) and send the left button to the
  active tool, without knowing which tools exist. Its settings are observable properties; the
  options bar picks the tool's `DataTemplate` by class.
  - **The options row** (`Views/ToolOptions`, `Controls/ToolRow`) is one row of a fixed height
    (`ToolbarHeight`) for every tool and every width of the window, so the views below never move
    when the tool changes. A tool's template is a `controls:ToolRow Classes="toolrow"` whose
    children are its settings, most used first: what does not fit (the last ones) goes behind a
    More toggle at the row's end, which lays them out in rows of the same height over the top of
    the views (on a `Border.tooloverflow`); another tool, a second click, a click anywhere outside
    the row and what it shows (a combo box's list dropped from it counts as inside) or Escape
    (wherever the keyboard is; the key goes no further) hides them. They
    stay in the tree with their bindings, clipped away while hidden; `ui` marks them `overflow:
    hidden|shown`, and automation's `set` opens More before setting one. The `ToolOptions` and its
    `ContentControl` do not clip (`ClipToBounds="False"`: Fluent's templated controls clip by
    default), and the options lie above the views (`ZIndex`). At 1280 pixels the sculpt tool shows
    its shape, mode and falloff and puts radius, strength and limits behind More
    (`ToolOptionsTest`).
  - `BrushStroke`, which both brushes use, holds the stroke's `EditTransaction` from press to
    release (`MainViewModel.StartStroke`, `EndStroke`): one undo step, shown while it is painted,
    dabs a quarter of the radius apart. A stroke is input, not a command, so it opens its own
    transaction; the commands that edit wait until it ends (`Needs.NoStroke`).
  - `SculptTool` (T-1): mode, falloff, radius, strength, the height to flatten to, set or scale
    around, the factor and noise size, limits on the heights. The painting tools derive from
    `GridPaintTool` (section "Painting the terrain's grids" below); `StrataTool` (S-1) paints the
    layer chosen in the Paint panel's one list of layers (`LayerTextures.Selected`, kept in step with
    `Painted`), within its limits of slope and
    height; the top view shows that layer's mask while it is active (`MainViewModel.ShownMask`,
    and `ShownGrid` for every painting tool). Each tool has its own radius.
  - **Brush shapes** (`Tools/BrushShapeSettings`, both tools' `Shape`, `IShapedBrushTool`): round,
    or one of the workspace's brushes (the shelf's, below), the first choice of the options row;
    a brush chosen on the shelf becomes the active tool's shape. A picture fills the square
    around the radius, its top to the north, turned clockwise by `Rotation` or at random per dab
    (`RandomRotation`). It is read once (`BrushShapes`, by file and write time; the library's
    `BrushShape`, reduced to 256 pixels), on a pool thread when chosen; a picture that cannot be
    read shows in the status line and paints nothing. With a picture, raising and lowering go up
    to 50 game units a dab (`StrengthMaximum`): a mountain at a click.
  - **Both views show the brush** under the mouse (`Tools/BrushPreview`) and send it the left
    mouse: the top view on the map's place, the 3D view on the place of the terrain it points at
    (`EditorCamera.PointAt`). They draw how strongly it works as a ramp from near black to the
    overlay's colour (the square root of the values, which shows where a picture works at all),
    with its edge as a line: the circle of the radius, or the picture's square turned with it
    (with a tick on its top edge in the top view). The 3D view draws it after the water, on the
    cells of the terrain's lattice under it (`TerrainShaders.BrushVertex`; the picture on texture
    unit 14, uploaded before the heights are bound, since an upload binds to the active unit).
    `MainViewModel.CursorChanged` (a tool's radius, shape or rotation, another tool) draws them again.
  - `brush.set {layer?, mode?, radius?, strength?, target?, erase?, shape?, rotation?,
    randomRotation?, falloff?, factor?, noiseScale?, minHeight?, maxHeight?, minSlope?, maxSlope?,
    limits?, waterMask?, mask?, terrainType?}` sets them up for automation: a layer chooses its tool
    (0 sculpting, 1 to 8 the strata tool with that layer), `waterMask`, `mask` and `terrainType` the
    painting tool of each; radius, strength, falloff and the shape (`round`, a brush's key or its
    path in the brushes folder) go to the active tool's brush; a limit given turns its limit on,
    `limits: false` turns them off first.
- Shortcuts are never a single letter (`IdsAndShortcutsAreUnique`): it would fire while typing.

## Layouts and the toolbar

User stories V-4 and F-1. `MainViewModel.Layout` (`LayoutViewModel`) holds which panels show,
their sizes, how the views share the width side by side, the view that shows and the toolbar's
commands; every change is written into the user's settings at once (`UserSettings.Layout`, the
library's `LayoutSettingsDto`) and read back when the editor starts (`MainViewModel.LoadLayout`).

- **Side by side** (`view.sideBySide`, Ctrl+3, the views' third tab): the top view left, the 3D
  view right, a splitter between them (its share is `SideBySideSplit`). They share the camera (the
  top view draws its footprint) and each takes its own mouse. Both views stay in the tree: only
  their visibility and column change (`MainWindow.ShowChosenView`). Going into or out of side by
  side fits the top view to the map; a panel or splitter that moves keeps the place in the top
  view's middle in the middle.
- **Panels**: the window's middle is a grid (`Shell`: mode panel, splitter, the views with the
  dock under them, splitter, inspector). `view.modePanel`, `view.inspector`, `view.dock` and
  `view.toolbar` (View menu, `{on?}`), and `view.togglePanel {panel, on?}`, show and hide them;
  dragging a splitter (`GridSplitter.layoutsplitter`) or `view.resizePanel {panel, size}` sizes
  them (120 to 1200; a click that moves nothing keeps the editor's own size, so the dock still
  grows while the shelf shows). `MainWindow.ApplyLayout` sets the columns and rows: a size the user
  gave, else the tokens (`ModePanelWidth`, `InspectorWidth`, `DockHeight`, `DockTallHeight`); the
  panels' styles hold no width. `view.resetLayout` shows every panel at its own size again and
  splits the views in half; the toolbar's commands and the view stay.
- **The toolbar** (F-1) lies in the menu bar left of the palette's field: a button per id of
  `ToolbarCommands`, made from the registry like the menus (`ToolbarItem`: an icon of
  `LayoutViewModel.Icons` from `Styles/Icons.axaml`, else the title; the tool tip with the
  shortcut; enabled as the registry answers on `CanRunChanged`). The default is undo, redo, save,
  the palette and the symmetry; `view.setToolbar {commands?}`, `view.addToToolbar {id}` and
  `view.removeFromToolbar {id}` (also a button's context menu) change it, and the settings file's
  `layout.toolbarCommands` can be edited by hand (an unknown id is left out with a warning).
- Automation: `tab {name: side}`, `state.layout` (the panels, sizes, split, the toolbar's commands
  with `canRun`, the settings file).
- Not built yet: moving a panel to another side or out of the window, choosing toolbar commands
  in the window (only the context menu's Remove).

## UI rules: one look, kept in one place

The look is concept A's (its colours, IBM Plex, the spacing). Fluent (Avalonia's theme, dark,
compact density) draws the standard controls; `Styles/` holds the look of everything:

- `Styles/Tokens.axaml`, the design tokens: the palette (concept A's greys, blues, corals and
  ambers), the brushes named after their use (`CanvasBrush`, `SurfaceBrush`, `SurfaceRaisedBrush`,
  `ControlBrush`, `ControlRaisedBrush`, `InputBrush`, `InputStrokeBrush`, `TextPrimaryBrush`,
  `TextSecondaryBrush`, `TextTertiaryBrush`, `SelectedBrush`, `SelectedStrokeBrush`, `DangerBrush`,
  `Warning*Brush`, `LinkBrush`, `Overlay*Brush`, ...), per theme variant, the fonts
  (`SansFontFamily`, `MonoFontFamily`) and the sizes (`FontSize*`, `Space*`, `*Padding`, `*Gap`,
  `*Width`). The accent is pinned (`AccentColor`, through Fluent's palette in `App.axaml`, with the
  window's background and text), so the editor looks the same on every computer.
- `Styles/Fluent.axaml` and `Styles/FluentBrushes.axaml`: the resource keys Fluent's control themes
  read (`ButtonBackground`, `TextControlBorderBrushFocused`, `SystemControlHighlightListAccentLowBrush`,
  `TabItemHeaderSelectedPipeFill`, ...; the names are in Avalonia's `src/Avalonia.Themes.Fluent/Controls/*.xaml`),
  pointed at the tokens. That is what makes buttons, combo boxes, text boxes, sliders, lists,
  tabs, menus and tool tips match the panels. The aliases are a separate file merged into both
  theme variants: a `StaticResource` to a brush of one variant resolves only inside that variant's
  dictionary (`ThemeTest` checks both). A control that looks like Fluent and not like concept A
  lacks a key here.
- Fonts: IBM Plex Sans and Mono (SIL Open Font License, `Assets/Fonts`), registered as
  `fonts:Editor` by `EditorFonts.WithEditorFonts()` in `Program` and in the tests' `HeadlessApp`.
- `Styles/Classes.axaml`, the classes the views say what a thing is with: text `title`,
  `heading`, `panelname`, `lead`, `secondary`, `caption`, `label`, `mono`, `shortcut`, `error`, `warning`,
  `undone`, `item`, `overline` (a group's label in capitals: bind the text through
  `Controls/UpperCaseConverter`, a `TextBlock` has no text transform), `story`; surfaces
  `Border.panel`, `Border.bar`, `Border.menubar`, the panels' `Border.panelhead` and `Border.group`
  (groups a line apart), `Border.planned`; layout `StackPanel.page` (`wide`), `StackPanel.stack`,
  `section` (a gap before), `Button.beside`; the shell's `Button.rail` (`selected`: filled),
  `Button.tool`, `Button.chip`, `Button.search`, `TabControl.compact` (the
  dock) and `TabControl.segmented` (the view tabs).
- Every `Button` centres its content (`Classes.axaml`): Fluent's leaves it at the top and relies
  on an uneven padding, which a button with its own height or padding loses. A class that changes
  the look of a state Fluent styles in the template (`:pointerover`, `:pressed`) sets it on
  `/template/ ContentPresenter#PART_ContentPresenter`, as Fluent does, or Fluent's wins.
- `Styles/Controls.axaml`, the control themes of the editor's own controls (`Section`: a
  heading with its content).
- Reusable pieces are controls: `Controls/FolderField` (a folder to choose: heading,
  explanation, path, Browse, with the labels for screen readers), `Controls/Section`. A pattern
  used twice becomes one.
- The views are split by what they are: `MainWindow` (menu, status bar, the two map views, the
  pages by stage), `ModeRail`, `ModePanel`, `ToolOptions`, `Inspector`, `Dock`, `AssetShelf`,
  `CommandPalette`, `AssetPicker`, `DiagnosticsPage`, `VersionControlPage`, `ReportProblemPage`, `SetupPage`,
  `UpdateAssetsPage`, `ConvertingPage`.
- **Numbers** show and are read alike on every computer (`ViewModels/EditorCulture`): the editor's
  threads run in the invariant culture (`EditorCulture.Use()` in `Program.Main`, and in the tests'
  `[AssemblyInitialize]`), so bindings and labels (`StringFormat='{0:0.00}'`) write a point; a
  number typed is read with a point, or a single comma when it has no point ("12.5" and "12,5"),
  never grouped. A view model parses a field's text with `EditorCulture.TryParse` (`Setting.TryParse`
  does) and shows it with `EditorCulture.Format`; a `TextBox` bound straight to a number
  (`double`, `float`, `int`) takes `Converter={x:Static controls:NumberTextConverter.Instance}`.
  `EditorCultureTest` checks a Dutch computer.
- Code that draws (the overlays of the top view, the 3D view's background where the map has no
  sky cube map) reads the tokens with
  `TryFindResource` and reads them again when the theme variant changes. Only colours that come
  from the map (a marker's) are made in code.
- `StyleRulesTest` fails on a colour, font size or weight, opacity or non-zero distance written
  in a view or control, and on a fixed colour in their code. `SnapshotTest` draws the setup page,
  the assets page and the editor with HardFFA headless (Skia, IBM Plex, 1280 by 800) and compares
  them with `tests/FAForever.MapEditor.App.Tests/snapshots/*.png` (a channel may differ by 24, 0.2 %
  of the pixels may differ); a failure writes the new picture into the tests' output
  (`snapshot-results/`). Look at it, and run the tests with `UPDATE_SNAPSHOTS=1` to accept it.
- Avalonia's documentation is at hand through the `avalonia-docs` MCP server (`.mcp.json`):
  ask it before writing Avalonia code from memory; WPF habits and older versions differ.

## The start: workspace first

The editor works in a workspace (the library's guide explains it). `MainViewModel.StartAsync`
hands the remembered one (`UserSettings`) to `WorkspaceSetupViewModel` (`model.Setup`), which
sets the window's `Stage` and raises `WorkspaceReady`, after which the editor opens the folder it
was started with:

- `SetUp`: no workspace, or the remembered one is gone. A page asks for the workspace folder, the
  game, FAF's data and the vault, filled in from FAF's `fa_path.lua`; `SetUpAsync` checks
  them, creates the workspace, remembers it and converts the assets.
- `UpdateAssets`: the assets are missing or outdated (a new `.nx2` from FAF, another editor
  version); the page says why, and its button converts them.
- `Converting`: a progress bar. The editor waits until it is done; closing the window stops the
  conversion, and the next start goes on where it was.
- `Ready`: the editor. Open and Save As start in the workspace's `projects`, Import in the vault's
  `maps`, and Export writes into the vault's `maps` without asking.

Started without a folder, the editor opens the workspace's sandbox once it is ready
(`MainViewModel.OpenSandboxWhenReady`, set by the application; `OpenSandboxAsync` writes it first
when it is missing; the library's guide describes it), so a new user and an agent's test editor
always have a map. A map that is not publishable says so in the title ("sandbox, not for the
vault") and the status line when it opens; exporting it works, with a warning in the status line
and the log.

Help, Show the workspace folder (`help.workspaceFolder`) opens the workspace in the system's file
manager. The menu and the editor are hidden until `Ready`. To try the start without touching your own
workspace, point `FAF_MAP_EDITOR_SETTINGS` at another settings file.

## Structure

- `ViewModels/MainViewModel`: the window without the window, and the editor's core. It holds
  the `EditedMap`, busy, the history, the tools, the stroke in progress (`StrokeInProgress`), the
  modes, overlays and tabs, the camera, and every operation of the menu as a plain method that
  takes its folder (`OpenAsync`, `LoadProjectAsync`, `ImportMapFolderAsync`, `SaveAsync`,
  `SaveAsAsync`, `SaveProjectAsync`, `ExportAsync`, `Undo`, `Redo`); `Stroke(points)` is the left
  mouse dragged through places, sent to the active tool.
- **The parts** with state of their own are classes it makes in its constructor and holds as
  properties, each one user story's area, readable without opening `MainViewModel`:
  `Diagnostics` (`DiagnosticsViewModel`: the Console and the Diagnostics page), `Setup`
  (`WorkspaceSetupViewModel`: the start, the setup page, the conversion of the assets),
  `VersionControl` (`VersionControlViewModel`: the git page), `Palette` (`PaletteViewModel`),
  `Assets` (`AssetsViewModel`: the shelf), `Picker` (`AssetPickerViewModel`),
  `Events` (`EditorEvents`), `Presets` (`PresetsViewModel`), `Symmetry` (`SymmetryViewModel`) and
  `Generation` (`GenerationViewModel`), the last four in the section "Events, presets, symmetry
  and generated results" below.
  Each is the one place its state lives: the views bind through it (`{Binding Setup.Folder}`),
  automation reads it there, and a handler that works on one part takes that part
  (`SetUpWorkspaceHandler(WorkspaceSetupViewModel setup)`; `CommandServices` holds them). A part
  writes the status line through an `Action<string>`, or through the core when it works on the
  open map (`VersionControlViewModel` saves before a commit inside the core's `BusyAsync`). Tests
  of a part make the part itself where they can. A new area with state of its own becomes a part
  the same way; no interfaces, messenger or container for them. A command's handler asks
  for a folder its request left out (`IEditorDialogs`), then calls those methods; tests call
  them directly too. It uses no Avalonia types, so `tests/FAForever.MapEditor.App.Tests` tests
  it without a window.
- Opening, importing, saving and exporting are asynchronous (`LoadProjectAsync`,
  `ImportMapFolderAsync`, `SaveAsync`, `SaveAsAsync`, `ExportAsync`): they read and write on a pool
  thread while `IsBusy` disables the commands that cannot run alongside. Painting goes on during a
  save; reloads wait until a save or stroke ends. Keep the user interface's thread free of work
  that grows with the map: a full redraw of the view runs its rows in parallel.
- `Controls/HeightmapView`: the terrain from above, one pixel per sample, shaded and coloured by
  height and water (`Rendering/TerrainPicture`), with the grid being painted over it (`ShownGrid`,
  a mask in the theme's `MapMaskBrush`, violet); it draws again only the tiles a change touched. A
  picture coloured anew for a map of 20 km and more (another map, size, water or overlay) is
  coloured on a thread of the pool (span `top.colour-all`, 135 ms on an 81 km map; `IsColouring`,
  which a settled screenshot waits for); a change meanwhile follows when it is done. While nobody
  zoomed or moved it (`_wholeMap`), every new size of the view fits the whole map again (side by
  side and back, a panel shown); after a zoom a new size keeps the middle. Props are dots on a dark
  rim (`MapPropRimBrush`), at most one per 4 pixels zoomed out. The markers lie
  over it at a fixed size on screen in the colour of their kind (spawns circles with their
  number, deposits squares, AI markers diamonds, weather triangles, path nodes dots with their
  links; View menu). The left mouse is the active tool's, right or middle mouse moves, the
  wheel zooms around the cursor.
- `Controls/TerrainViewGpu` (the 3D tab): the map on the graphics card with OpenGL through
  Avalonia's `OpenGlControlBase` (ANGLE, OpenGL ES 3.0, on Windows; OpenGL 3.3 elsewhere), bound
  with Silk.NET. The camera (`Rendering/Gpu/EditorCamera`, tested without a window) works as the
  FAF map editor's (`Assets/Scripts/Camera/CameraControler*.cs`): it starts straight down over
  the whole map; the wheel moves it along the ray under the mouse, towards what the mouse points
  at, three steps of 3.3 % of 2.6 times the distance to the ground a notch (out: straight back),
  from 4.5 to 11 000 ogrids; the middle mouse button drags the ground with the mouse; space and
  the mouse turn it around the pivot and tilt it from straight down (pitch 90) to 10 degrees
  above the horizon; F focuses on the selection when there is one (as the FAF editor does;
  `camera.focusSelection`), else on the place under the mouse from about half as far; View,
  Frame the selection (`view.frame`, Ctrl+Shift+F) frames it in both views; Home shows
  the whole map again. It
  glides to where it is sent (only while the 3D view draws), and its pivot is always the ground
  it looks at. The left mouse button is the active tool's, as in the top view.
  - The camera belongs to the view model (`MainViewModel.Camera`, `CameraMoved`), so both views
    share it: the top view draws what it sees (its footprint on the ground, the eye and a line to
    the pivot; View menu), and alt and a click there sends it to that place at once. Opening
    a map puts it over the whole map. The status bar shows the place under the mouse in both
    views (`EditorCamera.PointAt` on the terrain itself in 3D).
  - That ground is the terrain blurred (`Rendering/Gpu/SmoothGround`: averages over squares of
    8, 16, 32, ... ogrids, read between their centres and between sizes), over about 15 % of the
    camera's distance (8 to 2048 ogrids): spikes and cliffs do not throw the camera up and down,
    and from close by it still follows hills. Only the eye keeps 2 ogrids clear of the terrain
    itself. It follows the heights as they change (`Rendering/Gpu/CameraGround`, which the camera
    reads its heights from): `SmoothGround.Update` averages again only the squares over the tiles
    that changed and the larger ones holding them (4 ms after a stroke on an 81 km map); another map
    makes it anew, on a thread of the pool for 20 km and more (31 ms for 81 km), the camera resting
    on the middle's height meanwhile.
  `Rendering/Gpu/TerrainRenderer` draws the terrain, then the albedo decals, the normal decals
  and the water; `TerrainShaders` follows the FAF map editor's port of the game's
  `TTerrainXP` (`Assets/GFX/Shaders/FaTerrainShader.shader` in `D:\faf-development\FAForeverMapEditor`):
  the lower albedo, the strata by their masks (`clamp(mask * 2 - 1)`), the upper albedo by its alpha,
  each repeating every `AlbedoScale` ogrids, and the game's lighting (`CalculateXPLighting`
  without specular). Normals come from the heightmap per pixel; the strata's normal maps are not
  used (a fragment shader has 16 texture units).
  - The heightmap is a 16-bit integer texture read with `texelFetch`, the masks two colour
    textures of four layers each. A new document version uploads only the tiles that changed
    (`TiledBuffer.ChangedTiles`), so a stroke on an 81 km map shows within 0.2 s.
  - Textures (`GpuTextureCache`) are on the card once per file, whatever number of layers and
    decals use them; they are decoded on a pool thread (`GameTextures`, from the map's own files
    or the workspace's assets) and uploaded with the next frame. A texture whose source changes
    (a project file read again, an asset a tool saves: the view watches `assets/`) is loaded
    again; the old one shows until then. Textures no draw used are deleted.
  - Decals keep the map's order (which lies on top) and are drawn instanced: a run of decals with
    the same texture and size class is one draw (Seton's Clutch: 2230 decals, 48 textures, 523
    draws). A decal's position is its corner, and it turns around that corner. Normal decals
    (DXT5nm) change the lit colour below by the ratio of the light with and without them,
    blended as `2 × source × destination`, since the view has no deferred lighting.
  - What lies on the terrain (decals, the brush) is drawn on the terrain's own lattice: the
    cells under it, their corners made by the same invariant code as the terrain's
    (`TerrainShaders.Lattice`), so the two are one surface and the depth test with a small
    polygon offset puts it on top at any distance. A grid of its own would cut under the
    terrain between corners and fight with it, most visibly close by and looking straight
    down. A decal's patch has 4, 16, 64 or 256 cells a side (the smallest that covers it, the
    size class of its run); where a point lies in the decal is worked out from its place, and
    the fragment drops what lies outside.
  - The water is a surface at its elevation, drawn after the decals, darker and more opaque
    with depth down to the abyss elevation.
  - **Props, units and markers** (`Rendering/Gpu/ObjectRenderer`, `ObjectShaders`), after the
    terrain (so decals do not cover them) and, the markers, after the water. A model is read on a
    pool thread the first time its blueprint shows (`Rendering/ObjectModels`, span
    `3d.read-model`): every level of detail of the converted blueprint's JSON (`<name>_lod0.glb`,
    `_lod1.glb`, ...) with its cutoff, albedo and shader, scaled by its `uniformScale`; it goes on
    the card with the next frame. Asking for a model never touches the disk, and the models are
    read again when the catalog is (`assets.reload`). Props and units are instanced: their
    placements are made again only when the objects or the highlighted set change; per frame
    (only when the camera moved) the ones inside the view are sorted into runs of blueprint and
    level by their distance to the eye, one draw a run (`3d.arrange-objects`). The game hides a
    prop beyond its last cutoff; the editor keeps drawing its last level. Archsimkats Valley
    (8304 props, 98 units): 5.2 million triangles at the first level, 65 000 to 160 000 with the
    levels, about 1 ms a frame for the objects.
  - Placement and shading: a prop's model is turned by its three axes (`RotationX/Y/Z` as the rows
    of its rotation, so it can lean) and scaled by `Scale` (zero counts as 1); a unit's by its
    `Orientation` (yaw, pitch, roll in radians). Both are lit by the map's sun as the terrain is,
    from both sides; the albedo's alpha is cut out at the shader's reference (`MeshPreview.ShaderOf`:
    0x80 for the `...Alpha` shaders); UEF, Aeon and Cybran units are brightened (the game lights
    their metal with its environment), Seraphim units take their silver from the albedo's blue.
    There is no team colour, normal map or specular yet. A blueprint the workspace has no model
    of (or whose model cannot be read, logged once) is a box of one ogrid in the theme's
    `WarningBrush`. A prop the game shows as an effect (its blueprint names the marker mesh
    `marker01_lod0`: steam over lava, blowing sand, mist; `Rendering/EffectProps`) is a small
    upright diamond in `MapEffectPropBrush`, picked by that diamond.
  - Markers are upright diamonds in their kind's colour (`Marker.Color`) standing on the terrain
    at their place: spawns 5 ogrids high, path nodes 1.2, the rest 2; from `ObjectPlacement.MarkerNear`
    (80 ogrids) on they grow with the distance so they keep their size on screen. They are drawn
    in front of the water, so the markers under it show. The overlays `markers`, `pathNodes`,
    `props` and `units` turn them on and off.
  - **Picking** (`Rendering/ObjectPicking`, without OpenGL, tested in `ObjectPickingTest`):
    `TerrainViewGpu.PickAt(point)` makes the ray through the point (`EditorCamera.RayAt`) and
    returns the nearest thing (an `ObjectRef`, the selection's own reference) whose bounds it
    meets in front of the terrain: a
    model's box (the read model's, `ObjectBounds.Box` until then) turned and placed as drawn
    (`ObjectPlacement`, shared with the renderer), a marker's symbol as a sphere. When no object
    is hit, the decal under the point on top (the last in the map's order). Only the kinds that
    show are picked. A press of the left mouse gives the active tool both the place on the ground
    and what the ray met (`ToolInput.Picks`, `Picked`): the select tool selects that, so a click
    on a tree in 3D selects the tree, not whatever lies nearest the ground behind it. The keys the
    view does not use itself go to the active tool, as in the top view.
  - **Highlight**: `TerrainViewGpu.Highlighted` is a set of `ObjectRef`s drawn mixed with the
    theme's `SelectedStrokeBrush` (props, units, markers): the selection, which the view follows.
    Automation's `pick {x, y, highlight: true}` selects what it found, as a click would.
  - Uniforms of samplers must not share a texture unit with another sampler type: unit 0 holds
    the heights (`usampler2D`), 1 a decal's texture or a model's albedo (the objects set it
    active before their textures upload, since an upload binds to the active unit), 2 to 11 the
    layers, 12 and 13 the masks, 14 the brush's picture; the overlays' pass binds walkability to
    13 and the painted grid to 15 itself (section "Handles, hover and the 3D view's overlays").
    A sampler with a mipmapped filter on a texture without mipmaps reads as black: the masks have
    their own sampler.
  - Avalonia's hit testing only finds what a control draws itself, and this one draws nothing
    through `Render`: it claims its whole rectangle (`ICustomHitTest`), or the mouse would fall
    through to the tab control. Check a new control with automation's `hit`.
- `Views/MainWindow`: the shell of concept A (below), the pages of the start (`SetupPage`,
  `UpdateAssetsPage`, `ConvertingPage`) and the command palette over it; asks about unsaved
  changes before closing.
  `WindowDialogs` answers the view model's questions with the system's folder picker and a small
  save-changes dialog.
- The window watches the project folder (`ProjectWatcher`) and reads back what other tools change;
  during a stroke the changes wait until it ends.

## Painting the terrain's grids

User stories T-1, S-1, T-6, W-2, K-1. The library's brushes do the work (its guide, "Brushes");
the tools hold their settings and make one stroke (`BrushStroke`) a step of undo.

- **`Tools/GridPaintTool`** is the core of every tool that paints a byte grid: radius, strength,
  erase, falloff, shape (`IShapedBrushTool`), and per tool what it paints (`Target`, a library
  `PaintTarget`), how the top view shows it (`Shown`, a `Rendering/GridOverlay`), whether it sets
  codes (`SetsCodes`, `Code`: a `CodeBrush`) or blends a mask (`MaskBrush`), its `Limits` and the
  history's text (`Describe`). A stroke paints `EditTransaction.Grid(target)`. The tools:
  `StrataTool` (`paint.strata`, its `Limiting`: slope and height, each pair on or off),
  `WaterPaintTool` (`water.paint`: foam, flatness or depth bias, half the map's size),
  `MaskPaintTool` (`paint.masks`: the mask chosen in the panel) and `TerrainTypeTool`
  (`terrain.types`: the game's types, set where the brush works at least half, sharp by default).
  A new grid to paint is a `PaintTarget` in the library and a small subclass here.
- **What the top and 3D views lay over the terrain** is `MainViewModel.ShownGrid`, one grid owned
  by the active mode and tool: the mask rules preview only in the Paint mode while its rule editor
  is open and the strata tool is not active (opening a mask's rules chooses the masks tool), else
  the active painting tool's `Shown`, else the chosen mask for tools while the overlay `mask` is
  on. `TerrainPicture` colours a mask in `MapMaskBrush` (`MaskColour`, read from the theme by the
  top view), a grid with a `Palette` in a colour per code, and colours
  everything again only when the overlay's `Key` changes. The terrain types' colours are
  `Rendering/TerrainTypeColours` (the game has none: by kind, dirt brown to lava red, a shade per
  type); the tool options show the chosen type and a legend of the types the map uses
  (`TerrainTypeTool.Legend`, counted only while the tool is active and no stroke runs; span
  `tool.count-types`), their swatches through `Controls/ArgbBrushConverter`.
- **Masks for tools** (K-1, `model.Masks`, `ViewModels/MasksViewModel`): the document's masks by
  name and the chosen one; the Paint panel's `MasksSection` lists them (with their size), names a
  new one or a new name in its field, and runs `paint.addMask {name?, half?}`,
  `paint.renameMask {name?, newName?}` and `paint.deleteMask {name?}` (each `[UndoStep]`; left out,
  the field's name or the chosen mask); `paint.chooseMask {name}` chooses as the list does. Later
  features read the masks from the document (`MapDocument.Masks`) by name.
- Brush presets keep the painting tools too (`BrushPresetSettings.Tool` is `strata`, `water`,
  `masks` or `types`; the water's mask and the terrain type are kept, the stratum and the mask for
  tools are the map's and left out).
- Automation: `state.painting` (the sculpt tool's falloff, factor, noise size and limits, the
  strata tool's limits, the water, masks and types tools with the legend, the masks for tools, the
  chosen one, `showMask` and the key of `shownGrid`).
- **With symmetry** every painting tool paints in every part of the map (section "Symmetry applied
  to edits" below): `BrushStroke` does it, so a new brush tool gets it by passing the brush's
  `Rotation` and `Mirrored` from the `SymmetricDab` it is called with.
- Not built yet: hydraulic erosion, limits for the water and masks tools.

## Tests with the real window, and labels for screen readers and agents

- `tests/FAForever.MapEditor.App.Tests/MainWindowTest` runs the real `MainWindow` without a screen
  (Avalonia.Headless with Skia): the mouse, wheel and keys as a person uses them, and frames of
  what it shows. Test input and views there: the camera's controls, alt and a click in the
  top view, painting as one undo step, switching views. The headless platform has no graphics
  card, so the 3D view draws nothing and its camera does not glide by itself (the tests let it
  arrive); what it draws is checked with automation in a real editor. The tests of the 3D view's
  input also catch a view that the mouse does not reach.
- Helpers for these tests: `HeadlessApp.Capture(control)` draws one control into a PNG (not the
  whole window; `SaveCapture` writes it into the output's `captures/` to look at), `TestAssets`
  writes converted assets with models made in the tests (a rock with a pyramid mesh; with
  `unitModel: true` a civilian unit `uec1901` with a box mesh: no file of the game), and
  `TestAssets.SandboxProject()` writes the sandbox of those assets (one of every kind of thing on a
  map) as a project to open. `AutomationTest` drives the window through automation.
- Give every control one can read or use a name for screen readers: `AutomationProperties.Name`
  (and `HelpText` where the use is not obvious) in the XAML, unless its own text says it (a
  button's content, a menu's header). `MainWindowTest.EveryControlOneCanUseHasALabelOrText`
  fails otherwise. Avalonia hands the names to Windows UI Automation (and the other systems'
  equivalents), and automation's `ui` returns them.

## Performance: finding out why it lags

`Diagnostics/Performance` measures named spans (`using Performance.Span measure =
Performance.Measure("3d.frame");`) and runs a watchdog that asks the user interface's thread to
answer every 50 ms: an answer 100 ms late or more is a stall, kept with the spans that ran
meanwhile; stalls of 250 ms or more are a warning in the log (Diagnostics, below).
Automation's `perf` gives per span the count, total, mean, 95th percentile and longest, and the
recent stalls (`perf reset=true` starts over). Measure a change before and after, and put a span
around new work that may grow with the map: `3d.*` (the 3D view's frame and its parts),
`top.*`, `model.*`, `tool.*`, `camera.smooth-ground`.

What it found, and the rules that came of it:
- Looking a texture up asks the file system (`File.Exists`, the time of the file); the 3D view
  drew 533 decal runs a frame and spent 33 of 35 ms doing so. It keeps the sources by path and
  looks them up again only when the map's files change or the asset watcher reports a change.
  Nothing per frame may touch the disk.
- The tab control takes the view of a hidden tab out of the tree, and the 3D view then drops its
  OpenGL context: every switch rebuilt the context, the heights, masks and textures (110 ms
  frames and stalls). The tabs only choose; both views stay in the tree and only their
  visibility changes (`MainWindow.OnViewChosen`).
- A hidden view does no work: the top view skips updating its picture while hidden and catches
  up when shown.

## Version control: commit beside save

User story F-18; the library's guide explains the repository (`ProjectRepository`). File, Commit
(`file.commit`, Ctrl+Shift+C, for a map saved as a project) opens `VersionControlPage`: while the
project is no repository, its setup with the choices first (the author, suggested from git's
global settings; Git LFS, on; a repository to share through), and "Turn on version control"
makes the repository and its first commit (`VersionControlViewModel.EnableAsync`); afterwards
the commit, with how the repository stands (branch, changed files, unsaved changes, the last
commit) and a message (`CommitAsync`: saves first when the map has changes). What fails shows on
the page.

- **Sharing**: Push and Pull on the page (`file.push`, `file.pull`, File menu), with how many
  commits are to push or pull. Pull is refused while the map has unsaved changes (and, the
  library's, while the project has uncommitted ones), and only fast-forwards; the files it
  changed are read again as other tools' changes are (`MainViewModel.ReadAgainFromDisk`). Signing
  in is git's credential helper's; the editor keeps no secret.
- **History** (`file.history {count?}`, File menu: opens the page): the commits with hash,
  subject, author and date; choosing one (`file.showCommit {commit}`) lists its files. "Go back to
  this commit" (`file.restoreCommit {commit}`) makes the map the project as that commit holds it,
  as one step of undo named after the commit, unsaved: the files on disk and the history stay, so
  nothing is lost; saving and committing makes it a new commit. The editor never checks a commit
  out over the project.
- Automation's `version-control` adds `ahead`, `behind`, `history` (20), `chosen` and `files`.

## Assets: the shelf in the dock

User stories F-8 and F-9, concept C1 of the canvas "Map editor UI concepts" (row C, with the
picker C2, the atlas C3 and the interactions C4). `AssetsViewModel` (`model.Assets`) shows the
workspace's `AssetCatalog` (the library's guide), read on a pool thread when the workspace is
ready and again with Modes, Assets, Read the assets again (`assets.reload`):

- **Kind** follows the mode (Terrain brushes, Paint textures, Props props, Decals decals, Units
  units, Lighting cube maps; another mode keeps it); the chips above choose it by hand, with how
  many each shows. Cube maps (L-1) show their first face; their Narrow scope is the sky's and
  the reflections' (`Sky and reflections`), Everything adds the water's.
- **Brushes** are the workspace's own, not the game's: the greyscale PNGs of its `brushes` folder,
  in folders as the map maker likes (the folders are the shelf's groups; Modes, Assets, Show the
  brushes folder, `assets.brushesFolder`). The editor watches the folder and reads the brushes
  again when a file changes; a thumbnail whose file changed is read again too. Choosing one makes
  it the shape of the active brush tool (sculpt or strata, above).
- **Show**: what the map uses (`InMap`), the map's biomes (`Narrow`: the biomes of the props,
  decals and strata it uses, all when none; for units the civilian ones), or `Everything`. Then
  the biome (or faction) and the category: the left column holds what filters, nothing else. The
  search (each word in the name, code, biome or category) fills the bar above, and at its right
  end is how the tiles look: a prop's sizes and variants as one tile (Variants, `Families`:
  `pine06` with `_s2`, `_v1`; the inspector lists them) and the tile size (a button that steps
  through small, medium and large; control and the wheel over the tiles).
- **Tiles** in rows of as many as fit (`Columns`, measured by the view): the list makes only the
  rows in sight, and `Controls/AssetThumbnail` reads a picture small on a pool thread when its
  tile first shows, keeping the last 600. Decals and textures show their PNG over a checker; props
  and units their mesh, drawn on the same pool threads (`AssetPreview`, the span `assets.preview`)
  at the control's `DecodeWidth`, and their kind's icon when the blueprint names no mesh. A badge
  counts the map's uses
  (`AssetUse`, counted again after every step), another the variants. The tool tip shows a tile
  larger, above the tile (`ToolTip.Placement` Top in the tile's style): under the pointer it
  would take the wheel and stop the shelf from scrolling.
- **Choosing** a tile (click, the arrow keys) shows it in the inspector, in place of the mode's
  parts until Clear or Escape: its picture and normal map, its variants, facts, uses, how a map
  names it (selectable, to copy), Show the file (`assets.showFile`), and what it will be for, with
  its story (placing: P-1, U-2, D-1; a stratum's texture: S-2). Ctrl+Shift+A (`assets.browse`)
  shows the shelf with the search field taken; its arguments set the filters.
- The dock is higher (`DockTallHeight`) while the shelf shows (`IsDockTall`).
- **The picker** (concept C2, `AssetPickerViewModel` as `model.Picker`, `Views/AssetPicker` over
  the window): one asset of a kind for a slot, a tool or a command. A handler asks for it with
  `IEditorDialogs.PickAssetAsync(kind, title?, current?)`, which answers the chosen asset's key
  or null; `WindowDialogs` opens the picker, a test's answers say the key (the interface's default
  cancels), and `DialogGuard` refuses it without a person, naming the argument `key`. So a command
  that picks takes the key as its argument for automation. The picker has its own search, filters
  and selection (the shelf's stay as they are), and opens with them cleared, on `current` when
  given: the biome, faction or folder; the category, for units their kind (land, air, naval,
  structure, other, and civilian, which is any of them); and for units the tech level (the
  catalogue's `TechLevel`). Each choice counts what it would show with the other filters. A
  prop's variants each have a tile (a slot takes one). The search has the keyboard; up and down
  move a row from it, the four arrows in the tiles; Enter or a double click chooses, Escape or a
  click beside it cancels. The shelf and the picker search alike (`AssetSearch`). Units show
  their mesh, with the game's icon in the tile's corner (U-1: `AssetTile.Icon` over
  `IconBackground`, the picture of its layer as the build menu draws it; `AssetThumbnail` with
  `Checker="False"`, so no checker shows through); the inspector shows the icon larger.
  - `assets.pick {kind?, key?}` (Ctrl+P, F-9) picks an asset of the active mode's kind (else the
    shelf's) and `units.pick {key?}` (U-1) a unit; the chosen one shows in the inspector, as one
    chosen on the shelf (a brush also becomes the active tool's shape).
- **Dragging onto the map** (C4): a tile dragged from the shelf (6 pixels with the left button)
  carries the asset's key (`Controls/AssetDrag`, a format that stays in the editor) onto the top
  or 3D view, which show where it would land in the status bar's place and hand the drop to
  `MainViewModel.DropAsset(key, x, z)` (ogrids). The active tool takes it if it is an
  `IAssetDropTarget` that accepts the asset; else `AssetDropped` is raised, where a mode places
  what is dropped. What nobody takes says in the status line which user story will (P-1, U-2,
  D-1). `assets.drop {key, x, z}` does the same for automation. The views only attach
  `AssetDrag.Attach` with how a point of the view becomes a place of the map.
- **Where each comes from** (F-8): after the catalogue, the shelf reads the archives' lists of
  files (`AssetOrigins`, the library's; span `assets.origins`, about 60 ms for the game's and
  FAF's four archives). The inspector says it ("Comes from": the game's `env.scd`, FAF's `env.nx2` and whether it
  replaces the game's, the workspace's own); the shelf's Source chips filter by it
  (`assets.browse {source}`; the answer's tiles carry `source`).
- **Missing** (F-8): the scope Missing lists, per kind, what the map uses that the workspace and
  the map's own files lack (`MissingAssets`, counted again when the map's uses change; span
  `assets.missing`), with how often, "Read the assets again" (`assets.reload`) and a Problem button
  that shows the `missing-texture` or `missing-model` problem of that path
  (`MainViewModel.ShowMissingAsset`). `assets.browse {scope: missing}` answers `missing` and
  `missingCount`.
- **Reading one asset again** (F-8): the inspector's Read again (`assets.reloadShown`), or
  `assets.reload {key}` (without a key the whole catalogue): `AssetsViewModel.ReloadOne` reads that
  asset's files again (`AssetCatalog.Reload`, the library's; span `assets.reload-one`), forgets its
  thumbnails (`AssetThumbnail.Cache.Forget`), and the 3D view reads only its model again
  (`ObjectModels` drops the catalogue's `ReloadedKeys` when the new catalogue was made from the one
  it knows).
- The cube maps a map names (the sky's, those units reflect, the water's) are missing too when the
  workspace has cube maps and lacks one; a change of them alone finds the missing again.
- Not built yet: stars and recents; a mark of the drop place drawn in the views.

## The map's look: shader, sky, grid and decals

User stories S-4, L-1 and V-3. What the views draw follows the map's settings as the game does:

- **The terrain shader** (S-4): the Paint panel's Shader list under the layers (`LayerTextures.Shader`,
  `paint.shader {name}`, one step of undo), with what the chosen one reads, a line each
  (`TerrainShaderCatalog`, the library's: the game's and FAF's techniques of `effects/terrain.fx`).
  A name the editor does not know (a mod's) is kept, listed and drawn as `TTerrainXP`. The 3D view's
  terrain shader blends as it says (`uBlend`): four or eight strata, a mask whole or from its
  middle up (`saturate(mask * 2 - 1)`), stratum 8 and the upper layer as textures or left out as
  map-wide data. The generated preview blends alike.
- **The sky** (L-1): the Lighting mode's inspector (`SkySection`, `LightingSettings.Sky`): the
  sky's cube map, the one units reflect (the map's `<default>` environment cube map, added when
  it has none), the background; `lighting.setSky {cubemap?, environmentCubemap?, background?}`
  and `lighting.chooseCubemap {target: sky|environment|water, key?}`, which opens the picker of
  the workspace's cube maps (the Water inspector's Cubemap has its Choose too). The 3D view draws
  the sky's cube map behind everything (`TerrainShaders.SkyFragment`: the direction of each pixel
  from the inverse camera matrix; span `3d.draw-sky`), and the water reflects its own cube map
  (else the sky's) by FAF's Fresnel term (`water2.fx`, F0 0.08) times its sky reflection (its
  old default 1.5 counts as 1). The water itself is drawn as the game: its ramp's colour at the
  depth over the ground (`terrain.fx`), then the surface colour by the depth, clamped to the
  colour's near and far. Cube maps are a strip of six faces in the workspace (`CubeMaps`, the
  library's), uploaded as a cube by `GpuTextureCache.GetCube` (texture unit 14, which no 2D
  texture shares); a map's own DDS cube map is read as one (`GameTextures.FindCube`).
- **The grid** (V-3): the Grid chip (`ShowGrid`) says its spacing (`OverlayItem.Text`, `Grid 16`);
  its context menu and `view.gridSpacing {spacing}` (1 to 256 ogrids, shows the grid) set it. The
  top view draws a line a pixel wide every spacing ogrids in sight, none closer than
  `HeightmapView.MinGridLineGap` pixels (span `top.grid`); the 3D view draws them on the terrain in
  the overlay shader (`TerrainOverlays.LineSpacing`), fading where they would lie closer than a few
  pixels. Token `MapGridLineBrush`.
- **The decals** (V-3): the Decals chip turns `ShowDecals` over (the same switch as Select's hide
  a kind): in 3D they are drawn or not; the top view draws each as a faint outline, turned as it
  lies, those in sight and not smaller than `HeightmapView.MinDecalSize` pixels, in one geometry
  (span `top.decals`, about 1.5 ms for Archsimkats Valley's 3634). Token `MapDecalBrush`.
- **Automation**: `state.looks`: the shader (name, known, strata, whole mask range), the sky (its
  cube maps, background, the water's, whether the 3D view drew the sky and the reflection), the
  grid (shown, spacing, lines the top view drew), the decals (shown, outlines the top view drew),
  the units with icons and the cube maps of the workspace, and the cube maps the map names that
  are missing.

## Events, presets, symmetry and generated results

Four small parts that later features build on:

- **Events** (user story F-13, `model.Events`, `ViewModels/EditorEvents`): `WorkspaceOpened`,
  `MapOpened`, `MapSaved`, `MapExported`, `DocumentChanged` (the library's `DocumentChange`: an
  edit with its description, an undo or redo, what was read again; a stroke once, when it ends),
  and `BeforeSave` and `BeforeExport`, whose handlers may stop it with a reason
  (`VetoableEventArgs.Veto`): the operation then throws `OperationVetoedException` before it
  writes anything, and a command shows the reasons in the status line. `MainViewModel` raises
  them from its operations, on the window's thread; a handler that throws is logged (`Editor`)
  and stops neither the operation nor the other handlers. Lua plugins (F-15) listen through
  the same part (section "Lua plugins").
- **Presets** (user stories F-10, T-2, `model.Presets`, `ViewModels/PresetsViewModel`): the
  workspace's `presets/<kind>/*.json` (the library's `PresetLibrary`), read again when a file there
  changes (`ProjectWatcher`; opening writes nothing, so a workspace without the folder is watched
  once the first preset makes it). A feature registers what it can save and apply as a
  `PresetKind` (kind, capture, apply); today `brush` (`Tools/BrushPreset`: the active brush tool,
  radius, strength, mode or erase, target, shape, rotation; not the strata tool's layer, which is
  the map's). A mode whose sections hold a `PresetSection(kind, title)` lists the presets of that
  kind (Terrain and Paint: brush presets), each a button that applies it and a cross that deletes
  it, with a name field and Save. `edit.saveAsPreset {kind?, name?}` saves (the panel's kind; a
  name made from the settings, such as `Raise 12`, when left out), `presets.apply {kind, name}` and
  `presets.delete {kind, name}` are the panel's buttons. A preset whose brush the workspace lost
  applies with a round brush and says so. Wave 1 registers lighting, water, units, markers and
  palettes of props the same way.
- **Symmetry** (user story F-12, `model.Symmetry`, `ViewModels/SymmetryViewModel`): off unless
  chosen; rotational for 2 to 8 players or one of the four mirrors, chosen in the combo box after
  the overlay chips or with `select.symmetry {kind?, count?}` (Ctrl+Shift+Y; `off`, `rotational`,
  `leftRight`, `topBottom`, `diagonal`, `otherDiagonal`). Without arguments it turns it on or off:
  on, the one used last, else the map's own (`MapSymmetry.Find` on its spawns and deposits), else
  rotational for two. Each map keeps its own (`MapDocument.Symmetry`, saved in `map.json`): a
  choice is a step of undo (`Choose the symmetry ...`, choices that follow each other one step), an
  undo or another map chooses what the map keeps (`MainViewModel.FollowMapSymmetry`), and another
  map forgets the one used last, so turning it on takes that map's own. A new map comes with
  rotational for its players. The top view draws the lines between the parts, dashed over a dark line,
  while the overlay `symmetry` is on (`HeightmapView.DrawSymmetry`), and so does the 3D view on the terrain.
  `Transforms(document)` gives the library's `MapSymmetryTransform`s (the maths is in
  FileFormats.Map's `MapSymmetry`); the edits mirrored with them are the next section's.
- **Symmetry applied to edits** (user stories F-12, S-1; `ViewModels/MainViewModel.Symmetry.cs`, the
  library's `SymmetricDabs`, `SymmetryEdits`, `SymmetryChecks`). While a symmetry is chosen and
  "Mirror edits" is checked (the check box after the combo box, `SymmetryViewModel.MirrorsEdits`,
  `select.mirrorEdits {on?}`; off keeps the guides and the check only), `EditTransforms(document)`
  is every part, else the identity alone:
  - **Strokes**: `BrushStroke` asks the tool's shape for one rotation per dab and calls the tool's
    dab once per image (`SymmetricDab`: place, rotation, `Mirrored`, the part's transform), in the
    stroke's one step. Sculpting reads its noise where the sample came from (`HeightBrush.NoisePlace`),
    so every part gets the same noise. Images that coincide are painted once (a round dab on an
    axis or at the middle; a shaped one when its turn and flip coincide too); images that only
    overlap paint twice, as a stroke crossing an axis is painted by itself and by its image. The
    props painter passes `mirror: false` and mirrors what it scatters (`PropScatter.Images`): a
    prop goes only with all its images (none outside the map or within the spacing). The eraser
    erases at each image.
  - **Placing**: `markers.place`, `props.place`, `decals.place` (and the place tool's drag),
    `units.add` and `units.placePreset` add the thing, then `MirrorPlaced` its images in the same
    step, and select them all. Spawns become the next armies, a path node links to the images (or
    partners) of the node it goes on from, a structure snaps to the build grid, a unit's image goes
    into the army of the partner spawn (the civilians keep theirs), in a group of the same
    name; areas are never mirrored.
  - **Moving with partners**: `edit.nudge`, `edit.rotate`, `edit.scale`, `edit.place`, `edit.delete`,
    dragging with a select tool and its handles (move, turn, scale), `decals.scale`, `decals.size`
    and the Decals tool's scaling drag take each selected thing's mirror partners (`PartnersOf`: the
    same kind and blueprint within an ogrid of the image, found once at the press): moved by the
    image of the offset, turned the other way in a mirrored part (about the image of the
    selection's centre with the pivot `Selection`: the library's `RotateObjectsAbout` with
    partners), scaled by the same factors along the images of the axes about the image of the
    centre (`ScaleObjects` with partners; a decal's partner gets the same width and length),
    deleted with it. A selection that holds a thing and its partners (what placing selects) stays
    symmetric: the thing selected first leads and the others follow as its images (the library's
    `Partners`), and a drag of the select tool puts the thing under the mouse first, so it follows
    the mouse and its images go their own ways. Span
    `model.symmetry-partners` (13 ms for Archsimkats Valley's 8304 props).
  - **Curves**: a curve added while the symmetry mirrors edits (the curve tool, `terrain.addCurve
    {mirrored?}`, `terrain.addLayer kind=curve`) keeps that symmetry (`HeightCurve.Symmetry`): its
    layer shapes its images too, the views draw them (`ToolMarks.Images`: dashed in the top view,
    thin in 3D), and the inspector's Mirrored (`terrain.setCurve {mirrored}`) turns it on or off.
  - **Copies**: `edit.paste` and `edit.duplicate` give the copies their images (`MirrorPlaced`) and
    select them all; `edit.duplicate` copies the selection without the partners of things selected
    before them (the library's `WithoutPartners`), so a partner's copy is the image of its thing's.
  - **The brush under the mouse** shows every image where a stroke paints (`BrushPreview.Images`,
    the library's `SymmetricDabs`): the top view draws each outline (a shaped brush's picture
    turned, and flipped in a mirrored part; span `top.brush`), the 3D view's brush pass draws one
    brush per image (`TerrainRenderer.Render` takes the list; the shader's `uFlip` flips the picture).
  - **Commands**: `select.symmetryPartners` (Select menu) adds the partners to the selection;
    `edit.mirror` (Edit menu, `[UndoStep]`) copies the selection into every other part where it
    has no partner (span `model.mirror-objects`). Both work with the guides only too.
  - **The check** `not-symmetric` (registered with the Problems part, run while a symmetry is
    chosen): a warning per spawn and deposit without a partner, naming where it is missing; a
    warning per blueprint for units and AI markers; a note per blueprint, texture or layer for props,
    decals and path nodes. About 30 ms of `model.problems` on Archsimkats Valley.
  - `state.symmetry` has `mirrorsEdits` and `mirroring`.
  - A decal's image is turned, not mirrored (the game draws no mirrored decal).
- **Generated results** (user story F-7, `model.Generation`, `ViewModels/GenerationViewModel`):
  the library's `GeneratedResults`, where a feature registers its `IGenerator` (its inputs: parts
  of the document). Nothing generates by itself: after every step (not every dab) the part
  compares each last result with the document (`model.generated`); the status bar names those
  out of date in the warning colour. `generate.stale {list?}` generates them again (or only
  lists them), with the inputs that changed; a result never generated is its own command's
  (`generate.walkability`, ...). It generates one result at a time, each from the document as the
  ones before left it, in the order one feeds the next (`GeneratedResults.InOrder`: a generator's
  `Outputs`, the parts of the document its result changes, among another's `Inputs`, or a
  generator another `Reads`, whose last result is handed to it): masks from rules before props from
  rules, walkability before path networks; a result made out of date by one put in place before it
  is generated too, each generator once, so one run leaves nothing out of date. A new generator
  that writes the document names its `Outputs`. Another map forgets the results. A feature registers its
  generator with the command that generates it (`Generation.Register(generator, commandId)`), and
  the panel lists it (`Rows`); walkability, the normal map and the preview do (next section).

## Walkability, the normal map and preview, and problems

User stories T-7, S-4 and F-14; the core's side is `ViewModels/MainViewModel.Generation.cs`, the
maths the library's (`Generation/Walkability`, `TerrainImages`, `Checks/`; its guide).

- **Generating one result**: `MainViewModel.GenerateAsync(id)` runs the generator on a pool thread
  from the version of the map at that moment (span `model.generate`, logged and measured as the
  operation `generate`), keeps the result, and puts a result that is part of the map
  (`IDocumentGenerator`: the normal map, the preview) into it as one step of undo, named
  `Generate the ...`; while a stroke holds its transaction that step waits until the stroke ends.
  Editing goes on meanwhile; a result of a version edited since shows as out of date. A second
  run of the same generator while one runs is refused. `generate.stale` puts what it makes into
  the map the same way (`MainViewModel.GenerateStale`).
- **The Terrain panel's "Generated from the layers"** (`GeneratedSection`, its template in
  `Views/ModePanel.axaml`): a line per registered generator (`GenerationViewModel.Rows`: how it
  stands, Generate or Generate again), then walkability's choices: the layer of units (land,
  hover, amphibious, water for naval units), the overlay, whether what changed shows, and what
  changed in words.
- **Walkability** (`generate.walkability`, `ViewModels/WalkabilityViewModel`, `model.Walkability`):
  the library's `Walkability.Generate`, the game's navigational mesh made from the document.
  The part notices when the registry keeps another result (by any command) and compares it with
  the one it replaced (`WalkabilityChanges`: per ogrid opened or blocked, the counts, new passages
  and closed paths); another map forgets both. Generating turns the overlay `walkability` on and
  says what changed in the status line. The top view draws it one pixel an ogrid
  (`HeightmapView.DrawWalkability`, span `top.walkability`, made again only for another result,
  layer or theme): where units cannot go tinted (`MapBlockedBrush`), and while the changes show
  the ogrids blocked (`MapNewlyBlockedBrush`) and opened (`MapOpenedBrush`) since the time before.
  `view.walkability {layer?, changes?}` chooses for automation. On a map of 81 km generating takes
  about 3 s. Not built yet: marking by hand where it must be blocked or open (the tool
  `terrain.walkability`, which needs a layer of its own in the project).
- **The normal map and the preview** (`generate.normalMap`, `file.renderPreview`): the normal map
  at the size of the map's own, its format kept (`TerrainImages.NormalMap`); the preview
  (`Rendering/MapPreview`, S-4), the one the `.scmap` ships with, is drawn with the strata's
  textures by `TexturedPreview` (the library's): the layers blended as the terrain shader
  blends them, lit by the map's sun, the water's ramp and surface colour over the ground, 256 by
  256; the textures found as the 3D view finds them. Without a workspace it is the top view's
  picture (`TerrainPicture`, without a mask) reduced to 256 by 256 by averaging. The preview's
  inputs are the heightmap, the water, the strata, their masks, the lighting and the terrain
  shader. Both are out of date when the heightmap changes, the preview also
  with the water level.
- **Problems** (`ViewModels/ProblemsViewModel`, `model.Problems`, the dock's Problems tab): the
  library's `ProblemChecks` (`Problems.Checks`; a feature registers an `IProblemCheck` of its own),
  run on the window's thread (span `model.problems`, about 3 ms on Archsimkats Valley's 8304
  props) when a map opens, 300 ms after the last step of undo (`Events.DocumentChanged`, not
  each dab), when walkability is generated, when the assets are read again, and before a save or
  export. Problems never stop a save or export (a map maker saves work in progress): the errors are
  logged, and the status line after it names them. The context holds the workspace's catalogue
  (in memory) and whether the workspace has a file (`Workspace.FindAsset`, each answer kept until
  the assets are read again), so no check touches the disk twice. A check that throws is logged
  (`Editor`) and finds nothing. The tab's header counts the errors and warnings; a double click or
  Enter on a line runs `problems.show {index}` (`MainViewModel.ShowProblem`): it selects the
  problem's thing, the top view centres on its place at 4 pixels an ogrid and the 3D camera
  focuses on it (30 ogrids around). `plugins.check` runs the checks now and shows the tab.

## Selection: picking things on the map and changing them

User story F-11. `MainViewModel.Selection` (`SelectionViewModel`) holds what is selected as the
library's `ObjectRef`s (`marker:12`, `prop:5`, `decal:3`, `unit:UNIT_5`, `area:AREA_1`), in the order
they were selected, and what was copied (`Clipboard`). It holds references, never the things: on
`DocumentChanged` what the document no longer has drops out (an undo of a paste), and a thing
that moves stays selected. Another map clears it. The core's side is
`ViewModels/MainViewModel.Selection.cs`; the editing itself is the library's (`MapObjects`,
`ObjectEdits`, its guide).

- **What can be picked** (`SelectableKinds`): the active `SelectTool`'s `Kinds`, else the mode's
  (`ModeKinds`: Markers mode markers, path nodes and areas; Props props; Decals decals; Units units;
  Map areas; the others every kind), of what the views show (`ShownKinds`: the overlays Markers,
  Path nodes, Props, Units, Areas; decals always). A hidden kind is not picked. A later global
  Select tool is a `SelectTool` with `ObjectKinds.All`.
- **The select tool** (`Tools/SelectTool`, `markers.select` and `units.select`; the Props and
  Decals modes get theirs with their stories): a click selects the nearest thing within a few
  pixels (`PickAt`, the library's `Nearest`: markers, props and units by their centre, areas by
  their edge, decals inside their rectangle), shift adds, control toggles, a click on nothing
  clears; dragging on empty ground draws a box (`IMapTool.Box`, drawn by the view) that selects
  what lies in it; dragging a selected thing moves the whole selection, one step of undo held
  from press to release as a brush stroke is (`StartStroke`, `Move the selection`), each thing as
  high above the terrain as it was. Its keys (`IMapTool.Key`, which the top view sends while it
  has the keyboard, after a click): Escape runs `select.none`, Q and E `edit.rotate` by 15
  degrees (shift 90), the arrows `edit.nudge` by an ogrid (shift 8). Letters are the tool's and
  not menu shortcuts, which may never be a single letter.
- **The views give a tool more**: `Press` and `Drag` with a `ToolInput` (the keys held, the size
  of a pixel on the map, so "a few pixels" holds at any zoom) and `Key`; a tool that needs
  neither implements the plain methods. The 3D view (and its picking) may call the same
  operations with places in ogrids: `PickAt`, `SelectAt`, `SelectInside`.
- **Commands**: `edit.copy` (Ctrl+C), `edit.paste {x?, z?, dx?, dz?}` (Ctrl+V: centred on the
  place, else at the mouse, `Pointer`, else where the originals were; selects the copies),
  `edit.duplicate {dx?, dz?}` (Ctrl+D, 2 ogrids beside), `edit.delete` (Delete, with the links
  to a removed marker), `edit.rotate {degrees?}` (15 when left out, clockwise in the top view),
  `select.all` (Ctrl+A, the kinds that can be picked), `select.none`; outside the menus
  `edit.nudge {dx, dz}`, `edit.place {x?, z?, heading?}` (the inspector's fields) and
  `select.set {refs, change?}` (automation; `change` is `Replace`, `Add` or `Toggle`). The ones
  that edit are `[UndoStep]` and need `Needs.Selection`, which `Selection.Changed` raises.
- **The inspector** shows the selection in place of the mode's parts (the shelf's chosen asset
  and the selection take the inspector from each other): the count and kinds, X, Z, the height
  (read only: it follows the terrain) and the heading in degrees (when something turns; areas
  do not), with "—" where the selected things differ; a value typed (Enter or leaving the field)
  goes to every selected thing through `edit.place`. Below, a section per kind
  (`SelectionSection`: `MarkerSelection`, `PropSelection`, `DecalSelection`, `UnitSelection`,
  `AreaSelection` in `ViewModels/SelectionInspector.cs`, each with its `DataTemplate` in
  `Views/Inspector.axaml`); a kind's own fields go into its class (refreshed by `Refresh` with
  the document, kept while its kind stays selected) and its template.
- **The top view draws** the areas (dashed, with their names), props (dots, sparse below 2 pixels
  an ogrid: smaller, fainter `MapPropSparseBrush` and at most one per 3 pixels square, so 8000 props
  do not hide the terrain; picking takes every prop; overlay off by
  default) and units (squares), only what lies in the view (span `top.things`), and an outline
  per selected thing: a ring, or the rectangle of a decal or area (`top.selection`, tokens
  `MapSelectionBrush`, `MapSelectionFillBrush`). Alt and a click send the 3D camera there.
- Automation: `state.selection` (count, kinds, the first 200 references, what can be picked, how
  many are copied, the inspector's X, Z and heading). `stroke` drives the active tool with no
  keys held and two pixels an ogrid.
- **Handles**: the selection has handles in both views, to move (along one axis), turn and scale
  it; section "Handles, hover and the 3D view's overlays".
- **A box in the 3D view**: dragging on empty ground with a select tool draws a rectangle on
  screen (`SelectTool.ScreenBox`, drawn through `Shapes` in the 3D view only, never in the top
  view) and selects what shows inside it: each thing of the kinds that can be picked whose place
  (at its own height) projects into the rectangle (`MainViewModel.SelectOnScreen`, span
  `model.select-on-screen`, 6 ms for Archsimkats Valley's 8304 props), with shift and control as
  a click. The view comes with the input
  (`ToolInput.View`, not top-down, with its mouse).
- **A box leaves areas alone**: a box (either view) and Select all take `BoxKinds`, the kinds that
  can be picked without areas, unless areas are all a tool picks (the Map mode's Areas tool); a
  click still picks an area by its edge. So a box over markers never copies the playable area.
- **Selecting more easily** (`ViewModels/MainViewModel.SelectionTools.cs`, `Commands/Select/SelectMore.cs`,
  `KindsAndIsolation.cs`): `select.similar {family?}` (Ctrl+Shift+G: the same blueprint, texture or
  marker kind; `family` the same family of the catalogue, span `model.select-similar`),
  `select.invert` (Ctrl+Shift+I), `select.insideArea {area?, add?}` (the selected area, else the
  one named, else the playable area; the mode's kinds, every kind shown in the Map mode),
  `select.isolate {on?}` (Ctrl+Shift+H: only the selection shows in both views and can be picked,
  `MainViewModel.Isolated`; the top view through `IsDrawn` and `DrawnMarkers`, the 3D view through
  `ObjectFrame.Isolated` and `HiddenDecalIds`, picking through `IsPickable`; another map ends it),
  `select.lockKind {kind, locked?}` (a locked kind shows but is not picked, `LockedKinds`, which
  `SelectableKinds` leaves out; locking takes it out of the selection) and `select.hideKind {kind,
  hidden?}` (the overlays of markers, path nodes, props, units and areas; decals in the 3D view,
  `ShowDecals`). The select tools' options show the lock as the selection filter: "Picks" and a
  check box per kind the tool picks (`SelectTool.Filters`, `SelectionFilterItem`; for a tool of
  more than one kind). The Map mode's "Select anything" (`map.select`) is a select tool of every
  kind. `state.selectionTools`: locked, isolated, decalsShown, boxKinds.
- **R and a drag turn**: while R is held (the views track it as `ToolModifiers.Turn`, as X and Z),
  dragging with a select tool turns the selection as the mouse goes round its middle (about the
  handles' pivot; shift snaps to 15 degrees, structures in quarter turns), one step of undo.

## Decals: placing, scaling, order and groups

User stories D-1 and D-2, the Decals mode (F8); the edits are the library's `DecalEdits`.

- **The place tool** (`decals.place`, `Tools/DecalPlaceTool`, `model.DecalPlaceTool`) places the
  decal it holds (`Decal`, an asset's key): chosen with Choose... in its options or
  `decals.choose {key?}` (the picker for decals; it makes the place tool active), or on the shelf
  and with Ctrl+P (`assets.pick`): a decal the inspector shows becomes the tool's, and in the Decals
  mode the place tool becomes active. A click places it centred on the mouse, `Width` wide (the
  options; its length from the shape of the texture's picture, read once from the PNG's header
  when chosen), turned by `Heading`, with the cut-off of the options (500 at first: what maps use
  most); dragging from the click spans its width from the press to the mouse (its centre half
  way) and turns its width along the drag. One step of undo held from press to release, as a stroke; the new decal is selected.
  `decals.place {x, z, key?, width?, length?, heading?, cutOff?}` does the same for automation and
  a drop. A decal tile dropped on a view is placed there: the place tool takes it
  (`IAssetDropTarget`), and while another tool is active `AssetDropped` does
  (`MainViewModel.Decals.cs`).
- **The select tool** (`decals.select`, `Tools/DecalSelectTool`) wraps a `SelectTool` for decals
  (click, box, move, Q and E, arrows, Delete) and scales: `DragMode` (the options' Drag box, or S
  for the next) makes dragging a selected decal scale the selection about each centre, both sides
  (`Scale`), the `Width` or the `Length` only, by how much further the mouse is from the
  selection's middle than at the press; one step of undo. The keys `=`/`+` and `-` (`OemPlus`,
  `OemMinus`, the keypad's) scale by a tenth (shift: a hundredth), the side the drag mode names.
  `decals.scale {factor?, x?, z?}` is the command.
- **The inspector's decal section** (`DecalSelection`): type, width and length (`decals.size
  {width?, length?}`), "Shown within" and "Hidden within" (`decals.lod {cutOff?, nearCutOff?}`, the
  cut-off and near cut-off LOD), each for one or many with a dash where they differ, and the order
  buttons. A section runs commands through `SelectionSection.Run`, which the inspector sets.
- **Order**: `decals.forward` (Ctrl+]), `decals.backward` (Ctrl+[), `decals.toFront` (Ctrl+Shift+]),
  `decals.toBack` (Ctrl+Shift+[) on the selected decals, in the Decals menu: the map's list decides
  what lies on top (the 3D view draws in its order, a click picks the last).
- **Decal groups** (`DecalsViewModel`, `model.Decals`; the panel's `DecalGroupsSection`): the
  document's `DecalGroups`, each a row with a check box (shown), its name (typed: renames), how many
  decals, and Select, Add, Take out and a cross; "New group from the selection". Commands:
  `decals.addGroup {name?}`, `decals.renameGroup {group, name}`, `decals.deleteGroup {group}` (the
  decals stay), `decals.addToGroup {group}`, `decals.removeFromGroup {group}`, `decals.selectGroup
  {group, add?}`, `decals.showGroup {group, on?}`; `group` is the group's id. Hidden groups are the
  editor's, never the document's: another map shows every group again. Their decals
  (`HiddenDecals`) are not drawn in 3D (`TerrainRenderer` leaves them out of its runs), not picked
  in either view (`PickAt` passes `IsShown` to the library's `Nearest`; `PickOptions.HiddenDecals`),
  not selected by a box or Select all, and drop out of the selection when hidden.
- **3D**: the renderer rebuilds its decal runs when `document.Decals` or the hidden set changes
  (0.7 ms for Archsimkats Valley's 3634 decals). The selected decals are drawn once more over the
  others (`DecalHighlightFragment`): a light tint of `SelectedStrokeBrush` with an outline two pixels
  wide, from their own instance buffer in runs by size class. The 3D view gives the active tool what
  its ray meets among the kinds it may select (`PickAt(point, SelectableKinds)`), so a prop over a
  decal does not stop a click of the Decals mode's tool.
- Automation: `state.decals` (the map's count, the place tool's decal, width, heading and cut-off,
  the select tool's drag mode, how many decals are hidden, the groups with id, name, count, shown).

## Units and armies

User stories U-2, U-3, U-4 and M-4. The editing is the library's (`ArmyEdits`, `UnitGroupEdits`,
`BuildGrid`; its guide); the window's side is `MainViewModel.Armies` (`ArmiesViewModel`: the
Units mode's tree and the Map mode's list, made again only when the armies, groups or counts
change, span `model.armies`), the place tool and the commands.

- **The place tool** (`Tools/UnitPlaceTool`, `units.place`): a click runs `units.add {x, z,
  blueprint?, heading?, army?, group?, snap?}`, one step of undo, a free `UNIT_n`; what is left out
  is the tool's. Its unit comes from the picker (Choose..., `units.pick`), a unit tile chosen on
  the shelf (`Assets.Shown`), or a tile dropped on a view (`IAssetDropTarget`; a unit dropped while
  another tool is active chooses the place tool and places it, through `AssetDropped`). Its army
  is the civilians (`NEUTRAL_CIVILIAN`, else the first extra army) until another is chosen, its
  group `INITIAL` (the group whose units the game creates at the start) or the group chosen in the
  tree. A structure (the catalogue's `IsStructure`, its `Footprint` from the converted JSON;
  1 by 1 without one) turns in quarter turns and snaps to the build grid: an odd size on a half
  ogrid, an even one on a whole. Q and E turn what is placed next (a structure or preset 90
  degrees, a mobile unit 15, shift 90); the select tool turns a selection of structures 90.
  A blueprint the workspace lacks is placed as it is, with a warning.
- **The tree** (`UnitGroupsSection`, a `TreeView` of `GroupNode`s with their units counted
  with the groups in them; expanded nodes stay expanded when it is made again): choosing a node is
  where the place tool places and the buttons work: Add (`units.addGroup {army?, parent?, name?}`,
  the chosen group when left out; the new group is chosen), Rename (`units.renameGroup`), Remove
  (`units.removeGroup {keepUnits}`: the panel keeps the units, which go to the group above),
  Select units (`units.selectGroup`), Move here (`units.moveToGroup {group, army?}`: without an
  army each unit stays in its own). `units.moveGroup {army, group, toParent, toArmy?}` nests a
  group. The inspector's unit section shows the selection's army (choosing another runs
  `units.moveToArmy {army, group?}`) and group. A path is `INITIAL/Town`, empty for the root.
- **Units presets** (`Tools/UnitPreset`, kind `units`, the Units mode's Presets): Save keeps the
  selected units, else the chosen group's, with places relative to their middle (on a whole
  ogrid) and heights above the terrain, in their groups below the deepest one holding them all
  (placed, the top one is named after the preset).
  Applying one makes it what the place tool places (`Preset`); a click runs `units.placePreset
  {name?, x?, z?, army?, group?, heading?}`: a new group named after the preset in the tool's
  group, turned in quarter turns. Choosing a unit leaves the preset.
- **Armies** (`ArmiesSection` in the Map mode, `map.armies {army?}` shows it): the list with each
  army's summary, Add army and Add extra (`map.addArmy {name?, extra?, x?, z?}`), and the chosen
  army's name (`map.renameArmy`), playable, team and factions (`map.setArmy {army, playable?,
  factions?, team?}`, one step), alliances (`map.setAlliance {army, other, stance?, mutual?}`)
  and Remove (`map.removeArmy`). The spawn follows its army (renamed, removed, added for a
  playable army); the library's guide says why teams are written after the team FFA.
- The panel's and inspector's commands are in no menu (`units.add`, `units.renameGroup`,
  `units.removeGroup`, `units.moveGroup`, `units.moveToArmy`, `units.moveToGroup`,
  `map.renameArmy`, `map.setArmy`, `map.setAlliance`, `map.removeArmy`).
- **Wrecks** (U-5; the library's `UnitWrecks`, its guide says why a group): the place tool's
  As wrecks (`AsWrecks`, the check box after Choose...) places into the army's `WRECKAGE` group in
  place of the chosen group (`units.add {wreck?}`; left out, the tool's); `units.asWrecks {wreck?}`
  (Units menu, `[UndoStep]`) makes the selected units wrecks or, with `wreck: false`, units again in
  their army's `INITIAL`, and says when an army is playable; the inspector's unit section has a
  Wrecks check box (a dash when some are) that runs it. The 3D view draws a wreck grey and dark (the
  instance's second row's fourth value, `ObjectShaders.ModelFragment`), the top view an ashen cross
  (`MapWreckBrush`) where a unit is a square; `get` answers `wreck`. Not built yet: placing a units
  preset as wrecks (a preset places into a new group of its name).
- Automation: `state.units` (`place`: unit, preset, army, group, heading, structure, footprint,
  asWrecks; `tree`: the chosen group and the first 200 groups with their units) and `state.armies` (name,
  playable, team, factions, spawn, units, alliances, chosen).
- Not built yet: dragging units or groups in the tree, team colours in the views.

## Markers: placing, linking, chains

User stories R-1 to R-6, the Markers mode (F6). The library does the editing (`MarkerEdits`, its
guide); the App holds the tools, the panel's part `MarkersViewModel` (`model.Markers`) and the
inspector's `MarkerSelection`.

- **Placing** (`Tools/MarkerTools.cs`, R-1): `PlaceMarkerTool` with its `MarkerPlacement` for the
  tools Spawn, Mass, Hydrocarbon and AI marker; a click runs `markers.place {kind, x, z, type?,
  layer?, linkTo?}` (one step of undo), which puts the marker on the terrain, names it one more
  than the highest of its kind (`Mass 12`, `Rally Point 03`, `LandPN7`) and selects it, so the
  inspector shows it. Deposits always go to an ogrid's centre (`x.5`, as the game puts them);
  spawns and AI markers to the tool's grid (`SnapChoice`: none, half an ogrid, ogrid centres).
  A deposit on ground that rises more than 1 over its footprint (`MarkerEdits.Steepness`: 3
  ogrids for mass, the extractor with its storages; 4 for hydrocarbon) says so in the status
  line, the tool's options (`Markers.PlacementWarning`) and the inspector.
- **Spawns** are the playable armies `ARMY_1` to `ARMY_n`: a new one is the next number, skipping
  an army's name such as the civilians' `ARMY_9`, and its army is added to the playable armies
  and to the armies; deleting one removes its army (kept as an extra army when it has units)
  and numbers the spawns after it down, armies and adaptive deposits with them. The library's
  `AddMarker` and `RemoveMarker` do it, so paste, duplicate and delete keep it too.
- **Path nodes by hand** (`PathTool`, R-3): a click on empty ground places a node of the tool's
  layer linked to the one selected node of that layer and selects the new one, so clicks lay a
  path (shift starts a new one); a click on a node goes on from it; dragging from a node to
  another runs `markers.link {a, b, unlink?}` (control unlinks). Nodes of different layers never
  link. Choosing the tool shows the path nodes and its layer. Generating them from walkability is
  `markers.connectPaths` (below).
- **Chains** (R-4): the panel's section lists them (`ChainsSection`); "From the selection" runs
  `markers.addChain {name?, markers?}` (the selected markers in the order they were selected,
  `Chain_1` or the next free name) and chooses it; its markers have up, down and take-out
  buttons (`markers.setChain {name, markers}`), its name field `markers.renameChain {name, to}`,
  Delete `markers.deleteChain {name}`. `ChainTool` adds the marker it clicks to the chosen chain
  (the first click makes one), control takes it out, Escape lets go of the chain.
- **The inspector** (`MarkerSelection`, R-2, R-5): the name of one marker (a spawn keeps its
  army's), the resource of deposits, the type of AI markers (`AiMarker.KnownTypes` and the
  selection's own), the layer of path nodes (their links to the old layer go), the no-rush
  offset of spawns (empty: around the spawn), and the army deposits wait for on an adaptive map
  (Always, an army, or Nearest: each the army of its nearest spawn); every field runs
  `markers.set`, for one or many, and shows nothing or a dash where they differ. A section's
  fields run commands through `SelectionSection.Run`, which the inspector gives every section.
- **Adaptive map** (`AdaptiveSection`, R-5): `markers.adaptive {on?}` and per playable army how
  many deposits wait for it.
- **Per kind** (`MarkerKindsSection`, R-6): a check box per kind (`MarkerKinds`: spawns, mass,
  hydrocarbon, AI markers, weather, and path nodes per layer), with how many the map has, runs
  `markers.showKind {kind, on?}`. `MainViewModel.ShownMarkerKinds` is what both views draw and
  picking takes (`MapObjects.Nearest`/`Inside`/`All`, `PickOptions`, `ObjectFrame`): the panel's
  kinds of what the overlays Markers and Path nodes show.
- Automation: `state.markers` (the kinds shown, what can be picked, the chains with their
  markers, the chosen chain, adaptive, the last warning, `paths`: the panel's layers, spacing,
  replace, status and result; `optionalGroups`; the active marker tool's settings, the Preset
  tool's preset).
- **Generated path networks** (R-3, F-7; the panel's `PathNetworkSection`, `MarkersViewModel.Paths`
  as `PathNetworkPanel`): check boxes per layer, the spacing (empty: the library's default by the
  map's size), "Replace the layers' own nodes", Generate, and how the result stands (the generated
  results' line of `pathNetwork`, in the warning colour when out of date). `markers.connectPaths
  {layers?, spacing?, airSpacing?, replace?}` sets the library's `PathNetworkGenerator.Settings`
  (what is left out is the panel's), hands it the last walkability so the mesh is not made twice,
  and runs `MainViewModel.GenerateAsync`: one step of undo, `Generate the path networks`. Without
  `replace` nothing of the map goes: a part of the ground that holds a node keeps it, two of the
  map's nodes that reach each other already get no new way, so generating twice adds nothing.
  The result is out of date when walkability's inputs change (the heightmap, terrain types, water
  level, deposits, areas), and `generate.stale` makes it again with the same settings, replacing
  or adding as they say. The Terrain panel's generated results list it too. About 130 ms on
  Archsimkats Valley, all four layers with walkability.
- **Weather** (R-1): the tools Weather and Clouds (`PlaceMarkerTool` with
  `MarkerPlacement.WeatherDefinition`, `WeatherGenerator`) place the game's templates
  (`markers.place kind=weatherDefinition|weatherGenerator`); the inspector's `MarkerSelection.Weather`
  (`WeatherSelection`, two `SettingGroup`s drawn by `Controls/SettingList`) shows the first selected
  definition's style, four cloud types and chances and drift, and a generator's forced type and
  cloud settings; a value goes to every selected one through `markers.setWeather {mapStyle?, type?,
  cloudType?, chance?, driftX?, driftZ?, forceType?, spawnChance?, cloudCount?, ...}`.
- **Markers presets** (F-10, kind `markers`, the panel's Marker presets): Save keeps the selected
  markers (the library's `MarkerPresets`); applying one chooses the Preset tool (`Tools/MarkerPresetTool`),
  whose click runs `markers.placePreset {name?, x?, z?}`: the markers around the place, under free
  names (a spawn is the next army), links and chains with them, selected. Not mirrored.
- **Optional groups** (R-5, the Adaptive map section): the groups `Optional_<option>_<n>` the map's
  armies have (`MarkersViewModel.OptionalGroups`, made again when the armies change), and the
  option and value of a new one with "Add group with the selected units", which runs
  `markers.addOptionalGroup {option, level?, army?, fromSelection?}` (the civilians' army when left
  out). The library's guide says what the convention is.
- **Links in 3D**: while path nodes show, the 3D view draws the links of the layers that show, a
  line in the layer's colour between points half an ogrid above the nodes, over everything at a
  fixed width (`TerrainViewGpu.PathLinks`: the pairs made once per version of the markers, placed
  on screen each frame with the camera's matrix made once, those off screen left out, as
  `ToolMapLine`s through the `ShapeRenderer`; span `3d.path-links`: about 2 ms a frame, and 4 ms of
  `3d.draw-shapes`, for the 9,900 nodes of path networks generated at a spacing of 8 on Archsimkats
  Valley).
- Not built yet: a line drawn while a link is dragged, the slope under the mouse before a click.

## Props: placing, painting, reclaim

User stories P-1, P-2, P-3, P-5. `MainViewModel.Props` (`PropsViewModel`) holds what the Props
mode shares between its tools: how placed props stand (the panel's Placing: `Alignment` by kind,
upright or with the terrain; random turn, tilt and scale; a sink), the palette, whether props
follow the terrain, and the reclaim. The maths is the library's (`PropOrientation`, `PropEdits`,
`PropPlacement`, `PropScatter`, `PropReclaim`; its guide).

- **Tools** (Props mode, F7): `props.select` (a `SelectTool` of props), `props.place`
  (`Tools/PropPlaceTool`: a click runs `props.place`, one step, and selects the new prop, so Q, E
  and Delete work on it; it takes a prop dropped from the shelf), `props.paint`
  (`Tools/PropPaintTool`: a `BrushStroke` whose dabs scatter palette props, density per 100 square
  ogrids and a least spacing to every prop, kept with a `PropGrid` made when the stroke starts;
  the first dab fills its circle, the next ones the strip they move on) and `props.erase`
  (`Tools/PropEraseTool`, all props or the palette's).
- **Where it paints** (P-3): `OnlyOn` and `NotOn` are `PaintLayer`s (a name and a function from
  the document to a mask), the map's strata today (`PaintLayer.Stratum`); the mask layers of K-1
  are to join `Layers` the same way. A place passes a `MaskCondition` (at least the threshold).
- **The shelf and dropping**: a prop chosen on the shelf in the Props mode becomes the place
  tool's prop, or with the paint tool goes into the palette (another variant of the same tile
  takes the place of the one it added). A prop dropped on a view is placed there in any mode
  (`AssetDropped`), or by the place tool when it is active.
- **Follow the terrain**: `MainViewModel.EndStroke` hands every kept stroke to
  `PropsViewModel.FollowStroke` before it commits: when the heights changed, the props on the
  changed tiles keep their height above the ground (`PropsFollowTerrain`), in the stroke's step
  (span `tool.props-follow-terrain`, about 3 ms on Archsimkats Valley). Any height brush gets it.
- **Commands**: `props.place {x, z, key?, heading?}`, `props.choose {key?}` (picker),
  `props.alignToTerrain`, `props.upright`, `props.dropToGround {sink?}`, `props.tilt {pitch?,
  roll?}` and `props.scale {scale}` (the inspector's fields), `props.randomize {turn?, scaleMin?,
  scaleMax?}`, `props.replace {key?}` (picker), `props.randomVariant` (another variant of the
  same biome, folder and family; the status line counts the props that are their family's only
  variant or unknown to the workspace), `props.addToPalette {key?, weight?}`, `props.removeFromPalette {key}`,
  `props.followTerrain {on?}`, `props.reclaim` and, for automation, `props.set {alignment?,
  randomTurn?, randomTilt?, scaleMin?, scaleMax?, sink?, radius?, density?, spacing?, onlyOn?,
  notOn?, threshold?, eraseRadius?, onlyPalette?}`. The ones on the selection act on its props.
- **Panel**: Placing, Palette (thumbnail, weight, cross), Palettes (presets of kind
  `props-palette`: the props with weights, the paint brush, the Placing settings), Rules (P-4,
  planned), Reclaim (map and per area, counted again only when the props, areas or catalogue
  change; span `model.props-reclaim`). The inspector's `PropSelection` adds pitch, roll, scale,
  the selection's reclaim and the buttons of the commands above.
- Automation: `state.props` (the place tool's prop, Placing, palette, paint and erase settings,
  reclaim with areas).
- Not built yet: symmetry for scaling and turning about the selection's centre.

## Masks and props from rules

User stories K-2 and P-4, generated only when asked (F-7). The rules and their generators are the
library's (`MaskRules`, `PropRules`, `RuleEdits`; its guide, "Rules"); the core's side is
`ViewModels/MainViewModel.Rules.cs`, which registers `MaskRulesGenerator` (`generate.masks`) and
`PropRulesGenerator` (`generate.props`) with `Generation`, so they show in the status bar, the
Terrain panel's generated list and `generate.stale`.

- **Mask rules** (`model.MaskRules`, `MaskRulesViewModel`; the Paint mode's inspector section
  `MaskRulesSection`, drawn by `Views/RulePanels/MaskRulesPanel`): Rules... beside each mask of the
  Paint panel runs `paint.maskRules {name?}`, which chooses the mask, turns the mask overlay on and
  shows its rules. Each rule is a card with its fields, a `SettingGroup` that runs
  `paint.setMaskRule {index, mask?, kind?, combine?, invert?, min?, max?, soft?, source?, scale?,
  seed?, radius?}` (merged per field as the map panels' fields are), up, down and a cross
  (`paint.moveMaskRule {index, to}`, `paint.removeMaskRule {index}`); a kind and Add
  (`paint.addMaskRule {kind, ...}`); Generate. The cards are made again only when the kinds, the
  mask or the layers to choose from change. A layer is chosen as the paint tool names it
  (`Layer 3: SandLight`, `Mask: forest`; `RuleLayers` turns it into `stratum:3` or `forest`).
- **The preview**: while a mask's rules are open, `MaskRulesViewModel` makes what they make on a
  thread of the pool (span `model.mask-rules-preview`; the masks with rules it reads first) when
  the rules or the mask change, and `MainViewModel.ShownGrid` shows it before any tool's grid
  (`PreviewOverlay`, a key per version so the top view colours it again). Generating writes it
  into the mask; the check box "Preview in the top view" turns it off.
- **Prop rules** (`model.PropRules`, `PropRulesViewModel`): the Props panel's `PropRulesSection`
  (`Views/RulePanels/PropRulesPanel`: the rules with their props and counts, a name field, Add,
  Delete, Generate and how they stand) and the inspector's `PropRuleSection`
  (`Views/RulePanels/PropRulePanel`: the palette, "Take the panel's palette", the layer conditions
  with a cross each and Add, and three groups of fields). Commands: `props.addRule {name?, layer?,
  density?, spacing?}` (the panel's palette and Placing), `props.chooseRule {rule}`,
  `props.setRule {rule?, name?, density?, spacing?, fade?, clustering?, clusterSize?,
  clusterCover?, minSlope?, maxSlope?, aboveWater?, seed?, alignment?, randomTurn?, randomTilt?,
  scaleMin?, scaleMax?, sink?}`, `props.setRulePalette {rule?, props?}`,
  `props.addRuleCondition {rule?, layer?, threshold?, forbid?}`, `props.setRuleCondition {index,
  rule?, layer?, threshold?, forbid?, kind?}`, `props.removeRuleCondition {index}`,
  `props.removeRule {rule?, keepProps?}` (its props go too, unless kept) and, in the Props menu,
  `props.keepFromRules` (the selected props of rules become hand-placed). Pasted and duplicated
  props are hand-placed.
- Generating runs on a thread of the pool through `MainViewModel.GenerateAsync` and is one step of
  undo (`Generate the masks from rules`, `Generate the props from rules`); 7 500 trees on Seton's
  Clutch (20 km) take about 35 ms.
- Automation: `state.rules` (`masks`: the mask being edited, its rules with their fields, whether a
  preview shows, how they stand, the masks with rules; `props`: the chosen rule, the rules with
  their summaries, its palette and number of layers, how they stand).
- Not built yet: `generate.stale` makes every result from the version it started with, so props
  generated in the same run as the masks they read are out of date again at once (generate the
  masks, then the props); the rule editor shows no preview of the props.

## Handles, hover and the 3D view's overlays

User stories F-11, L-1 and V-3. The maths of the handles is the library's (`SelectionHandles.cs`:
`SelectionFrame`, `HandleLayout`, `HandleMaths`; `ObjectEdits.RotateObjectsAbout`, `ScaleObjects`).

- **What the views give a tool**: `ToolInput.View`, a `ToolView`: where a place shows in the view's
  pixels, how large a pixel is on the map at a place, the mouse, and in 3D the ray under it
  (`PlaneAt` meets the handles' level). Without a view (automation's `stroke`, tests)
  `ToolInput.ViewAt` stands in with one straight down at the input's zoom. X and Z held in a view
  are `ToolModifiers.AxisX` and `AxisZ` (the views keep them from key down to key up).
- **What a tool draws at its size on screen**: `IMapTool.Shapes(view)`, lines, squares and
  triangles in the view's pixels in an ink by use (`ToolInk`: selection, fill, hover, the axes'
  `MapHandleAxisXBrush` and `MapHandleAxisZBrush`, shadow, sun), or a `ToolMapLine` in a colour of
  the map's own (the 3D view's links between path nodes). The top view draws them with
  Avalonia (`HeightmapView.DrawShapes`), the 3D view over everything without the depth test
  (`Rendering/Gpu/ShapeRenderer`, triangles made every frame, span `3d.draw-shapes`), with the
  tool's `Marks` at the terrain's height. A `ToolMapLine` with a `Depth` (the links between path
  nodes) is drawn first with the depth test and without writing depth, so a hill or model in front
  hides it. The 3D box selection (`SelectOnScreen` with the eye) leaves out what the terrain hides
  from the camera (`ObjectPlacement.IsHiddenByTerrain`, the line of sight in at most 256 steps, the
  things in parallel: 54 ms for Archsimkats Valley's props).
- **The handles** (`SelectTool`, which `DecalSelectTool` wraps, so every mode's select tool has
  them): the move square in the middle, the arrows along x and z, the ring, the scale squares on
  the selection's box (corners for props, sides too for decals and areas); the one under the mouse
  lights up. A press takes a handle before anything under it (a click without a drag still clicks
  what lies there; in the Decals tool's scale modes the decal under the middle square scales).
  Dragging the move square moves on the handles' level, an arrow along its axis, the ring turns,
  a square scales; shift snaps a move's centre to the grid of the settings (½, 1 or 4 ogrids), a
  turn to 15 degrees (structures always 90) and a scale to tenths; X or Z held moves along that
  axis (also dragging a selected thing). Each drag is one step (`Move the selection`, `Turn the
  selection`, `Scale the selection`) and calls the library's edits. The settings are
  `MainViewModel.Handles` (`HandleSettings`: shown, the pivot `Selection` or `Each`, the snap),
  in the select tools' options and `select.handles {show?, pivot?, snap?}` (without arguments: on
  and off). `edit.rotate {degrees?, about?}` (Q and E) and `edit.scale {factor?, x?, z?, about?}`
  go about the handles' pivot unless `about` says.
- **Focus and the status bar**: `camera.focusSelection` (View menu; F in the 3D view) shows the
  selection's box; the status bar shows how many are selected and the size of their bounds
  (`MainViewModel.SelectionStatus`).
- **The sun in 3D** (`SunTool.Shapes`): an arrow from the middle of the map towards the sun, a
  quarter of the map long; its head dragged moves the sun to where the ray meets the sphere it
  moves on (`SunTool.DirectionAt`, at least 5 degrees high).
- **Hover**: with a select tool active the 3D view picks what lies under the mouse (at most every
  60 ms after it moved: a pick costs milliseconds on Archsimkats Valley) into
  `Selection.Hovered`, drawn lightly highlighted (props, units and markers: `ObjectFrame.Hovered`,
  0.45 of the selection's highlight; areas in the selection's stroke colour). Decals under the
  mouse are reported but not drawn.
- **The overlays on the terrain in 3D** (`TerrainOverlays`, `TerrainShaders.OverlayVertex` and
  `OverlayFragment`): one pass over the terrain's whole lattice, made invariantly as the terrain's,
  so it lies exactly on it without a polygon offset (the depth test, less or equal, keeps trees in
  front at any distance), after the water and the markers, before the brush: the shade outside the
  playable area (`MapOutsidePlayableBrush`), walkability (one texel an ogrid, its colours made on
  the processor once per result, `Rendering/WalkabilityPixels`), the grid being painted or the
  mask for tools (`ShownGrid`: a mask in `MapMaskBrush`, the terrain types in their colours; the
  tiles a stroke changed are uploaded again), the areas' outlines (the first, the playable one,
  and the selected ones wider; the selected in the selection's colour) and the symmetry's guides
  dashed. Each shows while its overlay is on; up to 32 areas and 8 guides.
- Automation: `state.handles` (show, pivot, snap; for the active select tool the box's centre,
  heading and size, which handles, the one hovered and dragged, and `points`: where each handle
  shows in the top and 3D views' pixels, to aim the mouse at; `sunHead` with the sun tool; the
  status bar's text) and `state.hover` (the thing under the mouse in 3D).
- Not built yet: dashed outlines of the box, decals drawn lighter under the mouse, a label for
  the ruler in 3D, handles for wave generators.

## The map and its atmosphere: panels of settings

User stories M-1, M-2, M-5, M-6, W-1, L-1, L-2, S-2, T-3, V-5. The parts are
`MainViewModel.MapInformation`, `AreaList`, `ScriptFiles`, `WaterSettings`, `LightingSettings`,
`LayerTextures` and `NewMapPage` (`ViewModels/MainViewModel.MapPanels.cs`), each refreshed on
`DocumentChanged` by comparing its part of the document by reference (span `model.map-panels`),
so a dab of a brush costs nothing there. Their sections are records in
`ViewModels/MapPanelSections.cs`, each drawn by a view of its own in `Views/MapPanels/` (the
mode panel's and inspector's templates only name it).

- **Settings** (`ViewModels/Settings.cs`): a field is a `Setting` (`NumberSetting` with an
  optional slider, `TextSetting`, `BoolSetting`, `ChoiceSetting`, `ColorSetting`: three numbers
  `0.95, 0.84, 0.76` with a swatch, or a vector without one; `TextureSetting`: a path with an
  optional Choose button for the picker), grouped in a `SettingGroup` and drawn by
  `Controls/SettingList` (Enter or leaving the field gives the value). A value given runs a
  command with the setting's argument; the command edits with `MainViewModel.EditMap(description,
  edit, mergeKey)`, whose merge key is the command and the names of its arguments, so typing in a
  field again or dragging a slider is one step. A slider's binding is one way and only a slider
  the person touches (focus or pointer over it) gives its value: a slider coerces its value by
  itself when its range arrives after the value, which once rewrote every area to 1 by 1.
- **Commands**: `map.information {name?, description?, author?, version?, type?, preview?,
  noRushRadius?, adaptiveMap?, reclaimMass?, reclaimEnergy?}` (without arguments: the Map panel),
  `map.addArea {name?, x0?, z0?, x1?, z1?}` (selects it and chooses the areas tool),
  `map.setArea {name, rename?, x0?, ...}`, `map.removeArea {name}`; `file.openScript` (the
  project's `script.lua` in the system's application for `.lua`, written with a skirmish map's
  script when the map has none; the watcher reads back what is saved; without a person it only
  answers the path), `file.showMapFiles`; `file.new {name?, size?, height?, water?,
  waterElevation?, deepElevation?, abyssElevation?, biome?, players?}` (without arguments, with a
  person: the page `Views/NewMapPage`; the map is no project until saved, `EditedMap.New`);
  `water.set {...}`, `water.setWaveTexture {index, ...}`, `water.addWaveGenerator {x?, z?}`,
  `water.removeWaveGenerator {index}`, `water.setWaveGenerator {index, ...}`; `lighting.set
  {azimuth?, elevation?, sunDirection?, sunColor?, ...}` (colours and vectors as `[r, g, b]`),
  `lighting.preset {name?}`; `paint.chooseTexture {layer?, key?, normal?}` (the picker of
  stratum textures; an albedo brings its normal map), `paint.setLayer {layer, ...}`,
  `paint.addStratum {key?, scale?}`, `paint.removeStratum {layer}`, `paint.moveStratum {layer,
  to}`; `terrain.importHeightmap {path?}` and `terrain.exportHeightmap {path?}` (they ask for a
  file through `IEditorDialogs.PickFileAsync` and `PickSaveFileAsync`).
- **Azimuth and elevation** (`SunAngles`): the azimuth clockwise from north (up in the top view,
  -z), the elevation above the horizon; the map keeps the vector towards the sun.
- **Tools**: `AreaTool` (`map.areas`): drag on empty ground draws an area, a handle of a selected
  area resizes it, its edge or inside moves it, whole ogrids, one step from press to release;
  `RulerTool` (`terrain.ruler`): distance in ogrids and km (256 ogrids are 5 km), rise, slope,
  until the next drag; `SunTool` (`lighting.sun`): the sun stands where the mouse points seen from
  the middle of the map (straight up there, 5 degrees at half the map's size), one step a drag, in
  either view (in 3D its arrow's head is dragged across the sky). A tool shows handles and a line
  with a label through `IMapTool.Marks` (`ToolMarks`), which both views draw (the 3D view without
  the label).
- **Presets**: the kinds `lighting` (all of it) and `water` (its look; the elevations, wave
  generators and masks stay the map's), the library's `AtmospherePresets`; the Lighting and Water
  panels list them.
- The top view labels the playable area (the one the scenario names, else the first, M-3). `state.mapPanels` has the information,
  areas, script and files, water, lighting angles, layers, the ruler's measurement and whether
  the page of a new map is open.
- The sky's cube maps are in the Lighting mode's inspector (section "The map's look").
- Not built yet: placing wave generators with the mouse, templates for a new map.

## Map size, playable area and versions

User stories M-3 and M-8; the core's side is `ViewModels/MainViewModel.MapSize.cs`, the maths the
library's (`MapResize`, `MapScript`; its guide).

- **The Map size tool** (`map.size`, `Tools/MapSizeTool`): its options row is the dialog: Apply
  first (so a narrow window does not scroll it away), the width and height (the game's sizes), the
  anchor, the offset (where the old map's corner lands; typed or dragged it stops following the
  anchor), Fill at a height, Fit the playable area, and the summary. The views draw the new map's
  rectangle as the tool's `Box` with its size on the top edge (`Marks`); dragging it moves it by 4
  ogrids. Apply runs `map.resize` with the tool's `Arguments()`.
- **`map.resize {width?, height?, anchor?, offsetX?, offsetZ?, fillHeight?, fitPlayableArea?}`**:
  without a size it chooses the tool; with one it resizes on a pool thread (`ResizeMapAsync`, span
  `model.resize-map`, operation `resize`) and puts the result in as one step of undo. The status line
  says what was removed (decision: things outside are removed, spawns kept at the edge, areas cut)
  and that the normal map and preview are out of date. A document of another size (a resize, its
  undo) raises `MapChanged` and resets the camera, so both views start over; the tool takes the size.
- **The playable area**: `map.setPlayableArea {name?}` (none: the first area again); the inspector of
  one selected area has a check box "Playable area"; the top view labels it and the Map panel's
  list marks it.
- **Export** (`file.export {folder?, version?, raise?}`): `raise` raises the map's own version first
  (a step of undo: the project keeps it); a preview out of date is rendered before the export; the
  status names the version written and the next. The map's own preview and normal map count as
  generated for the map as opened (results kept on `MapOpened`), so the status bar tells when they
  are out of date; a normal map of one colour everywhere (a new map's or the sandbox's
  placeholder) stays missing.
- Automation: `state.mapSize` (size, playable area and whether it is named, version, next version,
  folder, the tool's settings and summary).
- Not built yet: the rectangle and handles in the 3D view.

## Uploading to the vault

User story M-8; the protocol and the vault's rules are the library's (`Vault/`, its guide, "The
vault: rules, zip, upload and sign-in"). `MainViewModel.Upload` (`VaultUploadViewModel`) holds the
upload and the sign-in.

- **`file.upload {server?, confirm?, token?, ranked?}`** (File, Upload to the vault...): refuses a map
  that is not publishable (the sandbox), runs the checks and stops on errors (showing the Problems
  tab), checks the vault's own rules (`VaultRules.Check`: the name, a description, the version, the
  folder and files named after the name), exports the next version (`NextVersion`) into a
  temporary folder (the map keeps its version meanwhile; a stale preview is rendered first, as for
  an export), checks the files the vault wants, zips it (`VaultArchive`, the folder in lower case),
  asks the person (`IEditorDialogs.ConfirmAsync`: name, version, file, size, and where to), signs
  in when needed, uploads with the progress in the status line (`BusyAsync("upload")`), and then
  makes the map that version (a step of undo, `Raise the version to n (uploaded to the vault)`).
  The vault's errors (JSON:API, with their codes) show in the status line and the log; a 401
  forgets the sign-in. The temporary folder is removed afterwards.
- **Never by accident**: without a person the command refuses FAForever's API (any host under
  `faforever.com`) whatever it is given, and needs `server` (a test server) and `confirm: true`;
  `token` (a bearer token) is taken only for a test server. A person uploads to FAForever's API
  unless `server` says otherwise. Nothing was uploaded to the real vault while building this: the
  tests and checks use `FakeVaultServer` (an `HttpListener` on the loopback address, in the
  library's tests and linked into these) or a small fake in Python.
- **Signing in** (`FafSignIn`): the system's browser (`IEditorDialogs.OpenInBrowserAsync`) opens
  Hydra's page, which sends it back to `http://127.0.0.1:<free port>/` with the code; PKCE S256, the
  development client (`FafOAuthSettings.Faf`, the official FAF client's id, which has `upload_map`
  and takes loopback redirects on any port), five minutes to answer. The tokens stay in memory, per
  API, until the editor closes; the refresh token renews an expired access token. Nothing secret is
  written to disk (a decision: one sign-in per session costs a person little, a token on disk is a
  key to their account). `OAuth` can point at a test server.
- Automation: `state.upload` (stage `Idle`/`Checking`/`Exporting`/`Confirming`/`SigningIn`/
  `Uploading`/`Uploaded`/`Cancelled`/`Failed`, progress, the last result and errors with codes,
  whether signed in, publishable, the next version). `run id=file.upload interactive=true wait=false`
  opens the confirmation dialog in a running editor (a window of its own, outside `screenshot`).
- Tests: `VaultUploadTest` (the multipart request, refusals: sandbox, errors, rules, a missing file,
  no confirmation, the real vault; the person's no; a sign-in once for two uploads; the vault's error).
- Not built yet: choosing a licence (`licenseId`, the vault's default), a page with the vault's
  versions of the map, cancelling an upload under way.

## Height layers and curves

User stories T-5 and T-4; the library does the work (its guide, "Height layers and curves").

- `MainViewModel.HeightStack` (`HeightLayersViewModel`, `ViewModels/MainViewModel.HeightLayers.cs`):
  the Terrain panel's list (`Views/MapPanels/HeightLayersPanel`, the highest first, a check box to
  show, one to lock) and the chosen layer, which is the session's `SculptLayer`: the sculpt tool
  writes into it, and refuses with the reason in the status line when it cannot
  (`HeightLayers.SculptProblem`). The inspector's "Layer or curve" (`HeightLayerPanel`) shows its
  settings (`SettingGroup`s `Layer` and `Curve`, made again when the map's masks change). The list
  is made again only when what it shows changes, not with each dab.
- Commands: `terrain.addLayer {kind?: sculpt|imported|curve, name?, path?, combine?}` (above the
  chosen layer; the first starts the stack), `terrain.addCurve {points, profile?, width?, falloff?,
  height?, closed?, name?}`, `terrain.setCurve {layer?, profile?, width?, falloff?, height?, closed?,
  points?}` (a field typed again is one step, points a step each), `terrain.setLayer {layer?, name?,
  combine?, strength?, mask?, visible?, locked?}`, `terrain.moveLayer {layer?, to?, by?}`,
  `terrain.removeLayer {layer?}`, `terrain.chooseLayer {layer}`, `terrain.reloadLayer {layer?}`,
  `terrain.bake`. Each edit runs in the span `model.height-layers`.
- **The curve tool** (`terrain.curve`, `Tools/CurveTool`): while the chosen layer is an unlocked
  curve, a click adds a point at its end (shift: into the nearest part), dragging a point moves it
  (one step), control and a click removes one, Delete or Backspace the last; otherwise the first
  click makes a curve layer with the tool's profile, width, falloff and height (a road's points lie
  on the terrain). Escape: the next click starts another curve. Both views draw the spline and
  the points (`ToolMarks.Path`, `Handles`); the 3D view on the terrain, each part cut into pieces
  of at most two ogrids so the line follows the ground (`TerrainViewGpu.TerrainLine`), and the
  tool works there as in the top view (on the ground under the mouse).
- Automation: `state.heightLayers` (chosen, the session's sculpt layer, the sculpt problem, the
  layers with their settings and curves, the curve tool's settings).
- On an 81 km map: adding the first layer 0.2 s, a stroke's move about 30 ms, a curve edit 0.3 to
  0.7 s with the views' own reaction to the new heights (debug build).
- Not built yet: a height per point by hand in the views, exporting a layer for World Machine or
  Gaea, merging layers.

## Lua plugins

User stories F-15 and the Lua part of F-1, F-13 and F-14 (`ViewModels/PluginsViewModel` as
`model.Plugins`, `Plugins/`, `Views/PluginsPage`, `Commands/Plugins/ManagePlugins.cs`). The runtime
is [Lua-CSharp](https://github.com/nuskey8/Lua-CSharp) (`LuaCSharp`, Lua 5.2 in C#, MIT): pure
managed code, its calls cancellable in the middle of a loop.

- **Where they live**: a folder each in the workspace's `plugins/` with its `plugin.lua` and an
  optional `plugin.json` (`name`, `version`, `description`; the folder's name otherwise). The
  folder's name is the plugin's id (letters, digits, dashes, underscores). They are loaded when the
  workspace is ready (`Events.WorkspaceOpened`) and a plugin is read again when a file of its folder
  changes (`ProjectWatcher`); `plugins.reload` reads them all. The first time a workspace opens,
  the examples (`Plugins/Examples`, embedded) are written into its `plugins/`; `.examples` names
  the ones written, so one the user deletes stays deleted.
- **Loading** runs `plugin.lua` once, in a Lua state of its own (one per plugin, made again on each
  load); at its top level it registers what it has. A script that fails registers nothing.
  Then its commands go into the registry (`EditorCommand.Owner` is the plugin, `Menu` the Plugins
  menu: `Menus.WithPlugins` puts them at its end, by title; the window makes the menus again on
  `CommandRegistry.CommandsChanged`), its checks into `Problems.Checks`, its fields into a panel on
  the page (a `SettingGroup` of `Setting`s that run `plugins.setField`).
- **On or off** per plugin (`plugins.enable {plugin, on?}`, the page's check box), remembered in
  the user's settings (`UserSettingsDto.Plugins`: the folders turned off, and the fields' values per
  plugin); off, its commands and checks are gone.
- **A run of a plugin's command** (`plugin.<folder>.<id>`, needs a map and no stroke) is one step of
  undo named `<plugin's name>: <command's title>` (`Plugins/PluginRun`): a group of the session
  (`EditSession.BeginGroup`, the library's `EditGroup`) holds the plugin's own edits, collected in
  one transaction, and the steps of the commands it runs (the transaction ends before each, so the
  command reads them). It is committed when the function returns and thrown away whole when it
  fails or runs out of time: a failing plugin changes nothing. Undo waits until the group ends.
  The function's first result is the command's result (JSON). Span `plugins.run`.
- **Safety**: the Lua has the base, string, table, math, bit32 and coroutine libraries, without
  `dofile` and `loadfile`, and no `io`, `os`, `debug` or `package`; `require` and `plugin.read`
  read only files of the plugin's own folder. A call has a time limit (`RunLimit`, 10 s, for a
  command, without the time of the commands it runs; `CallLimit`, 2 s, for loading, a check or an
  event): the script is cancelled where it is, and its error says so. Lua runs on the window's
  thread; an error is logged (`Plugins`), shown in the status line for a command, and listed under
  the plugin on the page (the last five), and never stops the editor. Memory is not limited.
- **Checks and events only read**: a check (span `plugins.check`) and a handler of an event (span
  `plugins.event`) get the map but no edits, no `editor.run` and nothing that waits. A plugin whose
  command is running (waiting for a command it runs) skips its checks and events meanwhile, since a
  Lua state runs one thing at a time. The steps inside a plugin's run raise no `changed` for the
  plugins; the run raises one when it ends. A plugin cannot run a plugin's command.
- **The page** (`plugins.manage`, Plugins menu): the folder (Show, Reload), each plugin with its
  check box, description and what it registered, its errors, a button per command and its panel's
  fields; `state.plugins` has the same (on, loaded, running, errors, commands, checks, events,
  fields' values).
- **Performance**: a check runs a moment after every step, so it must be cheap; one that reads many
  things keeps its result while `map.revision(part)` stays the same (the example's check: 60 ms to
  count Archsimkats Valley's 8304 props, nothing when they did not change).
- **Tools** (`plugin.tool{...}`; `Tools/PluginTool`, `ViewModels/PluginsViewModel.Tools`,
  `MainViewModel.PluginTools`): a plugin's tool joins a mode's tools after its own
  (`ModeItem.PluginTools`), with a command `tool.plugin.<folder>.<id>` in the Plugins menu, the
  palette and automation (`tool id=plugin.<folder>.<id>`); turned off or read again, it leaves the
  mode (its mode's first tool takes its place while it was active). The left mouse calls its
  `press(x, z, keys)`, `drag(x, z, keys)` and `release(x, z)` (places in ogrids; `keys`: `shift`,
  `control`, `alt`, and `picked`, the reference the 3D view picked). From press to release is one
  stroke (`MainViewModel.StartStroke`, `PluginRun` over it): the handlers edit with the same
  `map.*` calls as a command, the views show the edits while the mouse is down, and the stroke is
  one step of undo named `<plugin's name>: <tool's title>` when it is let go (none when nothing
  changed). Each call has `CallLimit` (2 s) and may not wait; `editor.run` reaches only commands
  that work during a stroke (those with `[UndoStep]` refuse). A call that fails or runs out of time
  throws the whole stroke away (the map shows as it was at once), says so in the status line and
  on the page, and the rest of that stroke does nothing. Symmetry is the plugin's: the handlers get
  the place the mouse is at. Its options row (`ToolOptions.axaml`, `tools:PluginTool`) shows the
  plugin's fields the tool names, set as on the page; `radius` (a number, or a number field's key)
  is the circle the views draw. `state.plugins.items[].tools` lists them. Span `plugins.tool`. The
  example `plateau-brush` (Terrain mode) flattens what it is dragged over to the height where it
  was pressed.

The Lua API (`Plugins/PluginApi.cs`; places in ogrids, heights in game units, headings in degrees,
things named by reference as in automation: `prop:5`, `marker:3`, `unit:UNIT_1`, `decal:2`, `area:AREA_1`):

| Call | What it does |
|---|---|
| `plugin.command{ id, title, description?, run = function(args) ... end }` | A command `plugin.<folder>.<id>`; `args` is the table of its arguments (automation's, or empty). Returns its id. Only at the top level of `plugin.lua`, as the next two. |
| `plugin.check{ id, title, run = function() return problems end }` | A check of the Problems tab; a problem is a text (a warning) or `{ message, severity = "error"/"warning"/"info", thing = ref?, x?, z? }`. |
| `plugin.field{ key, label, type = "number"/"text"/"bool"/"choice", default?, min?, max?, choices?, help? }` | A field of the plugin's panel; a number with `min` and `max` has a slider. |
| `plugin.tool{ id, title, mode = "terrain", description?, radius? (number or a field's key), fields? = { key or field table, ... }, press = function(x, z, keys) end, drag?, release? }` | A tool `plugin.<folder>.<id>` of a mode (`map`, `terrain`, `paint`, `water`, `lighting`, `decals`, `props`, `units`, `markers`, ...); its options row shows the fields named (a table is a new field, as `plugin.field` takes); a stroke is one step of undo. Returns its id. |
| `editor.on(event, function(...) end)` | `mapOpened`, `mapSaved`, `mapExported (folder)`, `changed (description, reason)`, `beforeSave (folder)`, `beforeExport (folder)`; a `before...` handler that returns a text stops the save or export with it as the reason. |
| `plugin.value(key)`, `plugin.read(file)`, `require(name)`, `plugin.id`, `plugin.name` | A field's value; a text file of the plugin's folder; `name.lua` of its folder (dots are folders), run once. |
| `editor.log(...)`, `editor.warn(...)`, `editor.error(...)`, `print(...)`, `editor.status(text)` | The Console (category `Plugins`), the status line. |
| `editor.run(id, args?)` | Runs any command of the registry without a person and answers its result as a table (commands only); an error when it does not run. |
| `editor.selection()`, `editor.select(refs)` | The selected references; selecting (commands only). |
| `editor.assets{ kind?, search?, limit? }` | The workspace's assets: `{ key, path, name, kind, category, reclaimMass, reclaimEnergy }`. |
| `map.name()`, `map.size()`, `map.height(x, z)`, `map.slope(x, z)`, `map.water()` | Name; width and height; the height between samples; the slope in degrees; the water's elevation or nil. |
| `map.markers(kind?)`, `map.props()`, `map.units()`, `map.decals()`, `map.areas()`, `map.armies()`, `map.get(ref)` | Plain tables: `ref`, `x`, `y`, `z`, `heading` and what the thing is (a marker's `kind`: spawn, mass, hydrocarbon, path, ai, weather). |
| `map.masks()`, `map.mask(name, x, z)`, `map.reclaim(blueprint)`, `map.revision(part?)` | The masks' names; a mask's value 0 to 1; a prop's mass and energy; a number that stays while a part (`map`, `props`, `markers`, `units`, `decals`, `heights`, `masks`) is the same. |
| `map.addProp{ blueprint, x, z, heading?, scale? }` | Adds a prop standing on the terrain (by its kind, as the place tool does); answers its reference. Edits are for commands only. |
| `map.move(refs, dx, dz)`, `map.place(refs, x, z)`, `map.rotate(refs, degrees)`, `map.remove(refs)` | `ObjectEdits` on one reference or a list. |
| `map.setHeight(x0, z0, x1, z1, height or function(x, z, height) return height end)` | Sets the samples of a rectangle (into the sculpting layer while the map has height layers); a function returning nil leaves a sample. |
| `map.paintMask(name, x, z, radius, value)` | Sets a mask's pixels within a circle to a value 0 to 1; answers how many. |

Anything else the editor does is a command: `editor.run("markers.place", { kind = "mass", x = 10, z = 20 })`.

## Diagnostics: what goes well and wrong

User story F-17. The editor keeps what a service keeps for Loki and Prometheus, per session
(`Diagnostics/Telemetry`):

- **Logs**: every `ILogger` of the editor (`Microsoft.Extensions.Logging.Abstractions`; get one
  with `MainViewModel.Logger(category)`, categories `Commands`, `Files`, `Workspace`,
  `Rendering`, `Performance`, `Automation`, `VersionControl`, `Assets`, `Editor`, `Vault`, `Plugins`). The Console of the dock shows them, with
  their level, newest last; its tab counts the warnings and errors, and it can show only those.
  Logged now: commands run and asked for while not built; opening, importing, saving, exporting
  with their duration, or why they failed; files read again; the workspace and the conversion of
  the assets (files skipped); the OpenGL the 3D view runs on, why it could not start or draw, a
  texture that could not be read; stalls of 250 ms or more; crashes (unhandled exceptions, also
  of the window's thread, written at once).
- **Metrics** (`Diagnostics/EditorMetrics`, `System.Diagnostics.Metrics`): `editor.commands`
  (command, result), `editor.log.records` (level, category), `editor.span.duration` (the
  `Performance` spans), `editor.ui.latency`, `editor.ui.stalls`, `editor.operation.duration`
  (operation, result), memory. Attributes only take values from a known, small set (a command's
  id, a span's name), never a path or a map's name. `MetricCollector` adds them up since the
  session started.
- **On disk**: `%LOCALAPPDATA%\FAF Map Editor\telemetry\<start>-<session>.logs.jsonl` and
  `.metrics.jsonl`, OpenTelemetry's JSON (`Diagnostics/OtlpJson`), one export request a line: logs
  every two seconds, the metrics (cumulative) every minute and at the close. The newest 30
  sessions are kept, each file up to 20 MB; the home folder is written as `~`. With
  `FAF_MAP_EDITOR_SETTINGS` set, the folder is `telemetry` next to that settings file, so test
  editors keep theirs apart.
- **The user's choice** (Help, Diagnostics; remembered in the user's settings with a random
  install id): recording on unless turned off; sending off unless turned on, and then only what
  is recorded from that moment, line by line to the endpoint's `/v1/logs` and `/v1/metrics`
  (OTLP over HTTP, JSON) every half minute and at the close, going on where it stopped
  (`<file>.sent`). `FAF_MAP_EDITOR_LOG=Debug` logs more.
- `tools/telemetry` runs a collector, Loki, Prometheus and Grafana with a dashboard on this
  computer (`docker compose up -d`): it reads the sessions' files and takes what an editor
  sends. Its README says what to look at.
- **Report a problem** (`help.reportProblem {folder?, description?, includeMap?, sessions?}`, Help
  menu; `ReportProblemViewModel` as `model.Report`, `Views/ReportProblemPage`, the zip by
  `Diagnostics/ProblemReport`): a page to say what went wrong and whether the open map's project
  goes along (off unless chosen), then a folder; it writes `faf-map-editor-report-<time>.zip` with
  `report.json` (the editor's version, the system, the runtime, the description, the sessions, the
  map), the newest five sessions' logs and metrics (this one flushed first) and the project without
  `.git` and `procedural/temp`, and shows the folder. Nothing is sent. Given a folder, the command
  writes at once and answers the path.
- Not built yet: a collector on a server for everyone's sessions, sending a report.

## Automation: driving the running editor

Started with `--automation <port>`, the editor takes commands over HTTP on
`http://localhost:<port>/` (`Automation/AutomationServer`): the operations of the menu, each run on
the user interface's thread and answered with its result and a short state as JSON (title, map,
modified, mode, tool, status, undo/redo, the selection's count, the question over the window);
`state` answers the whole state (section "What an answer carries" below). Use it to check a change
in the real editor:

```sh
faf-map-editor tests/FAForever.FileFormats.Map.Tests/assets/maps/HardFFA.v0001 --automation 5371 &
faf-map editor state                                      # the CLI is the client
faf-map editor brush mode=Flatten radius=30 strength=1 target=6   # runs brush.set
faf-map editor stroke points=[[60,128],[196,128]]         # in ogrids; one undo step
faf-map editor height x=128 z=128
faf-map editor screenshot path=shot.png map=true          # then look at the PNG
faf-map editor undo
faf-map editor close                                      # closes without asking about changes
```

- The state's `gpu` says which OpenGL the 3D view runs on, its error if it could not start, the
  textures on the card, the last texture that could not be read, the decals with their draws
  and textures, and `objects`: the props, units and markers drawn in the last frame, their draws,
  the models on the card (`meshes`, one per level), read and being read, the blueprints drawn as
  a box (`missingMeshes`) and the triangles of the models.
- The state includes `busy` and `stage` (`SetUp`, `UpdateAssets`, `Converting`, `Ready`), with
  `workspace`, `setupError` and `assetReasons`; a command that opens, saves or converts answers
  when it is done. Start a test editor with `FAF_MAP_EDITOR_SETTINGS` set to a file of its own, so
  that its setup never changes the workspace the user's editor remembers.
- `ui {hidden?}` returns the window's controls as an agent needs them (`Automation/UiTree`): type,
  name (`x:Name`), label (`AutomationProperties.Name`), help, text or value (sliders, check boxes,
  selected tab, list item or combo box entry), whether enabled and focused, and the place in the
  window (`[x, y, width, height]`, for `hit` and the mouse). Layout without anything to read or
  use is left out, and a control's template is not opened up. Read it before guessing from a
  screenshot.
- **Operations are the registry's commands**: `commands` lists them (id, title, arguments,
  shortcut, story, `notBuilt`, `canRun`), and `run {id, ...arguments}` runs one without a person:
  it asks nothing, so a folder it would ask for is an argument (`ran: false` when it cannot run
  now or is not built yet; its answer in `result`). A command's id works as the path too
  (`/view.fit`). `help` lists automation's own commands, the registry's and the aliases.
- **Aliases**, the names from before the registry, run a command with their arguments renamed:
  `setup {folder, game?, faf?, vault?}` (`workspace.setUp`, answer in `setUp`), `update-assets`
  (`workspace.convert`, `converted`), `open {path}`, `import {path}`, `save {path?}`,
  `save-as {path}` (the path is the command's `folder`), `commit {message}` (`commit`: the hash, or
  null when nothing changed), `export {output, version?}` (`folder`), `undo`, `redo`,
  `goto {step}` (`done`), `brush {layer?, mode?, radius?, strength?, target?, erase?, shape?, rotation?, randomRotation?}`
  (`brush.set`, above), `fit`, `camera {x?, z?, distance?, yaw?, pitch?}`
  (`camera.look`: the 3D view, degrees; at once), `focus {x, z, radius?}` (`camera.focus`: with a
  radius as F, gliding while the 3D view shows; without, as alt and a click in the top view),
  `view {x?, z?, zoom?}` (`view.zoomTo`: the top view; without a place the one in its middle
  stays, and it stays in the middle when the view changes size), `gpu-debug {mode}` (`view.gpuDebug`: what
  the 3D view's terrain shows: 0 the picture, 1 the albedo, 2 the light, 3 the normals, 4 the
  heights, 5 the lower layer, 6 the first four masks after the remap, 7 as stored), `mode {id}`,
  `tool {id}`, `tab {name: top|3d|side}`, `show {markers?, pathNodes?, camera?, props?, units?, areas?}` (each overlay's
  command with `on`).
- `state` has `symmetry` (kind, count, title), `presets` (folder, the panel's kind, the names it
  lists), `generated` (per generator its status, `Missing`, `Stale` or `Current`, and the
  inputs that changed), `walkability` (generated, the layer, passable ogrids, what changed: the
  counts, the new passages and closed paths with their places) and `problems` (the counts per
  severity, the summary, the first 200 with severity, check, message, thing and place).
- The shelf: `run id=assets.browse kind=texture scope=everything query=rock` answers with what it
  shows (filters, counts per kind, biome and category, the first 200 tiles with their keys);
  `run id=assets.select key=uec1101` selects one; `state` has `dock` and `assets` (kind, scope,
  query, group, category, summary, the selected key).
- The picker: `run id=units.pick key=uec1101` chooses without it. To see and drive the picker as
  a person does, `picker {open?, query?, group?, category?, tech?, move?: [across, down], choose?,
  cancel?}`: `open` runs a command that picks (`assets.pick`, `units.pick`) as the menu does,
  without waiting for the answer; the rest types, filters, moves and chooses (empty clears a
  filter). Its answer has `pickerTiles` (the first 60); `state` has `picker` (open, kind, title,
  filters with their counts, summary, the selected key).
- **Automation's own**, reads and input: `state` (with `mode`, `tool`, `toolNotBuilt`, `palette`),
  `ui {hidden?}`, `perf {reset?}` (above), `logs {problems?, count?}` (what the Console shows),
  `diagnostics {record?, send?, endpoint?, flush?, upload?}` (the session's files and choices;
  what it changes runs `diagnostics.set`, `diagnostics.flush`, `diagnostics.upload`),
  `picker {...}` (above), `version-control {open?, enable?, name?, email?, lfs?, remote?}` (how the project stands in
  git; `open` runs `file.commit` without a message, which shows the page, `enable` runs
  `file.enableVersionControl` with the choices, which go only with it), `mask {layer, x, z}`,
  `stroke {points}` (the left mouse, so the active tool, no command), `height {x, z}`, `hit {x, y}` (the control the
  mouse reaches at a point of the window, with its parents), `pick {x, y, highlight?}` (the
  object under a point of the 3D view, in its pixels: `picked` with kind, id and name, or null;
  `highlight` draws it highlighted), `screenshot {path, map?, gpu?, brush?}`
  (`gpu: true` reads a frame of the 3D view back from the card: a screenshot of the window does
  not reach it; `brush: [x, z]` draws the active tool's brush there in that frame, as if the
  mouse pointed at it), `close`. A new operation is a
  command of the registry, not a case here.
- The method does not matter, and a command without arguments works as a GET
  (`curl localhost:5371/undo`); Windows refuses a POST without a body (HTTP 411, before the
  editor sees it), so the CLI always sends one (`curl -d ""` does too).
- A command that does not run answers `ran: false` with `why`: what it needs that is missing
  (`nothing to undo`, `nothing is selected`, `no map is open`) or that it is not built yet
  (`CommandRun.Why`, `CommandRegistry.WhyNot`). An answer that cannot be written as JSON is an error
  (status 400), never a request left hanging.
- It listens on the loopback address only and only when asked to. Commands do not show dialogs:
  `close` does not ask about unsaved changes.
- Query parameters read as JSON where they can (`?step=3`, `?on=true`), as the CLI sends them.
- An MCP server for agents would be a thin layer over these same commands. The tests have a
  typed client, `EditorDriver` (`InProcess(automation)` headless, `Http(port)` for a running
  editor): `Set`, `Run` (throws with `why`), `Click`, `Key`, `PointOf`, `Grid`, `Focus`,
  `Screenshot`.

### Checking a change: set, focus, grid, parts

`Automation/AutomationServer.Tooling.cs` (tests: `AutomationToolingTest`).

- `set {label, value, under?, commit?}`: a control found by its label, name or text, set as a
  person would and through the control, so its bindings, coercion and commands behave as for a
  person: a slider gets the keyboard, then the value (clamped to its range); a text field gets the
  keyboard, its text replaced by typing, Enter and then losing the keyboard to the view (`commit`:
  `enter`, `blur` or `both`, the default); a check box or toggle button is clicked when it differs
  (`true`/`false`); a combo box or list chooses the item whose text is the value (else a number as
  the index); a tree chooses a shown item by its text; a list or tree item named itself is clicked.
  A setting behind the options row's More is shown first (`revealed`). The answer's `set` reads
  the control back. Numbers in text fields are read with a point or a comma whatever the
  computer's language (`EditorCulture`): `value=12.5`.
  `faf-map editor set label="Height brush strength" value=0.3 --port 5372`
  `faf-map editor set label="Height brush mode" value=Flatten --port 5372`
  `faf-map editor set label="Keep the heights within limits" value=true --port 5372`
- `focus {ref | refs | selection: true}` runs `view.frame {refs?}`: the top view zooms to the
  rectangle around the things (at least 24 ogrids across) and the 3D camera glides to see it;
  without refs, the selection. `focus {x, z, radius?}` stays `camera.focus`.
  `faf-map editor focus ref=unit:UNIT_1 --port 5372`
- `grid {target, x, z}`: one value of any grid of the map at a place in ogrids: `heightmap`, `types`,
  `stratum:N`, `water:foam|flatness|depthBias`, `mask:<name>` (a half-size grid is read at the cell
  under the place); `{target, x0, z0, x1, z1, step?}` reads a rectangle (at most 64 by 64 values)
  with `min`, `max` and `mean`.
  `faf-map editor grid target=stratum:1 x0=0 z0=0 x1=256 z1=256 step=32 --port 5372`
- `screenshot {path, part}`: one part of the window: `options` (with what shows under More),
  `inspector`, `panel`, `dock`, `rail`, `top`, `window`; `3d` reads the 3D view's frame from the
  card as `gpu: true` does.
  `faf-map editor screenshot part=options path=<scratch>/options.png settle=true --port 5372`
- Fixtures for such tests: `TestMaps.Slope(degrees)` (256 ogrids, flat at 40, a ramp of exactly
  one rise per ogrid from x 96 to 160, flat above; `SlopeProject` saves it as a project) beside
  `TestAssets`.

### Input as a person gives it, and aiming it

`Automation/AutomationServer.Input.cs` and `Automation/WindowInput.cs`. The input goes into the
window's own input (`PlatformImpl.Input`, raw mouse, key, text and drag events), the path the
operating system's input takes and the one Avalonia.Headless uses: hit testing, pointer capture,
focus, the window's key bindings and drag and drop all apply. The real mouse and keyboard are never
touched; the user's cursor stays where it is. The raw events are not in Avalonia's reference
assemblies, so `WindowInput.Raw` makes them by reflection and says what is missing if an update
changes them. Places are in device-independent pixels of a view (`view: top|3d`) or of the window
(the default); a hidden view is refused (`tab` first). The mouse finds what lies under it in the
last frame drawn.

- `mouse {view?, x, y, button?: left|right|middle, action?: move|down|up|click|doubleClick|drag|wheel, to?: [x, y], steps?, notches?, modifiers?}`:
  `click` is the default; `drag` presses at `x, y` and lets go at `to` after `steps` moves; `wheel`
  turns `notches` (positive zooms in). The answer's `mouse` has where it is and what it hit.
- `key {key, action?, modifiers?, repeat?}`: a name (`E`, `Delete`, `Escape`, `Enter`, `F5`, `Up`, `5`,
  `Shift`, `Ctrl`, `Alt`) or a gesture (`Ctrl+K`, `Ctrl+Shift+Z`); modifiers as a list or
  `Shift+Control`. Down, the text a printable key types, up, to the focused control (a view has the
  keyboard after a click in it). `action: down` holds the key until `action: up`: a modifier held
  goes with every mouse event and key meanwhile (shift held across clicks adds to the selection; X
  held while dragging keeps the axis). The answer's `focused` names the control with the keyboard,
  `held` the keys held down (also `state.views.keysHeld`).
- `type {text}`: text into the focused control (a search field), as an input method hands it.
- `drag {view?, from, to?, data?, button?, steps?, modifiers?}`: with `data` (an asset's key) a drag
  from outside dropped at `to`, as a tile from the shelf (`dropped`: what the target did); without,
  the mouse dragged with a button held.
- `run {id, interactive: true, wait: false}` starts a command as the menu does and answers at once
  (`started`, `finished`), so a picker or question it opens can be driven with `key` and `type`;
  `interactive: true` alone waits for it, unless it asks a question over the window (then it
  answers at once with `asked`, see "What an answer carries").
- `view-point {x, z, y?}` or `{ref}`: where a place (or a thing, at its own height) shows, in the top
  view's and the 3D view's pixels (`point.top`, `point.3d`, each with `inside`; null in 3D behind the
  camera), for `mouse` and `pick`. `get {ref}` describes one thing (`prop:5`, `unit:UNIT_1`,
  `marker:12`, `decal:3`, `area:AREA_1`): place, heading in degrees, blueprint, scale, rotation,
  army and group, a path node's links; `get {name}` finds a marker, unit or area by its name
  (ignoring case). `objects {kind?, name?, near?: [x, z], count?}` lists a few with their places,
  nearest first when `near` is given, and the total; `name` keeps those whose name, blueprint or
  texture contains it (ignoring case).
- `wait {path, value, timeout?}` (`Automation/AutomationServer.Waiting.cs`) reads the state every
  tenth of a second until the value at a path (keys joined by dots, a number for a list's item:
  `selection.count`, `stage`, `history.0`) matches `value` (numbers by value, the rest by their
  text), or `timeout` seconds (10, at most 120) passed; the answer's `wait` has `matched`, the value
  last read, the milliseconds and the reads. `state.build` says which build runs: `version` and
  `built` (the time of the App's assembly file), to tell an editor started before a build from one
  after. `EditorDriver` has `KeyDown`, `KeyUp` and `Wait`.
- `state` has `pid`, `views` (the render scaling, the window's size, per view whether it shows, its
  bounds in the window and its size in pixels, the automation mouse) and `loading` (`assetsLoaded`,
  the catalogue's count per kind, `thumbnailsPending`, `modelsLoading`, `texturesLoading`).
- `screenshot {settle: true, ...}` first waits (at most 15 s) until nothing is busy, the assets are
  read, no thumbnail is drawn and, for the 3D view, no model or texture is read, each checked after
  a frame (`settled` says how long and what it still waited for). `ui {under}` describes only the
  control of that name, label or text and what lies in it.
- `perf {gpu: true}` times the 3D view's frames on the card (`Rendering/Gpu/GpuTimer`): timer
  queries where the card offers them (ANGLE's `EXT_disjoint_timer_query`, OpenGL 3.3), recorded as
  the span `3d.gpu-frame`; else `glFinish` around the frame (`3d.gpu-frame-finish`, which stalls).
  `gpuTiming` says whether it is on and how it measures. Off by default.
- Headless, `AutomationServer.HandleAsync(command, arguments)` runs the same commands without HTTP
  (`AutomationTest`); force a frame first (`AvaloniaHeadlessPlatform.ForceRenderTimerTick()`), since
  the mouse hits what the last frame drew.

### Questions over the window, clicks by label, keys wherever the keyboard is

- **Questions are part of the window** (`ViewModels/QuestionViewModel`, `Views/QuestionDialog`): the
  unsaved changes before a map closes (`IEditorDialogs.AskToSaveChangesAsync`: Save, Don't save,
  Cancel; Save has the keyboard) and confirmations (`ConfirmAsync`: the action and Cancel; Cancel
  has the keyboard) show over everything, the pages too, as the palette does. Tab goes round the
  buttons, Enter or space presses the one with the keyboard, Escape cancels wherever the keyboard
  is; no shortcut and no tool key runs behind it (`CommandAdapter.Execute` waits). When it is
  answered the keyboard goes back where it was. One question shows at a time; a second waits. Only
  the system's file and folder pickers are windows of their own (and are refused without a person).
  A new question is `model.Question.AskAsync(title, message, buttons, cancel, focus)` behind an
  `IEditorDialogs` method, never a `Window`.
- Automation: `state.dialog` (`title`, `message`, `buttons`, `cancel`, null when none),
  `dialog {button}` presses one by its text (ignoring case), `ui`, `screenshot` and `key` reach it
  like any control. `run {id, interactive: true, wait: false}` or a shortcut (`key Ctrl+N`) starts a
  command that asks.
- `click {label, under?, button?, doubleClick?}`: the control named, labelled or reading the label
  (a control one can use before a text of that name), shown first (behind More, scrolled into its
  scroll viewer), then clicked in its centre; its answer has the type, the place, `checked` and
  `focused`. `ui {under}` and `set` find a control the same way: `Combine` is the combo box labelled
  so, not the text beside it. `ui` keeps underscores in texts (only menu headers lose their access
  key's), leaves out what a person cannot see (hidden, transparent, a text without room), reads a
  combo box's chosen item and its first 40 items by what they show (`Title`, `Name`, `Label`,
  `Text`, else their text), describes the controls inside a list's item (a layer's check boxes) and
  says whether an overlay's chip is on (`checked`); `set` turns a chip on or off. `state.overlays`
  has every overlay by id with on or off, `state.paletteResults` the palette's query, chosen and
  first 20 command ids while it is open. `mouse action=drag` that starts on a splitter (any
  `Thumb`) moves one frame at a time, so the layout follows it.
- **The tool's keys wherever the keyboard is**: a tool's or mode's button (`Button.tool`,
  `Button.rail`) gives the view that shows the keyboard after it is clicked, and a key nothing took
  while the keyboard is on a control of the editor's panels that takes no typing (a button, a check
  box, a slider) or nowhere goes to the active tool (`MainWindow.OnKeyLeftOver`; not while a page,
  the palette, the picker or a question shows). Both views and the window pass keys through
  `MainViewModel.ToolKey`, which first offers them to the brushes (`Tools/BrushKeys`): `[` and `]`
  make the active brush's radius a fifth smaller or larger (1 to 128 ogrids, at least one a step),
  shift with them its strength by a twentieth of its range (the props brush: density); control and
  the wheel over a view the radius, control, shift and the wheel the strength, without zooming
  (`MainViewModel.BrushWheel`). The sculpt, strata, mask, water, terrain types, props paint and
  erase brushes take them; the status line says the new value and the options row's sliders follow,
  also behind More. Control with `[` and `]` stays the decals' order.
- **Undo brings back the selection** (`MainViewModel.UndoSelection`): each version of the document
  remembers what was selected while it was the current one (not while an edit runs: a command that
  deletes clears the selection inside its step); undo and redo select that again, as far as the
  things exist, and the status line says `Undone: ...` or `Redone: ...`. A command with
  `[UndoStep]` whose handler leaves the status line as it was gets `Done: <its title>` (with what
  is selected), so the status line never shows an earlier operation's text.
- A mode's panel opens at its top (`ModePanel`), the Sun tool's row shows the map's sun before the
  first drag, the ruler's label stays inside the top view, and a combo box of a setting shows a
  value the map has that is none of its choices (added at the end of the list).

### What an answer carries, and reads that compare

`Automation/AutomationServer.Answers.cs` (tests: `AutomationAnswersTest`).

- **The state in an answer**: every command but `state` answers its own result and a short state
  (`title`, `stage`, `busy`, `map.name`, `modified`, `mode`, `tool`, `status`, `undo`, `redo`,
  `selection.count` and `kinds`, `dialog`). Any command also takes `full: true` (the whole state, as
  every answer had before), `quiet: true` (no state, only its result, `ran` and `ok`) and
  `paths: ["selection.count", "history"]` (only those values, each under its path as the key:
  `{"selection.count": 3}`; a path that leads nowhere is null). `state` and `perf` also take
  `path`. A path is keys joined by dots and numbers for a list's items; a key that holds dots (a
  span `3d.frame`) is found too, the longest key first: `perf path=spans.3d.frame.meanMs`.
  `faf-map editor state path=selection.count --port 5372`
- **perf**: `spans: "plugins"` keeps the spans whose name holds the text; `save: <file>` writes the
  snapshot (answers `saved`); `before: <file>` (and `after: <file>`, else now) answers `compare`:
  per span the change of its mean and total and both sides' count, mean, 95th percentile and total,
  the one whose mean grew most first (a span on one side only counts as zero on the other).
  `faf-map editor perf reset=true` ... `perf save=D:/s/before.json` ... change ... `perf before=D:/s/before.json`
- **ui {type?, text?}**: the controls of a type (`Button`, `Slider`, ignoring case) and/or whose
  name, label or text holds the text (ignoring case), as a flat list without what lies in them;
  with `under` only that part. `faf-map editor ui type=Slider text=radius --port 5372`
- **A command that refuses** (its handler throws because it cannot do it: nothing selected, a value
  it does not take, a file it cannot write) answers `ran: false` with `why`, as one that cannot run
  now does (`CommandRegistry.TryRunAsync`, logged as a warning), not an error; a command that does
  not exist stays an error (status 400). In-process the same: `HandleAsync("run", ...)`;
  `EditorDriver.Run` throws with `why`.
- **run and questions**: `run {id, interactive: true}` waits for the command unless it asks a
  question over the window: then it answers at once with `started`, `finished: false` and `asked`
  (the question, as `state.dialog`), for `dialog {button}`; `wait: false` answers `asked` too.
  `screenshot {part: dialog}` is a picture of the question's card (an error when none shows).
- **focus {ref | refs | selection, pitch?}** frames them in both views and puts the 3D camera at
  once at `pitch` degrees above the horizon (50 by default; 90 straight down), far enough to see
  them with a margin (`view.frame {refs, pitch}`; `MainViewModel.FrameMargin`); without `pitch` the
  command `view.frame` glides keeping the camera's angles, as Ctrl+Shift+F does.

### A test editor of one's own

```sh
faf-map workspace scratch D:/scratch/ws --from "<the real workspace>" --settings D:/scratch/settings.json
faf-map editor start --port 5398 --settings D:/scratch/settings.json [--map <folder>]   # waits until ready
faf-map editor objects kind=props count=5 --port 5398
faf-map editor stop --port 5398                                                          # waits until it exited
faf-map workspace remove-scratch D:/scratch/ws                                           # the assets stay
```

- `faf-map editor start` runs the editor built beside the CLI (or `--exe`, `FAF_MAP_EDITOR_EXE`) from
  a copy of its build folder (`ShadowCopy`, the CLI's: `--shadow <folder>`, by default
  `faf-map-editor-shadow/<port>` in the temporary folder; only files that changed are copied again,
  and of `runtimes/` only this computer's; files the build no longer has are removed), so
  `dotnet build` writes the build output while the test editor runs; restart it to run the new
  build (`state.build.built` tells). `--no-shadow` runs the build itself. It sets
  `FAF_MAP_EDITOR_SETTINGS` to `--settings`, detached: its output goes to a log (`--log`, by
  default `editor-<port>.log` beside the settings file) and it inherits none of the CLI's handles,
  so a caller reading the CLI's output (an agent's shell) gets it when the CLI exits
  (`DetachedProcess`). It refuses a port that answers already, and waits (up
  to `--timeout`, 120 s) until the stage is `Ready`, nothing is busy, a map is open and the assets are
  read; it prints the process, map and workspace. Without `--map` the editor opens the workspace's
  sandbox, which writes it into that workspace when it is missing: use a scratch workspace, never
  the user's. `stop` sends `close` and waits until the process has exited (`--force` ends it after
  `--timeout`), so the build output is free again.
- A scratch workspace (`ScratchWorkspace`, the library's) has its own projects, presets, brushes
  (copied) and vault (`vault/maps`, where exports go), and the real workspace's assets through a
  directory junction (no administrator needed): a test editor may save, export and write presets.
