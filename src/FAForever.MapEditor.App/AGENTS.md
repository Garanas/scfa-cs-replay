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
   for its settings in `Views/ToolOptions.axaml`; a part of a panel not built yet a
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
    `markers.setChain`, `markers.renameChain`, `markers.deleteChain`), it selects what one
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
  - Run without a person (automation, later Lua: `RunAsync(id, arguments, interactive: false)`),
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
- **What the views draw again, by event**: `DocumentChanged` when the shown document changes
  (an edit, undo, reload, a dab, a stroke thrown away): the top view updates the tiles that
  changed, the 3D view uploads them. `OverlaysChanged` (an overlay turned on or off),
  `CameraMoved` and `CursorChanged`: only what lies over the terrain. `ShownMask`, a property:
  the top view's picture with that layer's mask. `MapChanged`: everything, for another map.
- **Tabs are enums**: `MapViewTab` (the top and 3D views) and `DockTab` (the dock), declared in
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
  - `BrushStroke`, which both brushes use, holds the stroke's `EditTransaction` from press to
    release (`MainViewModel.StartStroke`, `EndStroke`): one undo step, shown while it is painted,
    dabs a quarter of the radius apart. A stroke is input, not a command, so it opens its own
    transaction; the commands that edit wait until it ends (`Needs.NoStroke`).
  - `SculptTool` (T-1): mode, falloff, radius, strength, the height to flatten to, set or scale
    around, the factor and noise size, limits on the heights. The painting tools derive from
    `GridPaintTool` (section "Painting the terrain's grids" below); `StrataTool` (S-1) paints the
    layer chosen in the Paint panel's list (`Strata`/`Painted`), within its limits of slope and
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
  `CommandPalette`, `AssetPicker`, `DiagnosticsPage`, `VersionControlPage`, `SetupPage`, `UpdateAssetsPage`,
  `ConvertingPage`.
- Code that draws (the overlays of the top view, the 3D view's sky) reads the tokens with
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
  height and water (`Rendering/TerrainPicture`), with the mask of the layer the strata tool
  paints in orange (`ShownMask`); it draws again only the tiles a change touched. The markers lie
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
  above the horizon; F focuses on the place under the mouse (the FAF editor focuses on the
  selection; there is none yet), from about half as far; Home shows the whole map again. It
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
    itself. The view makes it again when the heights change, at most twice a second
    (`Rendering/Gpu/CameraGround`, which the camera reads its heights from).
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
    `WarningBrush`.
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
    layers, 12 and 13 the masks, 14 the brush's picture.
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
- **What the top view lays over the terrain** is `MainViewModel.ShownGrid`: the active painting
  tool's `Shown`, else the chosen mask for tools while the overlay `mask` is on. `TerrainPicture`
  colours a mask in the overlay's orange, a grid with a `Palette` in a colour per code, and colours
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
- Not built yet: hydraulic erosion, painting with symmetry, limits for the water and masks tools,
  the overlays in the 3D view.

## Tests with the real window, and labels for screen readers and agents

- `tests/FAForever.MapEditor.App.Tests/MainWindowTest` runs the real `MainWindow` without a screen
  (Avalonia.Headless with Skia): the mouse, wheel and keys as a person uses them, and frames of
  what it shows. Test input and views there: the camera's controls, alt and a click in the
  top view, painting as one undo step, switching views. The headless platform has no graphics
  card, so the 3D view draws nothing and its camera does not glide by itself (the tests let it
  arrive); what it draws is checked with automation in a real editor. The tests of the 3D view's
  input also catch a view that the mouse does not reach.
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

## Assets: the shelf in the dock

User stories F-8 and F-9, concept C1 of the canvas "Map editor UI concepts" (row C, with the
picker C2, the atlas C3 and the interactions C4). `AssetsViewModel` (`model.Assets`) shows the
workspace's `AssetCatalog` (the library's guide), read on a pool thread when the workspace is
ready and again with Modes, Assets, Read the assets again (`assets.reload`):

