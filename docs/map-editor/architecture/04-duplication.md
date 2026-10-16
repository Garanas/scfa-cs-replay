# 04. Lists kept by hand in two or three places

Priority medium, effort small. Duplication is fine where the copies may grow apart; these must
stay equal, and nothing checks that they do.

Built with file 01: the overlays' and the modes' commands are made from their data
(`EditorOverlays`, `EditorModes`), `assets.show` is gone, and the planned commands are a line each.

**Built** (2026-10-09), the rest: the tabs are the enums `MapViewTab` and `DockTab`, bound through
`EnumIndexConverter` (a test checks the order of the `TabItem`s), and the Markers panel's check
boxes are the overlays with `Panel = "markers"`, each running its overlay's command
(`OverlaySection`). *As built*: the dock keeps one request per tab (`view.problems`, ...), since
each has its own user story; their one handler names the tab by the enum.

## Overlays: three ways to show the markers

Today, the markers are shown or hidden by:

- the chips above the views: `OverlayItem`s made in `InitializeShell`
  (`ViewModels/MainViewModel.Shell.cs:172`);
- the View menu: `ViewController.ToggleMarkers`, `TogglePathNodes`, `ToggleCamera`, and seven
  `[Incomplete]` toggles;
- the check boxes of `MarkerVisibilitySection` in the Markers panel (`Views/ModePanel.axaml`).

A chip of an overlay not built yet runs its menu command by a name glued together:
`$"view.overlay.{overlay.Id}"` (`MainViewModel.Shell.cs:317`), and calls `overlay.Refresh()`
first to undo the toggle the button already made.

**Direction**: the overlay list is the one source. Register the `view.overlay.<id>` commands from
it, as the `tool.*` commands are registered from the tools: each an entry of the registry that
sends `new ToggleOverlay(id)`, one request and handler for all of them (file 01). An overlay not
built yet carries its
missing text (as `ToolDefinition.Missing`) and its command is not built. The chips and the menu
then come from the same list. The Markers panel binds its check boxes to the same `OverlayItem`s,
or drops them since the chips are always visible.

## Modes: tools are generated, modes are not

The `tool.*` commands come from `EditorModes`. The commands that choose a mode (`map.mode`,
`terrain.mode`, ..., F1 to F9) are written by hand on nine controllers and repeat the modes'
ids and titles.

**Direction**: add `Gesture` to `ModeDefinition` and register `<id>.mode` from `EditorModes`, each
sending `new ChooseMode(id)`. Nine methods go, and a new mode is one entry.

## Dock and view tabs as numbers

`DockIndex = 2` means the Assets tab, written in `AssetsController.Show` and
`ViewController.Assets`, which are the same command twice. `ViewIndex` 0 and 1 are the top and 3D
views.

**Direction**: an enum each (`DockTab`, `MapViewTab`) and one command per tab. Remove
`assets.show` or `view.assets`.

## The automation commands, three times

See file 01: generated from the registry.

## Commands not built yet: a line each

56 controller methods are empty with `[Incomplete]`. With requests and handlers (file 01) a
planned command is a request record without a handler, a line in its menu's `Planned.cs`. Its
place in the menu comes from the menu layout (`Views/Menus.cs`), which lists it with the built
commands, so the menu shows today what it will hold.

## The guides repeat the code

`AGENTS.md` of the App lists commands, classes and automation commands that the code already
names. Lists like these fall behind. Where the code or a command (`help`, `commands`) can answer,
the guide says where to look rather than copying the list. The rules and the reasons stay in the
guide; those the code cannot say.

## Done when

- A new overlay, mode or dock tab is one entry, and its menu item, chip and command follow.
- No command is a copy of another, and no code builds a command id from pieces.
