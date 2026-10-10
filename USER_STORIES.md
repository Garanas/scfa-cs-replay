# Map editor: user stories

What the FAF map editor (`src/FAForever.MapEditor.*`) is to do, as stories of the map maker. A
checked box is done; a story may be partly done, which its line says. The ids stay as they are
when stories move or change, so commits, issues and the UI design can refer to them.

The stories follow the editor's foundation (F): every operation is a command of the controller,
reachable from the window, automation and later Lua; every asset is a file on disk that the editor
reads again when it changes; and nothing that generates runs by itself.

## F: Foundation

- [x] **F-1 One controller.** Every operation is a command with a name, arguments and a
      description, in one registry (the controller of model, view, controller). Menus, toolbars,
      shortcuts, the command palette, automation, the CLI and Lua plugins all run the same commands.
      Today: every command is a request with its arguments and a handler, sent through MediatR
      by the registry; the menus, shortcuts and palette come from it, and automation and the CLI
      run it (`faf-map editor run id=...`); a toolbar in the menu bar is made from the registry
      too (undo, redo, save, the palette, symmetry by default; its commands kept in the user's
      settings, `view.setToolbar`); Lua plugins run any command (`editor.run`) and add their own
      (F-15).
- [x] **F-2 Command palette.** Find and run any command by typing (`Ctrl+K`), with its shortcut shown.
- [x] **F-3 Undo and redo.** Every change is a step; a brush stroke or a plugin run is one step.
      Undo and redo (`Ctrl+Z`; `Ctrl+Y` or `Ctrl+Shift+Z`) bring back the selection the step had
      (undoing a delete selects what came back) and say in the status line what they undid.
- [x] **F-4 History.** The steps in a panel; choosing one goes back or forward to it.
- [x] **F-5 Workspace.** One folder per computer with the projects and the game's assets converted
      from `env` and `units` (`.scd` with FAF's `.nx2` over them), set up at the first start and
      converted again when FAF or the editor changes. Every workspace has a sandbox
      (`projects/Sandbox`: a small map with one of each kind of thing, made of its own assets),
      written again when missing and opened when the editor starts without a folder; it is not
      publishable (exported to play with a warning, never uploaded). File, Workspace settings
      changes where the game, FAF's data and the vault are and shows the converted assets (format,
      date, current or not, Convert again); Help, Folders opens each of the workspace's folders.
      Help, Keyboard shortcuts (Shift+F1) lists every command's shortcut, the tools' keys and the
      mouse in each view, with a search.
- [x] **F-6 Assets on disk, read again when they change.** The project's files (JSON, PNG) and the
      workspace's assets: a tool that saves one, the editor shows it.
- [x] **F-7 Generation only when asked.** A layer or result that is generated (walkability, props
      from rules, a baked heightmap) shows that its inputs changed and offers to generate again; it
      never runs by itself. Today: the generators' registry with the inputs each result was made
      from; the status bar names the results out of date, and Generate, Everything out of date makes
      them again in the order one feeds the next (masks from rules before the props that read
      them, walkability before path networks), so one run leaves nothing out of date; walkability,
      the normal map and the preview are generated this way (T-7, S-4), and so are masks and props
      from rules (K-2, P-4). The Procedural mode lists every generated result as an entry with how
      it stands (missing, out of date, current), a picture and Generate; choosing one shows its
      settings, what it is made from and when it was made in the inspector.
