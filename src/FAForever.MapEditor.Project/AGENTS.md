# Agent guide: FAForever.MapEditor.Project

The map editor's model of a map and its project format: a map as JSON and PNG files that people and
their tools (World Machine, Gaea, Krita) work on, turned into the game's files on export. The
repository-wide guide is [`../../AGENTS.md`](../../AGENTS.md); the game's files themselves are read
and written by [`FAForever.FileFormats.Map`](../FAForever.FileFormats.Map/AGENTS.md). The command-line
tool `faf-map` (`FAForever.MapEditor.Cli`) is a thin layer over this library.

## Three shapes of a map

```
project folder  ⇄  Files.Serialized  ⇄  Files.InMemory  ⇄  Scmap + MapSave + MapScenario
(JSON + PNG)       records (DTOs)       MapDocument         (FileFormats.Map)
                   MapProjectStore          ▲               MapImporter / MapExporter
                                       Editing.EditSession
```

- **`Files.InMemory`**: what the editor works on, in the editor's own terms, `MapDocument` at the
  top: one immutable version of a map. Records with `init`, lists as `ImmutableArray`, maps as
  `ImmutableSortedDictionary` (ordinal, so that saving a project never reorders its JSON), and grids
  (heightmap, terrain types, masks, images) as `TiledBuffer<T>`. Not the game's records: no
  `LuaData`, no fields of the game's files that the editor does not model.
- **`Editing`**: the one way a document changes (below).
- **`Files.Serialized`**: what is on disk: one record per file, `required`/`init` properties, no
  primary constructors. It is our own format, with `MapProjectDto.FormatVersion`; change its shape
  only with a new version. Vectors are records (`Vector3Dto`, written `{ "x": .., "y": .., "z": .. }`),
  colours hexadecimal text (`#aarrggbb`), and there is no Lua or loose JSON in it.
- **The game's files**: `MapImporter` and `MapExporter` are the one place that knows both the
  editor's model and the game's. Everything about the game's conventions belongs there or in
  `MarkerTranslation`.

The types that are the same in memory and on disk have the same properties on both sides, mapped
property by property in `Files/MapProjectMapping.Records.cs`; adding a property means adding it to
both records and both mapping methods. The document, terrain and water (which carry grids) and the
prop are mapped in `Files/MapProjectMapping.cs`; the scenario, armies and markers in
`Files/MapProjectMapping.Markers.cs`. All of it is ordinary code: there is no generator.

## The model, not the game's files

- `ScenarioInfo` keeps the playable armies and the extra armies: every map of the vault has one
  configuration, `standard`, with one team, `FFA`, and `customprops.ExtraArmies`; the export builds
  them. Factions a playable army is limited to (two co-op missions) are in `ArmyFactions`. The type
  is an enum, text fields are never null (empty instead), `Reclaim` is optional as a whole.
- **Teams** (`ScenarioInfo.Teams`, user story M-4; optional in the project, absent for none): the
  game's lobby (`lua/ui/maputil.lua`, `GetArmies`) reads only the first team of `standard`, and
  only when it is named `FFA`; so the export always writes `FFA` with every playable army first,
  and the map maker's teams after it, each with its playable armies. The import takes the armies
  of a first team `FFA` as the playable ones and the teams after it as the map maker's (a first
  team of another name: all teams' armies are playable, and those teams are kept). An army is in
  one team at most; a map without teams exports as it always did.
- Markers derive from `Marker` (`Id`, `Name`, `Position`, `Orientation`, and a `Color` each kind
  fixes). Links between markers (`PathNode.AdjacentTo`, `Chain.MarkerIds`) are ids; names are what
  the files hold, on disk and in the game. A link to a name no marker has is dropped on import
  (glacier_valley has a few). A spawn holds its army's no-rush offset, whole or not at all.
