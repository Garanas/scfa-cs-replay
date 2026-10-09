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

1. **A command of the controller**: every operation a person can do is a method with
   `[EditorCommand]` on a controller (`Controllers/`), with its menu, shortcut and user story. It
   then is in the menus, the palette, the shortcuts and automation (`run id=...`) at once. Not
   built yet? Leave the method empty and mark it `[Incomplete(story, what it is to do)]`. A tool
   is a `ToolDefinition` of its mode (`ViewModels/EditorModes.cs`), with `Missing` until it works;
   a part of a panel not built yet a `PlannedSection`. What the feature shows or holds is in
   `state` (or its own automation command when it is large).
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

- **The controller** (`Commands/CommandRegistry`, user story F-1) holds every command: methods
  with `[EditorCommand(id, title, Menu, Gesture, Story, CanRun)]` on the controllers in
  `Controllers/` (File, Edit and Select, View, Map, Terrain, Paint, Water and Lighting, Markers,
  Props, Decals, Units, Generate, Assets, Plugins, Help), plus one `tool.<id>` command per tool. The
  window makes the menu bar and its shortcuts from them (`MainWindow.BuildMenusAndShortcuts`:
  File, Edit, Select, View, Map, Modes with a submenu per mode, Generate, Plugins, Help); the palette
  searches them, the ones that work first; automation lists and runs them (`commands`,
  `run id=`). A controller only calls the view model, which owns the state and the document.
- **Not built yet** is visible and traceable: a command with `[Incomplete(story, missing)]`
  only says what it is to do (status bar, Console, `run` answers `ran: false`) and shows in
  italics in the menus; a tool with `Missing` shows it in the tool options and does nothing in
  the views (`CanPaint` is true only for sculpting and painting strata); a `PlannedSection`
  shows a dashed box with its story. `CommandRegistryTest` checks that every story named
  anywhere exists in `USER_STORIES.md`, and lists the `[Incomplete]` functions.
- **Modes and tools** are data (`EditorModes`): a mode's id, title, icon (`Styles/Icons.axaml`),
  tools, panel sections and inspector sections. Choosing the sculpt tool paints the heights,
  the strata tool a layer's mask; choosing a layer to paint (automation's `brush layer=`)
  chooses the tool. A mode remembers its last tool.
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
  pages by stage), `ModeRail`, `ModePanel`, `ToolOptions`, `Inspector`, `Dock`,
  `CommandPalette`, `DiagnosticsPage`, `VersionControlPage`, `SetupPage`, `UpdateAssetsPage`,
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
reads the remembered one (`UserSettings`) and sets the window's `Stage`:

- `SetUp`: no workspace, or the remembered one is gone. A page asks for the workspace folder, the
  game, FAF's data and the vault, filled in from FAF's `fa_path.lua`; `SetUpWorkspaceAsync` checks
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

- `ViewModels/MainViewModel`: the window without the window. It holds the `EditedMap`, the
  brush settings and every operation of the menu as a plain method that takes its folder
  (`OpenAsync`, `LoadProjectAsync`, `ImportMapFolderAsync`, `SaveAsync`, `SaveAsAsync`,
  `ExportAsync`, `Undo`, `Redo`, `BeginStroke`,
  `ContinueStroke`, `EndStroke`, `Stroke`). The menu's commands ask for the folder first
  (`IEditorDialogs`), then call those methods; automation and tests call them directly. It uses no
  Avalonia types, so `tests/FAForever.MapEditor.App.Tests` tests it without a window.
- Opening, importing, saving and exporting are asynchronous (`LoadProjectAsync`,
  `ImportMapFolderAsync`, `SaveAsync`, `SaveAsAsync`, `ExportAsync`): they read and write on a pool
  thread while `IsBusy` disables the commands that cannot run alongside. Painting goes on during a
  save; reloads wait until a save or stroke ends. Keep the user interface's thread free of work
  that grows with the map: a full redraw of the view runs its rows in parallel.
- `Controls/HeightmapView`: the terrain from above, one pixel per sample, shaded and coloured by
  height and water (`Rendering/TerrainPicture`), with the mask of the
  layer being painted in orange; it draws again only the tiles a change touched. The markers lie
  over it at a fixed size on screen in the colour of their kind (spawns circles with their
  number, deposits squares, AI markers diamonds, weather triangles, path nodes dots with their
  links; View menu). Left mouse paints (a stroke is an `EditTransaction`, so one undo step, shown
  while it is painted), right or middle mouse moves, the wheel zooms around the cursor.
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
  it looks at. The left mouse button is left for editing.
  - The camera belongs to the view model (`MainViewModel.Camera`, `CameraMoved`), so both views
    share it: the top view draws what it sees (its footprint on the ground, the eye and a line to
    the pivot; View menu), and control and a click there sends it to that place at once. Opening
    a map puts it over the whole map. The status bar shows the place under the mouse in both
    views (`EditorCamera.PointAt` on the terrain itself in 3D).
  - That ground is the terrain blurred (`Rendering/Gpu/SmoothGround`: averages over squares of
    8, 16, 32, ... ogrids, read between their centres and between sizes), over about 15 % of the
    camera's distance (8 to 2048 ogrids): spikes and cliffs do not throw the camera up and down,
    and from close by it still follows hills. Only the eye keeps 2 ogrids clear of the terrain
    itself. The view makes it again when the heights change, at most twice a second. `Rendering/Gpu/TerrainRenderer` draws the terrain, then the albedo decals, the
  normal decals and the water; `TerrainShaders` follows the FAF map editor's port of the game's
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
  - The water is a surface at its elevation, drawn after the decals, darker and more opaque
    with depth down to the abyss elevation.
  - Uniforms of samplers must not share a texture unit with another sampler type: unit 0 holds
    the heights (`usampler2D`), 1 a decal's texture, 2 to 11 the layers, 12 and 13 the masks.
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