- [x] **F-8 Asset discovery.** See which assets are loaded and where each comes from (the game,
      FAF, the workspace's own, the map's own files), which are in use by the map, which are
      missing; open an asset's file; reload one. Today: the asset shelf (F-9) shows what the map
      uses and how often, shows an asset's file and reads the assets again; the inspector says
      where an asset comes from (the game's .scd, FAF's .nx2 and whether it replaces the game's,
      the workspace's own) and the shelf filters by it; its Missing scope lists what the map uses
      that the workspace and the map's own files lack (its cube maps too), each leading to its
      problem; the inspector's Read again (`assets.reload {key}`) reads one asset again, the rest
      of the catalogue kept. Unit and prop tiles show their mass and energy (build cost, reclaim)
      in the shelf and the picker, which sort by them.
- [x] **F-9 Pickers.** One way to choose an asset everywhere: a searchable picker with thumbnails
      and filters, for textures, decals, props and units (see UNT-1). Today: the asset shelf in the
      dock (Ctrl+Shift+A): the kind follows the mode; what the map uses, its biomes or everything;
      biome or faction, category, search; a prop's variants as one tile; pictures of decals and
      textures; the chosen asset in the inspector. The picker (Ctrl+P, Find an asset): over the
      window, for the active mode's kind, with search, biome or faction, category and for units
      tech, thumbnails (props and units their mesh), the arrows, Enter and Escape; what it
      chooses shows in the inspector; a command that picks takes the key from automation.
      Dragging a tile onto the top or 3D view hands the asset and its place to the active tool,
      which places it (P-1, U-2, D-1); the panels' texture fields open the picker (S-2). Stars and
      recents: an asset starred in the inspector, the picker or with `assets.star` shows a star on
      its tile and under Starred, and the assets chosen last (a tile clicked, a picker's choice)
      under Recent, the newest first, on the shelf and in the picker; kept per workspace in its
      `favourites.json`.
- [x] **F-10 Presets.** Save a set of things and place or apply it again: a civilian base (units in
      a group), a brush, a prop palette, a set of markers, a lighting and water setup. Presets are
      files in the workspace, shared like the rest of it. Today: `presets/<kind>/*.json` in the
      workspace, read again when they change; brush presets in the Terrain and Paint panels (save,
      apply, delete); lighting and water presets (L-2, W-1); units presets of the selected units or
      a group (U-4); prop palettes (P-2); markers presets: the Markers panel's Marker presets (the
      selected markers, with their links and chains), placed with the Preset tool
      (`markers.placePreset`); selection presets: the selected things of several kinds at once (a
      civilian base's units, props, decals and markers; Edit, Save the selection as a preset, or the
      Map panel's Selection presets), placed with the Map mode's Preset tool (`edit.placePreset`),
      turned in quarter turns, the units in a new group of an army, with the symmetry's images.
      One `presets/` folder of plural kinds (brushes, stamps, lighting, water, sky, props, units,
      markers, selections, maps: the map templates), the old folders moved once; stamp and sky
      presets; each preset section shows its folder.
- [x] **F-11 Selection.** Select anything that lies on the map (marker, prop, decal, unit, area) in
      the top and 3D views, one or many; an inspector shows and edits what is selected; move,
      rotate and scale with handles; copy, paste, duplicate and delete.
      Today: the Select tool of the Markers, Props, Decals and Units modes in the top and 3D views
      (click, shift adds, control toggles, a box on empty ground, drag to move as one step, Q and E
      turn, the arrows nudge, Escape clears; in 3D a click selects what the ray meets, and the
      selection is highlighted); copy, paste (at the mouse), duplicate, delete, turn, select all and
      none as commands; the inspector edits the position and heading of one or many, and each
      kind's own fields; decals and props scale; handles in both views (move, along x or z, turn
      about the selection's centre or each thing's own, scale; shift snaps, X or Z holds an axis),
      F focuses the 3D camera on the selection, the status bar shows its count and size, the thing
      under the mouse in 3D lights up; a box in the 3D view is a rectangle on screen that selects
      what shows inside it, leaving out what a hill hides from the camera. Select similar (blueprint or family), invert, select inside an area,
      the selection filter in the select tools' options (lock a kind), hide a kind (decals too),
      isolate the selection, R and a drag to turn, a Select anything tool in the Map mode; a box
      and Select all leave areas alone outside the Map mode.
- [x] **F-12 Symmetry.** Mirror every edit: horizontally, vertically, diagonally, or around the
      middle for 2 to 8 players; shown as guides in the views. Today: the symmetry is chosen (off,
      rotational for 2 to 8, four mirrors; on and off takes the map's own) and the top view shows
      its guides; the maths mirrors points, yaws and rotations. Every brush stroke paints in every
      part (shapes turned and flipped, props painted as mirrored props); placing markers, props,
      decals and units places their images; moving, turning and deleting take the mirror partners
      along, and so do scaling (the handles, Scale the selection), turning about the selection's
      centre and the decals' sizes; pasted and duplicated things get their images; the brush under
      the mouse shows every image in both views; the guides show in 3D; "Mirror edits" off keeps
      the guides only; Select the mirror partners, Mirror the selection, and the check "not
      symmetric". A selection holding things and their partners stays symmetric when moved,
      turned, scaled or deleted; mirrored units go to the partner spawn's army; curves drawn with
      the symmetry shape their images; each map keeps its symmetry in its project, and a new map
      comes exactly symmetric with rotational for its players.
- [x] **F-13 Events.** C# and Lua react to what happens (workspace opened, map opened or saved,
      document changed) and may check things before a save or export. Today: C# handlers of
      workspace opened, map opened, saved or exported, document changed, and checks that stop a
      save or export with a reason; Lua plugins' handlers (`editor.on`) of map opened, saved or
      exported and document changed, and before a save or export, which may stop it (F-15).
- [x] **F-14 Problems.** One list of what is wrong or doubtful (a missing texture, a spawn without
      mass, a new passage through a cliff, a check of a plugin), each leading to the place. Today:
      the Problems tab, checked a moment after each step and before a save or export (never
      stopping it), with checks for armies without a spawn, spawns without mass nearby, textures
      and blueprints or models the map and workspace lack, and passages opened or closed since
      walkability was generated last; a double click or Enter goes to a problem (selects it, the
      top view and 3D camera look at it); Lua plugins add checks of their own (F-15).
- [x] **F-15 Lua plugins.** Plugins with commands, tools and panels, through the controller and
      the events; a run is one step of undo. Today: the workspace's `plugins/` (a folder each with
      its `plugin.lua` and `plugin.json`), loaded when the workspace is ready and read again when a
      file changes; the Plugins page (Plugins, Plugins...) with each plugin on or off (remembered),
      its errors, its commands and its panel's fields; plugins register commands (the Plugins menu,
      the palette, automation; a run is one step of undo, a failing run changes nothing), tools of
      a mode (the left mouse in the views calls their press, drag and release with the place; a
      stroke is one step of undo shown while the mouse is down, a failing one changes nothing; their
      options row shows the plugin's fields), problem checks, a panel of fields and handlers of
      events; Lua reads the map as plain tables, edits it (props, moving, turning, removing,
      heights, masks), runs any command and logs, without files or the system beyond its own folder
      and with a time limit per call; three examples (reclaim per army, scatter rocks on steep
      slopes, a plateau brush) written into every workspace.
- [x] **F-16 Driven by agents and tests.** Automation over HTTP for every operation, labels for
      screen readers, headless tests of the window, performance spans (see the App's guide). The
      editor's questions (unsaved changes, confirmations) show in its window, so automation reads
      and answers them (`state.dialog`, `dialog {button}`); only the system's file and folder
      pickers are windows of their own.
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
- [x] **M-2 New map.** Size, starting height, water, biome or template; from scratch. Today:
      File, New map (`file.new`): one of the game's sizes, the height, water and its elevation,
      the strata of one of six biomes, spawns and the playable area; templates in the workspace's
      `templates` folder, offered first on the page: a small JSON of settings (size, height, water,
      strata, players, extra armies, lighting) fills the fields, a map project in a folder is copied
      under the new name, the sandbox is the editor's own; File, Save as a template
      (`file.saveAsTemplate {name?, whole?}`) makes either from the open map.
- [x] **M-3 Size and playable area.** Grow or crop the map; set the playable area. The Map mode's
      Map size tool (`map.resize`): another of the game's sizes per side, an anchor or an offset,
      the edges' heights or a height for what the map gains, every grid and thing along, what lies
      outside removed (spawns kept at the edge), one step of undo. The playable area is the area
      the scenario names (the inspector's check box, `map.setPlayableArea`), else the first; the
      export writes it into the script's `SetPlayableArea`.
- [x] **M-4 Armies.** Add, remove and rename armies; playable or extra (civilians); factions an army
      is limited to; alliances; teams for the lobby. The Map mode's Armies and teams: a renamed army
      renames its spawn, a removed one takes its units and spawn along, a playable one gets a spawn.
      Teams are kept in the project and written after the team FFA, the only one the game's lobby
      reads; the lobby does not use them yet.
- [x] **M-5 Areas.** Draw, name, move and resize areas (rectangles) in the views: the areas tool
      (handles in the top and 3D views), the Map panel's list, the inspector's fields; one is the
      playable area (M-3).
- [x] **M-6 Script.** Open the map's script in the user's editor; see the files of the map folder.
- [x] **M-7 Import and export.** Import a map folder of the game or the vault; export into the
      vault's `maps` folder.
- [x] **M-8 Versions and upload.** Raise the version on export; render the preview; upload to the
      FAF vault, refusing a map that is not publishable (`MapDocument.IsPublishable`: the sandbox).
      File, Render the preview (S-4); `file.export {raise}` raises the version (folder and
      `map_version`) and renders the preview first (M-9); File, Upload to the vault
      (`file.upload`): the map's errors and the vault's own rules checked first, the next version
      exported and zipped, the person's yes to name, version and size, a sign-in in the browser
      (kept in memory only), the upload with progress, then the map is that version. Without a
      person only to a test server, with `confirm`.
- [x] **M-9 Export as steps, and the editor named.** The export is a list of steps shown while it
      runs: generate what is out of date and needed, render the preview, then write the map folder;
      the editor that made the map is named in `_scenario.lua`. Today: the Procedural mode's Export
      section and the status line show each step (the normal map when out of date unless the map
      keeps its own, the preview always, the walkability marks into the terrain types, the writing);
      `_scenario.lua` starts with `-- Made with the FAF Map Editor <version> (<commit>) on <date>`
      and holds `map_editor = { name, version, commit, project_format, exported }`, which the game,
      the FAF client and the vault's upload check ignore. Missing: map-wide decals and path nodes as
      steps (wave 9).

## T: Terrain

- [x] **T-1 Sculpt.** Raise, lower, smooth, flatten, set to a height, add noise (a new seed per
      stroke), erode (thermal) and scale around a base with a brush, round (falloff smooth, linear
      or sharp) or the shape of one of the workspace's brushes (T-2), turned by hand or at random per
      dab; limits on the lowest and highest height it makes. Every brush's radius by `[` and `]`
      or control and the wheel over a view, its strength with shift. Missing: hydraulic erosion.
- [x] **T-2 Brush presets.** The user's own brushes and stamps, in the workspace. Today: the
      brush shapes, greyscale PNGs of the workspace's `brushes` folder, in the asset shelf of the
      Terrain mode, read again when they change; they are the shapes of the sculpt and strata
      brushes (T-1, S-1); presets of the brush tools' settings (F-10) in the Terrain and Paint
      panels. Stamps: the Terrain mode's Stamp tool presses a picture of the brushes folder at a
      click (one step, with the symmetry), turned (Q and E) and sized (`[` and `]`), raising,
      lowering or setting the heights to a base plus the picture, its edge fading; shift and a drag
      take a part of the map as a 16-bit stamp in `brushes/stamps` (`terrain.captureStamp`), set up
      to put it back as it was; Save the brush as a preset (`terrain.saveBrush`) keeps a stamp too.
- [x] **T-3 Heightmap from other tools.** `heightmap.png` or `.r16` in the project is read again
      when World Machine or Gaea writes it; one of any size is imported, resampled
      (`terrain.importHeightmap`), and the heightmap exported as PNG or `.r16` (`terrain.exportHeightmap`).
- [x] **T-4 Curves.** Draw a curve (spline) and shape the terrain along it: a ridge, a valley, a
      road, a cliff edge, with a profile, width and falloff; edit the curve afterwards and the
      terrain follows (it stays a layer, see T-5). Today: the curve tool draws a Catmull-Rom curve
      layer point by point in the top view, moves, inserts and removes points; the inspector sets
      its profile, width, falloff, height and whether it is closed; the 3D view draws the curve and
      its points on the terrain, where the tool works as in the top view. Missing: a height per
      point by hand in the views.
- [x] **T-5 Heightmap layers.** A stack of layers that add up to the heightmap: the base, sculpting,
      curves, imported results of World Machine or Gaea; per layer a way of combining (add, maximum,
      minimum, replace), strength, a mask, visible or hidden, locked. Shape the general form in the
      editor, bake it with the external tool, take the result back as a layer. Today: the Terrain
      panel's stack (the sculpt tool writes into the chosen layer, the heightmap follows tile by
      tile), the inspector's settings, imported layers read again when their file changes, bake;
      the heightmap's stack is the first of the layer stacks (also subtract and multiply, a preview
      per layer, larger in its tool tip), kept in the project's `stacks/heightmap.json` (format 6).
      Missing: exporting a layer for the external tool, merging two layers.
- [x] **T-6 Terrain types.** Paint the terrain type of each ogrid (what the game uses for footsteps
      and effects), from the list of types with their colours. Today: the Terrain types tool sets
      the chosen type where the brush works at least half; the top view shows every ogrid's type in
      its colour while the tool is active, the tool options the legend of the types the map uses.
- [x] **T-7 Walkability.** A layer of where land units can and cannot go, generated from the
      heightmap layers (slope); mark where it should be blocked or open; after generating again,
      see what changed: a new passage through a cliff, a closed path (see F-14). Today: generated
      when asked (the game's navigational mesh, per layer: land, hover, amphibious, naval), drawn in
      the top view and on the terrain in 3D with the ogrids blocked and opened since the time
      before, the counts in the status line, new passages and closed paths as problems; the
      Walkability tool marks by hand where land units must be blocked or may pass (a brush, blocked
      red and open green in the views), which walkability then takes over what it generates; kept
      as `walkability-marks.png`; the export writes blocked ogrids as a blocking terrain type, so
      the game agrees, and a check warns of ground marked open that the game still blocks.
- [x] **T-8 Navigation mesh.** See where each kind of unit can move: the regions of the game's
      navigational mesh per layer (land, hover, amphibious, naval), how large each is, which starts
      and deposits lie in it, and the routes between starts. Today: the Procedural mode's
      Navigation mesh (Generate, the layer, the routes between the nearest starts or every pair,
      kept with the map), the regions coloured in both views with the routes as lines, a list of the
      largest regions (a click or the Regions tool chooses one), the starts that lie in no region,
      and the routes with how much longer they are than the straight line. Walkability can
      regenerate on its own a second after a change to what it is made from (T-7).
- [x] **T-9 Draft and final heightmap.** Block out the rough shape in a draft, send it to World
      Machine or Gaea, and build the final on their output with layers of one's own, some of them
      shared with the draft so the procedural tool can never erode them away; the game gets the
      final. Today: two stacks of layers (a layer is in the draft, the final or both, with its own
      place and on/off in each), a switch (Ctrl+Shift+D) that shows either in both views while
      brushes write it, the list filtered by use and source, previews of both, the draft and ticked
      masks written as 16-bit PNG and RAW into `procedural/temp/` (again whenever they change, when
      chosen), and an output chosen as the final's base, read again whenever the tool writes it.

## S: Strata (terrain textures)

- [x] **S-1 Paint strata.** Paint and erase a layer's mask with a brush, round or the shape of one
      of the workspace's brushes (T-2), turned; only where the slope (degrees) and height are within
      limits, each pair on or off; with symmetry (F-12) in every part of the map.
- [x] **S-2 Layers.** Choose each layer's albedo and normal texture (picker, F-9) and their scales;
      add, remove and reorder layers; the lower and upper layer. The Paint panel's one list of the
      layers (a stratum chosen there is the one the strata tool paints) and the inspector; the
      strata the version holds (4 or 8), masks moving with them.
- [x] **S-3 Masks from other tools.** The masks are PNGs in the project, read again when they change.
- [x] **S-4 Shader and derived images.** The terrain shader; generate the normal map and the preview.
      Today: the Paint panel's Shader (`paint.shader`): the game's and FAF's terrain shaders, with
      what each reads; the 3D view and the preview blend the layers as it does. The normal map (as
      the game reads it) and the 256 by 256 preview the `.scmap` ships with, drawn with the strata's
      textures, lit by the sun, the water as the game colours it, generated when asked, each one
      step of undo, out of date when what they are made of changes.

## K: Masks for tools

- [x] **K-1 Mask layers.** Named masks for one's own purposes (where rivers go, where forests grow,
      what to erode), painted like strata, saved as PNG for World Machine, Gaea and the editor's
      own rules. Today: the Masks mode (Shift+F3) lists them with a preview each and adds, renames,
      duplicates and deletes them; the inspector shows the chosen mask as a stack of layers
      (painted, image, another mask, a stratum, rules; `layers.*`); the mask brush paints its chosen
      painted layer; the overlay Mask shows it. `masks/<name>.png` is the composite (never exported),
      `stacks/mask-<name>.json` the stack. Stratum masks (the Paint inspector) and the water's three
      maps (the Water inspector) are stacks the same way, exported as what they make.
- [x] **K-2 Masks from rules.** Generate a mask from slope, height, distance to water or other masks,
      when asked. Today: a rules layer of a mask's or stratum's stack shows its rules under the stack:
      slope, height and distance to water (ranges with soft edges), other masks or strata, noise,
      combined in order (multiply, min, max, add, subtract) and changed by blur, grow, shrink,
      threshold and invert; a preview in the top view; `generate.masks` makes them (one step of
      undo), out of date when the terrain, water level, rules or masks they read change. Kept in
      the stack's JSON; an older project's `masks/<name>.rules.json` becomes a rules layer.

## W: Water

- [x] **W-1 Water.** On or off; surface, deep and abyss elevations; colours, sun, reflection and
      refraction, textures; wave generators. The Water panel and inspector (`water.set`, the wave
      textures and generators); water presets; the Waves tool: a click places a generator, a drag
      aims its waves, with the symmetry, its square dragged moves it (`water.placeWaveGenerator`).
      The panel lists the settings groups, the maps and the wave generators; the inspector edits
      the chosen one.
- [x] **W-2 Paintable water.** Paint the water's foam, flatness and depth bias. Today: the Paint
      water tool, the chosen mask shown in the top view while it is active; a map chosen in the
      Water panel is the one it paints.

## L: Lighting and atmosphere

- [x] **L-1 Lighting.** The sun's direction (with a handle in the 3D view), colours, ambience,
      shadow fill, multiplier, specular, bloom, fog, sky and cube maps, seen at once in 3D. Today:
      the Lighting panel (`lighting.set`) and the sun tool, which turns the sun from either view,
      with an arrow in the 3D view whose head is dragged across the sky; the Lighting inspector's
      Sky: the sky's cube map, the one units reflect and the background, cube maps chosen in the
      picker from the workspace's (`lighting.chooseCubemap`, the water's too); the 3D view draws the
      sky's cube map around the map and the water reflects its own. The panel lists the sun,
      colours and ambience, fog, bloom, sky and cube maps and the shadow decal (to come); the
      inspector edits the chosen one; sky presets.
