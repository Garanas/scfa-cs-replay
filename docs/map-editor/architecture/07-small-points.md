# 07. Smaller points

Priority low, effort small each. Some are "leave it": listed so nobody spends time on them.

## Worth a small change

- **Keys of the 3D view that are not commands.** F (focus under the mouse) and Home (the whole
  map) are handled in `TerrainViewGpu.OnKeyDown` (`Controls/TerrainViewGpu.cs:449-455`). They are
  single letters on purpose, working only while the 3D view has the keyboard
  (`IdsAndShortcutsAreUnique` forbids single-letter shortcuts for the window). Register them as
  commands without a gesture (`view.focus`, `view.home`) that the view's keys run, so the palette
  and automation find them. Space (hold to turn) is input and stays in the view.
- **Ctrl+Shift+Z is added by hand** next to the generated shortcuts
  (`Views/MainWindow.axaml.cs:146`), since a command has one gesture. Allow several
  (`Gesture = "Ctrl+Y, Ctrl+Shift+Z"`) when a second command needs it; one exception is fine
  until then.
- **The same list of expected exceptions** (`MapProjectException or IOException or
  InvalidOperationException or ArgumentException`) is written in `TryAsync` and `ReloadPending`.
  One static `IsExpected(Exception)` keeps them equal.
- **Timing, metrics and logging of an operation** are written in `BusyAsync` and again in
  `ConvertAssetsAsync`. The pipeline's Busy behaviour (file 01) takes over `BusyAsync`; make
  converting the assets a command (`workspace.convert`) and it goes through the same behaviour.
- **`EditorCamera` and `SmoothGround` are in `Rendering/Gpu`** but use no graphics card; the view
  model imports a GPU namespace for them. Move them to `Rendering/` with the camera's ground
  (file 03, point 4).
- **`UndoHeader` and `RedoHeader`** are read only by tests (file 01).

## Leave it

- **`Performance`, `EditorMetrics` and `Program.Options` are static.** A `Meter` is static by
  convention, and a span in one line (`using Performance.Span measure =
  Performance.Measure("...")`) is what keeps measuring cheap to add. Passing them around would add
  more than it takes away.
- **The top view's zoom and offset live in the view, the 3D camera in the view model.** Nothing
  but the top view needs its zoom; the camera is shared by both views. The difference is real.
- **`AutomationServer` knows the `MainWindow`.** Automation reads the window (`ui`, `hit`,
  `screenshot`) by its nature.
- **The menus are built in code-behind** (`BuildMenusAndShortcuts`). It is short, and generating
  them from the registry and the menu layout (file 01) is the point.
