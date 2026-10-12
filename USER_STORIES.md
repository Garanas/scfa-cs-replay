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
      settings, what it is made from and when it was made in the inspector. Generate, Everything
      out of date or missing (`generate.all`) also makes what was never made and says why it leaves
      the rest (the sky cube map only when asked); results kept in the project (rules layers, props
      of rules, the sky cube map, the preview and normal map) count as current when it opens; the
      export generates into its copy only and leaves the history alone. A generated result that
      goes into the map is derived: no step of undo; undoing its source takes it back with it, and
      each version keeps the results made for it. Every result may regenerate on its own (each
      entry's check box, the map's), only while the views show it; the Auto switch in the views'
      bar turns that on and off for the editor and says what waits or runs; the workspace says
      what new and imported maps regenerate on their own and make on export (by default
      walkability, connected regions and masks from rules), edited on the Workspace settings page
      or taken from a map (Use as workspace default).
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
      walkability was generated last; a click selects a problem's things, a double click or Enter
      goes to it too (the top view and 3D camera look at it); Lua plugins add checks of their own (F-15).
- [x] **F-15 Lua plugins.** Plugins with commands, tools and panels, through the controller and
      the events; a run is one step of undo. Today: the workspace's `plugins/` (a folder each with
      its `plugin.lua` and `plugin.json`), loaded when the workspace is ready and read again when a
      file changes; the Plugins mode (at the rail's bottom, F12) lists them by category (`plugin.json`'s
      `category`, else the mode of their tools, else Other) with their kind as a tag (`kind`: brush,
      check, command, generator, export, other), and its inspector shows the chosen one on or off
      (remembered), its errors, its commands, its fields and what it registered; plugins register commands (the Plugins menu,
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
      preview image, no-rush radius, adaptive map: the Map panel's Information entry, its fields in
      the inspector in About, Game and Preview (`map.information`), one step of undo per field, the
      reclaim the export writes read only, with the folder, size, armies and the `.scmap`'s preview.
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
      is limited to; alliances; teams for the lobby. The Map panel's entry per army (its fields in
      the inspector, Add army and Add extra under the heading): a renamed army
      renames its spawn, a removed one takes its units and spawn along, a playable one gets a spawn.
      Teams are kept in the project and written after the team FFA, the only one the game's lobby
      reads; the lobby does not use them yet.
- [x] **M-5 Areas.** Draw, name, move and resize areas (rectangles) in the views: the areas tool
      (handles in the top and 3D views), the Map panel's entry per area (chosen, it is selected on
      the map and its fields are in the inspector), the inspector's fields; one is the playable area
      (M-3), chosen also in the inspector of the Map panel's Size and playable area.
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
- [x] **M-10 Textures as DDS.** The map's own textures in other formats (PNG, JPEG, TGA, ...) are
      exported as DDS, every reference pointed at them; the project keeps its pictures. Today: the
      export step `ddsTextures` (after every other step) and `faf-map pack` convert the strata, decals
      and decal sets, the water's ramp, waves and cube map, the sky's cube maps, background and sky
      dome, the pictures a Lua file of the map names and every picture in a folder of the map; DDS
      files and the game's textures are left as they are; a picture converted before comes from a
      cache by its hash.

## T: Terrain