- [x] **L-2 Lighting presets.** The lighting of a biome as a preset (F-10): the Lighting panel's
      presets, `lighting.preset`.

## R: Markers

- [x] **R-1 Place markers.** Spawns, mass and hydrocarbon deposits, AI markers, path nodes and
      weather by kind; move and delete them; snap to the grid. Weather: the Weather and Clouds tools
      (the game's weather definition and generator) and their fields in the inspector
      (`markers.setWeather`); with symmetry (F-12) a marker's images are placed with it.
- [x] **R-2 Marker properties.** The selected markers in the inspector.
- [x] **R-3 Path networks.** Connect path nodes per layer (land, water, amphibious, air), by hand
      or generated from the navigation mesh (T-8). By hand with the Markers mode's path tool (markers
      in the project); generated in the Procedural mode (`markers.connectPaths`: the skeleton of each
      region, nodes at a spacing, smoothed, linked where units walk straight; air a grid of 16 by 16),
      previewed in the views and written as markers only by the export; out of date when
      walkability's inputs or the settings change (F-7). Edits by hand on top of the generated
      network with the Path nodes tool (add, move, remove, link, cut; `generate.addPathNode`, ...),
      kept with the map and applied again after every generation.
- [x] **R-4 Chains.** Create a chain and put markers in order.
- [x] **R-5 Adaptive maps.** Which markers (deposits, civilians) exist for which number of players
      and options. Today: whether the map adapts, the army each deposit waits for, and the groups of
      civilians and wrecks FAF's adaptive script creates by lobby option (`Optional_<option>_<n>`,
      `markers.addOptionalGroup`); the export writes the convention's `_options.lua` (an option per
      group's option, and `dynamic_spawn`), `_tables.lua` (the deposits per army) and a script that
      creates the groups by option and leaves out the deposits of armies no one plays, for a map
      whose script is none, the new map's or the editor's own (a map maker's own script is kept);
      civilians by the number of players: groups `Optional_Players_<n>`, created by that script
      when n or more players are in the game.
- [x] **R-6 Show markers.** Markers and path nodes on or off in the top view and in 3D, per kind.

## P: Props

- [x] **P-1 Place props.** Place, select, move, rotate, scale and delete props (from the picker).
      Today: the Props mode's Select and Place tools (a click places the prop chosen with
      Choose..., Ctrl+P or on the shelf; a prop dropped from the shelf is placed where it lands);
      how placed props stand (by kind: rocks tilt with the ground, trees stand upright; upright;
      with the terrain), random turn, tilt and scale, a sink; on the selection: align to the
      terrain, upright, drop to the ground, pitch, roll and scale in the inspector, turn and scale
      at random, replace the blueprint, a random variant of each family; props follow the terrain
      a height brush moves, in the same step; the selection's handles in both views (F-11).
