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

- An automation command (`Automation/AutomationServer.cs`, `help`, the guide) that does the same
  through `MainViewModel`; its state in `state`.
- Tests: the operation in `MainViewModelTest`; the mouse, keys and what the window shows in
  `MainWindowTest` (headless, `HeadlessApp.Run`). The 3D view needs a graphics card: check it in a
  running editor.
- Work per frame, per mouse move, or growing with the map: a `Performance.Measure` span; no disk
  access per frame; a hidden view does no work.

## Check it

```sh
# build and test outside the user's editor's output (it locks bin/)
dotnet test tests/FAForever.MapEditor.App.Tests --artifacts-path <scratch>/artifacts
# a changed look: look at snapshot-results/*.png in the test output, then accept it
UPDATE_SNAPSHOTS=1 dotnet test tests/FAForever.MapEditor.App.Tests --artifacts-path <scratch>/artifacts

# a test editor of your own: port 5372, its own settings (never the user's on 5371)
FAF_MAP_EDITOR_SETTINGS=<scratch>/settings.json faf-map-editor <map folder> --automation 5372 &
faf-map editor ui --port 5372                         # the controls, labels, values and places
faf-map editor state --port 5372
faf-map editor screenshot path=<scratch>/shot.png --port 5372   # then look at it
faf-map editor tab name=3d --port 5372 && faf-map editor screenshot path=<scratch>/3d.png gpu=true --port 5372
faf-map editor perf --port 5372                       # where the time goes
faf-map editor close --port 5372                      # only an editor you started
```

Read `ui` before guessing from a screenshot; look at a screenshot before saying a look is right.