- **Kind** follows the mode (Terrain brushes, Paint textures, Props props, Decals decals, Units
  units; another mode keeps it); the chips above choose it by hand, with how many each shows.
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
  their mesh: the workspace converts no unit icons.
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
- Not built yet: stars and recents; a mark of the drop place drawn in the views.

## Events, presets, symmetry and generated results

Four small parts that later features build on:

- **Events** (user story F-13, `model.Events`, `ViewModels/EditorEvents`): `WorkspaceOpened`,
  `MapOpened`, `MapSaved`, `MapExported`, `DocumentChanged` (the library's `DocumentChange`: an
  edit with its description, an undo or redo, what was read again; a stroke once, when it ends),
  and `BeforeSave` and `BeforeExport`, whose handlers may stop it with a reason
  (`VetoableEventArgs.Veto`): the operation then throws `OperationVetoedException` before it
  writes anything, and a command shows the reasons in the status line. `MainViewModel` raises
  them from its operations, on the window's thread; a handler that throws is logged (`Editor`)
  and stops neither the operation nor the other handlers. Lua plugins (F-15) will listen through
  the same part.
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
  rotational for two. The top view draws the lines between the parts, dashed over a dark line,
  while the overlay `symmetry` is on (`HeightmapView.DrawSymmetry`); the 3D view does not yet.
  `Transforms(document)` gives the library's `MapSymmetryTransform`s (the maths is in
  FileFormats.Map's `MapSymmetry`): the brushes and placing tools are to mirror their edits with
  them; none does yet.
- **Generated results** (user story F-7, `model.Generation`, `ViewModels/GenerationViewModel`):
  the library's `GeneratedResults`, where a feature registers its `IGenerator` (its inputs: parts
  of the document). Nothing generates by itself: after every step (not every dab) the part
  compares each last result with the document (`model.generated`); the status bar names those
  out of date in the warning colour. `generate.stale {list?}` generates them again (or only
  lists them), with the inputs that changed; a result never generated is its own command's
  (`generate.walkability`, ...). Another map forgets the results. A feature registers its
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
  `terrain.walkability`, which needs a layer of its own in the project), and the overlay in 3D.
- **The normal map and the preview** (`generate.normalMap`, `file.renderPreview`): the normal map
  at the size of the map's own, its format kept (`TerrainImages.NormalMap`); the preview
  (`Rendering/MapPreview`) is the top view's picture (`TerrainPicture`, without a mask) reduced
  to 256 by 256 by averaging. Both are out of date when the heightmap changes, the preview also
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
- **The top view draws** the areas (dashed, with their names), props (dots, overlay off by
  default) and units (squares), only what lies in the view (span `top.things`), and an outline
  per selected thing: a ring, or the rectangle of a decal or area (`top.selection`, tokens
  `MapSelectionBrush`, `MapSelectionFillBrush`). Alt and a click send the 3D camera there.
