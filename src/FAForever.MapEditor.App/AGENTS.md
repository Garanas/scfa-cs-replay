# Agent guide: FAForever.MapEditor.App

The map editor's window (`faf-map-editor`, Avalonia 12, Fluent theme, dark). The repository-wide
guide is [`../../AGENTS.md`](../../AGENTS.md); everything about maps (the document, the project
format, undo, brushes, saving and reloading) is in the library and its guide,
[`../FAForever.MapEditor.Project/AGENTS.md`](../FAForever.MapEditor.Project/AGENTS.md). Keep this
project thin: what the menu does, the library does.

```sh
dotnet run --project src/FAForever.MapEditor.App -- [folder] [--automation 5371]
```

The folder is a project (it holds `map.json`) or a map folder of the game, which is imported.

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
  height and water (`Rendering/TerrainPicture`, which the 3D view shares), with the mask of the
  layer being painted in orange; it draws again only the tiles a change touched. The markers lie
  over it at a fixed size on screen in the colour of their kind (spawns circles with their
  number, deposits squares, AI markers diamonds, weather triangles, path nodes dots with their
  links; View menu). Left mouse paints (a stroke is an `EditTransaction`, so one undo step, shown
  while it is painted), right or middle mouse moves, the wheel zooms around the cursor.
- `Controls/TerrainView3D` (the 3D tab): a heightfield renderer in software ("voxel space"): per
  column of the picture, in parallel, it walks the ground away from the camera and fills the
  column from the bottom up, at half the control's resolution, with heights and colours
  (`TerrainPicture`) interpolated between samples, haze with distance and a flat water surface.
  No graphics card, so automation's screenshots show it; a column is a vertical plane, exact for
  a level camera and close for a tilted one. Left mouse turns, right or middle moves, the wheel
  zooms. On the 81 km test map a picture takes 0.1 to 0.2 s. A hidden view does no work.
- `Views/MainWindow`: menu, brush panel (heights or a layer's mask), history panel, the Top and 3D
  tabs, status bar; asks about unsaved changes before closing.
  `WindowDialogs` answers the view model's questions with the system's folder picker and a small
  save-changes dialog.
- The window watches the project folder (`ProjectWatcher`) and reads back what other tools change;
  during a stroke the changes wait until it ends.

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

- The state includes `busy`; a command that opens or saves answers when it is done.
- Commands: `help`, `state`, `open {path}`, `import {path}`, `save`, `save-as {path}`,
  `export {output, version?}`, `undo`, `redo`, `goto {step}`,
  `brush {layer?, mode?, radius?, strength?, target?, erase?}` (layer 0 the heights, 1 to 8 a mask), `mask {layer, x, z}`,
  `stroke {points}`, `height {x, z}`, `fit`, `show {markers?, pathNodes?}`, `view {zoom, x, z}`,
  `tab {name: top|3d}`, `camera {x?, z?, distance?, yaw?, pitch?}` (the 3D view, degrees),
  `screenshot {path, map?}`,
  `close`. A new menu item gets a command here too.
- The method does not matter, and a command without arguments works as a GET
  (`curl localhost:5371/undo`); Windows refuses a POST without a body, so the CLI always sends one.
- It listens on the loopback address only and only when asked to. Commands do not show dialogs:
  `close` does not ask about unsaved changes.
- An MCP server for agents would be a thin layer over these same commands.
