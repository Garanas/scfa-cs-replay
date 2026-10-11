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
    `view.resizePanel`, `view.movePanel`, `view.returnPanel`, `view.setToolbar`, `view.addToToolbar`, `view.removeFromToolbar`,
    `view.inspectPart`, `presets.showFolder`), it selects what one
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
  Walkability and a mask tint the terrain in turn: turning one on turns the other off. The tints
  (mask, walkability, regions) belong to the mode they were turned on in
  (`MainViewModel.FollowModeTints`): leaving a mode keeps what it showed, entering one shows what it
  showed when it was left (none the first time), so the Paint mode's mask never lies over the
  Lighting mode's views. The 3D camera's footprint in the top view is cut to the map, so beside the
  map the view's backdrop stays one colour at any width. The other overlays stay as they are in every mode (`ModeTintsTest`).
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
- **Who gets a key** (`MainWindow.OnShortcutKey`, a tunnel handler; the window has no `KeyBindings`):
  the commands' gestures run before the focused control sees the key, except a text field's own
  keys (`MainWindow.IsTextKey`: every key without control or alt but the function keys, and with
  control A, C, X, V, Z, Y, the arrows, Home, End, Back, Delete), and none while a question, the
  palette or the picker shows. **Pages** (diagnostics, plugins, version control, report a problem,
  new map, keyboard shortcuts, workspace settings: `MainViewModel.OpenPageName`, `ClosePage`) take the keyboard when they open, give it
  back to the views when they close, take no shortcut and no tool key, and Escape closes them
  (after a combo box's list). Enter in a one-line text field gives its text to its binding as losing
  the keyboard does (`OnEnterInField`: the options row's numbers); Enter in New map's fields presses
  Create. Tests: `ShellKeysTest`.
- **The status line** says what the last operation did (`MainViewModel.Status.cs`): a step of undo
  made outside a command (a stroke, a drag, a tool's key) gets `Done: <step>`, a mode `Terrain mode:
  Sculpt`, selection commands `Selected ...`; placing with the symmetry ends with `and N mirrored`
  (`MainViewModel.Mirrored`), counts of units read `1 unit` (`MainViewModel.Units`). A command may
  name its undo step after what it did (`EditTransaction.Description`: `Move 2 units into INITIAL
  of ARMY_1`, `Set the X of the marker`).

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
  with `canRun`, the settings file, `places`), `state.floatingWindows`.
- **Moving panels** (`LayoutViewModel.Places.cs`, `Views/MainWindow.Layout.cs`): the mode panel,
  the inspector, the dock and each of the dock's tabs (`MovablePanel`) lie `left`, `right`,
  `bottom`, `float` (a window of its own) or, a tab, in the `dock` (`PanelPlace`); by default
  left, right, bottom and the dock. `view.movePanel {panel, place}` moves one, `view.returnPanel
  {panel}` puts a floating one back where it lay; each panel's context menu (a tab's header in
  the dock has its own) moves, floats, puts back or hides it and resets the layout
  (`MainViewModel.LayoutMenuCommand`). The shell holds three sides (`LeftSide`, `RightSide`,
  `BottomSide`, grids) each with its splitter; `ArrangePanels` (span `window.arrange-panels`, on
  `LayoutViewModel.PlacesChanged` and a panel shown or hidden) moves the same controls into their
  side (one above the other left and right, side by side at the bottom, in the order of
  `MovablePanel`), so they keep their state and bindings; an empty side collapses with its
  splitter, whose label names what it resizes ("Resize the mode panel and the inspector"). A size
  belongs to the side: the left's is `ModePanelWidth`, the right's `InspectorWidth`, the
  bottom's `DockHeight` (the settings' names, after the panel that lies there by default);
  `view.resizePanel {panel}` sizes the side the panel lies on. A tab moved out of the dock
  (`Dock.TakeTab`, `ReturnTab`) lies in a host with its name over its content (`Border.layouttab`);
  its header hides and the dock shows another tab; choosing it (`view.assets`) brings its window
  to the front when it floats. A floating panel (`Views/FloatingPanelWindow`) lies in a window
  owned by the editor's (above it, with the editor's shortcuts), whose place and size are
  remembered as the person leaves it; closing it puts the panel back
  (`LayoutViewModel.Return`); hidden while it floats, its window closes and opens again when it
  is shown; closing the editor keeps it floating for the next start. Saved in the settings'
  `layout.places` and `layout.floating` (`FloatingPanelDto`: place, size, where it goes back),
  only what differs from the default; `view.resetLayout` puts every panel back.
- Automation reaches a floating panel: `ui` lists each floating window as a node of type
  `FloatingWindow` (its panel's `name`, its `place` on the screen) whose controls' places are
  relative to that window; `click`, `set` and `ui under` find controls there too and give the
  mouse to that window (`click` answers `window`: the panel); `screenshot part=inspector|panel|dock`
  cuts the part from the window it lies in.
- Not built yet: dragging a panel by its header to another side, choosing toolbar commands in the
  window (only the context menu's Remove).

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

Help, Folders opens the workspace and its other folders in the system's file manager (section
"Panels list, the inspector edits", below), and File, Workspace settings edits where the game,
FAF's data and the vault are. The menu and the editor are hidden until `Ready`. To try the start without touching your own
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
  `MaskPaintTool` (`masks.paint`, the Masks mode's mask brush: the mask chosen in its panel) and `TerrainTypeTool`
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
  name and the chosen one; the Masks panel's `MasksSection` lists them (with their size), names a
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
- Measure the editor in the foreground, or compare before and after in the same state: a window
  driven by automation while another window has the focus may be slowed by Windows (on the
  wave 12 machine a stack composite took 200 ms in the editor and 15 ms in a test of the same
  code). The library's speed tests (`CoverageStacksSpeedTest`) time the maths alone.
- Work in proportion to what changed, at every level (wave 12): a layer added to a stack
  composites only where it works (none for a new painted layer), a few dirty tiles are composited
  on the calling thread, and the top view colours only the part of a tile whose samples or mask
  pixels differ (`TerrainPicture.ChangedBox`), many rectangles on several processors. Three
  stratum layers added and a strata stroke over them, before and after: Archsimkats Valley
  `tool.stroke` 13.8 to 1.8 ms a move, `top.document-changed` 8.6 to 0.8 ms, the layers 49 to 28 ms
  each and no stall; an 81 km map `tool.stroke` 15.5 to 3.9 ms, the layers 430 to 135 ms each (a
  painted one a few ms; a mask reference composites everything it changes).
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
- **Stars and recents** (F-9): section "Content to use again" below.
- Not built yet: a mark of the drop place drawn in the views.

## The map's look: shader, sky, grid and decals

User stories S-4, L-1 and V-3. What the views draw follows the map's settings as the game does:

- **The terrain shader** (S-4): the Paint panel's Shader list under the layers (`LayerTextures.Shader`,
  `paint.shader {name}`, one step of undo), with what the chosen one reads, a line each
  (`TerrainShaderCatalog`, the library's: the game's and FAF's techniques of `effects/terrain.fx`).
  A name the editor does not know (a mod's) is kept, listed and drawn as `TTerrainXP`. The 3D view's
  terrain shader blends as it says (`uBlend`): four or eight strata, a mask whole or from its
  middle up (`saturate(mask * 2 - 1)`), stratum 8 and the upper layer as textures or left out as
  map-wide data. The generated preview blends alike.
- **The sky's lower half**: the game shows a cube map's upper half (below the horizon lie the map
  and its skirt), so `SkyFragment` draws below the horizon the horizon's colour fading within a few
  degrees into a haze of the sky low above it; the default cube map's photo landscape never wraps
  the map. A cube map of the map's own files (one generated from the map, whose lower half is the
  map itself) shows whole (`uWhole`), so a planet across its horizon is not cut off. Uncompressed DDS cube maps are decoded by their own masks (`CubeMaps.Uncompressed`):
  ImageMagick mixes the faces with their smaller levels when the pixel format has no alpha
  (`skycube_desert01`). A workspace converted before keeps the broken strip until its assets are
  converted again (`faf-map workspace convert`).
- **The sky** (L-1): the Lighting panel's Sky and cube maps, edited in the inspector (`LightingSettings.Sky`): the
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
  lies, those in sight, in one geometry clipped to the map (span `top.decals`, about 1.5 ms for
  Archsimkats Valley's 3634). Only decals whose longer side shows at least
  `HeightmapView.MinDecalSizeOutsideDecals` (40) pixels are outlined, in the Decals mode
  `MinDecalSize` (8): at the whole map a decal-heavy map shows its terrain, and the outlines come as
  one zooms in or works with decals. Token `MapDecalBrush`.
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
- **Presets** (user stories F-10, T-2, `model.Presets`, `ViewModels/PresetsViewModel`; selection presets
  and stamps in the section "Content to use again"): the
  workspace's `presets/<kind>/*.json`, the kinds plural folders (the library's `PresetLibrary` and
  `PresetKinds`; moved there once when a workspace opens, below), read again when a file there
  changes (`ProjectWatcher`; opening writes nothing but that move, so a workspace without the folder is watched
  once the first preset makes it). A feature registers what it can save and apply as a
  `PresetKind` (kind, capture, apply); `brushes` (`Tools/BrushPreset`: the active brush tool,
  radius, strength, mode or erase, target, shape, rotation; not the strata tool's layer, which is
  the map's). A mode whose sections hold a `PresetSection(kind, title)` lists the presets of that
  kind (Terrain and Paint: brush presets), each a button that applies it and a cross that deletes
  it, with a name field and Save. `edit.saveAsPreset {kind?, name?}` saves (the panel's kind; a
  name made from the settings, such as `Raise 12`, when left out), `presets.apply {kind, name}` and
  `presets.delete {kind, name}` are the panel's buttons. A preset whose brush the workspace lost
  applies with a round brush and says so. Stamps, lighting, sky, water, units, markers, selections
  and palettes of props register the same way. A preset section shows only while the mode's panel
  shows it (`MainViewModel.PanelSections`: `ForTools`, `ForParts`), and the panel lists the kind of
  the first one shown: the Terrain panel's stamp presets with the stamp tool, its brush presets
  with the others.
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
- **Everything out of date or missing** (`generate.all {list?}`, Generate menu, wave 12): what
  `generate.stale` makes and, in the same order, every listed result never made for the map,
  except those a feature leaves out with a reason (`Generation.LeaveWhenMissing(id, document =>
  reason or null)`: the sky cube map, made only when asked; masks or props from rules the map has
  not; a map-wide decal taken from a file or none). The status line and the answer (`made`, each
  with `missing`; `left`, each with `why`) say what was made and why the rest was left.
- **Results kept in the project count as current when it opens** (`KeepMapPreview`, on
  `MapOpened`): the preview, the normal map (unless a placeholder of one colour), the rules layers'
  grids, the props of prop rules and the generated sky cube map (`env/cubemaps/skycube_generated.dds`,
  decoded for its picture) are kept as the results of the version opened, so the list reads current
  until what they are made from changes. Walkability, kept outside the map, is generated again on
  opening when it was current at saving (`KeptResults`).

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
- **The list of generated results** is the Procedural mode's (section "The Procedural mode"
  below): an entry per registered generator (`GenerationViewModel.Rows`, through
  `ProceduralViewModel.Rows` and `ProceduralListViewModel`), walkability's choices (the layer of
  units, what changed) in its entry's inspector.
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
  about 3 s. Marking by hand where it must be blocked or open: the Walkability tool (section
  "Content to use again" below).
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

## The Procedural mode: generated results, the navigation mesh, export steps

User stories F-7, T-7, T-8 and M-9 (wave 8C). The mode at the bottom of the rail (`procedural`,
F11; its definition is the last of `EditorModes.All`, its tools and sections are
`ViewModels/ProceduralSections.cs`'s `ProceduralMode`, so the rail's entry and the contents change
apart). The core's side is `ViewModels/MainViewModel.Procedural.cs` (`InitializeProcedural`, after
every other part).

- **The panel lists, the inspector edits** (wave 10D; `ViewModels/ProceduralParts.cs`,
  `MainViewModel.ProceduralParts.cs`, `model.ProceduralList`, the views
  `Views/ProceduralPanels/ProceduralList` and `ProceduralPartView`): the panel's one section
  (`ProceduralListSection`) lists an entry per registered generator under a heading (Movement:
  walkability, navigation mesh, path networks; Terrain surface: normal map, normal decal, shadow
  decal; From rules: masks, props; Pictures: preview), each with a picture where one exists
  (`ImagePreview`: walkability and the regions as `NavGridPicture`, the normal map and preview as
  `ColourGridPicture`, the decals their slot's, the path networks' nodes and links as
  `PathNetworkPicture`, the masks from rules as the first rules layer's grid, the props from rules
  as `RulePropsPicture`, a dot per prop a rule placed; `Rendering/PointPictures.cs`), its title, how it stands (`Missing`, `Out of date`,
  `Current`, `Generating`; a decal taken from a file or none says its source) and Generate (its
  command, the ids kept); Export last (`procedural.export`). The entries are a `PanelParts`
  (`PanelPartLists["procedural"]`, ids `procedural.<generator id>`), so `view.inspectPart` and
  `state.panelParts` know them; one is always chosen, walkability first. The inspector
  (`ProceduralDetailsSection`, through `PanelPartView`'s templates for `ProceduralPart` and
  `ProceduralExportPart`) shows the chosen one: its picture larger, how it stands with Generate
  ("from the inspector" in its label), Preview in the views, Regenerate on its own, the export's
  check box or what the export does, what it is made from (`IGenerator.Inputs` and the results it
  `Reads`), when it was made in this session and how long it took (`GenerationViewModel.TimingOf`,
  noted by `GenerateAsync`, `AutoGeneration` and `GenerateStale`), facts (the normal map's size,
  the rules' counts), then its details: walkability's choices (`WalkabilityPanel`), the navigation
  mesh's layer, routes and regions (`NavigationPanel`), the path networks' part (`PathNodesPanel`,
  which keeps its own Generate and export check box), a map-wide decal's settings, the export's
  steps (`ExportStepsPanel`). An entry with a tool picks it (navigation mesh: Regions, path networks:
  Path nodes), and picking a tool in the mode shows its entry; coming into the mode with its first
  tool keeps the chosen entry. A generator registered later, also after the list was made (the sky
  cube map), is listed by itself before the export under Other; `ProceduralList.Describe(new
  ProceduralEntry(id, group) { Details, Picture, Facts, Detail, CanGenerate, Tool, OwnGenerate, Help })`
  gives it a heading, details (a template in `ProceduralPartView`) and a picture. `state.procedural.list`:
  the chosen entry and each entry's state, detail, picture, tool and when it was made.

- **Generated results** (`model.Procedural`, `ProceduralViewModel`): a row per generator with a
  command (`GenerationViewModel.Rows`, the commands keep their ids: `generate.walkability`,
  `markers.connectPaths`, ...), with "Preview in the views" when an overlay shows its result
  (`ProceduralViewModel.PreviewWith(generator, overlay)`: walkability, `navigation`, `pathNodes`;
  the check box runs the overlay's command), "Regenerate on its own" for a generator that opted in,
  and what the export does (`OnExport(generator, ExportUse)`: the normal map `WhenOutOfDate` with a
  check box, the preview `Always`). The settings are the map's (`MapDocument.Procedural`, the
  library's `ProceduralSettings`, kept in `map.json`): `generate.auto {id?, on?}` and
  `generate.onExport {id, on?}`, each a step of undo, in no menu.
- **Regenerating on its own** (`model.AutoGeneration`, `ViewModels/AutoGeneration.cs`): for a
  generator in `ProceduralSettings.AutoGenerate` that opted in (`AutoGeneration.Allow(id)`;
  walkability does), `Follow` (on every `DocumentChanged`) compares its inputs (`IGenerator.Inputs`)
  with those it was scheduled for: a change restarts a timer of `Delay` (1 s); then it generates on a
  pool thread from the map as it is (`IGenerator.Generate(document, CancellationToken)`, span
  `model.generate-auto`) and keeps the result as any other; a newer change to its inputs cancels the
  run (walkability stops between the steps of `NavGenerator`) and starts the timer again. A
  person's Generate takes over (`GenerateAsync` stops it), another map stops everything. Another
  generator opts in with one `Allow` line, and overrides `Generate(document, token)` when it can stop
  midway. `state.procedural.auto` (`pending`, `kept`, `cancelled`); tests set `Delay` small and await
  `WhenIdleAsync`.
- **The navigation mesh** (`generate.navigation`, Generate menu; `model.Navigation`,
  `NavigationViewModel`; the library's `NavigationMeshGenerator`): per layer the regions (largest
  first, the first 12 listed: size, starts, deposits; a start within 8 ogrids of a region belongs to
  it, starts in none are named), the routes between the nearest starts or every pair
  (`generate.setNavigation {layer?, routes?, region?}`: the layer and the chosen region are the
  view's, the routes the map's, a step of undo). The overlay `navigation` (the Regions chip) colours
  the regions of the chosen layer (`Rendering/RegionPixels`, tokens `MapRegion1Brush` to
  `MapRegion8Brush`, `MapRegionChosenBrush`; dark where units cannot go) in the top view
  (`HeightmapView.Navigation.cs`, span `top.regions`, made again only for another mesh, layer, region
  or theme: 28 ms on a 4096 map) and in 3D in walkability's slot of the overlays' pass
  (`TerrainViewGpu.Navigation.cs`), with the routes as lines (`MapRouteBrush`; 3D: on the terrain,
  span `3d.routes`). One tint at a time: walkability, the mask and the regions turn each other off.
  The Regions tool (`procedural.regions`) chooses the region under a click. On a 4096 map the mesh
  with its routes takes about 2.2 s (routes are searched in the game's blocks of 4 ogrids).
- **Export as steps** (`model.Export`, `ExportViewModel`; `MainViewModel.ExportAsync`): the
  registered `ExportStep`s in their `Order` (`GenerateOrder` 100: the normal map when out of date
  and the map does not keep its own; `PreviewOrder` 200: the preview, always; `GameFilesOrder` 500:
  the walkability marks into the terrain types), then writing the map folder. The export works on
  a copy: the normal map and the preview are generated on the pool into the version it writes
  (`GenerateIntoExportAsync`, span `model.generate-export`), never into the project, whose history
  and generated results stay as they were (Generate in the Procedural mode keeps a result with the
  project); each step is a span `export.<id>`. Each step may change `ExportRun.Document`, the version the export writes, without
  touching the project: wave 9 registers the map-wide decals at `DecalsOrder` and generated path
  nodes at `PathNodesOrder` the same way (`model.Export.Register(new ExportStep(id, title, order) {
  Applies = run => ..., Run = async run => run.Document = ... })`). The status line says each step
  (`Exporting, step 2 of 4: Render the preview`), the Export section lists them with how each stands
  (waiting, running, done or not needed with a note, failed with why; a failed step stops the
  export), `state.procedural.export`. The file the export writes names the editor (the library's
  `EditorStamp`).
- **Reclaim** (P-5, M-1; `MainViewModel.ReclaimExport.cs`): the step `reclaim` at `GameFilesOrder`
  sets `ScenarioInfo.Reclaim` (the totals the lobby shows) in the version the export writes from
  the props only (the library's `PropReclaim.IntoScenario`, rounded; none without props), the owner's
  decision: units and wrecks are not counted. Its note says the totals and names the first three
  blueprints the workspace does not know, which count as 0 (`ReclaimNote`; logged as a warning).
  Without converted assets it is not needed and the map's own totals stay. An imported map's
  reclaim stays in the project and is written over by the next export.
- **Textures as DDS** (M-10, wave 10E; `MainViewModel.DdsTextures.cs`): the step `ddsTextures` at
  `TexturesOrder` 600, after every step that may add a texture, runs the library's `TextureExport`
  on the version the export writes (performance span `model.dds-textures`), with its shared cache;
  its note says how many were converted, how many came from the cache and the first names
  (`2 textures converted (2 as before): moss_albedo.png, moss_normal.png`), and the log names each
  with its format. Not needed (`every texture is DDS`) when the map's own files hold no picture.
- **Path networks** (R-3, wave 9D; `model.PathNodes`, `ViewModels/PathNodesViewModel.cs`, the core's
  side `MainViewModel.PathNodes.cs`, the view `Views/ProceduralPanels/PathNodesPanel.axaml` on the
  part's own view model, so it moves into an inspector as it is). Generated from the navigation mesh
  by the library's `PathNetworks` (its guide says how: skeleton, spacing, smoothing, links) and kept
  in memory only: the views draw the result faintly (the overlay `generatedPathNodes`, chip
  "Generated nodes"; the top view the path-node markers' look at half strength,
  `HeightmapView.PathNetwork.cs`, span `top.generated-paths`; the 3D view faint links and small
  crosses in the scene, `TerrainViewGpu.PathNetwork.cs`, span `3d.generated-paths`; the layers the
  Markers panel's kinds show, and while the Path nodes tool is active only the layer it edits, so
  the air's grid never covers the land's nodes: `MainViewModel.GeneratedPathKinds`), never into the document. The settings are the map's
  (`ProceduralSettings.PathNodes`): `generate.setPathNodes {layers?, spacing?, airSpacing?,
  smoothing?, minimumDistance?, fill?, replace?, onExport?}` (0 for a default distance), each a step
  of undo, which put the result out of date. `markers.connectPaths {layers?, spacing?, airSpacing?,
  smoothing?, replace?}` (the row's and the panel's Generate) keeps what it is given in one step,
  `Generate the path networks`, turns writing on export on the first time, and generates on a pool
  thread with the current walkability or navigation mesh. It may regenerate on its own
  (`AutoGeneration.Allow`). The row's export check box is `ExportUse.Written` (the map's
  `PathNodes.OnExport`, through `ProceduralViewModel.OnExport(id, use, isOn, set)`).
- **Edits by hand on top** (the owner's decision: generated nodes are the base, edits go on top):
  the Path nodes tool (`procedural.pathNodes`, `Tools/PathNodeEditTool.cs`, its layer in the options
  row): a click on empty ground adds a node (`generate.addPathNode {x, z, layer?}`), a click on a
  node chooses it (a ring in both views), a drag onto empty ground moves it
  (`generate.movePathNode {x, z, toX, toZ, layer?, within?}`), onto another node links them
  (`generate.linkPathNodes {x, z, toX, toZ, cut?, layer?, within?}`, control cuts), control and a
  click or Delete removes (`generate.removePathNode {x, z, layer?, within?}`); a line follows the
  drag. Commands find the previewed node nearest the place within `within` ogrids (4). While the
  symmetry mirrors edits, an edit is mirrored as markers placed by hand are: a node added gets a
  node at its image in every other part (inside the map, not on itself), and moving, removing and
  linking take the mirror partners along (the generated node nearest each image, within
  `MainViewModel.PathNodePartnerReach`, 4 ogrids), all in one step. Each edit is
  a step of undo kept in the map (`PathNodeSettings.Edits`) and applied after every generation;
  the panel counts them and those whose node is gone (`generate.clearPathEdits {layer?, unmatched?}`
  forgets them). How an edit finds its node again is in the library's guide.
- **Hand-placed path nodes and the generated network**: path-node markers (placed with the Markers
  mode's Path tool, or generated into the markers by an editor before wave 9) stay ordinary markers
  of the project, edited only by the Markers tools; the Path nodes tool edits only the generated
  network. At export (`ExportStep` `pathNodes` at `PathNodesOrder`, when on): the network with the
  edits goes into the version the export writes (generated first when out of date), named on from
  the highest `LandPN` and so on; the map's own nodes stay and each piece of them is linked to the
  nearest generated node it sees, or with Replace they are left out of the files. The project never
  holds a generated node.
- Automation: `state.procedural` (rows, auto, navigation with regions and routes, `showNavigation`,
  `routesTop`, `routes3d`, export steps); `state.pathNodes` (settings, status, the preview per layer,
  the edits in words, `unmatched`, the chosen node).
- **The map-wide normal and shadow decals** (9C) are generators too (`normalDecal`, `shadowDecal`,
  previews of at most 2048 pixels; may regenerate on their own) and have the panel section Map-wide
  decals with the inspector part of the chosen one (section "Decals: map-wide decals").
- **The sky cube map** (L-1, wave 10A; `model.SkyCube`, `ViewModels/SkyCubeViewModel.cs`, the core's side
  `MainViewModel.SkyCube.cs`, the generator `Rendering/SkyCubeGenerator.cs`, id `skyCube`): an entry of the
  Procedural list under Pictures (`procedural.skyCube`; its details `Views/ProceduralPanels/SkyCubeView`: Use and
  the settings; the facts say the sky and what uses it; on export `ExportUse.Copied`). The sky is drawn on the
  processor (the library's `SkyboxSky`), the map below the horizon by the 3D view's renderer when it is shown
  (`Controls/TerrainViewGpu.SkyCube.cs`, `ISkyCubeFaces`: six views of 90 degrees into a framebuffer of the
  face's size, `FaceViewProjection`, the generated sky as every cube map, the textures shared between the faces
  with `Render(sweep: false)` and the faces drawn again in the same frame while textures or models load, at most
  20 s; span `3d.sky-cube`), else on the processor (`SkyCubeMaps.DrawTerrain`; also when the view does not answer
  within `FacesTimeout`, and always on the UI thread, where `generate.stale` runs). Spans `model.sky-cube.sky`,
  `.gpu`, `.terrain`, `.dds`. On Sandbox with the 1196 sky objects of a skybox preset: about 0.8 s for the sky at
  1024, 80 ms for the faces on the card, 0.2 s for the DDS. Commands: `generate.skyCube {use?}` (Generate menu;
  one step of undo putting `env/cubemaps/skycube_generated.dds` among the map's files), `generate.setSkyCube
  {resolution?, eyeHeight?, terrain?, sunDisc?}` and `generate.useSkyCube {sky?, environment?, water?}` (all
  three when none is given; the water's cube map is no input, so using it keeps the cube current), and
  `lighting.importSkybox {path?}` (Lighting menu: a skybox preset of the FAF map editor, JSON, into the map's
  sky). `state.skyCube`. Tests: `SkyCubeTest`.

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

### Decal sets, map-wide decals, decal masks (wave 9C)

- **Decal sets** (the library's `DecalSets`): with Variants on, the shelf groups a decal's textures
  by name and suffix into one tile (`AssetTile.Set`, `set of 3`, the albedo's picture). Choosing the
  tile chooses the whole set (`AssetsViewModel.WholeSet`, `ShownSet`): the place tool's `Decal` is
  the set's name (`env/rocky/decals/rock01`) and it places one decal carrying the set
  (`DecalEdits.NewDecalSet`); choosing one of its textures in the inspector places that one alone,
  "Choose the whole set" goes back. `decals.choose` and `decals.place` take a set's name; a set of the
  map's own files (`/maps/<folder>/env/decals/rock01`, `DecalSets.OfMap`) works the same (Git Bash:
  `MSYS_NO_PATHCONV=1`). The 3D view draws a set's normals with its albedo (`TerrainRenderer.AddToRun`);
  the inspector names it `rock01 (decal set)` and its type `set of albedo + normals + mask main`.
- **Map-wide decals** (`MapWideDecalsViewModel` as `model.WideDecals`, `MainViewModel.WideDecals.cs`):
  three slots (`MapWideSlotViewModel`: source, file, the albedo's specular, the shadow's radius,
  strength, darkest and sun's shadows as `Setting`s; a preview `TexturePicture`; how it stands;
  Generate now). The Decals panel lists them above the groups (`MapWideDecalsSection`,
  `Views/DecalPanels/MapWideDecalsList`) and the inspector shows the chosen one while nothing is
  selected (`MapWideDecalDetailsSection`, through `PanelPartView`'s template `MapWideSlotView`); the
  Lighting panel's Shadow decal entry is the shadow slot; the Procedural mode's list has the normal
  and shadow decals as entries with the slot's settings as their details. `view.inspectPart` knows
  `decals.wideAlbedo|wideNormal|wideShadow` and `procedural.normalDecal|shadowDecal`.
  Commands: `decals.setWide {kind, source?, file?, specular?}`, `decals.chooseWideFile {kind,
  target?: file|specular, path?}`, `decals.setShadow {radius?, strength?, darkest?, sunShadows?}`,
  `decals.generateNormal`, `decals.generateShadow` (Generate menu). Files are read small on the pool
  for the preview. The 3D view draws the slots' previews under the map's decals
  (`WideDecals.Shown(document)`, decals with ids below 0, textures `editor:wide/<kind>` from
  `TextureOf`); the top view does not outline them. The export step `wideDecals` at
  `ExportStep.DecalsOrder` runs the library's `MapWideDecalsExport` (an 8192 normals decal and its
  mipmaps take about 3 s). `state.wideDecals`.
- **Normal decals in 3D** follow the game: the decal's normal is its own (up in its frame, turned by
  the decal's heading), replacing the terrain's by red (`TerrainShaders.DecalNormalFragment`).
- **Decal-masks layers**: the add choice `LayerStackViewModel.DecalMasksChoice` in the heightmap's
  stack (`terrain.addLayer kind=decals {mask?, height?}`, then `terrain.setDecalMasks {layer?, mask?,
  sets?, height?}`, the inspector's Decal masks fields) and in every stack of coverage (`layers.add
  source=decals {target, mask?}`, `layers.setDecalMasks {target?, layer?, mask?, sets?}`, the
  inspector's Decal masks fields under the stack). The session reads the masks through `model.DecalPictures` (`GameMaskPictures`).
- Tests: `WideDecalsAndSetsTest`, `WideDecalsWindowTest`.

### A map maker's own decals (wave 12B)

- **The map's own files on the shelf and in the picker**: `AssetsViewModel.MapEntries` are the
  decals among the open map's files (the library's `MapOwnAssets`: pictures in a `decals` folder,
  and pictures a decal names), listed again when the files change, in the group `this map` and from
  the source `Map`, whose chip reads This map; they belong to the map's biomes (Narrow). Their keys
  are game paths with the file's own extension (`maps/<folder>/env/decals/rock01_albedo.png`).
  `AssetsViewModel.Find` and `AllEntries` cover both, and every place that resolves a decal's key
  uses them (the place tool, `decals.place`, drops, stars, the picker). The tiles read pictures from
  files, so `MapOwnPictures` writes each file's bytes once into the temporary folder
  (`faf-map-editor/map-files/<process>`): a PNG as it is, any other picture made small on the pool
  (span `assets.map-picture`), after which the list is made again.
- **Sets in the picker**: for decals the picker shows a set's tile first (the albedo's picture,
  `set of 4`) and its textures after it; choosing the set's tile answers the set's name, which
  `PickAssetHandler.PickAsync` turns into the shelf showing the whole set (`AssetsViewModel.SelectSet`)
  and the status `Chose the decal set rock01 (...)`.
- **Add files to the map** (`map.addFiles {files?, folder?}`, the Map menu and the shelf's button
  while it shows decals): copies files into the map's own files, one step of undo; pictures go to
  `env/decals` unless `folder` says otherwise, anything else to the map's folder. The dialog takes
  several files (`IEditorDialogs.PickFilesAsync`). The argument is `files`: `paths` is automation's.
- **A decal-masks layer's fields** in a stratum's, mask's and water map's stack:
  `CoverageStackViewModel.DecalMasks` (Mask: `main` and the names the placed sets have; Decal sets:
  Every set and each placed set by its short name), shown under the stack while such a layer is
  chosen; `layers.setDecalMasks` and `terrain.setDecalMasks` take a placed set's short name and
  `Every set`.
- **On the map only**: a click of the place tool beside the map places nothing (the status says
  why) and a drag keeps the decal's middle on the map; `decals.place` refuses a place outside it.
- **Map-wide files**: a file chosen for a slot (or the albedo's specular) outside the project is
  copied into the map's own files as `env/decals/map_albedo.png` (`map_specular`, `map_normals`,
  `map_lighting`, as Kaali names them; `MapWideFiles.Keep`), in the same step; the slot names the copy.
- Tests: `MapOwnDecalsShelfTest` (the shelf, the picker, adding files, the fields, the window).

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
- **Example unit presets and army roles** (U-6): the library's `ExampleUnitPresets` (38 presets
  embedded in FAForever.MapEditor.Project, all the civilians': settlements and wreck fields) are
  written into `presets/units` when the workspace opens (`PresetsViewModel.Open`; a deleted one
  stays deleted, an edited one is left alone, an untouched copy of an example that no longer
  ships is deleted). A
  preset may name an army role (`UnitPresetSettings.Role`: civilian, enemy, player) and wrecks
  (`Wrecks`): `units.placePreset {..., wreck?}` then places it for `UnitPresets.ArmyFor` (the
  civilians; an army no player takes besides the civilians', `ARMY_9` first, and on a map without
  one, a new map's `ARMY_17` and `NEUTRAL_CIVILIAN`, an extra army `ARMY_9` added in the same step
  (`UnitPresets.ArmyOrNewFor`; the answer's `addedArmy`), so an enemy base is never the town's army;
  the nearest spawn's army)
  in place of the tool's army, unless `army` is given, and wrecks into the army's `WRECKAGE` group;
  with the symmetry a player's preset's images go to the partner spawns' armies (`MirrorPlaced`).
  The tool keeps the role (`PresetRole`, `state` `presetRole`); applying says the army in the status line.
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
- **Dragging in the tree** (U-3): section "Content to use again" below.
- Not built yet: team colours in the views.

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
  link. Choosing the tool shows the path nodes and its layer. The generated networks are the
  Procedural mode's (`markers.connectPaths`, edited with its Path nodes tool; below).
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
- **Generated path networks** (R-3, F-7): the Procedural mode's (section "The Procedural mode",
  Path networks): previewed, edited by hand on top, written by the export only. The Markers
  mode's Path tool places and links path-node markers, which the export writes beside the
  generated network (joined to it) or leaves out (Replace).
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
- **The palette is the tool's**, like a brush's settings: its changes are no steps of the map's
  history. Clear is the one that throws work away, so it can be taken back: Restore
  (`props.restorePalette`, shown while the palette stays empty) brings the props back with their
  weights, and so does undo right after Clear, before the map is edited again
  (`PropsViewModel.UndoesClear`; `MainViewModel.Undo` asks it first); `state.props.canRestorePalette`.
- **Panel**: Placing, Palette (thumbnail, weight, cross; Add a prop, Clear, `props.clearPalette`, and Restore), Palettes (presets of kind
  `props`: the props with weights, the paint brush, the Placing settings), Rules (P-4,
  planned), Reclaim (map and per area, counted again only when the props, areas or catalogue
  change; span `model.props-reclaim`; the export writes the map's totals into the scenario, section
  "The Procedural mode", Reclaim). The inspector's `PropSelection` adds pitch, roll, scale,
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

- **Mask rules** (`model.MaskRules`, `MaskRulesViewModel`, drawn by `Views/RulePanels/MaskRulesPanel`
  under the stack of a mask or stratum when a rules layer is chosen; section "Stacks of coverage"):
  `paint.maskRules {name?}` chooses the mask and its rules layer in the Masks mode, turns the mask
  overlay on and shows its rules. Each rule is a card with its fields, a `SettingGroup` that runs
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
- **Editing the rules shows what they make** (wave 12): a rule added, changed, moved or taken out
  (`paint.addMaskRule`, `setMaskRule`, `moveMaskRule`, `removeMaskRule`) makes every rules layer
  again from its rules in the same step (`MainViewModel.WithRulesMade`, span `model.mask-rules-made`:
  the masks from rules generated, about 70 to 200 ms a rules layer on an 81 km map) and keeps that as
  the generator's result (`KeepRulesMade`), so the layer, its thumbnail, the composite and the views
  show it at once and the entry reads current; the edit is the person asking. A change of what the
  rules read (the terrain, the water level, a mask without rules) leaves them out of date until
  Generate, as every result (F-7). Rules that cannot be made keep the edit and log why.
- **Props from rules follow the symmetry**: on a map with a symmetry (`MapDocument.Symmetry`) a
  rule's props come with their images in every other part (the library's `PropRules`), unless the
  rule's Mirror is off (`props.setRule {mirror}`, the inspector's "Mirror with the map's symmetry").
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
  the ruler in 3D.

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
  noRushRadius?, adaptiveMap?}` (without arguments: the Map panel; the reclaim is not typed: a
  read-only line under the fields, `MapInformationViewModel.ReclaimAtExport`, `state.mapPanels`'s
  `information.reclaimAtExport`, says what the export step `reclaim` writes),
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
- **Presets**: the kinds `lighting` (all of it), `sky` (its cube maps, background and dome; listed while the
  Lighting panel's sky is chosen) and `water` (its look; the elevations, wave
  generators and masks stay the map's), the library's `AtmospherePresets`; the Lighting and Water
  panels list them.
- The top view labels the playable area (the one the scenario names, else the first, M-3). `state.mapPanels` has the information,
  areas, script and files, water, lighting angles, layers, the ruler's measurement and whether
  the page of a new map is open.
- The Water and Lighting panels list what there is and the inspector edits the chosen entry
  (section "Panels list, the inspector edits"); the sky's cube maps are the Lighting panel's Sky
  and cube maps (section "The map's look").
- **Templates for a new map** (M-2): section "Content to use again" below.
- **Wave generators with the mouse** (W-1, `Tools/WaveTool`, the Water mode's Waves tool, the
  library's `WaveGeneratorEdits`): a click on the water places one at the water's elevation, its
  waves rolling the tool's `Heading` (compass degrees, 0 north) at its `Speed` (ogrids a tick, 0.02);
  dragging from the place aims them the way the mouse goes. The game keeps the direction twice:
  the velocity's x and z, and the rotation, pi minus the direction's angle from +z (as abhor.v0004's
  211 generators have it); `WaveGeneratorEdits.Aimed` sets both. With the symmetry mirroring edits
  its images go into the other parts in the same step, their directions turned or mirrored. A
  press on a generator's square (within 8 pixels) selects it in the Water inspector and a drag
  moves it (one step; a click that moves nothing is none); Delete removes the selected one. While
  the tool is active both views draw every generator (`IMapTool.Shapes`: a square and an arrow the
  way its waves roll, the selected one in the hover ink; span `tool.wave-shapes`). Generators are
  not things of the selection (they have no ids): the tool selects and moves them itself.
  `water.placeWaveGenerator {x?, z?, heading?, speed?}` (Water menu) chooses the tool, or with a
  place places one (and its images) at once. `state.waves`: the tool's heading, speed, the
  selected generator and the generators' places and headings.

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

## Layer stacks: the stack control and previews

Wave 8; the model is the library's (its guide, "Layer stacks"). Wave 9 builds the stratum, mask and
water stacks and the draft and final heightmaps on these two pieces.

- **The stack control** (`Controls/LayerStackView`, over a `ViewModels/LayerStackViewModel`): the
  layers, the highest first, each a row with a show box, a preview, the name, a summary
  (`Sculpt · add · 50 % · mask rivers`) and a lock box; buttons to add a layer by source
  (`AddChoices`, each a `LayerAddChoice`: text, label, help, command and arguments), Up, Down and
  Remove for the chosen layer; with `ShowsSettings` the chosen layer's `Layer` settings (name,
  combine, strength, clip, shown, locked) under the list, for a stack in an inspector. Its labels
  follow `LayerStackCommands.Word` (`Show the height layer Sculpt`, `Move the mask layer up`; the
  list is named `Height layers`). `EmptyText`, `ListHelp`, `RemoveHelp` and `CanAdd` are the
  view's.
- **A stack of a new target** is a `LayerStackViewModel` made with the editor's `RunWithAsync`, a
  function reading the stack from a document, its commands (`LayerStackCommands`: set, move,
  remove; the layer's argument and the clip's argument and label) and its add choices; `Refresh`
  it on `DocumentChanged` and `MapChanged`. Subclass it for what only that target has, as
  `HeightLayersViewModel` does (curves, facts, bake, reading a file again; `Preferred`, `KindOf`,
  `SummaryOfStack`, `RefreshSettings`). The commands are the target's own, one per operation
  (`terrain.setLayer` and friends for the heightmap), each one step of undo with the layer named.
- **The list follows a stroke cheaply**: it is made again only when the layers' names, settings or
  files change; when only a layer's grids changed (a dab), each `StackLayerItem` stays and its
  `Preview` follows (`FollowPreviews`), so the list keeps its selection and scroll.
- **`ImagePreview`** (`Controls/ImagePreview`, in a `Border.thumbnail.imagepreview` of
  `ImagePreviewSize`): a small picture of a `Rendering/PreviewPicture`, the whole picture in its
  proportions, and the same picture `LargeSize` (512) pixels on its longer side in its tool tip
  (placed right of it, over the views, in a `ToolTip` of its own without the theme's limit of about 320 pixels on a tool tip's width, which cut it to a tall strip; its `LargeSize` of 0 means no tool tip). Pictures:
  `CoveragePicture` (a byte grid in grey), `HeightPicture` (heights with the top view's ramp and
  light from the north-west; `Differences` grey at 0, lighter up, darker down; a coverage shows a
  dark checker where there is none of the layer), `HeightmapPicture` (the heightmap's samples, for
  9A's draft and final), `FilePicture` (an image file read at the size asked, for stratum textures,
  cube maps and map-wide decals), and `PreviewPicture.OfLayer(document, stack, layer)` for a layer
  of any stack (a mask reference shows its mask). `ImagePreview.Cache` keeps one bitmap per grid
  (weakly: a grid no version holds takes its pictures along), way of drawing and size step (32, 64,
  128, 256, 512), so a preview is drawn once per version of its grid (span `preview.render`, about
  0.1 ms at 32 pixels); files by path, size and the time they were written. Wave 9 uses it for the
  masks list, every stack's layers (through the stack control), the draft and final heightmaps
  (`HeightmapPicture`), stratum textures and water maps, map-wide decals and cube maps.
- Automation: `ui` lists each preview as `ImagePreview` with its label (`Preview of the height layer
  Base`). The tool tip is a popup: `screenshot` and `ui` do not reach it; the headless test
  `LayerStackControlTest` captures the large picture (`captures/height-layer-preview-large.png`).

## Stacks of coverage: the Masks mode, stratum and water stacks

Wave 9B (user stories K-1, K-2, S-1, W-2); the model is the library's (its guide, "Stacks of
coverage"). Files: `ViewModels/CoverageStackViewModel.cs`, `MainViewModel.CoverageStacks.cs`,
`Commands/Layers/CoverageLayers.cs`, `Views/MapPanels/MaskStackPanel`, `LayerPanel`, `PanelPartView`.

- **Which files the editor writes** (every stack control, `LayerStackViewModel.ResultNote` and
  `FileNote`, the library's `ProjectFileNotes`): under the list, the grid's file and who writes it
  (`Makes stratum-masks/stratum-2.gen.png, the editor's: ...`, or `Saved as ..., the map's own` while
  the grid has no stack); above the chosen layer's settings, its file (`stacks/stratum-2/Hills.gen.png,
  the editor's`, a painted layer's `the map's own`, an image layer's `Reads procedural/temp/forest.png,
  the tool's file`, a mask reference's `No file`). The Terrain inspector shows the height layer's;
  the draft says where Write the draft saves it. The library's guide, "Who writes a file: `.gen`".
- **`CoverageStackViewModel`** (a `LayerStackViewModel`): the stack of a target given by a function
  (the chosen mask, stratum or water map), the `layers.*` commands with the target's key added, add
  choices Painted, Image, Mask, Stratum, Rules (`AddChoicesOf`; a new source is one more choice),
  `Reference` (what a mask layer reads: mask, threshold, inverted), `IsReference`, `IsRules`,
  `Retarget` (another grid shown: its painted layer chosen). Its last layer cannot be removed.
- **`MainViewModel`**: `MaskStack`, `StratumStack`, `WaterStacks` (one per map, the content of the
  Water panel's map entries through `PanelParts.Replace` as `WaterMapPart.Stack`),
  `ShownCoverageTarget` (the mode's grid), `CoverageStackOf`, `ShowCoverageTarget`. A stack's chosen
  layer is the session's paint layer for its key (`EditSession.PaintLayers`), so the mask brush, the
  strata tool and the water brush paint it; `GridPaintTool.Stack` names a tool's target, and a stroke
  on a stack without a layer that can be painted is refused with the reason in the status line.
- **The Masks mode** (Shift+F3): the panel's `MasksSection` lists the masks with an `ImagePreview`
  each (following strokes) and how many layers, a name field, Add, Rename, Duplicate
  (`paint.duplicateMask {name?, newName?}`), Delete and Show it over the terrain; the inspector's
  `MaskStackSection` the stack control (with settings), the mask layer's `Reference` and, for a
  rules layer, the rule editor. Its tool is the mask brush (`masks.paint`, moved from the Paint
  mode). The overlay Mask and the brush show the chosen mask. Masks are never exported.
- **The Paint mode**: the strata list has each texture's picture (`LayerItem.Texture`, the
  converted PNG through the catalog; made again when the catalog loads) and each stratum's final
  mask beside it (`LayerItem.Mask`, a `CoveragePicture` of `Terrain.StratumMasks`: what its stack
  makes, the map is drawn and exported with; it follows a stroke without the list being made
  again; the lower and upper layers have none); the inspector's
  `LayerSection` shows the chosen stratum's textures, then "Mask layers" (the stack) and the rule
  editor for a rules layer.
- **The Water mode**: each map's entry shows its stack above the brush's settings.
- **The rule editor** (`MaskRulesViewModel`) edits a rules layer: `Editing` the owner (a mask's name
  or `stratum:N`), `Layer` the rules layer (null: the lowest). Choosing a rules layer in a stack
  shows it (`EditLayer`); choosing another layer of that grid closes it; while it edits a grid of the
  mode, that rules layer is chosen (`ShowEditedRulesLayer`: a rule added to a mask chooses the
  rules layer it made). `paint.addMaskRule`, `setMaskRule`, `removeMaskRule`, `moveMaskRule` take
  `mask` (also `stratum:N`) and `layer`. `paint.maskRules {name?}` opens a mask's rules in the Masks
  mode. The preview shows while the mode's stack has a rules layer chosen (Masks and Paint modes).
- Commands: `layers.add {target?, source?: painted|image|mask|stratum|rules, name?, mask?, path?}`
  (in the Masks mode's menu), `layers.set {target?, layer?, name?, combine?, strength?, clip?,
  visible?, locked?, reads?, threshold?, invert?}`, `layers.move {target?, layer?, to?, by?}`,
  `layers.remove {target?, layer?}`, `layers.choose {layer, target?}` (chooses the grid in its mode
  too). A target is a stack's key, any name of a mask or stratum (`forest`, `mask:forest`,
  `stratum:2`, `stratum 2`, `Layer 2`) or a water map (`foam`, `flatness`, `depthBias`); left out,
  the mode's grid. Each edit is one step of undo (span `model.coverage-layers`).
- Automation: `state.coverageStacks` (`shown`, the `mask`, `stratum` and `water` stacks with their
  target, chosen layer, whether kept in the document and each layer's source, combine, strength,
  clip, what it reads and summary; `stacks`: the keys the document keeps); `state.rules.masks.layer`.
- Speed (Archsimkats Valley, debug): a strata stroke costs about 10 ms a dab without a stack and
  about 17 ms with a stack of three layers (a mask reference among them). On an 81 km map (wave
  12): a painted layer added costs 2 to 17 ms (it composites nothing), a mask reference a full
  composite of the stack and those that read it, a strata stroke over four layers about 4 ms a move.
- Tests: `CoverageStacksTest` (operations and the mouse), the library's `CoverageLayersTest`.

## Height layers and curves

User stories T-5 and T-4; the library does the work (its guide, "Height layers and curves").

- `MainViewModel.HeightStack` (`HeightLayersViewModel`, a `LayerStackViewModel`,
  `ViewModels/MainViewModel.HeightLayers.cs`): the Terrain panel's stack control
  (`Views/MapPanels/HeightLayersPanel`, with Bake under it) and the chosen layer, which is the
  session's `SculptLayer`: the sculpt tool writes into it, and refuses with the reason in the status
  line when it cannot (`HeightLayers.SculptProblem`). The inspector's "Layer or curve"
  (`HeightLayerPanel`) shows its settings (`SettingGroup`s `Layer` and `Curve`, made again when the
  map's masks change; the clip is labelled `Mask`).
- Commands: `terrain.addLayer {kind?: sculpt|imported|curve, name?, path?, combine?}` (combine also
  `Subtract` and `Multiply`; above the
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
  layers with their settings and curves, the curve tool's settings; each layer's `kind` as before,
  `Base`, `Sculpt`, `Imported`, `Curve`, and its `sourceKind`, `painted`, `image`, `curve`, `mask`).
- On an 81 km map: adding the first layer 0.2 s, a stroke's move about 30 ms, a curve edit 0.3 to
  0.7 s with the views' own reaction to the new heights (debug build).
- Not built yet: a height per point by hand in the views, merging layers.

## Draft and final heightmaps

User story T-9 (wave 9A); the library does the work (its guide, "Draft and final heightmaps").

- **The switch** (`MainViewModel.ShownHeights`, `ViewModels/MainViewModel.DraftAndFinal.cs`;
  `terrain.switchHeights {to?: draft|final}`, Ctrl+Shift+D, in the Terrain menu; the panel's Draft and
  Final chips): which heightmap both views show and brushes write (`EditSession.Heights`). It is the
  view's, not a step of undo. Units, markers, props, decals and areas stay; generators and the
  export read the final.
- **What the views draw**: `MainViewModel.ViewDocument`, the shown document with the draft's heights
  as the terrain's while the draft shows (the same object per version, so views compare cheaply);
  `ShownHeightmap` for the camera's ground and the curve tool's road points. Only drawing and looking
  use them (the top view's picture and cursor height, the 3D frame and picking); tools and edits
  take `ShownDocument`. A new view part that draws the terrain's heights reads `ViewDocument`. The
  switch raises `OverlaysChanged` and the property change: the top view colours the tiles that
  differ, the 3D view uploads them (the two heightmaps share every other tile).
- **The panel** (`Views/MapPanels/HeightLayersPanel`): "Heightmap" (the chips, `ImagePreview`s of
  both heightmaps as `HeightmapPicture`s, what the final's base is), "Height layers" (filter chips by
  use, `all`, `shared`, `draft`, `final`, and by source, `all`, `sculpt`, `image`, `curve`, `mask`:
  `terrain.filterLayers {use?, source?}`; the stack control over the shown stack, rows saying
  `shared`, `draft only` or `final only` once the stacks differ; a line naming the layers only the
  other heightmap has), and "World Machine and Gaea" (masks to tick, Write again whenever it changes,
  Write the draft, Use as the final's base..., Share the draft's base). `DraftFinalViewModel` holds
  its state. The inspector adds "Used in" (`terrain.setLayer use=...`: `Draft and final`, `Draft
  only`, `Final only`); its Shown is the shown heightmap's on/off.
- **Commands**: `terrain.writeDraft` (Needs a project; writes `procedural/temp/draft-heightmap.gen.png`,
  `.r16` and the ticked masks; span `model.write-draft`), `terrain.setDraftOut {masks?, auto?}` (the
  map's settings, a step of undo), `terrain.useAsFinalBase {path?}` (asks for the output, starting in
  `procedural/temp/`; a step of undo; a file outside the project is read again only when the project
  opens, a warning in the log), `terrain.shareDraftBase`; `terrain.addLayer` takes `use`. With
  "Write again whenever it changes" on, the draft is written `DraftWriteDelay` (1 s) after the last
  change to it or a ticked mask, not during a stroke (`WhenDraftWrittenAsync` for tests).
- Automation: `state.draftFinal` (shown, both stacks with each layer's use and on/off, the final's
  base, whether the draft has heights of its own, the height at the map's centre in the shown
  heightmap, the settings, how many writes, the outputs in `procedural/temp/`); `state.heightLayers`
  adds each layer's `use`, the filters and the listed names.
- Tests: `DraftAndFinalTest` (the commands), `DraftAndFinalWindowTest` (the shortcut and buttons with
  the top and 3D views' height under the mouse and the top view's picture, headless).

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
  on the page, and the rest of that stroke does nothing. **With the symmetry** mirroring edits the
  handlers are called once per image of the place, the mouse's first, each with `keys.image` (1
  for the mouse's), `keys.images`, `keys.mirrored` and `keys.turn` (degrees), so whatever they edit
  is mirrored as a built-in brush's dabs are (a handler that keeps state per stroke keeps it per
  `keys.image`, as the plateau example does); `plugin.tool{ symmetry = false }` gets the mouse's
  place only, for a tool that mirrors itself. A **command**'s `map.addProp` adds the prop's images
  too while the symmetry mirrors edits (`mirror = false` leaves them out); its other edits
  (`setHeight`, `paintMask`, moves) are its own, and `map.images(x, z)` gives the places to repeat
  them at. Its options row (`ToolOptions.axaml`, `tools:PluginTool`) shows the
  plugin's fields the tool names, set as on the page, each a setting of the row of its own
  (`Controls/ToolRowItems`), so only those that do not fit go behind More; `radius` (a number, or
  a number field's key) is the circle the views draw, and a radius field takes the brush keys
  (`[`, `]`, control and the wheel) within its limits. `state.plugins.items[].tools` lists them. Span `plugins.tool`. The
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
| `editor.assets{ kind?, search?, limit? }` | The workspace's assets: `{ key, path, name, kind, category, reclaimMass, reclaimEnergy }`, and a unit's `massCost` and `energyCost`. |
| `map.name()`, `map.size()`, `map.height(x, z)`, `map.slope(x, z)`, `map.water()` | Name; width and height; the height between samples; the slope in degrees; the water's elevation or nil. |
| `map.markers(kind?)`, `map.props()`, `map.units()`, `map.decals()`, `map.areas()`, `map.armies()`, `map.get(ref)` | Plain tables: `ref`, `x`, `y`, `z`, `heading` and what the thing is (a marker's `kind`: spawn, mass, hydrocarbon, path, ai, weather). |
| `map.masks()`, `map.mask(name, x, z)`, `map.reclaim(blueprint)`, `map.revision(part?)` | The masks' names; a mask's value 0 to 1; a prop's mass and energy; a number that stays while a part (`map`, `props`, `markers`, `units`, `decals`, `heights`, `masks`) is the same. |
| `map.addProp{ blueprint, x, z, heading?, scale?, mirror? }` | Adds a prop standing on the terrain (by its kind, as the place tool does), in a command with its images while the symmetry mirrors edits (`mirror = false`: alone); answers its reference. Edits are for commands only. |
| `map.images(x, z)` | The place and its images in the other parts of the map while the symmetry mirrors edits (else the place alone): `{ x, z, mirrored, turn }`, the place first. |
| `map.move(refs, dx, dz)`, `map.place(refs, x, z)`, `map.rotate(refs, degrees)`, `map.remove(refs)` | `ObjectEdits` on one reference or a list. |
| `map.setHeight(x0, z0, x1, z1, height or function(x, z, height) return height end)` | Sets the samples of a rectangle (into the sculpting layer while the map has height layers); a function returning nil leaves a sample. |
| `map.paintMask(name, x, z, radius, value)` | Sets a mask's pixels within a circle to a value 0 to 1; answers how many. |

Anything else the editor does is a command: `editor.run("markers.place", { kind = "mass", x = 10, z = 20 })`.

## Content to use again: presets, stamps, templates, stars, the tree, walkability, adaptive maps

User stories F-10, T-2, M-2, U-3, F-9, T-7 and R-5. The core's side is `ViewModels/MainViewModel.Content.cs`
(one `InitializeContent` line in the constructor); the library does the work (its guide, "Content to use again").

- **Selection presets** (F-10, kind `selections`, `Tools/SelectionPresetTool`): `edit.saveSelectionPreset
  {name?}` (Edit menu; also the Map panel's Selection presets, `edit.saveAsPreset kind=selection`) saves the
  selected things of every kind but areas around their middle (a whole ogrid), named after the first
  unit's group (else `Selection (n)`); applying one chooses the Map mode's Preset tool (`map.preset`),
  whose click runs `edit.placePreset {name?, x?, z?, heading?, army?}` (Edit menu, `[UndoStep]`): the
  library's `SelectionPresets.Place` (props and decals as high above the terrain as saved, markers with
  their links and chains, units as a new group named after the preset in the tool's army's `INITIAL`),
  turned about the place (Q and E a quarter turn, shift 15 degrees), then `MirrorPlaced`, and selects
  it all. A spawn of the preset moves the map's spawn nearest to where it goes (its mirror partners to
  the images) rather than adding an army, and the preset's units are then that spawn's army's (the
  army combo is disabled, `HoldsSpawn`); a map without spawns gets a new army. Before the click the
  preset shows under the mouse as outlines, its images too (`Tools/PresetPreview`, span
  `tool.preset-preview`; the units presets of the Units mode's place tool alike, which select what
  they place). The tool's options: the preset with what it holds, the army of its units (the
  civilians unless chosen), the turn. `state.selectionPreset` (`holdsSpawn`, `previewOutlines`).
- **Stamps** (T-2, `Tools/StampTool`, `terrain.stamp`, an `IShapedBrushTool`, so the shelf's brushes
  are its pictures and both views draw it under the mouse): a click is one `BrushStroke` of one dab
  (one step, every image with the symmetry, props follow the terrain, into the sculpting layer): the
  library's `HeightStamp` with the tool's mode (raise, lower, set), radius (half the side, to 512;
  `[` and `]`, shift with them the height), height, base (the ground under the click, or typed) and
  edge (a hard edge still blends over one ogrid, so a turned stamp has no stair-step rim); Q and E
  turn it 15 degrees (shift 90). Its picture combo has no Round (`BrushShapeSettings(pictureOnly)`,
  `BrushChoice.NoPicture` until one is chosen). Shift and a drag draw a box (`Box`) that becomes
  `terrain.captureStamp {x0, z0, x1, z1, name?}` (in no menu): a 16-bit PNG in the workspace's
  `brushes/stamps` (`HeightStampCapture`), chosen at once (`BrushShapeSettings.ChooseFile`, kept when
  the catalogue reads it) with mode Set, a pixel an ogrid, its height and lowest ground, so a click
  puts that part back as it was. `terrain.saveBrush {name?}` saves the active brush (a stamp too:
  `BrushPresetSettings.StampMode`, `Base`, `BaseFromGround`, `Edge`) as a brush preset.
  `state.stamp`. A brush shape's combo box that lets go of its item (its list replaced) leaves the
  shape as it was (`BrushShapeSettings.OnChosenChanged`).
- **Map templates** (M-2): the New map page's Template (first; `NewMapPageViewModel.Templates`, read
  again each time it opens: none, the workspace's, the sandbox) fills the fields from a template of
  settings (its own strata until another biome is chosen: `StrataFromTemplate`, the page then sends
  no biome) or, for a whole map, leaves only the name. `file.new {template?}` takes it; `file.saveAsTemplate
  {name?, whole?}` (File menu) saves the open map's settings, or with `whole` the map, into the
  workspace's `presets/maps`. `WorkspaceSetupViewModel.Use(workspace)` works in a workspace without
  checking its assets (tests of what keeps files in the workspace).
- **Dragging in the units tree** (U-3): the tree shows each group's units after its groups
  (`GroupNode.Items`, `UnitNode`; a unit never opens), its choice is `ArmiesViewModel.ChosenNode` (a
  unit chosen selects it with `select.set` and chooses its group). `Controls/UnitTreeDrag` (the
  attached `IsEnabled` on the tree): a unit or group pressed and moved six pixels captures the pointer,
  lights up the group or army under it (`TreeViewItem.unitdroptarget`) and says in the status line what
  a drop does or why it cannot (`ArmiesViewModel.DropProblem`); letting go runs `units.drop {army,
  group, units?, fromArmy?, fromGroup?}` (in no menu, `[UndoStep]`): units into the group (a selected
  unit takes the selected units along, `ArmiesViewModel.SelectedUnits`), a group into it. It stays
  inside the window's own input, so automation's `mouse action=down/move/up` drives it.
- **Stars and recents** (F-9, `ViewModels/AssetsViewModel.Favourites.cs`): `AssetsViewModel.Favourites`
  (the library's `AssetFavourites`, opened on `Setup.WorkspaceReady`): the shelf's scopes Starred and
  Recent (`AssetScope.Starred`, `Recent`, the newest first), a star on starred tiles (`AssetTile.IsStarred`,
  `Border.badge.starred`, `Icon.Star`), the inspector's star button; the picker's Starred and Recent
  chips (`AssetPickerViewModel.Show`, `Shows`), its Star button, and a choice noted as recent (`Close`).
  A tile clicked on the shelf and `assets.pick` note recents too. `assets.star {key?, on?}` (Assets
  menu): the picker's selected asset, else the inspector's. What cannot be written is logged
  (`Assets`). `state.favourites`.
- **Walkability marked by hand** (T-7, `Tools/WalkabilityMarkTool`, `terrain.walkability`, a
  `GridPaintTool` setting codes, sharp, radius 4): Blocked or Open (`Mark`), or Erase; the views show
  the marks while it is active (`Palette`: blocked red, open green); a stroke makes walkability and
  the path networks out of date (their input `walkability marks`). Brush presets keep it
  (`walkability`). `state.walkabilityMarks` (mark, erase, radius, counts).
- **Adaptive maps' files** (R-5): the Markers panel's Adaptive map says what the export writes
  (`MarkersViewModel.AdaptiveFilesText`, `WritesAdaptiveFiles`); an optional group `Optional_Players_<n>`
  reads "when n or more players are in the game". `state.adaptiveFiles`.
- Tests: `ContentTest` (the operations), `ContentWindowTest` (dragging in the tree with the mouse),
  `MainWindowTest.TheWalkabilityToolMarksWhereTheMouseDragsAsOneStep`, `AdaptiveScriptRunTest` (the
  exported script run in Lua with the game's functions stood in for).

## Panels list, the inspector edits; the rail, help, settings and presets

Wave 8's shell (user stories F-1, F-5, F-7, F-8, F-10, K-1, L-1, P-2, W-1, W-2). The parts are
`ViewModels/PanelParts.cs`, `MainViewModel.PanelParts.cs`, `MainViewModel.Help.cs`,
`ShortcutsViewModel`, `WorkspaceSettingsViewModel`, the library's `PresetKinds` and `PresetFolders`.

- **The rule for every mode: the panel says what there is, the inspector edits what you chose.**
  A setting lives in one place: the panel lists the things of the mode (layers, masks, groups,
  settings groups, maps, generators, presets) with a short detail each, and choosing one shows its
  fields in the inspector. A new part follows it: an entry of a list in the panel, its editing in
  the inspector. Checked against it (October 2026): Terrain (height layers listed, the chosen one
  in the inspector), Paint (strata listed, the chosen stratum in the inspector), Decals and Units
  (groups listed, the selection in the inspector), Water and Lighting (below) and Procedural (the
  generated results listed, section "The Procedural mode") follow it; the Map
  panel's Information fields and the Props panel's Placing are
  still edited in the panel, for a later wave to move.
- **Who has the inspector: what was chosen last** of three: a tile of the shelf (or the picker),
  things on the map (in the modes of things: Map, Markers, Props, Decals, Units), and a part of the
  mode. Choosing one unchooses the others, so the inspector never shows something older than the
  person's last choice: a tile chosen clears the map's selection, a selection unchooses the tile, and
  a part of the mode unchooses the tile (`MainViewModel.InspectTheMode`). A part of the mode is
  chosen by a mode on the rail, its F-key or the Modes menu (`<mode>.mode`; a command that switches
  to its own mode, `decals.choose`, is no such choice), an entry of a panel's list (`PanelParts.Picked`,
  raised by its button also when it was chosen already; `view.inspectPart`), and an item of a list or
  tree in the mode's panel (a stratum, a height layer, a mask, a group: `ModePanel`'s pointer press).
  Choosing a tool is not one: a unit tile stays in the inspector while the person picks the place
  tool. The tools keep what they took from a tile (the unit, prop or decal they place, the brush's
  shape), so unchoosing it changes nothing on the map. Tests: `InspectorOwnerTest`.
- **Lists of parts** (`PanelParts`, `PanelPart`): a mode panel's list of entries under headings,
  one always chosen (the first until another is); `Controls/PanelPartList` draws it in the panel
  (`Button.partentry`, filled while chosen, the detail at the right), `Views/MapPanels/PanelPartView`
  the chosen entry in the inspector: its title and help, then its `Content` by type
  (`SettingsPart`: groups of settings, their titles when there are several; `PlannedPart`: not
  built yet with its story; `WaterMapPart`; `WaveGeneratorsPart`). A new kind of content adds its
  template to `PanelPartView`; `PanelParts.Replace(id, content)` gives an entry another (a water
  map's layer stack), `Insert(index, part)` and `Remove(id)` change the list after it was made (the
  Procedural mode's, as generators register). `view.inspectPart {part}` (in no menu) chooses an entry and its mode;
  `state.panelParts` has each list's chosen entry, its content's type and every entry's detail.
- **Water** (`WaterSettings.Parts`): Water on (a check box above the list), Settings (Elevations
  and colour, its detail the surface's elevation; Surface and reflection; Textures and waves, the
  four wave textures with them), Maps (Foam, Flatness, Depth bias) and Wave
  generators (how many). A map chosen makes the Paint water tool paint it and chooses that tool;
  the inspector shows the map's stack of layers (the brush's shape, radius, strength and erase stay in
  the options row, which says `Map: Foam`, and are not repeated). Each map's entry has a picture of
  it and its count of layers (`PanelPart.Preview`, `Detail`; a map without a stack of its own has one). The Waves entry chooses the Waves tool; and back, choosing a tool or a
  map for the brush (`brush.set waterMask`, a preset) shows its entry. Presets below.
- **Lighting** (`LightingSettings.Parts`): Sun (azimuth, elevation, colour, multiplier; the Sun
  tool's line), Colours and ambience, Fog, Bloom, Sky and cube maps (the sky's cube maps and
  background), and Shadow decal under Map-wide decals (the map-wide shadow decal's slot itself,
  `MapWideSlotViewModel`, its detail the slot's source).
  Presets below: lighting presets, and sky presets while the sky is chosen (`ForParts`).
- **The rail** (`Views/ModeRail`): the modes of groups 0 and 1 at the top (`RailModes`), the bottom
  group (`EditorModes.BottomGroup`, `BottomRailModes`) at the bottom above a divider and the Plugins
  entry (`plugins.manage`). Masks (Shift+F3, after Paint, which it took the masks from; filled in by
  wave 9B, section "Stacks of coverage") and Procedural (F11, group `BottomGroup`; its
  sections are the generated results). F1 to F9 stay as they were. A mode's tool tip names its shortcut.
- **Keyboard shortcuts** (`help.shortcuts {query?}`, Shift+F1, Help menu; `Views/ShortcutsPage`): every
  command with a gesture from the registry (so a plugin's shows) by menu, the keys the tools take
  (`ShortcutsViewModel.ToolKeys`) and the mouse in each view (`Mouse`), each line its keys (`[` and
  `]` as themselves, `D1` as `1`: `KeysOf`), what and where; the search keeps the lines holding
  every word. A new tool key or mouse gesture gets a line in those lists. `state.shortcuts`.
- **Folders** (Help, Folders): the workspace (`help.workspaceFolder`), projects
  (`help.projectsFolder`), the open project (`help.projectFolder`, needs a project), presets
  (`help.presetsFolder`), brushes (`assets.brushesFolder`), plugins (`plugins.showFolder`), converted
  assets (`help.assetsFolder`), the game's (`help.gameFolder`), FAF's data (`help.fafDataFolder`), the
  export folder (`help.exportFolder`: the vault's `maps`) and the logs (`help.diagnosticsFolder`).
  Each answers its path; a workspace folder is made when missing, the others refuse when absent.
- **Workspace settings** (`file.workspaceSettings {gameFolder?, fafDataFolder?, vaultFolder?}`, File
  menu; `Views/WorkspaceSettingsPage`): without arguments the page (the three folders with Browse,
  Save and Revert; the converted assets' format, editor, date, counts and whether they are current,
  checked on a pool thread; Convert again, `workspace.convert`; links to the plugins and diagnostics
  pages); with folders it writes them into `workspace.json` (`WorkspaceSetupViewModel.ChangeFolders`,
  checked as the setup checks them) and keeps the assets: other archives make them out of date, and
  the page says so. `state.workspaceSettings`. Never press Convert again in a scratch workspace whose
  `assets` is a link to another's.
- **Presets at the bottom**: every mode's preset sections are the last of its panel (the Terrain
  mode's brush and stamp presets after the height layers, the Props mode's palettes after the rules
  and reclaim); a new section goes before them (`PanelsAndShellTest.EveryModesPresetsAreAtTheBottomOfItsPanel`).
- **Presets in one folder**: `presets/` with plural kinds (`brushes`, `stamps`, `lighting`, `water`,
  `sky`, `props`, `units`, `markers`, `selections`, `maps`: the map templates). `PresetsViewModel.Open`
  moves a workspace's old folders once (`PresetFolders.Move`: `brush`, `props-palette`, `selection`,
  `templates/`, brush presets of the stamp tool to `stamps`; `Moved`, logged); an old kind name in a
  command still names its folder (`PresetKinds.Of`). A stamp's picture (`brushes/stamps/*.png`) is a
  brush, not a preset: the shelf and the stamp tool's pictures list it; its settings saved
  (`terrain.saveBrush` with the stamp tool) are a stamp preset. Each preset section has Show the
  folder (`presets.showFolder {kind?}`).
- **Mass and energy** (F-8): a unit's and a prop's tile has a line of mass and energy
  (`AssetTile.Economy`, `5.2k · 47.4k`, mass first; `EconomyHelp` in full as its tool tip and in its
  label; a unit's build cost, a prop's reclaim), in the shelf and the picker; the inspector's facts
  have them in full. The shelf's sort button (`Icon.Sort`, beside the search, on while sorted) and
  the picker's combo box order the tiles as listed, the most mass first or the most energy first
  (`AssetSort`, `AssetSearch.Sorted`); `assets.browse {sort}`, whose tiles carry `economy`, `mass`
  and `energy`. Lua's `editor.assets` gives `massCost` and `energyCost`.
  Mass and energy show after their icons wherever the editor shows an asset's values:
  `Controls/EconomyAmount` (`Mass`, `Energy`, `IsShort` for a tile's `5.2k`, `Prefix`, `Suffix`,
  `EmptyText` for "No cost"; its theme in `Styles/Controls.axaml`) draws `Icon.Mass` in `MassBrush`
  and `Icon.Energy` in `EnergyBrush` (both theme variants) at its font's size, and names itself in
  words for a screen reader (`5,220 mass, 47,400 energy`; a name the view sets wins). It is in the
  shelf's and the picker's tiles (the energy goes to the next line in a narrow tile; the tiles of a
  row are as high as the highest), the inspector's facts (`AssetFact.Mass`/`Energy`, `IsEconomy`),
  the selected props' reclaim and the Props panel's reclaim (`ReclaimLine`, whole numbers as
  `PropsViewModel.Describe`). The shelf's sort button shows the icon of what comes first beside
  `Icon.Sort`; the picker's combo box lists `AssetSearch.SortChoices` with their icons. `ui` reads an
  `EconomyAmount` as one control (its `text` `5.2k 47.4k`, its `label` in words). Tests: `EconomyIconsTest`.
- Tests: `PanelsAndShellTest` (the operations), `PanelsAndShellWindowTest` (the mouse and keys).

## Small rules of editing, looks and speed

- **Placing on a marker**: a placing tool of the Markers mode pressed on a marker of its kind (any
  deposit for a deposit's tool) selects it and drags it with an inner `SelectTool`; `markers.place`
  refuses a deposit on an ogrid holding one and selects that one.
- **A box takes areas only in the Map mode** (`BoxKinds`), also with the markers locked.
- **Optional groups** (`markers.addOptionalGroup`) go to the selected units' own army when they are
  all of one extra army (the script creates a group of any army); units of another army change army
  and the status line says so.
- **New weather** takes the style of the map's biome (`WeatherStyles.StyleOf`), else Tundra.
- **The inspector** shows the active tool's parts (`PanelSection.ForTools`: the Paint mode's strata
  tool its stratum, the masks tool its rules, `MainViewModel.InspectorParts`) and the selection only
  in the modes of things (`InspectsSelection`).
- **Example plugins**: an untouched copy of an earlier version (its SHA-256 in `ExamplePlugins.Earlier`)
  is brought up to date; a file changed for a release adds its old hash there.
- **Speed** (Archsimkats Valley): the inspector and the selection's size follow a stroke at most
  every `StrokeRefreshMilliseconds` (200), and fully when it ends; the 3D view draws nothing while
  hidden; the props and units are made again for the hover only when a thing of their kind is under
  the mouse; 3D picking passes props whose bounding sphere the ray misses; the brushes' watcher
  ignores folders (the start read the assets twice). Leaf textures (`CutOutTextures`) give their
  holes the leaves' colour before the mipmaps, and cut-out foliage is lit with its normals leaning up.

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

- **`expand {label, under?, all?, collapse?}`** (`AutomationServer.Layout.cs`) opens the tree item
  (or tree, or expander) named, labelled or reading the label, its parents first (`all`: every item
  in it too; `collapse`: closes), so `ui` lists what lies in it; it answers how many it changed.

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
  scroll viewer), then clicked in its centre once its place stayed the same over two frames and the
  window's hit testing (what the mouse reaches: the last frame drawn) finds the control there
  (`AutomationServer.Aiming.cs`, `AimAsync`); a control the mouse cannot reach (hidden, outside the
  window, covered by a question or the views) is refused with why (`the mouse would reach ...`),
  never clicked elsewhere. It then waits (at most 10 s) for the commands the click started
  (`CommandRegistry.Running`), unless one asks a question or opens the picker; its answer has the
  type, the place, `checked`, `focused` and `finished`. `set` aims the same way, and chooses a
  list's item by clicking it where no check box or button of the item lies (`PlainPointOf`), so
  what a person's click starts runs too (`AutomationAimingTest`). `ui {under}` and `set` find a control the same way: `Combine` is the combo box labelled
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

### The menus and the window

`Automation/AutomationServer.Shell.cs` (tests: `AutomationShellTest`).

- `menu {path?}`: without a path the menu bar; `path: "File"` a menu's items (header, shortcut,
  enabled, notBuilt, submenu); `path: "File/Save as a template"` clicks that item (its command, as a
  click does; refused while disabled). The menus' popups lie outside the window's tree, so `ui` and
  `click` do not reach them.
- `window {width?, height?}` sizes the window (a maximized one becomes normal); `window {close: true}`
  closes it as its close button does: with unsaved changes the question shows (`state.dialog`).
- `key` reads `Ctrl+2` as the digit's key (`D2`), `[` `]` `+` `-` as theirs, and refuses a name that is
  no key. Its answer's `focused` names the control with the keyboard and the text it holds
  (`TextBox Name of the new map "QA Ridge"`, `Button "Save"`). `click` measures the control once a
  scroll has settled. `under` (in `ui`, `set`, `click`) takes a section's heading too (`Sky`,
  `Generated results`): the part it heads. `dialog {text, button}` fills a question's text
  field first (Save as a template's name; `state.dialog.field`).
- A call that fails or a command that refuses is logged as information (category `Automation`): the
  caller has the answer, and the Console's count stays the editor's own warnings.
- Commands run without a person that would replace the open map (`file.open`, `file.import`,
  `file.new` with arguments) refuse while it has unsaved changes, unless given `discard: true`.
- Results kept outside the map (walkability) that were current at a save are noted in the project's
  `.generated.json` (`KeptResults`) and generated again in the background when it opens.

### A test editor of one's own

```sh
faf-map workspace scratch D:/scratch/ws --from "<the real workspace>" --settings D:/scratch/settings.json
faf-map editor start --port 5398 --settings D:/scratch/settings.json [--map <folder>]   # waits until ready
faf-map editor objects kind=props count=5 --port 5398
faf-map editor restart --port 5398                                                       # stop, then start with the arguments of the last start (the newest build)
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
  `--timeout`), so the build output is free again. `restart` stops it and starts it again with the
  arguments its last start was given (`start-<port>.json` beside the shadow copies; an option given
  to `restart` takes the place of the remembered one, `--map` another map), the copy brought up to
  date first: the way to run a new build.
- `faf-map workspace scratch <folder> --convert-into <assets folder>` gives the scratch workspace
  assets of its own, converted from the game into that folder (made when absent, linked in;
  nothing converted when they are current already), for a change of the assets' format that must
  not touch the real workspace's; several scratch workspaces may share the folder.
  `faf-map workspace init <folder> --settings <file>` remembers the workspace in that settings
  file, never the user's.
- In Git Bash an argument that starts with `/` is turned into a Windows path (`label=/env/...`
  becomes `label=C:/Program Files/Git/env/...`): set `MSYS_NO_PATHCONV=1` for such a call.
- A scratch workspace (`ScratchWorkspace`, the library's) has its own projects, presets, brushes
  (copied) and vault (`vault/maps`, where exports go), and the real workspace's assets through a
  directory junction (no administrator needed): a test editor may save, export and write presets.