- Left to the export, as every map of the vault has them: an army's economy (0, 0) and
  personality (empty), `starts = true`, and the two numbers before the decals, which the FAF map
  editor keeps as counters (`Unknown7`, `Unknown8`): one more than the highest decal and group id.

## Versions, undo and plugins

- A change makes a new `MapDocument` that shares everything it does not change: `with` on records,
  `SetItem` on arrays, and `TiledBuffer<T>` tiles of 64 by 64 that a `Builder` copies only on the
  first write. A brush stroke on a 2049 by 2049 heightmap copies the few tiles it touches.
- `EditSession` keeps the versions: `Edit(description, document => ...)` makes one undoable step,
  `Begin(description)` opens an `EditTransaction` that collects many changes (a brush stroke, a
  plugin run) into one, and is thrown away unless committed. `Changed` reports the document before
  and after; parts that did not change are the same objects, and `TiledBuffer.ChangedTiles` names
  the tiles to redraw. An edit that changes nothing (`Equals`) is not kept.
- Edits name what they change, never a position in a list: props by `Prop.Id` (in memory only; a
  project numbers them on load), markers and armies by name. Shared edits live in `DocumentEdits`.
  - `DocumentEdits`: add, update and remove props, decals (removing one takes it out of its
    groups), units (`AddUnit(army, group path, unit)`, found by name with `FindUnit` and
    `AllUnits` as `PlacedUnit`: army, group path, unit), markers of every kind by id
    (`AddMarker` gives a new id and a free name, `FreeName`: `Mass 08` becomes `Mass 12`;
    `RemoveMarker` drops every path node link and chain entry to it; `UpdateMarker` may not change
    a marker's kind or id) and areas by name.
  - `ArmyEdits` (M-4): add an army (a free `ARMY_n`; playable ones get a spawn, at a place or the
    middle), remove one (with its units, its spawn, and every mention), rename one (the scenario's
    lists, factions, team, the others' alliances and its spawn marker; a name a marker has is
    refused), make it playable or extra (extra leaves its team and factions), its factions (none or
    all four: no limit), alliances (both ways unless told) and team. Army names are Lua names
    (`IsArmyName`): the scenario lists extra armies separated by spaces.
  - `UnitGroupEdits` (U-2, U-3, U-4): groups by army and path (`INITIAL/Town`, empty for the root
    group `Units`): add, rename (unique among siblings), remove (its units and groups to the group
    above, or along with it), move into another group of any army; `MoveUnits` between armies and
    groups; `CaptureUnits` and `PlaceUnits`, a template of units relative to its middle (rounded
    to a whole ogrid, so structures stay on the build grid) placed as a new group, turned. The game
    creates the units of each army's `INITIAL`, with the groups in it, at the start.
    `BuildGrid.Snap`: a structure an odd number of ogrids wide on a half ogrid, an even one on a
    whole; a quarter turn swaps its sides.
  - `ObjectRef` names one thing of any kind (`marker:12`, `prop:5`, `decal:3`, `unit:UNIT_5`,
    `area:AREA_1`), for the editor's selection (user story F-11). `MapObjects` gives a thing's
    place (its centre: a decal's position is a corner), heading (about the up axis, from +x towards
    +z, clockwise in the top view; a prop's from its matrix, whose rows are `RotationX/Y/Z`), the
    outline of a decal or area, and finds what a click picks (`Nearest`, with the size of a pixel)
    or a box holds (`Inside`). `ObjectEdits` moves, places, turns (each about its own centre; a
    prop's whole matrix turns, so a tilt stays), removes, copies and pastes many things at once
    (`ObjectClipboard`; copied path nodes keep their links among each other). Things keep to the
    terrain: they stay as high above it as they were (`Heightmap.HeightAt`, between the corners).
    `Nearest` takes what may be picked at all (`include`): the editor leaves out hidden decals.
  - `DecalEdits` (user stories D-1, D-2): `NewDecal` centred on a place from a texture (an asset's
    key becomes the game's path with a slash, `/env/common/decals/erosion018_normals.dds`, in the
    first of two slots; a `_normals` texture makes a `Normals` decal, anything else `Albedo`; the
    cut-off 500, the distance maps use most), `PlaceDecal` (on top, with its new reference),
    `ScaleDecals` and `SizeDecals` (about each centre; `Scale.Y` follows x, as maps write it),
    `SetDecalLods` (the cut-off and near cut-off of many at once), `OrderDecals` (`Forward` and
    `Backward` one place, a block of selected decals moving together; `ToFront`, `ToBack`: the list's
    order decides what lies on top, the last drawn last), and the groups: `AddDecalGroup` (a free
    name, only decals the map has), `RenameDecalGroup`, `RemoveDecalGroup` (its decals stay),
    `AddToDecalGroup`, `RemoveFromDecalGroup`, `DecalsOf`. Hiding a group is the editor's, never the
    document's.
  - Props stand by their matrix (`PropOrientation`): the rows `RotationX/Y/Z` are where the
    model's axes point on the map, so `RotationY` is its up (checked against the tilted props of
    the vault maps: the rows follow the terrain's normal, the columns do not). Aligning to a
    normal, standing upright and tilting by pitch and roll keep the heading (`PropHeading`, which
    a turn about the prop's own up changes by exactly that angle). `TiltsWithTerrain` decides by
    the blueprint's folder: rocks, crystals, logs and clutter tilt, trees and the rest stand.
    `PropEdits` changes many props by id in one pass (align, upright, drop with a sink, tilt,
    scale, replace blueprints, add and remove many) and sets them back on the ground where the
    heightmap's tiles changed (`PropsFollowTerrain`). `PropPlacement` makes a placed prop,
    `PropScatter` paints a palette by weight with a density and spacing (`PropGrid` finds the
    props near a place) where `IPlacementCondition`s allow (`MaskCondition`), and
    `Workspaces/PropReclaim` adds up the blueprints' reclaim (`AssetEntry.ReclaimMass`, `ReclaimEnergy`).
- Lua plugins (Lua-CSharp, later) get a facade over the session, never the document: reads give
  values, writes happen inside a transaction, so each plugin run is one undo step and a failing
  plugin changes nothing.

## Brushes

A brush is a record with `Apply(builder, ...)`: one dab into a grid's `TiledBuffer.Builder`, in
the map's ogrids whatever the grid's size (`GridDab`: each pixel counts at its centre).
`BrushFootprint` gives the weight: round with a `BrushFalloff` (smooth bell, linear, sharp) or a
`BrushShape` picture, turned.

- `HeightBrush` (T-1): raise, lower, smooth, flatten, set height (all the way where the brush
  works, whatever the strength), noise (`HeightNoise`, three octaves of value noise fixed by `Seed`
  and place, so the dabs of a stroke fit), erode (one pass of thermal erosion per dab: drops
  steeper than `Talus` slide to the lower neighbours, the earth kept) and scale (around `Target`
  by `Factor`); `MinHeight`/`MaxHeight` keep what it writes within limits (a height beyond one
  already is not pulled back, only kept from going further).
- `MaskBrush` blends towards full or empty; `CodeBrush` sets a code where the weight reaches
  `Threshold` (terrain types, T-6). Both take `PaintLimits` (slope in degrees and height, S-1),
  checked against the heightmap handed in.
- `PaintTarget` names a byte grid of the document (`Stratum(layer)`, `TerrainTypes`,
  `Water(WaterMask)`, `Mask(name)`): its `Read` and `Write`; `EditTransaction.Grid(target)` gives
  a builder per target for a stroke.
- **Masks for tools** (K-1): `MapDocument.Masks`, named grids of the map's size or half, edited
  with `MaskLayers` (`Add`, `Rename`, `Remove`, `Problem` for a name: letters, digits, spaces,
  dashes, underscores, unique without case since it is a file name). The project keeps them as
  `masks/<name>.png` (no JSON); a save writes the changed ones and removes the files of masks gone;
  a mask of another size is refused on load and reload. The export leaves them out.

## Opening, saving and reloading

- `EditedMap` is a map being edited: its session and its project folder, with the operations of
  the File menu (open, import, save, save as, export). The CLI, the editor and its automation all
  use it, so they behave the same.
- `EditedMap.Save` passes the last saved document to `MapProjectStore.Save`, which writes only
  the images whose part changed since and only the JSON files whose text changed. A save after a
  brush stroke writes the heightmap, not the twenty other files.
- The `...Async` operations of `EditedMap` read and write on a pool thread. A save takes the
  version current when it starts; the editing goes on meanwhile (versions never change), and the
  save marks exactly that version as saved (`EditSession.MarkSaved(document)`). On an 81 km map
  (4096 by 4096 ogrids) importing takes about 5 s, the first save 8 s, a save after a stroke 2 s.
- `MapProjectStore.Reload` reads the files that changed on disk (another tool saved
  `heightmap.png`, `markers.json`, ...) into the document, part by part; a file that holds what the
  document holds changes nothing. `EditedMap.Reload` makes it one undoable edit, and first
  leaves out the files that hold what the last save wrote: the editor's own, also when the
  document changed since (an edit during a save in the background must not be undone by it). A `map.json` that changes the map's size is refused: the grids would
  no longer fit.
- `procedural/` holds the procedural work: `sources/` the map maker's World Machine and Gaea files
  (versioned with the project), `temp/` what those tools build from them (heightmaps, masks; left
  out of version control). Every save makes both folders; the editor reads nothing in them by
  itself: a result comes into the map when the map maker brings it in (T-3, later T-5).
- `ProjectWatcher` reports the changed files of a folder once it has been quiet for a moment
  (tools save in bursts), on a pool thread. `faf-map pack --watch` opens the project once and
  reloads only what changed before each export.

## Version control

One git repository per project folder (`Versioning/ProjectRepository`, user story F-18), with the
computer's `git`, so every other git tool works on it too. Nothing is under version control until
the map maker turns it on (`Enable`, with `RepositorySettings`): `git init` on `main`,
`core.longpaths` (Windows' 260 characters), Git LFS for the binary files when chosen (the default:
a 16-bit heightmap of 81 km is tens of megabytes, and every save of it would stay in the history),
a `.gitignore` (`procedural/temp/`) and `.gitattributes` (JSON and Lua with LF line endings, the
binary patterns to LFS or as binary) unless the folder has them, the author and `origin`; then
the first commit of everything on disk. `Commit` adds everything and commits, or returns null when
nothing changed. Git runs without a terminal prompt or an editor; what it says when it fails is the
exception's message. Hidden folders (`.git`) are no files of the project: `ProjectWatcher` skips them.

## What the export derives

- The paths of the map's files in the scenario (`/maps/<folder>/<file>.scmap`, `_save.lua`,
  `_script.lua`) from `Name`, `FileName` and `Version`, and the scenario's size from the heightmap.
  The vault has folders whose files differ in case or name from what their scenario says; the
  export makes them agree.
- Every path into the map's own folder (`/maps/<name>.v0003/...`, in any case, with or without a
  version) is pointed at the exported folder: in the Lua files and in the `.scmap`'s textures and
  props (`MapFolderName.RewritePaths`).
- The decoration of markers: colour, editor prop, `hint`, and for deposits `resource`, `size` and
  `amount`, from their type (`MarkerTranslation.Decorations`, the values most of 99 vault maps
  use). Path nodes get their `graph` from their layer. A node without links gets
  `adjacentTo = ''`, as most maps write it.
- The save's editor counters (`next_unit_id`, ...) from the names in use (`MapSaveWriter`).
- Not kept at all: the platoons, orders and platoon builders of co-op saves, and the `orders` and
  `platoon` of unit groups and units (empty in every vault map but six groups of one co-op mission);
  the export writes them empty, as the map editors do.

A `-0` in a marker's orientation becomes `0` on import: they turn a marker the same, and the
project leaves an orientation of zero out.

## Markers

`MarkerSet` groups the markers by what they are: spawns (blank markers named `ARMY_<n>`), deposits,
path nodes (by layer, with their links), AI markers (by the game's type name, also for types the
editor does not know), weather definitions and generators, areas and chains. Weather settings
that a map leaves out stay absent (`double?`): the game decides what they default to, not us.

`MarkerEdits` (user stories R-1 to R-6) holds the edits beyond one marker: the playable armies of
spawns (`NextSpawnName`, `RenameArmy` everywhere an army is named, `RenumberSpawns`; `AddMarker`
and `RemoveMarker` call them for a spawn, so the spawns stay `ARMY_1` to `ARMY_n` and each has its
playable army), links between path nodes of one layer (`LinkPathNodes`, `UnlinkPathNodes`,
`SetPathLayer`), chains by name (`AddChain`, `UpdateChain`, `RemoveChain`), the grids a marker
snaps to (`Snap`, `MarkerSnap`), how steep the ground under a deposit is (`Steepness`), and the
kinds of markers one by one (`MarkerKinds`, `KindOf`), which `MapObjects.Nearest`, `Inside` and
`All` take to leave hidden kinds out.

## Images and textures

- ImageMagick (Magick.NET, Q16) reads and writes the PNGs (`ProjectImages`) and compresses the
  DXT5 textures; it reads what map makers' tools save (8 or 16 bits, grey or colour). It is native
  code, so this library is desktop only: never reference it from the Viewer.
- `DdsTexture` reads and writes the DDS files of the `.scmap`: uncompressed A8R8G8B8 or DXT5, no
  mipmaps (every texture in 100 vault maps), with the FAF map editor's header. Uncompressed
  textures survive unpacking and packing pixel for pixel; DXT5 is decoded once on import and
  compressed again on export (a mean error of about 0.02 per channel).
- ImageMagick never opens or writes a file itself: its file access fails on paths longer than
  260 characters. Read and write the bytes with .NET and hand them over.
- Each stratum mask texture holds four layers, one per channel (red, green, blue, alpha): layer 1
  to 4 in the first, 5 to 8 in the second. A project keeps one grey PNG per layer.

## The workspace and the game's assets

One workspace per computer (`Workspaces/`): a folder with the map projects and the assets converted
from the game's archives (`WorkspaceLayout` lists its files). `workspace.json` (`WorkspaceDto`) says
where the game, FAF's data and the vault are; maps are exported to the vault's `maps` folder.
`UserSettings` remembers where the workspace is, outside it (`%APPDATA%\FAF Map Editor\settings.json`;
the environment variable `FAF_MAP_EDITOR_SETTINGS` names another file, which tests and automation
use so that the user's own stays alone). `FafInstallation` reads FAF's `fa_path.lua` for the defaults.

- `GameArchives` lays the archives over each other as the game mounts them: per name (`env`,
  `units`) the game's `.scd`, then FAF's `.nx2`, which wins. `props.scd` holds only placeholders.
  Paths are compared in lower case.
- `AssetConverter` turns them into `assets/`, keeping the game's paths in lower case: textures as
  8-bit PNG with alpha, prop and unit blueprints as the editor's JSON (`PropBlueprintDto`,
  `UnitBlueprintDto`, read with FileFormats.Blueprints by `AssetBlueprints`), meshes as glTF
  (`.glb`, by `GlbWriter` of FileFormats.Mesh: the triangles, the skeleton as a skin, no material);
  scripts, animations and the rest are left out. The levels of detail get the names the engine
  fills in (`<name>_lod<n>.scm`, `_albedo.dds`, `_normalsts.dds`, `_specteam.dds`); a name is
  relative to the blueprint's folder and may step out of it (the tree groups use
  `../Brch01_s1_albedo.dds`), and a file the archives lack is left out. It writes into `assets.new/` and replaces `assets/` when done; a stopped
  conversion goes on where it was. For game version 3839: 32 s, 2.9 GB, 6256 textures, 2188 meshes,
  563 props, 606 units.
- `AssetCheck` tells whether the assets are current: converted by this editor version (and
  `AssetConverter.FormatVersion`, raise it when the output changes) from the archives as they are.
  Only FAF's `.nx2` are checked; same size and time is unchanged, otherwise the SHA-256 decides.
- `Workspace.FindAsset("/env/Evergreen/layers/Eg_Grass001_albedo.dds")` gives the converted file.
- `AssetCatalog.Load(assets)` lists what a map maker chooses from (user stories F-8, F-9): the
  props and units from their converted blueprints, the decals (`decals/`, `splats/`) and the
  stratum textures (`layers/`, albedo and normal map as one) from their PNGs. Each `AssetEntry`
  has its kind, the keys a map names it by (`AssetCatalog.KeyOf`: the game path as
  `GameArchives.Normalize` writes it, `.dds`; a unit's id), its group (the biome of `env/`, or a
  unit's faction), its category (a prop's folder, a unit's layer, ...), its family (a prop without
  `_v1`, `_s2`: the variants share a tile) and its facts. `AssetUse.Count(document)` counts what a
  map uses by those keys, and `AssetUse.Biomes` gives the biomes of that, most used first.
- A prop's or unit's picture is drawn from its most detailed mesh (`AssetEntry.Model`: the
  `.glb`, the albedo, normal and specular maps and the shader of its first level of detail) by
  `AssetPreview.Render`, with `MeshPreview` of FileFormats.Mesh: on the processor, from above and
  in front, shaded as the game's `effects/mesh.fx` (FAF's, in `effects.nx2`) shades it. The
  shader's name chooses the formula (`MeshPreview.ShaderOf`: the factions' PBR shaders with their
  team colour, normal-mapped props, vertex-normal props) and the alpha test (`AlphaRef` 0x80 for
  the `...Alpha` shaders of trees and bushes, 0x23 for `VertexNormal`). The light is the game
  editor's default, the environment a gradient fitted to `DefaultEnvCube.dds`, the army blue;
  there are no shadows. The textures are read at most twice the picture's width. A unit takes
  about 50 to 300 ms at 160 pixels, most of it reading the three textures.
