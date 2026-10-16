# 05. Coarse events and refreshes

Priority low, effort small. Measure first: these are what the code does, not stalls anybody has
seen. Some of it disappears with files 01 and 02.

**Built** (2026-10-10): `CommandRegistry.CanRunChanged`, `MainViewModel.OverlaysChanged`, and
`ShownMask` as a property the top view listens to; the App's guide describes them. "Today" says
what it replaced.

*Measured*: the menus' refresh (span `window.commands`) takes 0.06 ms for all their items, so
the change is for the reader, not for time. It runs for a stroke's start and end, an undo and a
redo, and never while a slider moves (`MainWindowTest`, `MainViewModelTest` count it).

*As built*, beyond the plan: starting and ending a stroke change what the commands need
(`Needs.NoStroke`), so `StartStroke` and `EndStroke` raise `CanRunChanged` too. (A closed menu
reads `CanExecute` again when it opens, so the menus would show it right without; a shortcut's
or a plugin's button would not.) Switching tools raises `CursorChanged` (the brush around the
mouse); the top view's picture is drawn again only when the shown mask changes with it.

## Today

- **`DocumentChanged` carries nothing** and is raised for an edit, an undo, a reload, every dab
  of a stroke, and also when an overlay is turned on or off (`OnShowMarkersChanged`) and when
  another paint target is chosen. The views then work out what changed (`TerrainPicture.Update`,
  the changed tiles). The library's `EditSession.Changed` does say what changed
  (`DocumentChange(Previous, Current, Description, Reason)`).
- **`Refresh()`** (`ViewModels/MainViewModel.cs:615`) runs on every change of the session and of
  `IsBusy`: ten property notifications, seven `NotifyCanExecuteChanged` and a new history list.
- **The window refreshes every menu item and shortcut on any property change** of the view model
  except two (`Views/MainWindow.axaml.cs:150`): over a hundred `CanExecuteChanged` events while
  the radius slider is dragged, while the conversion reports progress, for every status line.

## What it costs

Mostly reasoning: "who reacts to `DocumentChanged`" has no short answer, and a reader cannot tell
from the event whether to redraw the terrain or only the markers. The cost in time is unknown.

## Direction

1. **Measure**: put a span around the adapters' refresh in `MainWindow.OnModelChanged` and drag
   the radius slider; look at `perf`. If it is under a millisecond, leave point 3 for when it
   matters.
2. **Name the two kinds of change.** `DocumentChanged` only when the shown document changes (an
   edit, undo, reload, a dab). The overlays and the painted layer are properties; the views
   listen to those and draw again without rebuilding the terrain picture where they can.
3. **Command availability changes in one place.** The `CommandRegistry` gets one event,
   `CanRunChanged`, raised by `MainViewModel.Refresh` (where a map opens, busy changes or undo
   changes) and when the stage changes. The window refreshes its adapters on that event only.
4. With the `RelayCommand`s gone (file 01), their seven `NotifyCanExecuteChanged` calls go too.

## Avoid

- Reactive libraries (ReactiveUI, Rx) or observable state frameworks: two well-named events do.
- Fine-grained events per kind of edit before a view needs them.

## Done when

- A reader can say from the event's name what a view must do with it.
- Dragging a slider raises no `CanExecuteChanged` (checked with a span or a test that counts).
