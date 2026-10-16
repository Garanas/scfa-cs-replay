# 02. What a tool does, in one class

Priority high, effort medium. Do it before the next tool is built.

**Built** (2026-10-09): `src/FAForever.MapEditor.App/Tools/`, and the App's guide describes it.
"Today" says what it replaced; the direction below is what was built, with "As built" where it
differs from the plan.

## Today

A tool is a `ToolDefinition` (`ViewModels/EditorModes.cs`): id, title, story, and what it is to
do while it is not built. What the two working tools do is spread out:

- **The views decide.** `HeightmapView.OnPointerPressed` (`Controls/HeightmapView.cs:203`):
  control and a click moves the camera, else when `CanPaint` a stroke starts, else the right or
  middle button pans. `OnPointerMoved` and `OnPointerReleased` continue and end the stroke. The
  3D view (`TerrainViewGpu`) cannot edit.
- **The view model knows tools by their id.** `IsSculpting => ActiveTool?.Definition.Id ==
  "terrain.sculpt"`, `IsPaintingStrata` likewise, `CanPaint` from both
  (`ViewModels/MainViewModel.Shell.cs:100`).
- **The stroke is in the view model**, with a branch per tool: `BeginStroke` names the undo step
  with a `switch` over the brush mode, `Dab` paints the heights or a mask by `PaintedLayer`
  (`ViewModels/MainViewModel.cs:314-400`, `644`).
- **The settings of both tools are on the view model**: `BrushMode`, `BrushRadius` (shared),
  `BrushStrength`, `BrushTarget`, `MaskStrength`, `MaskErase`, `SelectedPaintTarget`.
- **Tool and paint target follow each other both ways.** `SelectTool` chooses a paint target,
  and choosing a paint target (`OnSelectedPaintTargetChanged`, `FollowPaintTarget`) chooses the
  tool (`MainViewModel.Shell.cs:211-255`). Guards keep it from looping.
- **The options bar shows a block per tool** with `IsVisible="{Binding IsSculpting}"` and
  `IsPaintingStrata` (`Views/ToolOptions.axaml`).

## What it costs

A new tool touches `EditorModes`, the view model (a flag, `CanPaint`, its settings, its branch
in the stroke), `ToolOptions.axaml`, `HeightmapView`, and for 3D `TerrainViewGpu`. A reader of
`HeightmapView` has to know which tools exist. The two-way link between tool and paint target
has to be traced through four methods to be sure it ends. Some 20 tools are still to come, and
each would add a branch to the same places.

## Direction

**One class per working tool**, in `Tools/`: `SculptTool`, `StrataTool`. A tool holds its
settings (observable properties, so the options bar binds to them) and what the left mouse does,
in map coordinates:

```csharp
/// <summary>
/// What the left mouse does in the views while a tool is active, in ogrids.
/// </summary>
public interface IMapTool
{
    void Press(float x, float z);

    void Drag(float x, float z);

    void Release();
}
```

The interface earns its place: the views call "the active tool" and no longer know which tools
exist. That is knowledge taken out of them, not added.

- **The views keep navigation**: panning, zooming, control and a click for the camera. That is
  the same for every tool. The left mouse goes to `model.ActiveTool?.Tool`.
- **A stroke is a small concrete class**, `BrushStroke`, used by both tools: it opens the
  `EditTransaction`, spaces the dabs a quarter of the radius apart, commits or cancels, and
  says when the document changed. Composition, not a base class: a tool that is not a brush
  (curve, ruler) does not get it. A stroke is input that lasts while the mouse is down, not a
  command: it opens its own transaction instead of going through the mediator's `[UndoStep]`
  (file 01), and commands that edit wait until it ends.
- **`ToolItem` holds the tool**: `IMapTool? Tool`, null while it is not built. The tools are
  made once per editor, so their settings last while the person switches tools.
- **The options bar picks a template by type**: `<ContentControl Content="{Binding
  ActiveTool.Tool}" />` with a `DataTemplate` per tool class in `ToolOptions.axaml`. Avalonia
  chooses it; the `IsVisible` flags go.
- **`StrataTool` owns its layer.** Choosing a stratum in the Paint panel sets
  `StrataTool.Layer`; the link from paint target back to tool goes. The top view shows the mask
  of `(ActiveTool?.Tool as StrataTool)?.Layer`, through one property on the view model.
  Automation's `brush layer=` becomes a command with an argument (`paint.layer {layer}`, file 01).
  *As built*: `brush.set` keeps its arguments, so scripts and agents work as before; a layer
  chooses its tool on purpose there, and radius and strength go to the active tool. The view
  model has `StrataTool` as a property, for the Paint panel's list to bind to.
- **The 3D view can edit later** with no tool changes: it turns the mouse into a place on the
  ground (`Camera.PointAt`) and calls the same three methods.

A decision for the owner: the radius is shared between sculpting and painting today. Per tool is
simpler to reason about; if a shared radius is wanted, both tools read one `BrushRadius` that the
view model keeps. *As built*: per tool.

*As built*, beyond the plan: `IMapTool` also gives the radius of the circle the views draw
around the mouse (`CursorRadius`, 0 for none), and the view model keeps the stroke's transaction
(`StartStroke`, `EndStroke`, `StrokeInProgress`), since the shown document, undo, reloads and
`Needs.NoStroke` depend on it. A stroke the editor ends (another map opens) is left alone by the
tool that held it.

## Avoid

- A tool registry, factory or descriptor system: `EditorModes` already lists the tools.
- A generic options editor built from descriptions of the settings. A `DataTemplate` per tool is
  plainer. Lua needs the generic one (file 06); build it then, and move built-in tools to it only
  if it turns out simpler.
- A base class `BrushTool` that later tools have to fit into.

## Done when

- `IsSculpting`, `IsPaintingStrata`, `CanPaint` and the stroke methods are gone from the view
  model, and no code compares a tool's id outside `EditorModes`.
- `HeightmapView` has no tool-specific code.
- A new tool is one class, one `DataTemplate` and one line in `EditorModes`, with its test.