- `GameTextures.Find(path, document, workspace)` finds a texture a map names as the game does: a
  path into `/maps/<any folder>/` is one of the map's own files (`MapDocument.Files`), any other
  one of the workspace's assets. Its `Key` is the same for every spelling of the path and its
  `Version` changes with the file, so a view can keep one copy per texture and reload it.
- `faf-map workspace init|convert|status` does the same without the editor.
- **Presets** (user story F-10): `presets/<kind>/<name>.json` (`Workspace.Presets`, a
  `PresetLibrary`), a folder per kind (lower-case letters, digits and dashes: `brush`, `lighting`,
  `water`, `units`, `markers`, `props-palette`) and a file per preset (`PresetDto`: format version,
  kind, name, settings). The settings are a JSON object whose shape the feature of the kind owns:
  `Preset.Of(kind, name, record)` writes a record of its own as the editor writes JSON, and
  `preset.Read<T>()` reads it back. The file name is the name with the characters a file name
  cannot hold made dashes; the name itself is in the file. The library keeps nothing in memory:
  every call reads the folder, so presets other tools or editors write show at once; `List` leaves
  out a file it cannot read and names it.

## Generated results

Something made from the map when asked (user story F-7: walkability, the normal map and
preview, props or masks from rules) is an `IGenerator` (`Generation/GeneratedResults`): its id,
title, story, its inputs (`GeneratedInput`: a named part of the document, `Heightmap`, `Water`,
`Markers`, ...) and `Generate(document)`. `GeneratedResults` keeps the generators and the last
result of each; a `GeneratedResult` holds the parts of the version it was made from, and since
versions share what they do not change, `ChangedInputs(document)` is a few reference comparisons
(an undo back to that version makes it current again). `States` tells per generator `Missing`,
`Stale` or `Current`; nothing generates by itself. A generator that runs on a pool thread makes
its result with `GeneratedResult.Of(generator, document, value)` from the version it started
with and hands it to `Keep`. Results live in memory only; keeping them in the project is for the
generators that need it. A generator whose result is a part of the map (`IDocumentGenerator`)
also puts it in its place (`Apply`); the editor makes that a step of undo. Inputs read only what
a generator needs (`WaterLevel`, `DepositsAndAreas`, `MapType`), so a change of the water's colour
or a path node leaves walkability current.