- Automation: `state.selection` (count, kinds, the first 200 references, what can be picked, how
  many are copied, the inspector's X, Z and heading). `stroke` drives the active tool with no
  keys held and two pixels an ogrid.
- Not built yet: picking and a box in the 3D view (its `Pick(ray)` will call `SelectAt` and the
  like), handles, scaling, snapping, moving along one axis, turning about the selection's centre.

## Decals: placing, scaling, order and groups

User stories D-1 and D-2, the Decals mode (F8); the edits are the library's `DecalEdits`.

- **The place tool** (`decals.place`, `Tools/DecalPlaceTool`, `model.DecalPlaceTool`) places the
  decal it holds (`Decal`, an asset's key): chosen with Choose... in its options or
  `decals.choose {key?}` (the picker for decals; it makes the place tool active), or on the shelf
  and with Ctrl+P (`assets.pick`): a decal the inspector shows becomes the tool's, and in the Decals
  mode the place tool becomes active. A click places it centred on the mouse, `Width` wide (the
  options; its length from the shape of the texture's picture, read once from the PNG's header
  when chosen), turned by `Heading`, with the cut-off of the options (500 at first: what maps use
  most); dragging from the click sets its width to twice the distance and turns its width along
  the drag. One step of undo held from press to release, as a stroke; the new decal is selected.
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
  ogrid) and heights above the terrain, in their groups below the deepest one holding them all.
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
- Automation: `state.units` (`place`: unit, preset, army, group, heading, structure, footprint;
  `tree`: the chosen group and the first 200 groups with their units) and `state.armies` (name,
  playable, team, factions, spawn, units, alliances, chosen).
- Not built yet: dragging units or groups in the tree, mirroring placed units with symmetry,
  team colours in the views.

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
  `markers.connectPaths` (not built yet).
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
  markers, the chosen chain, adaptive, the last warning, the active marker tool's settings).
- Not built yet: weather markers, a line drawn while a link is dragged, the slope under the
  mouse before a click, mirroring by the symmetry (wave 2), marker presets (`markers.placePreset`).

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
  scaleMax?}`, `props.replace {key?}` (picker), `props.randomVariant` (same biome, folder and
  family), `props.addToPalette {key?, weight?}`, `props.removeFromPalette {key}`,
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
- Not built yet: symmetry for placed and painted props (wave 2), handles, the K-1 masks as layers.

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
  either view. A tool shows handles and a line with a label through `IMapTool.Marks`
  (`ToolMarks`), which the top view draws; the 3D view does not yet.
- **Presets**: the kinds `lighting` (all of it) and `water` (its look; the elevations, wave
  generators and masks stay the map's), the library's `AtmospherePresets`; the Lighting and Water
  panels list them.
- The top view labels the first area "(playable area)". `state.mapPanels` has the information,
  areas, script and files, water, lighting angles, layers, the ruler's measurement and whether
  the page of a new map is open.
- Not built yet: the sky and cube maps (L-1), a sun handle and area handles drawn in 3D,
  placing wave generators with the mouse, templates for a new map.

## Diagnostics: what goes well and wrong

User story F-17. The editor keeps what a service keeps for Loki and Prometheus, per session
(`Diagnostics/Telemetry`):

- **Logs**: every `ILogger` of the editor (`Microsoft.Extensions.Logging.Abstractions`; get one
  with `MainViewModel.Logger(category)`, categories `Commands`, `Files`, `Workspace`,
  `Rendering`, `Performance`, `Automation`, `VersionControl`, `Assets`, `Editor`). The Console of the dock shows them, with
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
- Not built yet: a report of a problem that bundles the recent sessions (`help.reportProblem`),
  and a collector on a server for everyone's sessions.

## Automation: driving the running editor

Started with `--automation <port>`, the editor takes commands over HTTP on
`http://localhost:<port>/` (`Automation/AutomationServer`): the operations of the menu, each run on
the user interface's thread and answered with the editor's state as JSON (title, map, modified,
undo/redo, history, the brush tools' settings, zoom). Use it to check a change in the real editor:

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
  `view {x, z, zoom?}` (`view.zoomTo`: the top view), `gpu-debug {mode}` (`view.gpuDebug`: what
  the 3D view's terrain shows: 0 the picture, 1 the albedo, 2 the light, 3 the normals, 4 the
  heights, 5 the lower layer, 6 the first four masks after the remap, 7 as stored), `mode {id}`,
  `tool {id}`, `tab {name: top|3d}`, `show {markers?, pathNodes?, camera?, props?, units?, areas?}` (each overlay's
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
  (`curl localhost:5371/undo`); Windows refuses a POST without a body, so the CLI always sends one.
- It listens on the loopback address only and only when asked to. Commands do not show dialogs:
  `close` does not ask about unsaved changes.
- Query parameters read as JSON where they can (`?step=3`, `?on=true`), as the CLI sends them.
- An MCP server for agents would be a thin layer over these same commands.
