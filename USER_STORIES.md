# Map editor: user stories

What the FAF map editor (`src/FAForever.MapEditor.*`) is to do, as stories of the map maker. A
checked box is done; a story may be partly done, which its line says. The ids stay as they are
when stories move or change, so commits, issues and the UI design can refer to them.

The stories follow the editor's foundation (F): every operation is a command of the controller,
reachable from the window, automation and later Lua; every asset is a file on disk that the editor
reads again when it changes; and nothing that generates runs by itself.

## F: Foundation

- [ ] **F-1 One controller.** Every operation is a command with a name, arguments and a
      description, in one registry (the controller of model, view, controller). Menus, toolbars,
      shortcuts, the command palette, automation, the CLI and Lua plugins all run the same commands.
      Today: every command is a request with its arguments and a handler, sent through MediatR
      by the registry; the menus, shortcuts and palette come from it, and automation and the CLI
      run it (`faf-map editor run id=...`); a toolbar in the menu bar is made from the registry
      too (undo, redo, save, the palette, symmetry by default; its commands kept in the user's
      settings, `view.setToolbar`). Missing: Lua.
- [x] **F-2 Command palette.** Find and run any command by typing (`Ctrl+K`), with its shortcut shown.
- [x] **F-3 Undo and redo.** Every change is a step; a brush stroke or a plugin run is one step.
- [x] **F-4 History.** The steps in a panel; choosing one goes back or forward to it.
- [x] **F-5 Workspace.** One folder per computer with the projects and the game's assets converted
      from `env` and `units` (`.scd` with FAF's `.nx2` over them), set up at the first start and
      converted again when FAF or the editor changes. Every workspace has a sandbox
      (`projects/Sandbox`: a small map with one of each kind of thing, made of its own assets),
      written again when missing and opened when the editor starts without a folder; it is not
      publishable (exported to play with a warning, never uploaded).
- [x] **F-6 Assets on disk, read again when they change.** The project's files (JSON, PNG) and the
      workspace's assets: a tool that saves one, the editor shows it.
- [ ] **F-7 Generation only when asked.** A layer or result that is generated (walkability, props
      from rules, a baked heightmap) shows that its inputs changed and offers to generate again; it
      never runs by itself. Today: the generators' registry with the inputs each result was made
      from; the status bar names the results out of date, and Generate, Everything out of date makes
      them again; walkability, the normal map and the preview are generated this way (T-7, S-4).
      Missing: props and masks from rules (P-4, K-2).
- [ ] **F-8 Asset discovery.** See which assets are loaded and where each comes from (the game,
      FAF, the workspace's own, the map's own files), which are in use by the map, which are
      missing; open an asset's file; reload one. Today: the asset shelf (F-9) shows what the map
      uses and how often, shows an asset's file and reads the assets again; the inspector says
      where an asset comes from (the game's .scd, FAF's .nx2 and whether it replaces the game's,
      the workspace's own) and the shelf filters by it; its Missing scope lists what the map uses
      that the workspace and the map's own files lack, each leading to its problem. Missing:
      reading one asset again (the whole catalogue is read).
- [ ] **F-9 Pickers.** One way to choose an asset everywhere: a searchable picker with thumbnails
      and filters, for textures, decals, props and units (see UNT-1). Today: the asset shelf in the
      dock (Ctrl+Shift+A): the kind follows the mode; what the map uses, its biomes or everything;
      biome or faction, category, search; a prop's variants as one tile; pictures of decals and
      textures; the chosen asset in the inspector. The picker (Ctrl+P, Find an asset): over the
      window, for the active mode's kind, with search, biome or faction, category and for units
      tech, thumbnails (props and units their mesh), the arrows, Enter and Escape; what it
      chooses shows in the inspector; a command that picks takes the key from automation.
      Dragging a tile onto the top or 3D view hands the asset and its place to the active tool.
      Missing: slots that open the picker (S-2 and others), placing what is dropped (P-1, U-2,
      D-1), stars and recents.
- [ ] **F-10 Presets.** Save a set of things and place or apply it again: a civilian base (units in
      a group), a brush, a prop palette, a set of markers, a lighting and water setup. Presets are
      files in the workspace, shared like the rest of it. Today: `presets/<kind>/*.json` in the
      workspace, read again when they change; brush presets in the Terrain and Paint panels (save,
      apply, delete). Missing: presets of lighting, water, units, markers and prop palettes, and of
      a selection.
- [ ] **F-11 Selection.** Select anything that lies on the map (marker, prop, decal, unit, area) in
      the top and 3D views, one or many; an inspector shows and edits what is selected; move,
      rotate and scale with handles; copy, paste, duplicate and delete.
      Today: the Select tool of the Markers, Props, Decals and Units modes in the top and 3D views
      (click, shift adds, control toggles, a box on empty ground, drag to move as one step, Q and E
      turn, the arrows nudge, Escape clears; in 3D a click selects what the ray meets, and the
      selection is highlighted); copy, paste (at the mouse), duplicate, delete, turn, select all and
      none as commands; the inspector edits the position and heading of one or many, and each
      kind's own fields; decals and props scale. Missing: handles in the views, moving along one
      axis, turning about the selection's centre.
- [ ] **F-12 Symmetry.** Mirror every edit: horizontally, vertically, diagonally, or around the
      middle for 2 to 8 players; shown as guides in the views. Today: the symmetry is chosen (off,
      rotational for 2 to 8, four mirrors; on and off takes the map's own) and the top view shows
      its guides; the maths mirrors points, yaws and rotations. Missing: mirroring strokes and
      placing, guides in the 3D view.
- [ ] **F-13 Events.** C# and Lua react to what happens (workspace opened, map opened or saved,
      document changed) and may check things before a save or export. Today: C# handlers of
      workspace opened, map opened, saved or exported, document changed, and checks that stop a
      save or export with a reason. Missing: Lua.
- [ ] **F-14 Problems.** One list of what is wrong or doubtful (a missing texture, a spawn without
      mass, a new passage through a cliff, a check of a plugin), each leading to the place. Today:
      the Problems tab, checked a moment after each step and before a save or export (never
      stopping it), with checks for armies without a spawn, spawns without mass nearby, textures
      and blueprints or models the map and workspace lack, and passages opened or closed since
      walkability was generated last; a double click or Enter goes to a problem (selects it, the
      top view and 3D camera look at it). Missing: Lua plugins' checks (F-15).
- [ ] **F-15 Lua plugins.** Plugins with commands, tools and panels, through the controller and
      the events; a run is one step of undo.
- [x] **F-16 Driven by agents and tests.** Automation over HTTP for every operation, labels for
      screen readers, headless tests of the window, performance spans (see the App's guide).
- [x] **F-17 Diagnostics.** See what goes well and wrong, as a service's logs and metrics: the
      Console with levels; every session's logs (warnings, errors, crashes) and measurements
      (commands, operations and their durations, frames, stalls, memory) on disk in
      OpenTelemetry's format, recording on or off; sent to the developers' collector only when the
      user turns it on; a problem report that bundles them (Help, Report a problem: the recent
      sessions, the editor and system, and the map's project when chosen, in a zip to attach;
      nothing is sent).
- [x] **F-18 Version control.** One git repository per project, turned on from a page that shows
      the choices first: the author, Git LFS for the images and the procedural sources (on), a
      repository to share through; a `.gitignore` that leaves `procedural/temp` (what World Machine
      and Gaea build) out and a `.gitattributes`. Then a commit beside save (File, Commit), which
      saves first. Push and pull through the shared repository (only a fast-forward, never over
      uncommitted work; git's credential helper signs in), the history of commits with each one's
      files, and going back to a commit as one step of undo (the files and history stay).

## M: The map

- [x] **M-1 Map information.** Name, description, author, version, type (skirmish, campaign),
      preview image, no-rush radius, adaptive map, reclaim: the Map panel's fields
      (`map.information`), one step of undo per field; the inspector shows the size, armies and
      the `.scmap`'s preview.
- [ ] **M-2 New map.** Size, starting height, water, biome or template; from scratch. Today:
      File, New map (`file.new`): one of the game's sizes, the height, water and its elevation,
      the strata of one of six biomes, spawns and the playable area. Missing: templates.
- [ ] **M-3 Size and playable area.** Grow or crop the map; set the playable area.
- [x] **M-4 Armies.** Add, remove and rename armies; playable or extra (civilians); factions an army
      is limited to; alliances; teams for the lobby. The Map mode's Armies and teams: a renamed army
      renames its spawn, a removed one takes its units and spawn along, a playable one gets a spawn.
      Teams are kept in the project and written after the team FFA, the only one the game's lobby
      reads; the lobby does not use them yet.
- [x] **M-5 Areas.** Draw, name, move and resize areas (rectangles) in the views: the areas tool
      (handles in the top view), the Map panel's list, the inspector's fields; the first is the
      playable area. Missing: handles in the 3D view.
- [x] **M-6 Script.** Open the map's script in the user's editor; see the files of the map folder.
- [x] **M-7 Import and export.** Import a map folder of the game or the vault; export into the
      vault's `maps` folder.
- [ ] **M-8 Versions and upload.** Raise the version on export; render the preview; upload to the
      FAF vault, refusing a map that is not publishable (`MapDocument.IsPublishable`: the sandbox).
      Today: File, Render the preview (S-4). Missing: raising the version, the upload.

## T: Terrain

- [x] **T-1 Sculpt.** Raise, lower, smooth, flatten, set to a height, add noise (a new seed per
      stroke), erode (thermal) and scale around a base with a brush, round (falloff smooth, linear
      or sharp) or the shape of one of the workspace's brushes (T-2), turned by hand or at random per
      dab; limits on the lowest and highest height it makes. Missing: hydraulic erosion.
- [ ] **T-2 Brush presets.** The user's own brushes and stamps, in the workspace. Today: the
      brush shapes, greyscale PNGs of the workspace's `brushes` folder, in the asset shelf of the
      Terrain mode, read again when they change; they are the shapes of the sculpt and strata
      brushes (T-1, S-1); presets of the brush tools' settings (F-10) in the Terrain and Paint
      panels. Missing: stamps.
- [x] **T-3 Heightmap from other tools.** `heightmap.png` or `.r16` in the project is read again
      when World Machine or Gaea writes it; one of any size is imported, resampled
      (`terrain.importHeightmap`), and the heightmap exported as PNG or `.r16` (`terrain.exportHeightmap`).
- [ ] **T-4 Curves.** Draw a curve (spline) and shape the terrain along it: a ridge, a valley, a
      road, a cliff edge, with a profile, width and falloff; edit the curve afterwards and the
      terrain follows (it stays a layer, see T-5).
- [ ] **T-5 Heightmap layers.** A stack of layers that add up to the heightmap: the base, sculpting,
      curves, imported results of World Machine or Gaea; per layer a way of combining (add, maximum,
      minimum, replace), strength, a mask, visible or hidden, locked. Shape the general form in the
      editor, bake it with the external tool, take the result back as a layer.
- [x] **T-6 Terrain types.** Paint the terrain type of each ogrid (what the game uses for footsteps
      and effects), from the list of types with their colours. Today: the Terrain types tool sets
      the chosen type where the brush works at least half; the top view shows every ogrid's type in
      its colour while the tool is active, the tool options the legend of the types the map uses.
- [ ] **T-7 Walkability.** A layer of where land units can and cannot go, generated from the
      heightmap layers (slope); mark where it should be blocked or open; after generating again,
      see what changed: a new passage through a cliff, a closed path (see F-14). Today: generated
      when asked (the game's navigational mesh, per layer: land, hover, amphibious, naval), drawn in
      the top view with the ogrids blocked and opened since the time before, the counts in the
      status line, new passages and closed paths as problems. Missing: marking by hand where it
      must be blocked or open; drawing it in the 3D view.

## S: Strata (terrain textures)

- [x] **S-1 Paint strata.** Paint and erase a layer's mask with a brush, round or the shape of one
      of the workspace's brushes (T-2), turned; only where the slope (degrees) and height are within
      limits, each pair on or off. Missing: painting with symmetry.
- [x] **S-2 Layers.** Choose each layer's albedo and normal texture (picker, F-9) and their scales;
      add, remove and reorder layers; the lower and upper layer. The Paint panel's layer textures
      and the inspector; the strata the version holds (4 or 8), masks moving with them.
- [x] **S-3 Masks from other tools.** The masks are PNGs in the project, read again when they change.
- [ ] **S-4 Shader and derived images.** The terrain shader; generate the normal map and the preview.
      Today: the normal map (as the game reads it) and the 256 by 256 preview (the top view's
      picture) generated from the terrain when asked, each one step of undo, out of date when the
      heightmap changes. Missing: the shader; a preview with the strata's textures.

## K: Masks for tools

- [x] **K-1 Mask layers.** Named masks for one's own purposes (where rivers go, where forests grow,
      what to erode), painted like strata, saved as PNG for World Machine, Gaea and the editor's
      own rules. Today: `masks/<name>.png` in the project (the map's size or half, read again when
      another tool changes them, never exported); the Paint panel's "Masks for tools" adds, renames,
      deletes and chooses them; the Masks tool paints the chosen one; the overlay Mask shows it.
- [ ] **K-2 Masks from rules.** Generate a mask from slope, height, distance to water or other masks,
      when asked.

## W: Water

- [x] **W-1 Water.** On or off; surface, deep and abyss elevations; colours, sun, reflection and
      refraction, textures; wave generators. The Water panel and inspector (`water.set`, the wave
      textures and generators); water presets. Missing: placing generators with the mouse.
- [x] **W-2 Paintable water.** Paint the water's foam, flatness and depth bias. Today: the Paint
      water tool, the chosen mask shown in the top view while it is active.

## L: Lighting and atmosphere

- [ ] **L-1 Lighting.** The sun's direction (with a handle in the 3D view), colours, ambience,
      shadow fill, multiplier, specular, bloom, fog, sky and cube maps, seen at once in 3D. Today:
      the Lighting panel (`lighting.set`) and the sun tool, which turns the sun from either view.
      Missing: a handle drawn in the 3D view; the sky and cube maps.
- [x] **L-2 Lighting presets.** The lighting of a biome as a preset (F-10): the Lighting panel's
      presets, `lighting.preset`.

## R: Markers

- [x] **R-1 Place markers.** Spawns, mass and hydrocarbon deposits, AI markers, path nodes and
      weather by kind; move and delete them; snap to the grid. Missing: weather markers, and
      placing mirrored by the symmetry.
- [x] **R-2 Marker properties.** The selected markers in the inspector.
- [ ] **R-3 Path networks.** Connect path nodes per layer (land, water, amphibious, air), by hand
      or generated from walkability (T-7). Today: by hand, with the path tool.
- [x] **R-4 Chains.** Create a chain and put markers in order.
- [ ] **R-5 Adaptive maps.** Which markers (deposits, civilians) exist for which number of players
      and options. Today: whether the map adapts, and the army each deposit waits for.
- [x] **R-6 Show markers.** Markers and path nodes on or off in the top view and in 3D, per kind.

## P: Props

- [x] **P-1 Place props.** Place, select, move, rotate, scale and delete props (from the picker).
      Today: the Props mode's Select and Place tools (a click places the prop chosen with
      Choose..., Ctrl+P or on the shelf; a prop dropped from the shelf is placed where it lands);
      how placed props stand (by kind: rocks tilt with the ground, trees stand upright; upright;
      with the terrain), random turn, tilt and scale, a sink; on the selection: align to the
      terrain, upright, drop to the ground, pitch, roll and scale in the inspector, turn and scale
      at random, replace the blueprint, a random variant of each family; props follow the terrain
      a height brush moves, in the same step. Missing: handles in the views.
- [x] **P-2 Paint props.** A palette of props with weights; density, random scale and rotation;
      an eraser. Today: the paint tool (radius, density, spacing; the Placing settings), the
      palette in the Props panel (from the picker, or the shelf while painting) saved as presets
      (Palettes), the eraser (all props, or only the palette's).
- [ ] **P-3 Paint where a layer is.** Paint only where a stratum or mask covers the terrain (trees
      only on grass), and not where another one does. Today: only on and not on a stratum, with
      a threshold. Missing: the mask layers of K-1 as choices.
- [ ] **P-4 Props from rules.** Rules that place props from layers: where a stratum or mask allows
      (tree groups), where others forbid, with density, clustering and spacing; generated when
      asked (F-7), into their own layer, so they are generated again after the terrain moves
      without touching props placed by hand.
- [x] **P-5 Reclaim.** The mass and energy of the props, for the map and per area. Today: the
      Props panel's Reclaim (the map, per area), the selected props' in the inspector, from the
      blueprints' values (`props.reclaim`).

## D: Decals

- [ ] **D-1 Place decals.** Place from the picker, move, rotate and scale on the terrain; the
      distances at which they show; their order. Today: the Place tool (the decal from the picker,
      Ctrl+P or the shelf; a click places it, a drag sizes and turns it; a tile dropped on a view);
      the Select tool selects, moves, turns and scales them (a drag mode for both sides, the width
      or the length; plus and minus); the inspector edits width, length and the distances of many
      at once; bring forward, send back, to front, to back; drawn in 3D, the selection highlighted.
      Missing: handles in the views, mirroring with symmetry.
- [x] **D-2 Decal groups.** Group decals, show and hide groups. Today: the Decals panel's groups,
      made from the selection, renamed, filled and emptied, selected, deleted; hidden groups are not
      drawn in 3D or picked (an editor setting, not saved).

## U: Units

- [ ] **U-1 Pick a unit.** A dialog: search by name or id, filter by faction, tech, kind
      (land, air, naval, structure, civilian), with the unit's icon and size. Today: Units,
      Choose a unit opens the picker with those filters and each unit's mesh, tech and id; the
      inspector shows the chosen unit with its footprint. Missing: the game's unit icons (the
      workspace does not convert them).
- [x] **U-2 Place units.** Place units for an army, rotate and move them. The Units mode's Place
      tool: the unit from the picker, the shelf or a dropped tile, the army and group in its
      options; structures snap to the build grid and turn in quarter turns; the selection's army in
      the inspector. Missing: mirroring with symmetry (wave 2).
- [ ] **U-3 Unit groups.** Per army a tree of groups: create, rename, nest, move units between groups.
      Today: the Units mode's tree with the units per group; nesting and moving between groups by command
      (`units.moveGroup`, Move here); missing: dragging in the tree.
- [x] **U-4 Unit presets.** A civilian base, a defence line: a group saved as a preset and placed
      again, for any army (F-10). The Units mode's Presets: the selected units or the chosen group,
      placed with the Place tool at the mouse as a new group, turned in quarter turns.
- [ ] **U-5 Wrecks.** Place units as wrecks.

## V: Views

- [x] **V-1 Top view.** The terrain from above with the brush, markers and the 3D camera.
- [x] **V-2 3D view.** On the graphics card, with strata, decals and water; the FAF editor's camera;
      the props and units with their models, the markers as symbols, the object under the mouse
      found by picking and drawn highlighted.
- [ ] **V-3 Overlays.** Choose what the views show over the terrain: a mask, walkability, grid,
      symmetry guides, areas, markers by kind, props, decals, units. Today: markers, path nodes and
      the 3D camera; props and units on or off in 3D; the chosen mask for tools (K-1) and, while
      their tools are active, a layer's or the water's mask and the terrain types in the top view.
- [ ] **V-4 Layouts.** Top and 3D side by side; panels that can be moved, closed and opened again.
      Today: the views side by side (sharing the camera, each with its own mouse), the mode panel,
      inspector, dock and toolbar hidden and shown, the panels and views resized with splitters,
      all remembered in the user's settings; Reset layout. Missing: moving a panel to another side
      or out of the window.
- [x] **V-5 Ruler.** Measure distances and heights: the ruler tool (Terrain mode), the distance in
      ogrids and km, the rise and slope, drawn in the top view and in the status line.