- **Walkability** (user story T-7, `Generation/Walkability`, `WalkabilityGenerator`): the game's
  navigational mesh, `NavGenerator` of FileFormats.Map (which stays browser-safe), made from the
  document: the heightmap and terrain types as the `.scmap` holds them, the water level, the mass
  and hydrocarbon markers, and the playable area of a skirmish map (its first area when larger
  than 32 by 32 ogrids, as `NavGenerator.FindPlayableArea`). `WalkabilityTest` checks that it is
  ogrid for ogrid what FileFormats.Map makes from the game's files of the three test maps.
  `WalkabilityChanges.Compare` gives what changed in a layer between two generations: per ogrid
  opened or blocked, and where regions were joined (a new region over two or more old ones: a new
  passage) or split (a closed path), each at the changed ogrid nearest the centre of its changes.
  A culled region (small, without deposits) counts as blocked, as it does for the game's AI.
- **The normal map** (user story S-4, `Generation/TerrainImages.NormalMap`, `NormalMapGenerator`):
  per pixel the terrain's normal at its centre (the four corners' normals blended), written as the
  FAF map editor writes it and the game reads it: x in alpha and z in green as 0.5 + n / 2, red and
  blue 0. Theta Passage's own normal map differs from the generated one by less than one step per
  channel on average.