## Tests with the real window, and labels for screen readers and agents

- `tests/FAForever.MapEditor.App.Tests/MainWindowTest` runs the real `MainWindow` without a screen
  (Avalonia.Headless with Skia): the mouse, wheel and keys as a person uses them, and frames of
  what it shows. Test input and views there: the camera's controls, control and a click in the
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
`top.*`, `model.*`, `camera.smooth-ground`.

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
makes the repository and its first commit (`EnableVersionControlAsync`); afterwards the commit,
with how the repository stands (branch, changed files, unsaved changes, the last commit) and a
message (`CommitAsync`: saves first when the map has changes). What fails shows on the page.

## Diagnostics: what goes well and wrong

User story F-17. The editor keeps what a service keeps for Loki and Prometheus, per session
(`Diagnostics/Telemetry`):

- **Logs**: every `ILogger` of the editor (`Microsoft.Extensions.Logging.Abstractions`; get one
  with `MainViewModel.Logger(category)`, categories `Commands`, `Files`, `Workspace`,
  `Rendering`, `Performance`, `Automation`, `VersionControl`, `Editor`). The Console of the dock shows them, with
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
undo/redo, history, brush, zoom). Use it to check a change in the real editor:

```sh
faf-map-editor tests/FAForever.FileFormats.Map.Tests/assets/maps/HardFFA.v0001 --automation 5371 &
faf-map editor state                                      # the CLI is the client
faf-map editor brush mode=Flatten radius=30 strength=1 target=6
faf-map editor stroke points=[[60,128],[196,128]]         # in ogrids; one undo step
faf-map editor height x=128 z=128
faf-map editor screenshot path=shot.png map=true          # then look at the PNG
faf-map editor undo
faf-map editor close                                      # closes without asking about changes
```

- The state's `gpu` says which OpenGL the 3D view runs on, its error if it could not start, the
  textures on the card, the last texture that could not be read, and the decals with their draws
  and textures.
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
- Commands: `help`, `state` (with `mode`, `tool`, `toolNotBuilt`, `palette`), `commands` (every
  command of the controller: id, title, shortcut, story, `notBuilt`, `canRun`), `run {id}` (runs
  one as a menu item would; `ran: false` when it cannot run or is not built yet), `mode {id}`,
  `tool {id}`, `ui {hidden?}`, `perf {reset?}` (above), `logs {problems?, count?}` (what the
  Console shows), `diagnostics {record?, send?, endpoint?, flush?, upload?}` (the choices, the
  session's files, and with `flush`/`upload` writes or sends at once), `setup {folder, game?, faf?, vault?}` (the setup page; fields left
  out keep what it suggests), `update-assets`, `open {path}`, `import {path}`, `save`, `save-as {path}`, `version-control {open?, enable?, name?, email?, lfs?, remote?}` (how the project
  stands in git; `enable` turns it on with the choices), `commit {message}` (the commit's hash, or null
  when nothing changed),
  `export {output, version?}`, `undo`, `redo`, `goto {step}`,
  `brush {layer?, mode?, radius?, strength?, target?, erase?}` (layer 0 the heights, 1 to 8 a mask), `mask {layer, x, z}`,
  `stroke {points}`, `height {x, z}`, `hit {x, y}` (the control the mouse reaches at a point of
  the window, with its parents), `fit`, `show {markers?, pathNodes?, camera?}`, `view {zoom, x, z}`,
  `tab {name: top|3d}`, `camera {x?, z?, distance?, yaw?, pitch?}` (the 3D view, degrees; at once),
  `focus {x, z, radius?}` (with a radius as F, gliding while the 3D view shows; without, as
  control and a click in the top view),
  `screenshot {path, map?, gpu?}` (`gpu: true` reads a frame of the 3D view back from the card:
  a screenshot of the window does not reach it), `gpu-debug {mode}` (what the 3D view's terrain
  shows: 0 the picture, 1 the albedo, 2 the light, 3 the normals, 4 the heights, 5 the lower
  layer, 6 the first four masks after the remap, 7 as stored),
  `close`. A new menu item gets a command here too.
- The method does not matter, and a command without arguments works as a GET
  (`curl localhost:5371/undo`); Windows refuses a POST without a body, so the CLI always sends one.
- It listens on the loopback address only and only when asked to. Commands do not show dialogs:
  `close` does not ask about unsaved changes.
- An MCP server for agents would be a thin layer over these same commands.