- [x] **T-1 Sculpt.** Raise, lower, smooth, flatten, set to a height, add noise (a new seed per
      stroke), erode (thermal) and scale around a base with a brush, round (falloff smooth, linear
      or sharp) or the shape of one of the workspace's brushes (T-2), turned by hand or at random per
      dab; limits on the lowest and highest height it makes. Every brush's radius by `[` and `]`
      or control and the wheel over a view, its strength with shift; a brush's picture turned with
      Q and E (15 degrees, shift 90) or alt and the wheel (5 degrees a notch). Missing: hydraulic
      erosion.
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
      its points on the terrain, where the tool works as in the top view. Wave 17 (library and
      commands): width, falloffs, strength and a sharp corner per point; smooth heights between
      points; reverse; falloff shapes (terraced too) and a cliff's face width; edge noise and
      variation along the curve as ranges, alike in every part of a symmetric map; closed curves
      fill (older ones open as line only); a river; road modes (cut only, fill only) and camber;
      ramps (a point's values, an even grade, points snapped to the ground, the grades).
      Decals along a curve (D2): `decals.alongCurve` (Decals menu, the curve inspector's "Decals
      along the curve", the place tool's "Along a curve...") places a decal or decal set along the
      curve once, as ordinary decals with spacing, width, offset, alternating sides, random turn and
      jitter, and the images of a mirrored curve; one step of undo. Wave 18 (window): the
      inspector shows every setting in groups that follow the profile; a table of points (place,
      height, width, falloffs, strength, sharp; empty is the curve's value) with the grade of each
      stretch (marked above a limit, 30 % by default), chosen points, Reverse, Even the grade and
      Snap to the ground; the curve tool chooses a point it presses, sets a height by hand (shift
      and a drag, + and -) and takes a new point's height from the last point, the ground or a
      typed number; both views draw the width (dashed) and the fade's end (dotted). Cliff faces block
      walkability (D3): a cliff curve's face counts as marked blocked unless marked open by hand,
      `blocksWalkability`, the inspector's "Cliff face blocks walkability".
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
- [x] **T-8 Navigation mesh.** See where each kind of unit can move: the connected regions of the
      game's navigational mesh per layer (land, hover, amphibious, naval), how large each is, and
      which starts and deposits lie in it. Today: the Procedural mode's Navigation mesh (Generate,
      the layer), the connected regions coloured in both views (the Connected regions chip), a list
      of the largest (a click or the Connected regions tool chooses one) and the starts that lie in
      none. It regenerates on its own while its overlay shows, as walkability does (T-7).
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
      threshold and invert; a preview in the top view; editing the rules makes the rules layers in
      the same step, so the mask shows what they make at once; `generate.masks` makes them again
      (one step of undo) when the terrain, water level or masks they read change. Kept in
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
      inspector edits the chosen one; sky presets. A sky cube map made from the map (the Procedural
      list's Sky cube map, `generate.skyCube`): the version 60 sky (or one from the lighting) above the
      horizon, the map and its water below, written into the project's `env/` as a DDS cube and chosen with
      one click for the sky, the units and the water (`generate.useSkyCube`); skybox presets of the FAF map
      editor read into the sky (`lighting.importSkybox`).
- [x] **L-2 Lighting presets.** The lighting of a biome as a preset (F-10): the Lighting panel's
      presets, `lighting.preset`.

## R: Markers

- [x] **R-1 Place markers.** Spawns, mass and hydrocarbon deposits, AI markers, path nodes and
      weather by kind, and blank markers (a kind of their own, the Blank tool); move and delete
      them; snap to the grid. Weather: the Weather and Clouds tools
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
      kept with the map and applied again after every generation, mirrored with the symmetry as
      markers placed by hand are (F-12).
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
      The Markers panel lists the kinds and the layers of path nodes with their colours, counts and
      show check boxes, the chains and the adaptive map; the chosen entry's inspector selects or
      clears every marker of its kind (`markers.select`, `markers.clear {kind, layer?}`: one step of
      undo, links, chains and spawns' armies kept right) and says how the export treats the generated
      networks beside a layer's own nodes.

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
      (Palettes), cleared at once (`props.clearPalette`; Restore, `props.restorePalette`, or undo right
      after brings it back), the eraser (all props, or only the palette's).
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
      generated props hand-placed. On a symmetric map each prop comes with its images (a rule's
      Mirror, on by default). Kept as `prop-rules.json`; exported as ordinary props.
- [x] **P-5 Reclaim.** The mass and energy of the props, for the map and per area. Today: the
      Props panel's Reclaim (the map, per area), the selected props' in the inspector, from the
      blueprints' values (`props.reclaim`); the export writes the props' totals into the scenario
      for the lobby (its step `reclaim`; units and wrecks not counted).

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
      A map maker's own decals and sets: Add files to the map (`map.addFiles`, the shelf's button)
      copies pictures into the map's `env/decals`; the shelf and the picker list them under the
      source This map (a set one tile, chosen whole); a decal-masks layer chooses its mask and sets in
      the inspector; a set's masks are never exported; a decal's middle lies on the map. Importing
      a map takes Kaali's layout as the map-wide slots and an albedo with its set's normals as one set.
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
      selects it), the chosen army or group edited in the inspector (a group's name, Remove, Add a
      group, Select its units, Move the selection here, Save as a preset; an army's fields as in the
      Map mode); nesting and moving between groups by command (`units.moveGroup`, Move here) and by
      dragging units (a selected one takes the selection along) and groups onto a group or army in
      the tree, which lights up, one step of undo (`units.drop`).
- [x] **U-4 Unit presets.** A civilian base, a defence line: a group saved as a preset and placed
      again, for any army (F-10). The Units mode's Presets: the selected units or the chosen group,
      placed with the Place tool at the mouse as a new group, turned in quarter turns.
- [x] **U-6 Unit templates from real maps.** Every workspace starts with 38 example unit presets
      taken from the campaign, coop and vault maps: 26 civilian settlements of the four building
      styles (UEF, Aeon, Cybran, Seraphim) from hamlets to cities, farms, an industrial yard, a
      research campus, a network hub, a rift site, without walls or defences, and 12 small wreck
      fields (a few tanks, crashed gunships, a destroyed convoy, naval graveyards, mixed factions).
      A preset names an army role (civilian, enemy, player) that places it for the matching army of
      the map, an enemy on a map with only the civilians' armies in an extra army `ARMY_9` added
      for it (`faf-map dev scan-units`, `dev unit-templates`); untouched copies of the examples of
      earlier releases (enemy bases, players' starts) leave the workspace.
- [x] **U-5 Wrecks.** Place units as wrecks: the place tool's As wrecks and `units.asWrecks` on the
      selection (the inspector's Wrecks check box) put them into their army's `WRECKAGE` group, which
      the game turns into wrecks when it starts, as the vault's maps keep them; grey and dark in 3D, a
      cross in the top view; a warning for wrecks of a playable army.
- [x] **U-7 Place many at once.** The Place tool's ways to place: a line (structures butt together
      at their skirt, walls above all; mobile units at a chosen spacing; each facing along the
      line or the chosen heading), a rectangle (filled, or hollow for walls), a ring around the
      selection, and a scatter brush for mobile units and wrecks (a count per stroke, random
      headings, a least distance); each one step of undo, with its symmetry images, its footprints
      shown under the mouse before the mouse is released, and a single unit's footprint shown too.
      Today: the options row's How to place (Single, Line, Rectangle, Ring, Scatter), the unit, Army
      and Group first, the way's settings behind More (`units.placeMode`); the army is that of the
      spawn nearest the view's middle on a map with armies to play on, else the civilians, and the
      group the tree's chosen one; the status line says where units went; a line or rectangle is
      dragged out with every footprint under the mouse (red where it would overlap another or break
      a placement rule, or lies beyond the map's edge, which is left out) and the count in the
      status line (Shift keeps a line to 45°, Escape cancels) and placed on release as one step
      (`units.addMany`; more than 500 units are asked about first); structures are spaced by their
      skirt, as the game's build templates are; a ring goes around the selected units' skirts or
      the structure under the mouse at a distance from them; the scatter brush (`[` and `]` its radius) puts
      the count in its circle at random headings, none closer than the least distance, and a drag
      goes on at that density; every way places into the row's army and group, as wrecks when
      asked, with the symmetry's images, and selects what it placed; a single unit's footprint
      shows under the mouse.
- [x] **U-8 Palettes.** A named set of ten slots, each a unit or a unit preset, saved in the
      workspace beside the presets; the Units mode shows the chosen palette, keys 1 to 0 pick a slot
      while the view has the keyboard and Tab goes to the next palette; a unit from the shelf or the
      picker and a preset from the list are dropped into a slot; example palettes per faction (a T1
      base, T2, defences, naval, civilian); the units placed last are offered again.
      Today: the Units panel's Palette section (a combo of the palettes, + for a new one, a menu to rename,
      delete or show the folder, ten slots with their keys and the units' icons, the chosen one filled,
      "drop here" while empty) and the last six placed as chips (the Scatter brush's too; a click chooses
      the Place tool); 1 to 9 and 0 pick a slot and Tab and
      Shift+Tab change palette while the Place tool is active; a shelf tile (dragged or from its context
      menu) and a units preset (dragged or from its context menu) go into a slot, a slot clears from its
      menu; `units.palette`, `units.slot`, `units.setSlot`, `units.newPalette`, `units.renamePalette`,
      `units.deletePalette`, `state.units.palette`; twenty example palettes written into each workspace.
- [x] **U-9 Preset instances.** The group a unit preset is placed as knows the preset: its name, a
      fingerprint of the preset's content, and where and how it was placed; the tree and the
      inspector say so, the presets list counts how often each is placed and whether the preset
      changed since; select the instance, save the instance's layout back into the preset, or
      detach it. The link lives in the project and is never used to change the map; the export
      leaves it out. Today: placing a preset makes the group its instance (units added to it later
      belong to it), kept in `armies.json`; the tree tags the group with the preset's name and
      "preset changed" or "preset missing", the Unit presets list shows "×2 placed" and "1 out of
      date", the inspector's "Placed from a preset" block says which preset, where, how and when,
      and whether the file changed, with Select the instance (also a double click on one of its
      units), Save back to the preset (asked first; the file keeps its role and wrecks; undo puts
      the file back as it was), Show the
      preset (highlighted in the list) and Detach; Problems notes instances whose preset changed or
      is missing (`preset-changed`); the export writes the group as any other. A second placement of a
      preset is named "<preset> 2"; a group named after its preset shows no repeated tag; the list has
      the filters All, Starred, Civilian, Wrecks and Mine and a star per preset.
- [x] **U-10 Select by and stored selections.** Select units by army, faction, kind, tech,
      blueprint and wrecks, within the map, the selection or an area, replacing the selection or
      adding to it; store the selection under 1 to 9 (Alt and the digit) and recall it (the digit),
      as the game's control groups, kept with the project and shown along the view; a click inside
      a structure's footprint picks it; the top view draws units as their footprint in the army's
      colour.
      Today: `select.where` and the select tool's Select by popover choose units by all of those, within
      the map, the selection or an area, replacing or adding (`select.by` opens it); Alt and a digit store
      the selection and a digit recalls it (`select.store`, `select.recall`, in every select tool), kept per
      project in the user's settings and shown as chips along the top view (`3 · Cybran farm · 17`); a click
      inside a structure's footprint, or a box that touches it, picks it (Picks: Footprint or Centre); the
      top view draws a structure as its footprint and a mobile unit as a diamond in the army's colour (the
      chip Army colours turns it off; the 3D view is not coloured by army); a double click on a unit
      selects its group's units, Alt and a double click its army's; a stored selection whose units are
      gone stays, dimmed, until stored over or cleared.
- [x] **U-11 Editing many units.** Group the selection into a new group (Ctrl+G) and ungroup; the
      group as a choice in the inspector; replace the blueprint of the selected units, or convert
      them to another faction's unit of the same kind and tech; a move keeps structures on the
      build grid; distribute the selection evenly. Today: `units.group` (Ctrl+G, a group per army
      under the deepest group holding the selected units) and `units.ungroup`; the inspector's
      Group is a combo of the army's groups, with Blueprint, Replace..., the faction chips UEF,
      Cybran, Aeon and Seraphim (the same id under the other faction's letters, else the unit with
      the same layer and tech that shares most categories; a unit without a counterpart stays and
      is counted), Group, Ungroup and Distribute (`units.distribute`, three units or more); a
      handle drag, the arrows and `edit.place` round the move so the first structure lands on the
      build grid (the select tool's check box "Keep on grid").
- [x] **U-12 Placement checks.** Problems lists structures whose footprints overlap and structures
      where the ground does not allow them (too steep, the wrong layer: water or land), from the
      blueprint's skirt, build layers and ground rule, which the workspace's converted assets carry;
      the footprint under the mouse turns red there. Today: the converted assets carry each unit's skirt, build layers, `MaxGroundVariation`, `FlattenSkirt` and least water depth (asset format 7); the checks `units-overlap` (by the skirt the engine reserves) and `units-ground` (the layer, the water's depth and the engine's flatness rule for structures: the ground under the skirt may differ by `MaxGroundVariation`, 1 by default) warn in Problems (choosing an overlap selects both units), and the Place tool's footprints (single, line, rectangle, ring) turn red, also where planned units overlap each other, with the reason in the status line; placing stays allowed.

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

## N: Notes and sketches

Drafts of the map maker's own, kept in the project and never exported: pencil strokes to work out a
layout before building it, and pictures projected onto the map to trace or build towards.

- [x] **N-1 Sketch.** Draw on the map with a pencil to draft it: the Sketch mode (on the rail, below
      the modes that edit the map), six colours (red, yellow, green, blue, white, black; keys 1 to
      6) and three widths, strokes mirrored by the map's symmetry, a straight line with Shift;
      an eraser that rubs out where it passes (Alt: whole strokes); several named sheets (layers),
      each shown or hidden, one drawn into, each with a legend that says what its colours mean.
      The sketch shows in every mode, dimmed outside the Sketch mode, and a key shows or hides it.
      Today: the Sketch mode (F10) with the pencil (keys 1 to 6 pick the colour, [ and ] the width;
      Mirror repeats a stroke by the map's symmetry, Smooth thins the line to few places, Shift draws
      a straight line, Escape gives a stroke up) and the eraser (a radius; it cuts a stroke in two
      where it crosses its middle, with Alt or Whole strokes it removes whole strokes, Mirror);
      several sheets, each shown or hidden, with an opacity outside the mode and a legend of six
      texts shown as a key in the top view's corner; the strokes are kept in the project as
      sketch/sketch.json (optional, read again when it changes on disk, never exported), every
      operation is a command (sketch.*) and one step of undo; the top view draws them as lines of a
      fixed width on screen at every zoom, dimmed outside the mode; the Sketch chip, the View menu
      and Ctrl+Shift+K show or hide the sketch in every mode. Missing: the 3D view draws no strokes
      (N-3), no symbols, labels or selecting (N-4).
- [x] **N-2 Projected images.** Lay pictures of one's own over the map in the top view (a drawing,
      a screenshot of another map): several per map, each moved, scaled and turned with handles or
      exact numbers, fitted to the map or its playable area, with an opacity, greyscale, locked,
      below or above the sketch, shown or hidden each and all at once with a key. The pictures
      are copied into the project. The 3D view does not show them (a picture stretches over hills).
      Today: the Sketch mode's Image tool and its "Projected images" section: Project an image...
      (any picture ImageMagick reads, copied into `sketch/images/` with its list in
      `sketch/images.json`, at most 4096 pixels on a side), several per map, each drawn in the top
      view only, turned and scaled in map coordinates at its opacity, greyscale when set, below or
      above the sketch; a click chooses an image, a drag moves it, the corner squares scale it
      (keeping its proportions), the knob turns it (Shift: steps of 15 degrees); the inspector has the
      picture's size, name, opacity, centre, size, turn, Fit to map, Fit to playable area, Keep
      proportions, Greyscale, Locked, Order, Replace the picture... and Remove; the Images overlay
      (chip, View menu, Ctrl+Shift+J) hides them all; every operation is a command (`images.*`)
      and one step of undo. Missing: moving an image forward or back in the drawing order (the
      list's order is the order they were projected in).
- [ ] **N-3 Sketch in 3D.** The 3D view draws the sketch's strokes over the terrain, the same width
      on screen at every distance.
- [ ] **N-4 Symbols and labels.** Stamp symbols on a sheet (spawn, mass, hydrocarbon, arrow, cross,
      question mark) and write labels in a hand-drawn style; select strokes, symbols and labels
      to move, recolour or delete them.

## H: Help and learning

Learning the editor from the editor: guided tours that point at the window's parts, and a guide made from
the same tours. The tours are files (`*.tour.json`) in the workspace's `tours` folder, so a map maker's
own sit beside the editor's.

- [x] **H-1 Tours.** Guided tours over the window that dim it, ring a control or an area, name it and
      explain it, step by step, and can ask you to try it (with Do it for me). Today: Help, Tours lists
      the tours (title, summary, steps, a check once finished, Start; a file that cannot be read is
      listed with its error); `help.tour {id, step?, card?}`, `help.tourNext`, `help.tourBack`,
      `help.tourEnd`, `help.tourDo`; one card at a time beside the target (right, left, below or above,
      always inside the window) with the step count, title, text, what it waits for or what happened,
      End the tour, Back, Next, Do it for me; Escape ends it; the window's keys keep working, and the
      mouse reaches only the target of a step that asks you to act. A step whose target is behind a
      tool row's More shows it first; a panel that is hidden or floating comes back for its step, and
      when the tour ends the layout, the view, the mode and the tool are as they were. The tour's own
      commands run without a person and never leave the map (no open, import, export, upload, push,
      report). The editor's tours are written into `tours/` when the workspace opens (a deleted one is
      not written again, an untouched older copy is brought up to date) and read again when a file
      there changes. On the first start, once a map is open, a card at the window's corner offers the
      tour of the window (Start, Not now, Don't ask again; kept in `tours/.progress.json` with the
      tours finished). The tour of the window (twelve steps) is the first. Missing: the tours of the
      modes (Terrain, Masks, Paint, Markers, Units, Procedural with the export).
- [x] **H-2 A guide made from the tours.** `faf-map guide` plays the tours in a test editor and writes a
      static guide (HTML and Markdown in `docs/guide`) with a picture per step, the target ringed and
      numbered, made again when the window changes. Today: `faf-map guide build --out <folder> --port <n>
      --settings <file> [--tours a,b] [--map <folder>] [--size 1280x800]` starts a test editor in a scratch
      workspace (never port 5371), plays each tour's steps without the card (rings and dimming only: no
      text in the pictures), crops a picture round each target, does a step's Do it for me and pictures
      the result (the 3D view's frame put in where it shows), draws an overview of the window with
      numbered outlines, and writes `guide.json` (texts, targets, bounds, pictures, the editor's version
      and the date), WebP pictures with alt text, and a Markdown and a dark HTML page per tour and an
      index; `guide render` writes the pages again from `guide.json` without an editor; `guide check`
      plays the targets without pictures and reports targets not found (exit 1), targets that moved by
      more than 8 pixels, and steps and tours added or removed. The pictures are made on the person's
      computer (`docs/guide/README.md`); CI keeps only the tours true. Missing: the guide of the
      modes' tours (they are not written yet), a theme besides dark, and a check run by CI (it needs
      the editor started on a virtual display).
- [ ] **H-3 Record a tour.** The editor writes a draft tour from what a person does (the commands run
      and the labels of the controls clicked). Missing: all of it.
- [ ] **H-4 Tours of one's own.** A map maker writes tours for their team or community. Today: the
      editor reads every `*.tour.json` of the workspace's `tours` folder, with the same format as its
      own, and lists a tour that cannot be read with why. Missing: tours that come with a plugin, a
      page that checks a tour against the window.
