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
      run it (`faf-map editor run id=...`); missing: toolbars and Lua.
- [x] **F-2 Command palette.** Find and run any command by typing (`Ctrl+K`), with its shortcut shown.
- [x] **F-3 Undo and redo.** Every change is a step; a brush stroke or a plugin run is one step.
- [x] **F-4 History.** The steps in a panel; choosing one goes back or forward to it.
- [x] **F-5 Workspace.** One folder per computer with the projects and the game's assets converted
      from `env` and `units` (`.scd` with FAF's `.nx2` over them), set up at the first start and
      converted again when FAF or the editor changes.
- [x] **F-6 Assets on disk, read again when they change.** The project's files (JSON, PNG) and the
      workspace's assets: a tool that saves one, the editor shows it.
- [ ] **F-7 Generation only when asked.** A layer or result that is generated (walkability, props
      from rules, a baked heightmap) shows that its inputs changed and offers to generate again; it
      never runs by itself.
- [ ] **F-8 Asset discovery.** See which assets are loaded and where each comes from (the game,
      FAF, the workspace's own, the map's own files), which are in use by the map, which are
      missing; open an asset's file; reload one. Today: the asset shelf (F-9) shows what the map
      uses and how often, shows an asset's file and reads the assets again; missing: where each
      comes from, the missing ones.
- [ ] **F-9 Pickers.** One way to choose an asset everywhere: a searchable picker with thumbnails
      and filters, for textures, decals, props and units (see UNT-1). Today: the asset shelf in the
      dock (Ctrl+Shift+A): the kind follows the mode; what the map uses, its biomes or everything;
      biome or faction, category, search; a prop's variants as one tile; pictures of decals and
      textures; the chosen asset in the inspector. Missing: the picker a slot or tool opens
      (Ctrl+P), pictures of props and units, dragging onto the map.
- [ ] **F-10 Presets.** Save a set of things and place or apply it again: a civilian base (units in
      a group), a brush, a prop palette, a set of markers, a lighting and water setup. Presets are
      files in the workspace, shared like the rest of it.
- [ ] **F-11 Selection.** Select anything that lies on the map (marker, prop, decal, unit, area) in
      the top and 3D views, one or many; an inspector shows and edits what is selected; move,
      rotate and scale with handles; copy, paste, duplicate and delete.
      Today: the Select tool of the Markers and Units modes in the top view (click, shift adds,
      control toggles, a box on empty ground, drag to move as one step, Q and E turn, the arrows
      nudge, Escape clears); copy, paste (at the mouse), duplicate, delete, turn, select all and
      none as commands; the inspector edits the position and heading of one or many; props, units
      and areas drawn in the top view. Missing: picking in the 3D view, handles, scaling, the
      Select tools of the Props and Decals modes.
- [ ] **F-12 Symmetry.** Mirror every edit: horizontally, vertically, diagonally, or around the
      middle for 2 to 8 players; shown as guides in the views.
- [ ] **F-13 Events.** C# and Lua react to what happens (workspace opened, map opened or saved,
      document changed) and may check things before a save or export.
- [ ] **F-14 Problems.** One list of what is wrong or doubtful (a missing texture, a spawn without
      mass, a new passage through a cliff, a check of a plugin), each leading to the place.
- [ ] **F-15 Lua plugins.** Plugins with commands, tools and panels, through the controller and
      the events; a run is one step of undo.
- [x] **F-16 Driven by agents and tests.** Automation over HTTP for every operation, labels for
      screen readers, headless tests of the window, performance spans (see the App's guide).
- [ ] **F-17 Diagnostics.** See what goes well and wrong, as a service's logs and metrics: the
      Console with levels; every session's logs (warnings, errors, crashes) and measurements
      (commands, operations and their durations, frames, stalls, memory) on disk in
      OpenTelemetry's format, recording on or off; sent to the developers' collector only when the
      user turns it on; a problem report that bundles them. Today: all but the report.
- [x] **F-18 Version control.** One git repository per project, turned on from a page that shows
      the choices first: the author, Git LFS for the images and the procedural sources (on), a
      repository to share through; a `.gitignore` that leaves `procedural/temp` (what World Machine
      and Gaea build) out and a `.gitattributes`. Then a commit beside save (File, Commit), which
      saves first. Missing: push and pull, the history of commits, going back to one.

## M: The map

- [ ] **M-1 Map information.** Name, description, author, version, type (skirmish, campaign),
      preview image, no-rush radius, adaptive map, reclaim. Today: kept and exported, not editable
      in the window.
- [ ] **M-2 New map.** Size, starting height, water, biome or template; from scratch.
- [ ] **M-3 Size and playable area.** Grow or crop the map; set the playable area.
- [ ] **M-4 Armies.** Add, remove and rename armies; playable or extra (civilians); factions an army
      is limited to; alliances; teams for the lobby.
- [ ] **M-5 Areas.** Draw, name, move and resize areas (rectangles) in the views.
- [ ] **M-6 Script.** Open the map's script in the user's editor; see the files of the map folder.
- [x] **M-7 Import and export.** Import a map folder of the game or the vault; export into the
      vault's `maps` folder.
- [ ] **M-8 Versions and upload.** Raise the version on export; render the preview; upload to the
      FAF vault.

## T: Terrain

- [x] **T-1 Sculpt.** Raise, lower, smooth and flatten with a brush, round or the shape of one of
      the workspace's brushes (T-2), turned by hand or at random per dab. Missing: set height, add,
      scale, noise, erosion; a choice of falloff; limits on the lowest and highest height.
- [ ] **T-2 Brush presets.** The user's own brushes and stamps, in the workspace. Today: the
      brush shapes, greyscale PNGs of the workspace's `brushes` folder, in the asset shelf of the
      Terrain mode, read again when they change; they are the shapes of the sculpt and strata
      brushes (T-1, S-1). Missing: presets of settings.
- [x] **T-3 Heightmap from other tools.** `heightmap.png` or `.r16` in the project is read again
      when World Machine or Gaea writes it. Missing: import or export with a different size.
- [ ] **T-4 Curves.** Draw a curve (spline) and shape the terrain along it: a ridge, a valley, a
      road, a cliff edge, with a profile, width and falloff; edit the curve afterwards and the
      terrain follows (it stays a layer, see T-5).
- [ ] **T-5 Heightmap layers.** A stack of layers that add up to the heightmap: the base, sculpting,
      curves, imported results of World Machine or Gaea; per layer a way of combining (add, maximum,
      minimum, replace), strength, a mask, visible or hidden, locked. Shape the general form in the
      editor, bake it with the external tool, take the result back as a layer.
- [ ] **T-6 Terrain types.** Paint the terrain type of each ogrid (what the game uses for footsteps
      and effects), from the list of types with their colours.
- [ ] **T-7 Walkability.** A layer of where land units can and cannot go, generated from the
      heightmap layers (slope); mark where it should be blocked or open; after generating again,
      see what changed: a new passage through a cliff, a closed path (see F-14).

## S: Strata (terrain textures)

- [x] **S-1 Paint strata.** Paint and erase a layer's mask with a brush, round or the shape of one
      of the workspace's brushes (T-2), turned. Missing: limits by slope or height, painting with
      symmetry.
- [ ] **S-2 Layers.** Choose each layer's albedo and normal texture (picker, F-9) and their scales;
      add, remove and reorder layers; the lower and upper layer.
- [x] **S-3 Masks from other tools.** The masks are PNGs in the project, read again when they change.
- [ ] **S-4 Shader and derived images.** The terrain shader; generate the normal map and the preview.

## K: Masks for tools

- [ ] **K-1 Mask layers.** Named masks for one's own purposes (where rivers go, where forests grow,
      what to erode), painted like strata, saved as PNG for World Machine, Gaea and the editor's
      own rules.
