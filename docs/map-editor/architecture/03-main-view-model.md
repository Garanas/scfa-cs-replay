# 03. `MainViewModel` holds everything

Priority medium, effort medium. Easier after 01 and 02, which take parts of it away.

## Today

One class in five partial files, 1747 lines:

| File | Lines | What |
|---|---|---|
| `MainViewModel.cs` | 717 | the open map, opening, saving, exporting, busy, undo, history, the watcher and reloads, the brush and the stroke, the dialogs |
| `MainViewModel.Workspace.cs` | 363 | the stages of the start, the setup page, checking and converting assets |
| `MainViewModel.Shell.cs` | 333 | controllers, modes, tools, overlays, view and dock tabs, the palette |
| `MainViewModel.Diagnostics.cs` | 215 | the Console, the choices about recording and sending |
| `MainViewModel.Camera.cs` | 119 | the 3D camera and the blurred ground it rests on |

The partials split the file, not the state. Every method can reach every field: `SaveAsync`
depends on `_stroke`, `_pendingReload`, `IsBusy` and `ReloadPending`; `ConvertAssetsAsync` on
`_pendingWorkspace`, `_openWhenReady` and `_closing`. Every controller (a handler, after file 01)
gets the whole view model.
The XAML binds deep paths into it from every view
(`$parent[UserControl].((vm:MainViewModel)DataContext).ChooseToolCommand`).

## What it costs

To change one part safely, a reader has to check which of some forty fields and properties the
other partials touch. Each user story adds to the same class, and tests of one part build the
whole editor.

## Direction

Take out the parts that have their own state and few links to the rest, as plain classes that
`MainViewModel` makes and holds as properties. The views bind through them
(`{Binding Setup.Folder}`). In order, the loosest first:

1. **`DiagnosticsViewModel`**: the Console, its problems and filter, the choices about recording
   and sending, opening the folder. It needs `Telemetry` and `UserSettings`. One class for
   user story F-17; split the Console off if it grows.
2. **`PaletteViewModel`**: open, query, results, selection, run. It needs the `CommandRegistry`.
3. **`WorkspaceSetupViewModel`**: the stage, the setup fields, validating, converting, the browse
   buttons. It needs `IEditorDialogs`, `UserSettings` and a way to say "ready": it raises an event
   with the `Workspace`, and `MainViewModel` sets it and opens the folder it was started with.
4. **The ground of the camera**: the cache of `SmoothGround` (`MainViewModel.Camera.cs:79`) moves
   next to `EditorCamera`, which takes the heightmap from a function as it does now.
   `MainViewModel` keeps `Camera` and `CameraMoved`.

What stays: the open map and its operations, busy, history, modes and tools, overlays. With the
stroke moved to the tools (file 02), that is the editor's core, around 500 to 700 lines.

**The status line** is written by most parts. It stays on `MainViewModel`; a part that writes it
gets an `Action<string>` in its constructor. That keeps who writes it visible without a service.

**Marshalling to the window's thread** is written twice (`Watch` at `MainViewModel.cs:548`,
`OnLogged` in the Diagnostics partial): one private helper `OnUi(Action)`, or one in the
diagnostics class if that is the only user after the split.

## Avoid

- Making the view models with the container that the mediator brings (file 01): four objects made
  in one constructor read better. The container holds the `MainViewModel` instance for the
  handlers, and nothing more of this file.
- Handlers that take the whole `MainViewModel` when they need one part: a handler takes the part
  it works on (`WorkspaceSetupViewModel` for `setup`), so its constructor says what it touches.
- An interface per view model; they have one implementation.
- CommunityToolkit's `WeakReferenceMessenger` or another message bus: it hides who listens.
- Splitting the core into "services" before a second user needs them. A separate class for the
  open map and its operations is worth it when plugins need it without the window (file 06), not
  before.

## Done when

- No partial or class in `ViewModels/` holds more than one user story's area, and each split-off
  class can be read without opening `MainViewModel`.
- Their tests make the class itself, not the whole editor.
- `MainViewModelTest`, `WorkspaceStartTest` and automation use the new paths
  (`model.Setup.Folder`).