- [x] **P-2 Paint props.** A palette of props with weights; density, random scale and rotation;
      an eraser. Today: the paint tool (radius, density, spacing; the Placing settings), the
      palette in the Props panel (from the picker, or the shelf while painting) saved as presets
      (Palettes), cleared at once (`props.clearPalette`), the eraser (all props, or only the palette's).
- [x] **P-3 Paint where a layer is.** Paint only where a stratum or mask covers the terrain (trees
      only on grass), and not where another one does: the paint tool's Only on and Not on offer
      the strata and the masks for tools of K-1 (`Mask: forest`), with a threshold.
- [x] **P-4 Props from rules.** Rules that place props from layers: where a stratum or mask allows
      (tree groups), where others forbid, with density, clustering and spacing; generated when
      asked (F-7), into their own layer, so they are generated again after the terrain moves
      without touching props placed by hand. Today: the Props panel's Rules and the inspector's
      Rule (palette, layers that allow or forbid with thresholds, density, spacing, clustering by
      noise or copses, slope, water, how they stand); `generate.props` replaces the props the rules
      placed (each carries its rule's name) and keeps the others; `props.keepFromRules` makes
      generated props hand-placed. Kept as `prop-rules.json`; exported as ordinary props.
- [x] **P-5 Reclaim.** The mass and energy of the props, for the map and per area. Today: the
      Props panel's Reclaim (the map, per area), the selected props' in the inspector, from the
      blueprints' values (`props.reclaim`).

## D: Decals

- [x] **D-1 Place decals.** Place from the picker, move, rotate and scale on the terrain; the
      distances at which they show; their order. Today: the Place tool (the decal from the picker,
      Ctrl+P or the shelf; a click places it, a drag spans it from the press to the mouse and turns it; a tile dropped on a view);
      the Select tool selects, moves, turns and scales them (a drag mode for both sides, the width
      or the length; plus and minus); the inspector edits width, length and the distances of many
      at once; bring forward, send back, to front, to back; drawn in 3D, the selection highlighted;
      the selection's handles in both views (F-11); with symmetry, mirrored when placed, moved,
      turned and scaled (F-12). Decal sets (textures named alike with `_albedo`, `_normals`,
      `_spec`, `_mask`, `_mask_<name>`): one tile on the shelf (Variants), placed, selected, moved
      and undone as one decal, written as one decal per texture (`decals.place key=<set name>`);
      the map's own files make sets too. Map-wide decals: the Decals panel's Albedo, Normal decal
      and Shadow decal (none, generated from the final heightmap, or a file; Generate now previews
      them in the views), also in the Lighting panel and the Procedural mode, written at export as
      Kaali's (`decals.setWide`, `decals.setShadow`, `decals.chooseWideFile`, `decals.generateNormal`,
      `decals.generateShadow`). Decal-masks layers draw the sets' masks into height, stratum, mask
      and water stacks and follow the decals (`terrain.addLayer kind=decals`, `layers.add source=decals`).
- [x] **D-2 Decal groups.** Group decals, show and hide groups. Today: the Decals panel's groups,
      made from the selection, renamed, filled and emptied, selected, deleted; hidden groups are not
      drawn in 3D or picked (an editor setting, not saved).

## U: Units

- [x] **U-1 Pick a unit.** A dialog: search by name or id, filter by faction, tech, kind
      (land, air, naval, structure, civilian), with the unit's icon and size. Today: Units,
      Choose a unit opens the picker with those filters and each unit's mesh, tech and id, and the
      game's icon over the picture of its layer (the workspace converts them); the inspector shows
      the chosen unit with its footprint and icon.
- [x] **U-2 Place units.** Place units for an army, rotate and move them. The Units mode's Place
      tool: the unit from the picker, the shelf or a dropped tile, the army and group in its
      options; structures snap to the build grid and turn in quarter turns; the selection's army in
      the inspector; with symmetry, a placed unit's images are placed too (F-12).
- [x] **U-3 Unit groups.** Per army a tree of groups: create, rename, nest, move units between groups.
      Today: the Units mode's tree with the units per group (each unit under its group; choosing one
      selects it); nesting and moving between groups by command (`units.moveGroup`, Move here) and by
      dragging units (a selected one takes the selection along) and groups onto a group or army in
      the tree, which lights up, one step of undo (`units.drop`).