## Checks: problems of a map

User story F-14 (`Checks/`): an `IProblemCheck` (or a `ProblemCheck` made of a function) reads a
`ProblemContext` (the document; the workspace's catalogue and whether it has a file, the generated
results, the last walkability changes: each may be missing) and yields `Problem`s (check, severity
`Info`/`Warning`/`Error`, message, the `ObjectRef` to select and the place to look at).
`ProblemChecks` holds the editor's own (`MapChecks.All`) and those registered, and runs them all,
worst first; a check that throws is reported apart and finds nothing. Checks run after each step,
so they stay cheap: no disk access but through the context's function, nothing slower than the
map's things.

- `army-without-spawn` (error): a playable army of a skirmish map without its spawn marker.
- `spawn-without-mass` (warning): no mass deposit within 30 ogrids of a spawn.
- `missing-texture` (warning): a stratum's or decal's texture that neither the map's own files
  nor the workspace has (decals grouped by texture); without a workspace only the map's own files.
- `missing-model` (warning, or a note): props and units grouped by blueprint whose blueprint the
  workspace lacks, or has without a converted model (drawn as a box). Needs the catalogue.
- `walkability-changes` (warning): new passages and closed paths of the land since walkability was
  generated the time before.

## Tests

`tests/FAForever.MapEditor.Project.Tests` uses the three maps of the map library's tests (linked,
not copied). `GameContent` writes what the game reads from a save or scenario as text, without the
decoration the export derives; an imported and exported map must give the same text. A project
that is saved and loaded must export to the same bytes. Over the local vault copy, all 98 maps the
importer accepts (one folder holds two `.scmap` files) give the same game content after import and
export.