- [ ] **K-2 Masks from rules.** Generate a mask from slope, height, distance to water or other masks,
      when asked.

## W: Water

- [ ] **W-1 Water.** On or off; surface, deep and abyss elevations; colours, sun, reflection and
      refraction, textures; wave generators. Today: on or off (`water.toggle`).
- [ ] **W-2 Paintable water.** Paint the water's foam, flatness and depth bias.

## L: Lighting and atmosphere

- [ ] **L-1 Lighting.** The sun's direction (with a handle in the 3D view), colours, ambience,
      shadow fill, multiplier, specular, bloom, fog, sky and cube maps, seen at once in 3D.
- [ ] **L-2 Lighting presets.** The lighting of a biome as a preset (F-10).

## R: Markers

- [ ] **R-1 Place markers.** Spawns, mass and hydrocarbon deposits, AI markers, path nodes and
      weather by kind; move and delete them; snap to the grid. Today: shown in the top view.
- [ ] **R-2 Marker properties.** The selected markers in the inspector.
- [ ] **R-3 Path networks.** Connect path nodes per layer (land, water, amphibious, air), by hand
      or generated from walkability (T-7).
- [ ] **R-4 Chains.** Create a chain and put markers in order.
- [ ] **R-5 Adaptive maps.** Which markers (deposits, civilians) exist for which number of players
      and options.
- [x] **R-6 Show markers.** Markers and path nodes on or off in the top view. Missing: per kind,
      and in 3D.

## P: Props

- [ ] **P-1 Place props.** Place, select, move, rotate, scale and delete props (from the picker).
- [ ] **P-2 Paint props.** A palette of props with weights; density, random scale and rotation;
      an eraser.
- [ ] **P-3 Paint where a layer is.** Paint only where a stratum or mask covers the terrain (trees
      only on grass), and not where another one does.
- [ ] **P-4 Props from rules.** Rules that place props from layers: where a stratum or mask allows
      (tree groups), where others forbid, with density, clustering and spacing; generated when
      asked (F-7), into their own layer, so they are generated again after the terrain moves
      without touching props placed by hand.
- [ ] **P-5 Reclaim.** The mass and energy of the props, for the map and per area.

## D: Decals

- [ ] **D-1 Place decals.** Place from the picker, move, rotate and scale on the terrain; the
      distances at which they show; their order. Today: drawn in 3D.
- [ ] **D-2 Decal groups.** Group decals, show and hide groups.

## U: Units

- [ ] **U-1 Pick a unit.** A dialog: search by name or id, filter by faction, tech, kind
      (land, air, naval, structure, civilian), with the unit's icon and size.
- [ ] **U-2 Place units.** Place units for an army, rotate and move them.
- [ ] **U-3 Unit groups.** Per army a tree of groups: create, rename, nest, move units between groups.
- [ ] **U-4 Unit presets.** A civilian base, a defence line: a group saved as a preset and placed
      again, for any army (F-10).
- [ ] **U-5 Wrecks.** Place units as wrecks.

## V: Views

- [x] **V-1 Top view.** The terrain from above with the brush, markers and the 3D camera.
- [x] **V-2 3D view.** On the graphics card, with strata, decals and water; the FAF editor's camera.
- [ ] **V-3 Overlays.** Choose what the views show over the terrain: a mask, walkability, grid,
      symmetry guides, areas, markers by kind, props, decals, units.
- [ ] **V-4 Layouts.** Top and 3D side by side; panels that can be moved, closed and opened again.
- [ ] **V-5 Ruler.** Measure distances and heights.