- [x] **U-4 Unit presets.** A civilian base, a defence line: a group saved as a preset and placed
      again, for any army (F-10). The Units mode's Presets: the selected units or the chosen group,
      placed with the Place tool at the mouse as a new group, turned in quarter turns.
- [x] **U-5 Wrecks.** Place units as wrecks: the place tool's As wrecks and `units.asWrecks` on the
      selection (the inspector's Wrecks check box) put them into their army's `WRECKAGE` group, which
      the game turns into wrecks when it starts, as the vault's maps keep them; grey and dark in 3D, a
      cross in the top view; a warning for wrecks of a playable army.

## V: Views

- [x] **V-1 Top view.** The terrain from above with the brush, markers and the 3D camera; props as
      dots on a dark rim, sparse when zoomed out. Frame the selection (or any things) in both views.
      While nobody zoomed or moved it, it shows the whole map at every size (side by side and back,
      a panel shown or hidden).
- [x] **V-2 3D view.** On the graphics card, with strata, decals and water; the FAF editor's camera;
      the props and units with their models (props the game shows as an effect, such as steam over
      lava, as small diamonds), the markers as symbols, the object under the mouse found by picking
      and drawn highlighted.
- [x] **V-3 Overlays.** Choose what the views show over the terrain: a mask, walkability, grid,
      symmetry guides, areas, markers by kind, props, decals, units. Today: a chip per overlay in
      the views' row (what does not fit behind its More), each saying whether it shows, those that
      tint the terrain with a swatch of their colour (walkability red, a mask violet, the playable
      area's shade); markers, path nodes and the 3D camera; props and units on or off in 3D; the
      chosen mask for tools (K-1) and, while their tools are active, a layer's or the water's mask
      and the terrain types in the top view, one grid at a time and owned by the active mode and
      tool (the rules preview only in the Paint mode); walkability and a mask take turns; in 3D on
      the terrain too: the areas (the playable one marked), walkability, the mask or grid being
      painted, the symmetry's guides, and the handles of the area and sun tools; the links between
      path nodes, hidden behind hills; the grid's lines in both views, its chip saying its spacing
      (`view.gridSpacing`, the chip's context menu); decals on or off, the top view drawing them
      as faint outlines.
- [x] **V-4 Layouts.** Top and 3D side by side; panels that can be moved, closed and opened again.
      Today: the views side by side (sharing the camera, each with its own mouse), the mode panel,
      inspector, dock and toolbar hidden and shown, the panels and views resized with splitters,
      the mode panel, inspector, dock and each of the dock's tabs moved left, right, to the bottom
      or into a window of their own (a context menu on each, `view.movePanel`), all remembered in
      the user's settings; Reset layout; the tool's options and the views' bar one row each, so at
      1280 by 800 the top view keeps 464 pixels of height. Missing: dragging a panel by its header.
- [x] **V-5 Ruler.** Measure distances and heights: the ruler tool (Terrain mode), the distance in
      ogrids and km, the rise and slope, drawn in the top view and in the status line.
