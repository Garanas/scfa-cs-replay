---
name: editor-ui
description: How to build and change the FAF map editor's window (src/FAForever.MapEditor.App, Avalonia 12) so it stays consistent, accessible, testable and drivable by agents. Use before adding or changing a view, control, panel, page, style, colour or any UI behaviour of the map editor, and when checking such a change in a running editor.
---

# The map editor's UI

The full rules and their reasons are in `src/FAForever.MapEditor.App/AGENTS.md` (sections "Every
feature: the checklist" and "UI rules"). This is the short form to work from.

## Before writing Avalonia code

Ask the `avalonia-docs` MCP server (Avalonia's own documentation, v12) when unsure of an API: WPF
habits and Avalonia 11 answers differ (`ActualThemeVariantChanged`, not a property; `InputHitTest`
is an extension in `Avalonia.Input`; `Bitmap.Save` takes encoder options).

## Look: tokens and classes, never values

- Numbers in fields: a point or a comma typed, a point shown (`EditorCulture`); a `TextBox` bound
  straight to a number takes `Converter={x:Static controls:NumberTextConverter.Instance}`.
- Colours, font sizes, weights, opacities and distances come from `Styles/Tokens.axaml` (by use:
  `CanvasBrush`, `SurfaceBrush`, `TextTertiaryBrush`, `DangerBrush`, `SpaceMedium`, `PagePadding`, ...)
  through the classes of `Styles/Classes.axaml`:

  | Instead of | Write |
  |---|---|
  | `FontSize="26" FontWeight="SemiBold"` | `Classes="title"` |
  | `FontWeight="SemiBold"` on a heading | `Classes="heading"`, or a `controls:Section Header="..."` |
  | `Opacity="0.7" FontSize="12" TextWrapping="Wrap"` | `Classes="caption"` |
  | `Opacity="0.8"` on an introduction | `Classes="lead"` |
  | `Foreground="#ff8a7a"` | `Classes="error"` |
  | `Background="#1a1a1a" Padding="12"` | `Border Classes="panel"` |
  | `Spacing="8"` | `StackPanel Classes="stack"` |
  | a page's `Margin="32" Spacing="16" MaxWidth=...` | `StackPanel Classes="page"` (`page wide`) |
  | `Margin="0,12,0,0"` before a part | `Classes="section"` |

- Fluent's own controls (buttons, combo boxes, sliders, lists, tabs, menus) take the tokens
  through `Styles/FluentBrushes.axaml`; a control that still looks like Fluent lacks a key there.
- Group labels: `Classes="overline"` with the text through `controls:UpperCaseConverter`; numbers
  and coordinates `mono`, shortcuts `shortcut`.
- A value the tokens lack becomes a token (both theme variants), a look used twice becomes a class,
  a piece of UI used twice becomes a control (`Controls/FolderField`, `Controls/Section`).
- Code that draws reads tokens with `TryFindResource(key, ActualThemeVariant, ...)` and reads them
  again on `ActualThemeVariantChanged`. Only colours from the map (a marker's) are made in code.
- `StyleRulesTest` fails otherwise.

## Every control one can read or use

- `AutomationProperties.Name` (and `HelpText` when the use is not obvious) unless its own text
  says what it is. `MainWindowTest.EveryControlOneCanUseHasALabelOrText` fails otherwise.
- A new kind of control that `Automation/UiTree.cs` does not know as usable, or whose text it
  cannot read, gets a line there.

## Every operation a person can do

- A request with `[EditorCommand(id, title, Gesture = ..., Story = ..., Needs = ...)]` and its
  handler in one file under `Commands/<menu>/`, and its id in `Commands/Menus.cs`: it is in the
  menus, the palette, the shortcuts and automation (`run id=... ...arguments`) at once. Its
  arguments are the record's properties; a folder left out is asked for, but never without a
  person. An edit of the map: `[UndoStep]`, edit through `CurrentEdit`. Not built yet: a request
  without a handler with `[Incomplete("T-4", "what it is to do")]`, in the menu's `Planned.cs`.
  A tool: a `ToolDefinition` in `ViewModels/EditorModes.cs` (`Missing` until it works, then
  `Make`), a class in `Tools/` implementing `IMapTool` (the left mouse; a brush uses
  `BrushStroke`), and a `DataTemplate` for its settings in `Views/ToolOptions.axaml`, its root a
  `controls:ToolRow Classes="toolrow"` with the settings most used first (what does not fit at the
  window's width goes behind the row's More, over the views; the row never grows); the views
  never name a tool. A part of a panel not built yet: a `PlannedSection`. Every story named must be in `USER_STORIES.md`
  (`CommandRegistryTest`). Its state in automation's `state`.
- Tests: the operation in `MainViewModelTest`; the mouse, keys and what the window shows in
  `MainWindowTest` (headless, `HeadlessApp.Run`). The 3D view needs a graphics card: check it in a
  running editor.
- Work per frame, per mouse move, or growing with the map: a `Performance.Measure` span; no disk
  access per frame; a hidden view does no work.
- What can go wrong is logged: `model.Logger("Files").LogError(exception, "Saving {Folder} failed: {Reason}", ...)`,
  a template with named values; it shows in the Console and goes to the session's files
  (the App's guide, "Diagnostics"). Read it with `faf-map editor logs problems=true`.

## Check it

```sh
# build and test outside the user's editor's output (an editor started with faf-map editor start runs a copy and locks nothing)
dotnet test tests/FAForever.MapEditor.App.Tests --artifacts-path <scratch>/artifacts
# a changed look: look at snapshot-results/*.png in the test output, then accept it
UPDATE_SNAPSHOTS=1 dotnet test tests/FAForever.MapEditor.App.Tests --artifacts-path <scratch>/artifacts

# a test editor of your own: its own port and a scratch workspace (never the user's editor on 5371,
# never the user's workspace: the editor writes the sandbox into it when started without a map)
faf-map workspace scratch <scratch>/ws --from "<real workspace>" --settings <scratch>/settings.json
faf-map editor start --port 5372 --settings <scratch>/settings.json   # waits until ready; opens the sandbox
faf-map editor start --port 5372 --settings <scratch>/settings.json --map <map folder>
faf-map editor restart --port 5372                    # stop and start with the same arguments: runs the new build
faf-map editor run id=view.movePanel panel=inspector place=float --port 5372   # left|right|bottom|float, a dock tab also dock
faf-map editor expand label="Armies and their unit groups" all=true --port 5372 # tree items open, so ui lists them
faf-map editor ui --port 5372                         # the controls, labels, values and places
faf-map editor ui under=MainMenu --port 5372          # one part of the window
faf-map editor state --port 5372                      # views (bounds, pixels, scaling), loading, selection
faf-map editor screenshot path=<scratch>/shot.png settle=true --port 5372   # then look at it
faf-map editor tab name=3d --port 5372 && faf-map editor screenshot path=<scratch>/3d.png gpu=true settle=true --port 5372
faf-map editor tab name=side --port 5372              # both views side by side (gpu=true captures the 3D half)
faf-map editor run id=view.togglePanel panel=inspector on=false --port 5372   # more room for the views; view.resetLayout
faf-map editor perf gpu=true --port 5372              # where the time goes, with the card's (3d.gpu-frame)
faf-map editor stop --port 5372                       # only an editor you started; waits until it exited
```

Drive the window as a person does (its own input, never the real mouse):

```sh
faf-map editor objects kind=units count=3 --port 5372          # refs and places to aim at
faf-map editor get ref=unit:UNIT_1 --port 5372                 # place, heading, blueprint, army (or name=ARMY_1)
faf-map editor objects kind=markers name=mass --port 5372      # by part of the name, blueprint or texture
faf-map editor view-point ref=unit:UNIT_1 --port 5372          # its pixel in the top and 3D views
faf-map editor tool id=units.select --port 5372
faf-map editor mouse view=3d x=335 y=260 --port 5372           # click there (action=move|down|up|drag|wheel)
faf-map editor mouse view=top x=100 y=80 action=drag to=[160,80] modifiers=Shift --port 5372
faf-map editor key key=E --port 5372                           # the view has the keyboard after the click
faf-map editor key key=Shift action=down --port 5372           # held across mouse actions until action=up
faf-map editor key key=Ctrl+K --port 5372 && faf-map editor type text=undo --port 5372
faf-map editor run id=units.pick interactive=true wait=false --port 5372   # opens the picker, answers at once
faf-map editor key key=Down --port 5372 && faf-map editor key key=Enter --port 5372
faf-map editor drag view=3d from=[10,10] to=[300,200] data=<asset key> --port 5372   # a shelf tile dropped
faf-map editor state --port 5372                               # handles.points: each handle's pixel in the top and 3D views; hover: what the 3D mouse is over
faf-map editor click label="Sculpt layer" --port 5372                  # scrolls the control into view, clicks its centre
faf-map editor state --port 5372                               # dialog: the question over the window (unsaved changes, confirm)
faf-map editor dialog button="Don't save" --port 5372                 # presses its button; Escape or key Tab/Enter work too
faf-map editor key key=OemCloseBrackets --port 5372            # [ and ] the brush's radius, shift: strength; Ctrl+wheel over a view
```

Set controls, look at things, read the map:

```sh
faf-map editor set label="Height brush strength" value=0.3 --port 5372   # slider, text field (typed, Enter), check box, combo, list, tree
faf-map editor set label="Height brush mode" value=Flatten --port 5372   # opens the options row's More when the control is behind it
faf-map editor focus ref=unit:UNIT_1 --port 5372                         # frames it in both views (view.frame); selection=true: the selection
faf-map editor grid target=stratum:1 x=100 z=60 --port 5372             # heightmap, types, stratum:N, water:foam, mask:<name>; or x0 z0 x1 z1 step
faf-map editor screenshot part=options path=<scratch>/o.png --port 5372 # options, inspector, panel, dock, rail, top, 3d
faf-map editor undo --port 5372                                         # ran: false comes with why ("nothing to undo")
faf-map editor wait path=selection.count value=4 timeout=5 --port 5372  # polls the state until it matches; state.build: version, built
faf-map editor view zoom=4 --port 5372                                  # the top view zooms about its middle
faf-map editor state --port 5372                                        # looks: shader, sky (drawn in 3D?), grid and decal lines the top view drew, missing cube maps
faf-map editor run id=plugin.scatter-rocks.scatter count=10 --port 5372  # a Lua plugin's command (state.plugins: errors, commands, tools)
faf-map editor tool id=plugin.plateau-brush.plateau --port 5372          # a Lua plugin's tool: then mouse/stroke as with any tool
```

Keep answers small, compare, and see what a command opened:

```sh
faf-map editor key key=E --port 5372                      # every command answers its result and a short state (mode, tool, status, undo, selection count, dialog)
faf-map editor key key=E full=true --port 5372            # the whole state; quiet=true none; paths='["selection.count","history"]' only those
faf-map editor state path=selection.count --port 5372     # one value of the state ({"selection.count": 3})
faf-map editor ui type=Button text=save --port 5372       # a flat list of the controls of a type and/or holding a text (with under: one part)
faf-map editor perf save=<scratch>/before.json --port 5372   # then change something, and:
faf-map editor perf before=<scratch>/before.json --port 5372 # each span's change, the one that grew most first (spans=3d. filters by name)
faf-map editor run id=decals.forward --port 5372          # a command that refuses answers ran: false with why, never an error
faf-map editor run id=file.open interactive=true --port 5372 # answers at once with asked (the question it opened); then dialog button=Cancel
faf-map editor screenshot part=dialog path=<scratch>/q.png --port 5372   # the question's card
faf-map editor focus ref=unit:UNIT_1 --port 5372          # the 3D camera at 50 degrees, at once, framing it with a margin (pitch=90: straight down)
faf-map editor menu path=File --port 5372                 # a menu's items; path="File/Save as a template" clicks one
faf-map editor window width=1280 height=800 --port 5372   # the window's size; close=true closes it as its close button (the unsaved-changes question)
faf-map editor dialog text="My template" button="Save the settings" --port 5372   # a question with a text field
faf-map editor ui under=Sky type=Button --port 5372       # under takes a section's heading too
faf-map editor run id=file.open folder=<project> discard=true --port 5372   # without discard it refuses while the map has unsaved changes
faf-map editor run id=view.inspectPart part=water.foam --port 5372   # an entry of the Water or Lighting panel's list in the inspector (state.panelParts)
faf-map editor run id=help.shortcuts query=undo --port 5372          # the keyboard shortcuts page (state.shortcuts); help.presetsFolder ... answer their paths
faf-map editor run id=file.workspaceSettings --port 5372             # the workspace settings page (state.workspaceSettings); never Convert again in a scratch workspace
```

A new part of a mode follows "the panel says what there is, the inspector edits what you chose":
an entry of the panel's list (`PanelParts`), its fields in the inspector (`PanelPartView`).

`faf-map editor start` detaches the editor (its output in `editor-<port>.log` beside the settings
file, or `--log`), so the call returns with its JSON even inside a pipe. It runs a copy of the build
(`faf-map-editor-shadow/<port>` in the temporary folder, `--shadow`), so `dotnet build` works while
it runs; stop and start it again to run a new build (`--no-shadow` runs the build itself).

Headless: `new AutomationServer(window, model, 0).HandleAsync("mouse", ...)` runs the same commands
(`AutomationTest`), and `EditorDriver.InProcess(automation)` (or `EditorDriver.Http(port)`) wraps them
typed (`Set`, `Run`, `Click`, `Key`, `PointOf`, `Grid`, `Focus`, `Screenshot`); `HeadlessApp.Capture(control)`
draws one control; `TestAssets.Catalog(unitModel: true)` and `TestAssets.SandboxProject()` give assets
with models and a map with one of every kind of thing; `TestMaps.Slope(degrees)` a flat map with a
clean ramp (slope limits, alignment).

## Editing files from the shell

The files are CRLF and UTF-8: heredocs with apostrophes break, `sed -i` may write LF, Python's
`open()` is cp1252. Prefer Edit/Write; for a scripted change write the old and new text to files and
run `faf-map dev replace <file> --old-file old.txt --new-file new.txt [--count n | --all]`: it keeps
the file's line endings, UTF-8 and byte order mark, and refuses when the old text does not occur as
often as expected (once by default).

A floating panel lies in a window of its own: `ui` lists it as a `FloatingWindow` node (places
inside relative to it), and `click`, `set` and `screenshot part=` reach it. In Git Bash, an
argument starting with `/` (`value=/env/...`) is rewritten into a Windows path: prefix the call
with `MSYS_NO_PATHCONV=1`. `faf-map workspace scratch <ws> --convert-into <assets>` converts the
assets into a folder of its own (an asset-format change); `workspace init --settings <file>`
never touches the user's settings.

Read `ui` before guessing from a screenshot; look at a screenshot before saying a look is right.
