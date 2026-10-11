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
  personality (empty), `starts = true`, and the decal counters before the decals
  (`Scmap.NextDecalId`, `NextDecalGroupId`; the FAF map editor's `Unknown7`, `Unknown8`): one more
  than the highest decal and group id.

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
    groups), units (`AddUnit(army, group path, unit)`; `AddUnits(army, group path, units, out
    names)` puts many into one group as one version, each empty or taken name the next free
    `UNIT_n`; found by name with `FindUnit` and
    `AllUnits` as `PlacedUnit`: army, group path, unit), markers of every kind by id
    (`AddMarker` gives a new id and a free name, `FreeName`: `Mass 08` becomes `Mass 12`;
    `RemoveMarker` drops every path node link and chain entry to it; `UpdateMarker` may not change
    a marker's kind or id) and areas by name.
  - `ArmyEdits` (M-4): add an army (a playable one the next player's `ARMY_n` after the spawns,
    whatever extra armies such as `ARMY_17` exist, with a spawn at a place or the middle; an extra
    one the next free name after `ARMY_17`), remove one (with its units, its spawn, and every mention), rename one (the scenario's
    lists, factions, team, the others' alliances and its spawn marker; a name a marker has is
    refused), make it playable or extra (extra leaves its team and factions), its factions (none or
    all four: no limit), alliances (both ways unless told) and team. Army names are Lua names
    (`IsArmyName`): the scenario lists extra armies separated by spaces.
  - `UnitGroupEdits` (U-2, U-3, U-4): groups by army and path (`INITIAL/Town`, empty for the root
    group `Units`): add, rename (unique among siblings), remove (its units and groups to the group
    above, or along with it), move into another group of any army; `MoveUnits` between armies and
    groups; `CaptureUnits` and `PlaceUnits`, a template of units relative to its middle (rounded
    to a whole ogrid, so structures stay on the build grid) placed as a new group, turned;
    `SetInstance` (below). The game
    creates the units of each army's `INITIAL`, with the groups in it, at the start.
    `BuildGrid.Snap`: a structure an odd number of ogrids wide on a half ogrid, an even one on a
    whole; a quarter turn swaps its sides.
  - `UnitLayouts` (U-7, pure maths; the editor's place tool and `units.addMany` put the units on
    the terrain with `AddUnits`): the places and headings (`UnitPlace`: x, z, radians clockwise
    from +x) of many units at once from a unit's footprint (`Extent` turned in quarter turns; none
    is 1 by 1). `Line(from, to, footprint, spacing, faceAlong, heading, isStructure)`: structures
    butt together at their footprint along the line, each on the build grid (two that land on one
    place are one), mobile units a spacing apart (at least `MinimumSpacing`, a quarter ogrid),
    each facing along the line (structures in the nearest quarter turn) or the heading;
    `Constrain(from, to, step)` keeps a line's angle to whole steps (`EighthTurn` with shift).
    `Rectangle(cornerA, cornerB, footprint, hollow, spacing, heading, isStructure)`: rows of the
    footprint from the corner nearest the origin, the rectangle rounded to whole ogrids (mobile
    units a grid of the spacing); hollow keeps the border. `Ring(bounds, footprint, distance,
    spacing, heading, isStructure)`: a hollow rectangle whose inner edge lies `distance` outside
    the bounds (mobile units spread evenly about the spacing apart). `Scatter(centre, radius,
    count, leastDistance, seed, existing)`: random places and headings within a circle, none closer
    than the least distance to each other or to the places given, the same for the same seed,
    fewer when the circle is full. Tests: `UnitLayoutsTest`.
  - **Preset instances** (U-9, `Editing/PresetInstances`, `UnitGroup.Instance`, a `PresetInstance`):
    the group a unit preset is placed as knows the preset: its name, a fingerprint of the preset's
    content (`UnitPresets.Fingerprint`: the first 16 hex digits of the SHA-256 of its
    `UnitPresetSettings` as one-line JSON in the editor's order of properties, the same on every
    computer; a unit added, taken out, moved or turned, a group renamed, the role or wrecks changed
    gives another), the place and heading it was placed with, and when. `UnitPresets.Place` sets it
    on the group it makes (the owner's decision: that group is the instance, units added to it later
    belong to it; a group in it is part of the instance, not one of its own); `PlaceUnits` itself,
    selection presets and hand-made groups make none, and an import never does. `Instances` lists
    them, `InstanceOf` finds the one a group or unit belongs to (the deepest group up its path with
    an instance), `CountInstances` counts a preset's. `StateOf` compares the fingerprint with the
    preset file's (`FingerprintOf`): `Current`, `Changed`, `Missing`. `SaveInstance` captures the
    group's units as Save as a preset does (`CaptureUnits`: around their middle, heights above the
    terrain), writes them over the preset's file (which keeps its role, wrecks and source; a file
    that is gone is made again) and sets the fingerprint to the new content's, so the instance is
    current and the preset's other instances are out of date; where and when it was placed stays.
    `Detach` clears the link; the units stay. The link is never used to change the map (no update
    from the preset). On disk `armies.json` keeps it as a group's optional `instance`
    (`PresetInstanceDto`; absent for none and in every older project, which reads as having none:
    no new format version, see `MapProjectDto`); the export writes the group as any other, since
    the game has no field. Tests: `PresetInstancesTest` (the fingerprint's value is pinned there).
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
  - **Handles of the selection** (`SelectionHandles.cs`, user story F-11): `SelectionFrame.Of` is
    the box they sit on (the middle of the things' bounds at their mean height; turned with decals
    that all face one way; which handles the things have: the ring when something turns or several
    things orbit, scale squares for props, decals and areas, sides only for decals and areas).
    `HandleLayout.Of(frame, ogridsPerPixel)` places the handles for a view so they keep their size
    on screen (the move square, the arrows along x and -z, the ring, the corners and sides) and
    `HitTest` finds the one under the mouse in the view's pixels, through a function that projects
    a place (so the top and 3D views share it). `HandleMaths`: `MoveDelta` (one axis, the centre
    snapped to a grid), `TurnAngle` (about the centre, snapped to steps), `ScaleFactors` (a side
    along its axis, a corner both alike, snapped to steps, never below a hundredth), `PlaneHit`
    (a ray on the handles' level). `ObjectEdits.RotateObjectsAbout` turns things about one place
    (each turns, its centre goes round) and `ScaleObjects` scales along turned axes, about a pivot
    (the centres spread, markers and units too) or each about its own centre (props alike along
    every axis by the factor that changes most, decals by width and length, areas by their sides).
    Every handle's drag in the editor is a call of these edits, so a later hook (symmetry) can wrap them.
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
- Lua plugins (user story F-15, the App's guide "Lua plugins") get a facade over the session, never
  the document: reads give values, writes happen inside a transaction, so each plugin run is one
  undo step and a failing plugin changes nothing. A run that also runs commands (each its own
  step) holds a group (`EditSession.BeginGroup`, `EditGroup`): the steps made while it is open
  become one step when it is committed, and are undone and forgotten when it is disposed; undo
  waits until it ends, and the history keeps all of the group's steps whatever its limit.
- **Derived changes** (`EditSession.Derive(description, edit)`, the owner's decision Q4): what is derived
  from the document (a generated result that is part of the map: the normal map, the preview, masks or
  props from rules, the sky cube map) replaces the current version's document in place, through the same
  following of layers as an edit, without a step: the map counts as modified, `Changed` is raised with
  `ChangeReason.Derive`, the history's steps and those that can be redone stay, a merge key goes on. While
  a transaction is open it waits (`DerivedWaiting`) and is made on the document the transaction left, also
  when it is thrown away. Each version keeps the results derived for it, so an undo returns the older
  results with their sources. Tests: `EditSessionTest`.

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
- **Symmetry** (F-12): `SymmetricDabs.Of(transforms, x, z, rotation, round)` gives a dab's image in
  every part: its place, its shape turned by the part's turn and, in a mirrored part, flipped
  (`Mirrored` on every brush, `BrushFootprint`'s flip of u before the turn; the maths in its
  remarks, `MapSymmetryTransform.TurnAngle`). Coinciding images are dropped. `HeightBrush.NoisePlace`
  reads the noise through the inverse of the part's transform.
- **Mirror partners and images** (`SymmetryEdits`): `PartnerOf`/`Partners` find a thing's
  counterpart in a part (same likeness: prop or unit blueprint, decal texture, marker kind, AI type,
  path layer; within `DefaultTolerance`, an ogrid), through an index per document version;
  `ImageOf` for props (rotation by `MapSymmetryTransform.Apply(Matrix4x4)`), decals, units and
  markers, keeping the height above the terrain; `MirrorObjects` adds images where no partner is;
  `MoveObjects`, `RotateObjects`, `RotateObjectsAbout` (a partner about the image of the pivot),
  `ScaleObjects` (along the images of the axes, about the image of the pivot) and `PlaceObjects`
  with partners; `WithoutPartners` leaves out the partners of things kept before them (what a
  duplicate copies before mirroring the copies). `Partners` of things that hold a thing and its
  images (a mirrored placement selects them all) lets the one first in the list lead and makes
  the others its partners, and the edits with partners move only the leading things themselves:
  each part moves by its own image of the offset, so the selection stays symmetric (the editor's
  select tool puts the thing under the mouse first). A unit's image goes to the army whose spawn
  is the partner of its army's spawn (`PartnerArmy`; the civilians keep theirs), into a group of
  the same name. `Checks/SymmetryChecks` is the `not-symmetric` check, given the transforms by
  the caller.
- **The map's symmetry** (`MapDocument.Symmetry`, a `SymmetrySetting` of kind and count, null for
  none): the symmetry the editor mirrors edits by, kept in `map.json` (`symmetry`, an optional
  `SymmetryDto`: the kind's name and the count; an unknown kind reads as none) and never exported.
  `NewMap` sets rotational for its players (2 to 8) and puts the spawns exactly on its images: the
  first above the middle on an ogrid's centre, the others turned about the middle.
- **Deposits stay on ogrid centres**: `ObjectEdits` moves, places, turns about a place and scales
  about a place a mass or hydrocarbon marker onto the nearest ogrid centre (halfway it goes to the
  one nearer the map's middle, so a deposit and its partner moved by images of one offset stay
  images), as placing one does.
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
  (versioned with the project), `temp/` what goes between the editor and those tools (left out of
  version control): the draft and ticked masks the editor writes for them and the outputs they
  write beside them (`ProceduralFiles`, section "Draft and final heightmaps"). Every save makes
  both folders; an output comes into the map when the map maker chooses it (an image layer, or the
  final's base), and is read again whenever the tool writes it.
- `ProjectWatcher` reports the changed files of a folder once it has been quiet for a moment
  (tools save in bursts), on a pool thread. `faf-map pack --watch` opens the project once and
  reloads only what changed before each export. Tests wait for a report with `NextChangeAsync`
  and skip the quiet time with `Flush` (once `HasPending`: the file system's event comes on its
  own thread), instead of polling.

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

- **Sharing**: `Push` sends the branch to `origin` (`--set-upstream`), `Pull` takes `origin`'s
  branch only as a fast-forward and only when nothing is uncommitted, and answers the files that
  changed (the editor reads them again). Neither ever merges or overwrites work: a refusal says
  why. Signing in is the computer's git credential helper's (Git Credential Manager on Windows):
  the editor never sees or keeps a password; network operations stop after `NetworkTimeout`.
  `Status` counts the commits `Ahead` and `Behind` the branch followed, as git knew it last.
- **History**: `History(folder, count)` (`CommitInfo`: hash, short hash, author, date, subject,
  newest first), `FilesOf(folder, commit)` (`CommitFile`: added, changed, deleted, renamed) and
  `Resolve` (any name git knows to a full hash; a name that starts with `-` is refused).
- **Going back**: `ReadAt(folder, commit)` reads the project as a commit holds it from a worktree
  of its own in the temporary folder (so Git LFS's files are real files), and removes it again;
  the project's own files and index are never touched. The editor makes that document one step
  of undo, so nothing is lost.

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
- **The editor named** (`EditorStamp`, user story M-9): with a stamp (`MapExporter.ToBytes`/`WriteFolder`
  take one; `EditedMap.Export` and `ExportAsync` always pass `EditorStamp.Now()`), `_scenario.lua` starts
  with `-- Made with the FAF Map Editor <version> (<commit>) on <date>` and `ScenarioInfo` holds
  `map_editor = { name, version, commit, project_format, exported }`. Without one the bytes are the same
  every time (the tests' comparisons). The version and commit come from the build (`EditorBuild`: the
  informational version, `1.0.0+<sha>`, which the SDK makes from git; a build without `.git` knows no
  commit and writes `commit unknown`). Checked 2026-10-10: the game (`lua/ui/maputil.lua`,
  `LoadScenarioInfoFile` runs the file and reads named keys; nothing walks ScenarioInfo's keys; the lobby
  sends the file's path, not the table), the FAF client (`MapService.readMap`, luaj, named keys) and the
  vault's upload check (faf-java-api `MapService.validateScenarioLua`, luaj, named keys; `updateLuaFiles`
  rewrites only lines holding `/maps/<folder>`, so keep that path out of the stamp) all ignore the extra
  key and the comment. The importer leaves `map_editor` out.
- Not kept at all: the platoons, orders and platoon builders of co-op saves, and the `orders` and
  `platoon` of unit groups and units (empty in every vault map but six groups of one co-op mission);
  the export writes them empty, as the map editors do.

A `-0` in a marker's orientation becomes `0` on import: they turn a marker the same, and the
project leaves an orientation of zero out.

## What the game ignores and what it derives

How the engine treats what the export writes, from Draiget/faf-re at commit 2ba55b2 (the Map
library's guide, "What the game does with the data", has each source; the main one is
`src/sdk/moho/sim/CWldMap.cpp`, `CWldTerrainRes::Load` and `Finalize`). What it means for the editor:

- **The normal map is not read by the game.** The engine skips the stored tiles and makes the normal
  map from the heightmap in tiles of at most 2048 (`InitNormalMap`, `UpdateNormalMap`). The editor's
  normal map and its generator (`NormalMapGenerator`, "Generated results") serve the editor's
  pictures and other tools (the FAF map editor reads it); keep them, but do not sell them as changing
  the game.
- **A map stored in tiles** (larger than 2048: the game's 4096 maps store four tiles of 2048) is
  imported as one image, the tiles put in place row by row (`NormalMapTiles.Stitch`). Keeping the
  pixels is the simplest faithful choice: no data is lost, the tools that read the normal map see what
  the map had, and nothing about it matters to the game. Tiles that do not cover the map (their count
  against the tile size and the map's size) are refused with a message. The tile size is kept
  (`Terrain.NormalMapTileWidth`/`Height`, `map.json`'s optional `terrain.normalMapTileWidth`/`Height`)
  only when it is not the engine's (`min(size, 2048)`): one vault map of version 53 and 4096 ogrids,
  `SCMP_haz09fa`, stores one tile of 4096. The export splits the image into the kept tiles, else into
  the engine's (`NormalMapTiles.Split`), each tile the image's share of the grid (a normal map smaller
  than the map gives smaller tiles; one that does not divide into the grid is written whole). A
  generated normal map, a resize and a normal map PNG changed outside the editor drop the kept size.
  Checked on the game's three 4096 maps and `SCMP_haz09fa`: import and export give the same tiles and
  sizes (mean error under 0.07 per channel from the DXT5).
- **The water map texture is rewritten by the game** from the heightmap, the water's elevations and
  the foam and flatness masks (`RebuildWaterMapRect`): the project's water map PNG has no effect in the
  game. The foam and flatness masks (the Water mode's `foam` and `flatness` stacks) matter; the depth
  bias mask is read and saved by the engine but nothing in faf-re uses it (INFERRED unused).
- **Props' scale is ignored by the game** (`CWldProps::Load` reads it into nothing), and its rotation
  becomes a normalised quaternion: skew or scale in the axes is lost too. The editor's prop scaling
  (`PropEdits`, `PropPlacement`, `ObjectEdits.ScaleObjects` on props, the Props mode's random scale)
  is written and shown in the editor, not in the game.
- **Decals**: the position is a corner, the footprint running from it along the scale's x and z, turned
  only by the rotation around y (`ProjectDecalBoundsXZ`). The cut-offs: with a near cut-off above 0 the
  decal fades in from 0.75 of it and is unseen closer (the far cut-off then fades nothing); otherwise it
  fades out from 0.75 of the far cut-off. The engine's own default for a new decal is the diagonal of
  its footprint's bounds (`ComputeCutoffLOD`); the editor's new decals take 500 (`DecalEdits.DefaultCutOffLod`),
  a candidate to change. A decal has exactly two texture slots in the engine: the export writes a
  missing second slot as empty, and the writer refuses more than two.
- **Decal counters** (`Scmap.NextDecalId`, `NextDecalGroupId`): the ids the engine gives decals and
  groups made at run time; the export writes one more than the highest id in use.
- **The strategic view** (`Minimap`): `ContourInterval` is the number of contour bands (1 to 100, 0
  counts as 1), `ImagerElevationOffset` lifts the map imager's borders (stored under the key `unknown`
  in `map.json`, the name it had before its meaning was known; renaming the key would change every
  project). The meaning of `ContourColor` is uncertain (the Map library's guide).
- **The scenario** (`WLD_LoadScenarioInfo` in `src/sdk/moho/sim/CWldSession.cpp`, `CLobby::LaunchGame`
  in `src/sdk/moho/net/CLobby.cpp`, `src/sdk/moho/sim/CArmyImpl.cpp`): the file runs after
  `/lua/dataInit.lua` and only its global `ScenarioInfo` is kept; the launch needs
  `Configurations.standard.teams` and uses only the team named `FFA` (any case), whose `armies` are the
  start slots in order (more players than armies fails); `customprops.ExtraArmies` (space-separated) adds
  civilian armies unless the `CivilianAlliance` option is `none`; `ScenarioInfo.map` is the `.scmap` the
  session loads. The sim reads `norushradius` (default 100 when absent), `norushoffsetX_<army>` and
  `norushoffsetY_<army>` (both or the offset is 0, 0) and `pathcap_land`, `pathcap_sea`, `pathcap_both`
  (default by the larger side: under 512 ogrids 500, under 1024 1000, under 2048 2000, under 4096 10000,
  else 20000). The editor's export writes the `FFA` team and `ExtraArmies` as the engine needs them.
  Open: a scenario without `norushradius` imports as 0 (`ScenarioInfo.NoRushRadius` is not nullable)
  and exports `norushradius = 0`, where the game would have used 100.

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
`All` take to leave hidden kinds out. A blank marker (an `AiMarker` of type `AiMarker.BlankType`, `IsBlank`)
is a kind of its own, `MarkerKinds.Blank`, and not among `AiMarker.KnownTypes`. `RemoveKind(kinds, out removed)` (the Markers mode's Clear)
removes every marker of some kinds as `RemoveMarker` would one by one: spawns go through it, the
highest number first, so each takes its playable army (one with units stays as an extra army) and no
army is renamed on the way; the other kinds go in one pass (links and chain entries to them go, an
emptied chain stays; tested equal to the one-by-one result). `CountKind` and `CountLinks` (a link
both ways once) count what the Markers panel shows.

**Generated path networks** (user story R-3, `Generation/PathNetworks`, `PathSkeleton`,
`PathNetworkEdits`; `PathNetworkGenerator`, a plain `IGenerator` with walkability's inputs and
`GeneratedInput.PathNodeSettings`): per layer the AI's graph from the navigational mesh, kept in
memory (a preview) and written into the markers only by the export (`Apply` on the version it
writes). The settings and the edits by hand are the map's (`ProceduralSettings.PathNodes`,
`PathNodeSettings`; `map.json`'s optional `procedural.pathNodes`, `PathNodesDto`/`PathEditDto`).
- **Ground layers** (`GenerateLayer`): the labels in blocks of about a twelfth of the spacing (a
  block takes the label most of its passable ogrids have; `DefaultSpacing`: a sixteenth of the map,
  16 to 64); each region thinned to its skeleton (`PathSkeleton.Thin`: distance-ordered homotopic
  thinning with Yokoi's connectivity number, ends of lines kept; `Prune` drops spurs shorter than
  half the spacing; `Trace` gives junctions, dead ends and chains); nodes at junctions and dead
  ends and along each chain about a spacing apart, linked one to the next (a link that cannot be
  walked straight gets a node at the chain's middle cell, and at last a route, `Route`: A* in a box
  that grows); with `Fill` a node on a grid of the spacing wherever open ground lies further than
  three quarters of a spacing from every node of its region; links between nodes within 1.5
  spacings that see each other both ways (`CanWalk`) and have no node in the circle over them (a
  Gabriel graph; the chains' links always stay); `Smoothing` rounds move every node but dead ends
  halfway to its neighbours' middle when it stays in its region and its links stay walkable; nodes
  closer than `MinimumDistance` (0.4 spacing) merge (dead ends, then junctions stay); links made
  again; every region joined into one network. A region under a sixteenth of a spacing squared gets
  no nodes; one the blocks lost gets a node near its middle. Layers run side by side. Measured: a
  4096 map (SCMP_haz09fa, spacing 64) 0.7 s for all four layers (the mesh itself 2.3 s); Archsimkats
  Valley 0.4 s; HardFFA 60 ms.
- **Air** (`GenerateAir`): a grid over the playable area, 16 by 16 unless `AirSpacing`, each node
  linked to the eight around it, as the game's maps have (256 air nodes on maps of every size).
- **Deterministic**: the same map and settings give the same nodes (`GivesTheSameNodesForTheSameMap`).
- **Edits by hand** (`PathNetworkEdits`, `PathEdit`: `Add`, `Move`, `Remove`, `Link`, `Cut`;
  `PathNodeRef`): an edit names a generated node by the place it was generated at, a node added by
  hand by its number. After every generation each remembered place is matched to the nearest
  generated node of its layer within a third of the spacing (at least 2 ogrids), nearest pairs first,
  each node once; the matched node then carries the remembered reference. Where the terrain did not
  change, nodes come back exactly; where it changed a little, they move a little and are still found.
  An edit whose node is gone: a removal does nothing, a move places a node of its own at its target,
  a link or cut with a lost end does nothing; such edits stay (they may match again) and are listed
  as unmatched (`ForgetUnmatched`). Order: removals, moves, added nodes (each linked to up to three
  of the nearest nodes it sees within 1.5 spacings, at least the nearest it sees), cuts, links.
  Making edits: `AddNode`, `MoveNode` (one move per node; an added node's own edit changes),
  `RemoveNode` (drops the edits that refer to it), `Link` (a cut and a link of one pair cancel), `Clear`.
- **Into the map** (`Apply(document, network, replace)`): names as the map editors do (`LandPN`,
  `WaterPN`, `AmphPN`, `AirPN`, going on from the highest number), on the terrain, linked both ways.
  The map's own path nodes of a layer (placed by hand; generated by an editor before wave 9, which
  stay ordinary markers) stay, and each piece of them is linked to the nearest generated node it
  sees; with `replace` they go, out of chains too.
- Tests: `PathNetworksTest` (no link crosses blocked ground and every region is one network on the
  test maps, the wall's gap, smoothing, the minimum, the air grid, `Apply`), `PathNodeEditsTest`
  (skeletons of test grids, edits surviving regeneration after the terrain changed, the format).

**Weather** (`Editing/WeatherStyles`, static data of `lua/weatherdefinitions.lua` and
`lua/MarkerTemplates.lua`): the map styles, each style's cloud types (its own and the cirrus layers)
and new definitions and generators as the game's templates make them.

**Markers presets** (F-10, `Workspaces/MarkerPresets`, kind `markers`): the chosen markers as a
`MarkerSetDto` (what `markers.json` holds, links and chains by name) around their middle, heights
above the terrain; the army deposits wait for is left out. `Place` pastes them through
`ObjectEdits.Paste` (free names, a spawn the next army, links among them) and makes their chains again.

**Adaptive maps' optional groups** (R-5, `Editing/AdaptiveGroups`): the game has no field for
which civilians exist by players or options. FAF's adaptive maps (the script by CookieNoob and
KeyBlue, e.g. `adaptive_canis_river`, `dualgap_adaptive`) keep them in groups directly under an
army's units beside `INITIAL`, named `Optional_<option>_<n>`, and the map's script creates them with
`ScenarioUtils.CreateArmyGroup` when the lobby option `optional_<option>` (the map's `_options.lua`)
has value n or more (or as wrecks, by option). `Find` lists them, `AddOptionalGroup` makes one in
the civilians' army (`CivilianArmy`: `ARMY_17`, else `NEUTRAL_CIVILIAN`, else the first extra army).
The export writes the options, tables and script of that convention for a map whose script is the
editor's (section "Content to use again", `AdaptiveScript`), with civilians by the number of players
(`Optional_Players_<n>`) besides.

**Wrecks** (U-5, `Editing/UnitWrecks`): the game has no field for a wreck. A map's `_save.lua`
holds wrecks as ordinary units (the same five fields) in a group named exactly `WRECKAGE` of an
army, and `ScenarioUtils.InitializeArmies` (`lua/sim/ScenarioUtilities.lua` in
[FAForever/fa](https://github.com/FAForever/fa/blob/develop/lua/sim/ScenarioUtilities.lua)) finds
it (`FindUnitGroup`: by name, case-sensitive, at any depth, the first it meets), creates its units
and turns each into its wreck (`CreateWreckageUnit`, `unit:CreateWreckageProp(0)`), even with the
civilians turned off; no script line is needed. 34 maps of the vault keep wrecks so, nearly all in
an extra army (`ARMY_9`, `NEUTRAL_CIVILIAN`, `ARMY_17`) with the default script (`abhor.v0004`,
`sweepwing_sanctum.v0003`, `setons_clutch_-_faf_version.v0004`, the test map Theta Passage). An army's
groups exist only when the army is in the game, so wrecks of a playable army show only when a player
takes the slot (the check `wrecks-in-playable-army`, a warning). The FAF map editor reads a wreck
from the group's name the same way and writes nothing more. So the document needs no field: a unit
is a wreck when it lies in a `WRECKAGE` group (`IsWreck`, `Wrecks`), `SetWrecks` moves units into
their army's `WRECKAGE` (the one the game finds, `WreckagePath`, else a new one under the root) or
back into `INITIAL`, and import and export carry the group as any other (`UnitWrecksTest`: the same
game content after import and export of vault maps with wrecks). Groups that adaptive maps' scripts
create as wrecks by option (`Optional_Wreckage_<n>`, `CreateArmyGroup(army, group, true)`) are
ordinary groups to the editor: only the script decides.

## The vault: rules, zip, upload and sign-in

User story M-8 (`Vault/`). How FAForever takes a map, from the source of the
[FAF client](https://github.com/FAForever/downlords-faf-client) (`MapUploadTask`,
`FafApiAccessor.uploadMap`) and the [API](https://github.com/FAForever/faf-java-api)
(`map/MapsController.java`, `map/MapService.java`, `error/ErrorCode.java`; read 2026-10):

- **`POST https://api.faforever.com/maps/upload`**, `multipart/form-data`, a bearer token of a person
  signed in with the scope `upload_map` (`@PreAuthorize("hasScope('upload_map')")`). Parts: `file`,
  the zip (its file name must end in `.zip`), and `metadata`, JSON `{"isRanked": false}` with an
  optional `licenseId` (the vault's default licence without one; a licence less permissive than the
  map's current one is refused). The answer is 200 with no body; a refusal is 422 with
  `{"errors": [{"status", "title", "detail", "code": "107", "meta": {"args": [...]}}]}` (the code is
  `ErrorCode`'s number as text), 401 without a valid token, 403 for a vault ban. At most 350 MB
  (`spring.servlet.multipart.max-file-size`); zip bombs are refused.
- **The zip** (`VaultArchive`): exactly one folder at the root, holding files ending in
  `_scenario.lua`, `.scmap`, `_save.lua` and `_script.lua` (an adaptive map also `_options.lua` and
  `_tables.lua`). The FAF client zips the map folder itself; the editor names the folder in lower
  case. The API renames the folder to the display name in lower case with spaces as underscores and
  the version (`theta_passage.v0004`), rewrites `/maps/<old folder>` in every `.lua` file, renders
  the previews itself and zips it again.
- **The scenario** must have `name`, `description`, `type`, `size`, `map_version` 1 to 9999, a
  no-rush radius and a first team `FFA`; `map`, `save` and `script` must be
  `/maps/<folder name>(.vNNNN)?/<folder name><ending>` (case ignored), the folder name made of the
  display name as above. The name: letters a to z, digits, spaces and minus signs, 4 to 50
  characters, at most 3 minus signs, starting with a letter. The version must not exist yet (107);
  a name another author has is refused (106). `VaultRules.Check` checks what the map can tell
  before an upload (`VaultRules.FolderName`); ownership and versions only the server knows.
- **`MapUploadClient.UploadAsync`** sends it with progress and throws `VaultUploadException` with the
  parsed errors; `IsRealVault` tells FAForever's own hosts, which the editor refuses without a person.
- **Signing in** (`FafSignIn`, RFC 8252): Hydra's page in the system's browser, a redirect to a
  listener on `http://127.0.0.1:<free port>/`, the code exchanged with the PKCE verifier (S256) at
  `https://hydra.faforever.com/oauth2/token` (no CORS needed outside a browser). The client is the
  official FAF client's (`2e8808cf-...`, the repository's development client): in
  [gitops-stack](https://github.com/FAForever/gitops-stack/blob/main/apps/ory-hydra/values.yaml) it has
  `upload_map`, `redirectUri: http://127.0.0.1` (any port) and no secret. The Viewer's production
  client lacks `upload_map` (TODO.md). The FAF client itself now signs in with the device flow.
- Tests (`VaultUploadTest`, `FakeVaultServer`): the zip, the rules, the multipart request's parts
  and headers, the errors read back, and a sign-in against a fake token endpoint. Never the real vault.

## Images and textures

- ImageMagick (Magick.NET, Q16) reads and writes the PNGs (`ProjectImages`) and compresses the
  DXT5 textures; it reads what map makers' tools save (8 or 16 bits, grey or colour). It is native
  code, so this library is desktop only: never reference it from the Viewer.
- `DdsTexture` reads and writes the DDS files of the `.scmap`: uncompressed A8R8G8B8 or DXT5, no
  mipmaps (every texture in 100 vault maps), with the FAF map editor's header; `WriteMipmapped`
  writes the map-wide decals' DXT5 and DXT1 with their mipmaps (below, "Decals over the whole map"). Uncompressed
  textures survive unpacking and packing pixel for pixel; DXT5 is decoded once on import and
  compressed again on export (a mean error of about 0.02 per channel).
- ImageMagick never opens or writes a file itself: its file access fails on paths longer than
  260 characters. Read and write the bytes with .NET and hand them over.
- Each stratum mask texture holds four layers, one per channel (red, green, blue, alpha): layer 1
  to 4 in the first, 5 to 8 in the second. A project keeps one grey PNG per layer.

## The workspace and the game's assets

One workspace per computer (`Workspaces/`): a folder with the map projects and the assets converted
from the game's archives (`WorkspaceLayout` lists its files). `workspace.json` (`WorkspaceDto`) says
where the game, FAF's data and the vault are (maps are exported to the vault's `maps` folder), and
the Procedural defaults of new and imported maps (`procedural`, section "Generated results").
`UserSettings` remembers where the workspace is, outside it (`%APPDATA%\FAF Map Editor\settings.json`;
also the layout, the plugins turned off and whether results regenerate on their own, `generation`;
the environment variable `FAF_MAP_EDITOR_SETTINGS` names another file, which tests and automation
use so that the user's own stays alone). `FafInstallation` reads FAF's `fa_path.lua` for the defaults.

- `GameArchives` lays the archives over each other as the game mounts them: per name (`env`,
  `units`, and `textures` when the game has it) the game's `.scd`, then FAF's `.nx2`, which wins.
  `props.scd` holds only placeholders. Paths are compared in lower case. Of `textures` only
  `GameArchives.TextureFolders` is converted (`IsConverted`): `textures/environment` (the sky's
  cube maps and backgrounds, the cube maps units reflect), `textures/engine` (the water's ramps,
  waves and cube map) and `textures/ui/common/icons/units` (the units' icons and the pictures of
  their layers behind them), about 900 files and 76 MB.
- **Cube maps** (`CubeMaps`): a DDS cube map (`DDSCAPS2_CUBEMAP`) becomes one PNG with its six faces
  side by side, square each, in the order +x, -x, +y, -y, +z, -z (a strip six times as wide as
  high), each face as the DDS holds it; `CubeMaps.Decode` reads a strip, a DDS cube map (a map's
  own) or a plain picture (the same on every side) into a strip, and `Face` cuts one out.
  `GameTextures.FindCube` finds the cube map a map names as `Find` finds a texture, with its own key.
- `AssetConverter` turns them into `assets/`, keeping the game's paths in lower case: textures as
  8-bit PNG with alpha, prop and unit blueprints as the editor's JSON (`PropBlueprintDto`,
  `UnitBlueprintDto`, read with FileFormats.Blueprints by `AssetBlueprints`), meshes as glTF
  (`.glb`, by `GlbWriter` of FileFormats.Mesh: the triangles, the skeleton as a skin, tangents in
  glTF's convention, no material; the mesh library's guide,
  [`../FAForever.FileFormats.Mesh/AGENTS.md`](../FAForever.FileFormats.Mesh/AGENTS.md), says how
  the engine reads a mesh); scripts, animations and the rest are left out. The levels of detail
  get the names the engine fills in (`<name>_lod<n>.scm`, `_albedo.dds`, `_normalsts.dds`,
  `_specteam.dds`); a name is relative to the blueprint's folder and may step out of it (the tree
  groups use `../Brch01_s1_albedo.dds`), and a file the archives lack is left out. A level without
  `LODCutoff` keeps no cutoff in the JSON; the 3D view takes the engine's default, 1000. A file
  that cannot be converted is named in `assets/skipped.txt`. It writes into `assets.new/` and
  replaces `assets/` when done; a stopped conversion goes on where it was. For game version 3839
  (FAF of October 2026): 32 s, 3 GB, 7044 textures, 2188 meshes (every mesh of the game and FAF,
  also the four whose vertex section is named `VRTX`), 563 props, 606 units, nothing skipped.
- `AssetCheck` tells whether the assets are current: converted by this editor version (and
  `AssetConverter.FormatVersion`, raise it when the output changes) from the archives as they are.
  Only FAF's `.nx2` are checked; same size and time is unchanged, otherwise the SHA-256 decides.
- `Workspace.FindAsset("/env/Evergreen/layers/Eg_Grass001_albedo.dds")` gives the converted file.
- **Case.** The game's file system ignores case, and maps name their files in any case
  (`/env/Geothermal/layers/Geo_Ash...`). The converted assets are in lower case and every lookup into
  them lowers the path first (`WorkspaceLayout.AssetOf`, `AssetCatalog.KeyOf`, `GameTextures`), and a
  map's own files are compared ignoring case, so they resolve on Linux too: `faf-map dev resolve
  <map folders...> --workspace <folder>` (`AssetResolution`) resolves every texture, decal, cube map,
  prop and unit of a map down to its file and lists what does not (Archsimkats Valley, Seton's
  Clutch, Kaali and HardFFA: all 311 resolve on Linux, 85 of them named in mixed case). Files outside
  the assets are found in any case with `FilePaths.Existing`/`FindFile`/`InFolder` (only off
  Windows does it look further than the path as written): the game's `gamedata/*.scd` and FAF's
  `.nx2`, a map's `.scmap`, scenario and save (`FilePaths.IgnoringCase` for patterns), presets by
  name (another case is the same preset, as on Windows), a plugin's `plugin.lua`/`plugin.json`, and
  the images a project names relative to its folder (either separator). File names made from what a
  person types keep to what Windows allows on every system (`FileNames.Clean`). The user's folders
  (`UserFolders`: Documents, else home; `~/.config`, `~/.local/share`) never give a relative path,
  and FAF's folder is `~/.faforever` off Windows.
- `AssetCatalog.Load(assets)` lists what a map maker chooses from (user stories F-8, F-9): the
  props and units from their converted blueprints, the decals (`decals/`, `splats/`) and the
  stratum textures (`layers/`, albedo and normal map as one) from their PNGs, and the cube maps
  (`AssetKind.Cubemap`, L-1: the strips of `textures/environment` and `textures/engine`; the
  folder is the group, the category `sky`, `reflection`, `water` or `other` by the name). A unit
  has the game's icon (`AssetEntry.Icon`, U-1: `textures/ui/common/icons/units/<id>_icon`) and the
  picture of its layer behind it (`IconBackground`: `<General.Icon>_up`, land when the blueprint
  names none of land, air, sea and amph, as `GetBackgroundTextures` of `lua/ui/game/construction.lua`;
  the converted blueprint keeps it as `buildIcon`).
- **Reading one asset again** (F-8): `catalog.Reload(key, assetsFolder)` reads one asset's files
  again by the reader of its kind (over its folder) and answers a new catalogue with it replaced
  (left out when its files are gone) and the asset as it is now; the new catalogue names the one it
  was made from (`ReloadedFrom`) and the keys read again (`ReloadedKeys`), so what keeps something
  per asset drops only those. Each `AssetEntry`
  has its kind, the keys a map names it by (`AssetCatalog.KeyOf`: the game path as
  `GameArchives.Normalize` writes it, `.dds`; a unit's id), its group (the biome of `env/`, or a
  unit's faction), its category (a prop's folder, a unit's layer, ...), its family (a prop without
  `_v1`, `_s2`: the variants share a tile) and its facts; a prop its reclaim (`ReclaimMass`,
  `ReclaimEnergy`), a unit its build cost (`MassCost`, `EnergyCost`, from the converted blueprint:
  `uab0301` 5220 and 47400), and both as `Mass` and `Energy`. `AssetUse.Count(document)` counts what a
  map uses by those keys, and `AssetUse.Biomes` gives the biomes of that, most used first.
- **Where an asset comes from** (F-8, `AssetOrigins`): `Read(workspace)` reads the lists of files
  (never their content) of the archives the manifest names, laid over each other as the converter
  did, and `Of(entry)` answers an `AssetSource`: the game's (`env.scd`), FAF's (`env.nx2`, with
  `ReplacesGame` when the game has the file too), the workspace's own (brushes) or the map's own
  (`/maps/...`); `Unknown` when the archives moved. FAF's `.nx2` hold every blueprint, so every
  prop and unit is FAF's by its blueprint; textures and decals are mostly the game's.
- **Missing assets** (F-8, `MissingAssets.Find(document, catalog)`): what a map uses that neither
  the catalogue nor the map's own files have, with how often and the check that finds it too
  (`missing-texture`, `missing-model`), most used first. The cube maps a map names (`CubemapsOf`:
  the sky's, those units reflect, the water's) count only when the catalogue has cube maps at all
  (`ChecksCubemaps`): a workspace converted before them knows none, and nothing is missing then;
  `missing-texture` finds them under the same rule.
- A prop's or unit's picture is drawn from its most detailed mesh (`AssetEntry.Model`: the
  `.glb`, the albedo, normal and specular maps and the shader of its first level of detail) by
  `AssetPreview.Render`, with `MeshPreview` of FileFormats.Mesh: on the processor, from above and
  in front, shaded as the game's `effects/mesh.fx` (FAF's, in `effects.nx2`) shades it. The
  shader's name, resolved as the engine resolves it (`MeshPreview.ShaderName`: no name is `Unit`,
  `TMeshAlpha` is `NormalMappedAlpha`, `Simple` and `Team` are `Unit`, ...), chooses the formula
  (`MeshPreview.ShaderOf`: the factions' PBR shaders with their team colour, normal-mapped props,
  vertex-normal props) and the alpha test (`AlphaRef` 0x80 for the `...Alpha` shaders of trees
  and bushes, 0x23 for `VertexNormal`). The light is the game
  editor's default, the environment a gradient fitted to `DefaultEnvCube.dds`, the army blue;
  there are no shadows. The textures are read at most twice the picture's width. A unit takes
  about 50 to 300 ms at 160 pixels, most of it reading the three textures.
- `GameTextures.Find(path, document, workspace)` finds a texture a map names as the game does: a
  path into `/maps/<any folder>/` is one of the map's own files (`MapDocument.Files`), any other
  one of the workspace's assets. Its `Key` is the same for every spelling of the path and its
  `Version` changes with the file, so a view can keep one copy per texture and reload it.
- `faf-map workspace init|convert|status` does the same without the editor. `faf-map workspace scratch
  <folder> --from <workspace> [--settings <file>]` makes a scratch workspace for test editors
  (`ScratchWorkspace`: its own projects, presets, copied brushes and a vault inside it, the other
  workspace's assets through a directory junction on Windows, a symbolic link elsewhere, a copy
  when neither works); `remove-scratch` deletes it, the junction first, since .NET does not delete
  a junction inside a recursive delete. Converting assets in a scratch workspace would replace the
  linked ones; `--convert-into <assets folder>` links a folder of its own instead and converts into
  it (`ScratchWorkspace.Create(folder, source, assetsInto)`), never the source's own. `init
  --settings <file>` remembers the workspace in that file rather than the user's.
- **The user's settings** (`UserSettings`) are read part by part (workspace, telemetry, layout,
  plugins): a part with a value of the wrong type is left out and named in `Problem` (the editor
  logs it and says so in the status line), the rest is read; a file that is not JSON at all reads
  as empty and is never written over (`MapProjectException`), so a typo does not lose the user's
  workspace.
- **The sandbox** (`Workspaces/SandboxMap`, user story F-5): `projects/Sandbox`, a map of 256
  ogrids of version 60 with the game's default skybox, two armies and one of every kind of thing (spawns, mass, hydrocarbon, three land path
  nodes with links, an AI marker, an area, a chain, a decal in a decal group, props, civilian units
  in the group `INITIAL` of `NEUTRAL_CIVILIAN`), for trying the editor out. `SandboxMap.Build(catalog)`
  takes the stratum textures of one biome (evergreen first), a few of its props and decals and a few
  units from the workspace's converted assets, by their game paths; nothing of the game is copied.
  An empty catalogue gives the plain sandbox (terrain, water, markers, area, chain). `SandboxMap.Ensure`
  writes it when it is missing: `Workspace.Create` and the end of `AssetConverter.Convert` call it,
  and the editor before it opens it. A plain sandbox nobody changed is written again once there are
  assets (the hidden `.sandbox` holds a hash of the project's files); a saved one is kept.
- **Publishable**: `ScenarioInfo.Publishable` (in `map.json`'s scenario as `publishable: false`,
  left out while true, so older projects read as publishable) and `MapDocument.IsPublishable`. The
  sandbox is not; the upload to the vault (M-8) refuses such a map, while the export to the
  game's maps folder works for every map. The game's files do not carry it.
- **Presets** (user story F-10): `presets/<kind>/<name>.json` (`Workspace.Presets`, a
  `PresetLibrary`), a folder per kind named in the plural (`PresetKinds`: `brushes`, `stamps`,
  `lighting`, `water`, `sky`, `props`, `units`, `markers`, `selections`, and `maps`, the map templates
  of `MapTemplates`, which are no preset files; a kind is lower-case letters, digits and dashes) and
  a file per preset (`PresetDto`: format version,
  kind, name, settings). The settings are a JSON object whose shape the feature of the kind owns:
  `Preset.Of(kind, name, record)` writes a record of its own as the editor writes JSON, and
  `preset.Read<T>()` reads it back. The file name is the name with the characters a file name
  cannot hold made dashes; the name itself is in the file. The library keeps nothing in memory:
  every call reads the folder, so presets other tools or editors write show at once; `List` leaves
  out a file it cannot read and names it. An old kind name (`brush`, `props-palette`, `selection`,
  `templates`: `PresetKinds.Renamed`) names its new folder (`PresetKinds.Of`, `FolderOf`), and
  `PresetFolders.Move(workspace)` moves the old folders' files into the new ones (a name taken gets
  a number), brush presets whose tool is the stamp into `stamps`, and `templates/` into
  `presets/maps`, deleting what it emptied; the editor runs it when a workspace opens, so it happens
  once. A stamp's picture (`brushes/stamps/*.png`) is a brush, never moved. `AtmospherePresets.OfSky`
  and `ReadSky` keep the sky (`SkyDto`).

## New maps, heightmaps, strata and areas

- `NewMap.Create(NewMapSettings)` (user story M-2) makes a version 60 map from scratch
  (`NewMap.ScmapVersion`; `TenLayersFrom`, 56, is the first version with ten terrain layers) with the
  skybox the game gives a map without one (`Skyboxes.GameDefault`, below), as the FAF map editor's new map: one of the game's sizes (`NewMap.Sizes`, 256 to 4096 ogrids), flat at
  a height, water with its deep and abyss elevations (5 and 15 below unless given), the strata
  textures of a biome (`NewMap.Biomes`, static data: lower layer, four strata, the macro texture
  as upper layer), spawns on a circle, `AREA_1` over the whole map, the extra armies
  `ARMY_17` and `NEUTRAL_CIVILIAN`, the skirmish script (`NewMap.DefaultScript`), the FAF editor's
  default lighting and water, the water reflecting the sky cube map of its biome
  (`NewMapBiome.WaterCubemap`: `skycube_desert01a` for the desert). Masks and the water's images are half the map's size, as in the
  vault. It exports and imports again unchanged (`NewMapTest`). `EditedMap.New` opens it as a
  map that is no project yet and counts as changed.
- `HeightmapFiles` (T-3): reads a heightmap of any size (PNG and what ImageMagick reads, as 16-bit
  grey; `.r16`/`.raw` as a square of little-endian samples), resamples it bilinearly to the map's
  (width + 1) by (height + 1) samples (`Import`), and writes the map's as PNG or `.r16`.
- `StrataEdits` (S-2): the layer count is the version's (6 or 10); `AddStratum` fills the first
  unused stratum (empty albedo), `RemoveStratum` takes one out and moves the ones above down with
  their masks, `MoveStratum` moves one with its mask.
- `AreaEdits` (M-5): an area's eight handles, which one a place hits (`HandleAt`), and what
  dragging it does (`Drag`, kept in order).
- `AtmospherePresets` (L-2, W-1): a lighting as a preset (its `LightingDto`), and the look of the
  water (`WaterPresetSettings`; the elevations, whether it has water, the wave generators and the
  masks stay the map's).

## Content to use again

User stories F-10, T-2, M-2, F-9, T-7 and R-5.

- **Selection presets** (F-10, `Workspaces/SelectionPresets`, kind `selections`): `Capture(document,
  things, name)` keeps props, decals, markers (`MarkerPresets.Capture` around the same middle) and units
  (`CaptureUnits` around it) as a project writes them (`SelectionPresetSettings`: `PropDto`s, `DecalDto`s,
  a `MarkerSetDto`, a `UnitGroupDto`), around their middle rounded to a whole ogrid, each height above
  the terrain; areas are left out (a map's layout, not a piece placed again). `Place(document, settings,
  name, army, parent, x, z, radians, out added)`: props and decals inside the map, markers by
  `MarkerPresets.Place` (kept inside it), units by `PlaceUnits` as a new group named after the preset
  (the groups to it made when missing), then everything turned about the place (`RotateObjectsAbout`;
  deposits back on ogrid centres). `Summary()` says what it holds.
- **Height stamps** (T-2, `Editing/HeightStamp`): `HeightStamp.Apply` presses a `BrushShape` at once,
  turned (and flipped for a mirrored image) as a brush's shape: raise or lower by value × height, or
  set to base + value × height; the outer `Edge` of the picture fades (`BrushShape.Border`), and the
  picture keeps its edge's value to its side (`BrushShape.Sample(u, v, clamp: true)`).
  `HeightStampCapture.Of(heights, x0, z0, x1, z1)` takes a part of the heightmap stretched from its
  lowest (0) to its highest (65535) sample, `Save` writes it as a 16-bit grey PNG: pressed with mode Set,
  base the lowest, height the difference and a radius of half its side, it puts the part back sample
  for sample (`HeightStampTest`).
- **Map templates** (M-2, `Workspaces/MapTemplates`, the workspace's `presets/maps/`,
  `Workspace.TemplatesFolder`): a JSON file of a new map's settings (`MapTemplateDto`: size, height,
  water and its elevations, biome or its own strata, players, extra armies, lighting; each optional),
  a map project in a folder, and the sandbox (`MapTemplateKind.Sandbox`, built from the catalogue).
  `List` reads the folder (an unreadable one named, the sandbox last), `Apply(dto, defaults)` gives
  `NewMapSettings` (with the new optional `Layers`, `ExtraArmies`, `Lighting`: a template's strata are
  laid out as a version 56 map holds them, the lower first and the upper last), `Copy` makes a new map
  of a whole-map template (renamed, version 1, publishable), `SaveSettings` (the mean height, the
  nearest of the game's sizes) and `SaveProject` write them.
- **Stars and recents** (F-9, `Workspaces/AssetFavourites`, the workspace's `favourites.json`,
  `AssetFavouritesDto`): the keys starred in the order they were starred and the 24 chosen last, the
  newest first; every change is written at once, nothing before a workspace opens it.
- **Walkability marked by hand** (T-7, `Editing/WalkabilityMarks`, `MapDocument.WalkabilityMarks`, an
  optional grid of the map's size: none 0, blocked 1, open 2; `PaintTarget.WalkabilityMarks` makes it
  when first painted). The game decides by the heights, the water and the terrain types, reading an
  ogrid's type at its far corner; so `Walkability.Generate` hands `NavGenerator` the terrain types as
  the export writes them (`WalkabilityMarks.TerrainTypes`: a blocked mark puts the blocking `Dirt09`
  there, an open one takes a blocking type away) and the ogrids marked open as `forcedOpen` (FileFormats.Map's
  `NavGenerator.Generate`, an optional argument: flat and open, the water rules still apply). The
  export writes those types (`MapExporter`), so the game blocks what is marked blocked; no file makes
  steep ground walkable, and the check `walkability-marks` warns of ground marked open that the game
  blocks (kept per version of the marks, heights and water). The walkability and path networks
  generators take the marks as an input (`GeneratedInput.WalkabilityMarks`). On disk
  `walkability-marks.png` (black none, grey blocked, white open; absent without marks), read again
  when another tool changes it; `MapResize` keeps the marks at their place.
- **Adaptive maps' files** (R-5, `AdaptiveScript`, after `adaptive_archsimkats_valley`'s files): for a
  map that adapts (`Adapts`: the scenario says so, a deposit waits for an army, or an army has
  optional groups) whose script is none, the new map's or one the export wrote before (first line
  `AdaptiveScript.Header`; `Writes`), the export writes `<file>_options.lua` (`dynamic_spawn` as the
  convention's, and an option `optional_<option>` per option of the groups, 1 none up to its highest
  value), `<file>_tables.lua` (`maxPlayerOnMap`, `playableArmies`, `spwnMexArmy` and `spwnHydroArmy`: a
  row per playable army of the numbers after `Mass `/`Hydrocarbon ` in its deposits' names, as text)
  and the script: the armies, then the groups up to each option's value (as wrecks when the option's
  name holds `Wreck`), `Optional_Players_<n>` when n or more playable armies are in the game, and
  `ScenarioUtils.CreateResources` replaced by the convention's dynamic spawn (mirror slots, used slots,
  or only full pairs), the playable area the scenario names set as `MapScript` sets it. A map maker's
  own script is kept, with no options or tables. Tested as data (`AdaptiveScriptTest`) and run in Lua
  in the App's tests.

## Size, playable area and versions

User stories M-3 and M-8.

- **Resizing** (`Editing/MapResize`): `Resize(document, MapResizeSettings)` makes the map another of
  the game's sizes (`NewMap.Sizes` per side; sides may differ), the old map at an anchor
  (`MapAnchor`, top is north) or an offset (where its corner (0, 0) lands, in ogrids). Every grid
  keeps its size relative to the map (`Grid<T>`, `Shift<T>`: a half-size mask stays half, an offset
  between two of its pixels rounds down): the heightmap (the edges continue, or `FillHeight`),
  terrain types, stratum masks, the water's map and masks and the normal map continue their edges;
  masks for tools are empty where the map gains; the preview keeps its 256 pixels. A new grid of the
  document gets its line in `Resize`. Things move by the offset and keep their height; outside the
  new map props, decals (by their centre), units, markers (with their links and chain entries) and
  wave generators are removed (`MapResizeRemoved` counts them); spawns move to the nearest edge, so
  no army loses its spawn; areas are cut to the map and removed when nothing is left;
  `FitPlayableArea` makes the playable area the whole map. A sky dome over the old centre moves to
  the new one.
- **The playable area** (`ScenarioInfo.PlayableArea`, a name, optional in `map.json`'s scenario as
  `playableArea`; `MapDocument.PlayableArea` gives the area: the named one, else the first). The game
  has none of its own: the script calls `ScenarioFramework.SetPlayableArea('AREA_1', false)`
  (`lua/ScenarioFramework.lua`), as the map editors write it into `OnPopulate`. `MapScript` reads
  the start's call (the first with a plain name in `OnPopulate` or `OnStart`, not behind a comment:
  adaptive maps call it again later to grow the map) and rewrites it, or adds one after
  `ScenarioUtils.InitializeArmies()`. The import names the area only when the script names another
  than the first, so vault maps export unchanged; the export changes the script only for a named
  playable area. Renaming the playable area names it (also the first by convention), removing a
  named one falls back to the first. Walkability takes the named area whatever its size.
- **Versions**: `MapDocument.NextVersion` (one more, or 1); the folder name and `map_version` follow
  `Version` (`MapFolderName`).

## Layer stacks

The one model of layers (wave 8; `Files/InMemory/LayerStack.cs`, `Editing/LayerStacks`,
`LayerStackEdits`). The heightmap's stack is the first; wave 9 gives the stratum masks, the masks
for tools and the water's masks theirs in the same shape (the plan "Map Editor Rework").

- **`LayerStack`**: a `LayerTarget` (`Heightmap`, `Stratum(n)`, `Mask(name)`, `Water(mask)`; its
  `Key` names the files, `heightmap`, `stratum-2`, `mask-highlands`, `water-foam`; `IsHeights`) and
  `Layers`, lowest first. Empty: the target is simply its own grid. A layer's `Name` is its identity
  (unique in its stack without case, a file name): edits, the editor's choice and the project name
  it, so there is no separate id.
- **`Layer`**: `Source`, `Combine` (`Add`, `Subtract`, `Maximum`, `Minimum`, `Replace`, `Multiply`;
  the names are written in JSON), `Strength` 0 to 1, `Clip` (a mask key, `LayerStacks.MaskKey`
  `mask:<name>` or `StratumKey` `stratum:<n>`; a missing mask counts as none, a renamed mask for
  tools is followed by `FollowRenamedMasks`), `Visible`, `Locked`, `IsBase` (the lowest, full,
  unclipped, replacing; the heightmap's stack always has one), and its grids: in a stack of heights
  `Values` (game units at the heightmap's corners, so its tiles are the heightmap's) and an optional
  `Coverage` (0 to 255, how much of the layer there is: a road's soft edge, the sculpted part of a
  replacing layer); in a stack of coverage `Coverage` is the layer's values at the target's size and
  `Values` is null. A coverage layer has no alpha of its own: its way of combining works wherever
  its grid is, so a painted `Minimum`, `Replace` or `Multiply` layer works everywhere unless clipped
  (wave 9 may add an alpha when strata need one).
- **Sources** (`LayerSource` records): `PaintedSource` (`LayerSource.Painted`: a brush writes its
  grid), `ImageSource(Path)` (a file, relative to the project when inside it, read again when it
  changes and when the project opens; the layer's PNG keeps the last reading for when the file is
  gone, except for a height layer whose file lies in the project folder and is there, such as the
  final's base from `procedural/temp`: the project owns that file, so it keeps no copy, and a base
  whose file is gone opens with the map's saved heights, as a replacing layer does), `MaskRefSource(Key, Threshold, Invert, Height)` (another mask read while compositing; in a
  stack of heights its value is the mask times `Height` game units), `RulesSource(MaskRuleSet)` (the
  grid holds the rules' last result; coverage only), `CurveSource(HeightCurve)` (heights only).
  `LayerSources` names them (`painted`, `image`, `mask`, `rules`, `curve`), says which fit which
  stack (`Fits`) and which a brush paints (`CanPaint`). **Every source but a mask reference keeps
  its values in the layer's grids**, made again by the edit that changes the source (reusing the
  tiles that come out the same, as `HeightCurves.Rasterize` does), so the compositor reads grids
  only. A new kind (wave 9C's decal masks) is a record here, a name in `LayerSources`, its fields in
  `LayerDto` and its read in `MapProjectStore.Stacks`, its rasterizing in its own edit, and a
  follow step like `FollowRenamedMasks` when the things it reads change (decals moved).
- **The compositor** (`LayerStacks`): per place the value starts at 0 and each visible layer works
  on it with its weight w (strength × clip × in heights its coverage) and value v: add h + w·v,
  subtract h − w·v, maximum and minimum w of the way to v when higher or lower, replace w of the way
  to v, multiply h·(1 + w(v − 1)). Coverage runs 0 to 1 and is kept within it after every layer.
  `GridOf` gives a stack's `StackGrid` (corners for heights, pixel centres for coverage; clips and
  mask references of any size are sampled between their pixels, `SampleAt`). `CompositeHeights`
  and `CompositeCoverage` make the target's grid again over the tiles marked dirty (all when null),
  keeping tiles that come out the same and the grid itself when nothing changed; `Composite` gives
  floats (previews, the draft of 9A); `TileOf` one tile below a layer. `ChangedTiles(previous,
  before, next, after)` marks the tiles a change touched (a layer's grid tiles, the tiles reading a
  changed part of a mask), all of them when the layers, their order or settings changed, except
  that one layer added or removed (the others as they were) marks only where it works: none for a
  hidden or powerless layer, the tiles where its alpha (coverage; in heights its coverage) is above
  0, all for a mask reference or a layer without one. A grid without a stack follows as its own
  painted layer (`CoverageLayers.Trivial`), so the first layer added to it is as cheap. A mask of a
  coverage grid's own size is read pixel for pixel (its pixels lie at the grid's samples), and a
  composite of up to 16 dirty tiles runs on the calling thread (`CoverageStacksSpeedTest`: an 81 km
  map's painted layer added in 0.1 ms, a mask reference in about 14 ms, a stroke of four dabs over
  three layers about 1 ms).
  `TakeChanges` puts a brush's change of the target itself into the chosen painted layer
  (`PaintProblem` says why a layer cannot be painted), so the composite is what the brush made: a
  weaker or clipped layer gets more, under a replacing layer it does not show. A stroke on an 81 km
  map costs what it cost before the stacks (`tool.stroke` about 10 ms a dab in a debug build).
- **Edits** (`LayerStackEdits`, stack in, stack out; messages name a layer as `what` says, `height
  layer`): `NameProblem`, `FreeName`, `IndexOf`, `Add` (above a layer or on top; the source must fit,
  a base only at the bottom), `Remove`, `Move` (above the base), `Rename`, `SetCombine`,
  `SetStrength`, `SetClip`, `SetVisible`, `SetLocked`. A target's own edits put the stack in its
  place and add the target's rules (`HeightLayerEdits` for the heightmap).
- **Two stacks of one target** (the draft and final heightmaps, section "Draft and final
  heightmaps"): each a `LayerStack` of its own; a shared layer is the same name in both with the
  same source, grids and settings, at a place of its own in each and with its own `Visible`
  (`HeightStacks.WithLayer` makes an edit of its content in both, `HeightStacks.Sync` after every
  step carries what a brush wrote). Another target that gets two stacks follows the same pattern.
- **Following after every step** (for wave 9's targets): the heightmap's `HeightLayers.Follow` is
  the pattern: `FollowRenamedMasks`, `ChangedTiles`, `TakeChanges` when the target grid itself
  changed (a brush through `EditTransaction`), `CompositeHeights`/`CompositeCoverage` over the dirty
  tiles, and a composite that came out as it was is the previous grid (no step). A stratum's or
  mask's stack follows the same way from `EditSession.Record` and `EditTransaction`'s read.

## Stacks of coverage: strata, masks and water maps

Wave 9B (`MapDocument.CoverageStacks`, `Editing/CoverageLayers`, `CoverageLayerEdits`,
`MapProjectStore.CoverageStacks.cs`): every stratum mask, mask for tools and water map is a stack
of the model above, keyed by its target's key (`stratum-2`, `mask-forest`, `water-foam`;
`LayerTarget.FromKey`).

- **A grid without a stack is its own painted layer.** `CoverageLayers.StackOf` gives the stored
  stack or `Trivial` (one painted layer `Painted`, replacing, holding the grid itself). The first
  stack edit stores it; a stack that comes back to that shape (`IsTrivialShape`, its layer as the
  grid holds it) leaves the dictionary again. So an older project opens with one painted layer per
  grid holding what it held (lossless) and saves under the grid's own name (`stratum-masks/stratum-2.png`,
  `masks/forest.png`, `water/foam.png`). While a stack makes the grid, the editor writes it as its
  own file, `stratum-masks/stratum-2.gen.png` (section "Who writes a file: `.gen`").
- **The untouched part of a painted layer** (8A's caveat): a coverage layer gets an alpha,
  `Layer.Alpha` (0 to 255, the target's size; null for all of it). A painted layer added to a stack
  (`NewPainted`) starts with value and alpha 0 and combines by Replace, so it changes nothing until
  painted; `LayerStacks.TakeChanges` raises the alpha to 255 where a brush paints, starting from
  what the layers below make (Add and Subtract from 0, Multiply from 1), as a height layer's
  coverage does. The weight of a coverage layer is strength × clip × alpha. Erasing in such a layer
  paints towards 0 where it was painted (it does not give the place back to the layers below: hide
  or remove the layer for that). The grid's own painted layer has no alpha. Saved as
  `stacks/<key>/<layer>.coverage.png`, the name a height layer's coverage has.
- **New layers** (`CoverageLayerEdits.New*`): painted (Replace, alpha), image (`ReadImage`: the
  file's grey resampled to the grid, Maximum), mask reference (`NewMaskRef`: any name of a mask,
  stored as its key; Maximum), rules (`NewRules`, empty until generated; Maximum). A stack never
  reads its own target (refused), and keeps its last layer (`Remove` refuses).
- **Following** (`CoverageLayers.Follow(previous, next, chosen)`): run by `EditSession.Record` and
  `EditTransaction`'s flush before `HeightLayers.Follow` (height clips read the composites). Stacks
  follow in `Order` (after the stacks of the masks they read; a circle is broken where it closes):
  renamed masks followed, `ChangedTiles`, a change of the grid itself (a brush) into the chosen
  painted layer (`EditSession.PaintLayers[key]`, else the highest that can be painted;
  `PaintProblem` says why none can), composited over the dirty tiles. A stack whose target is gone
  goes. `Recomposite` after opening and resizing.
- **What takes stacks along**: `MaskLayers.Rename` (via `RuleEdits.MaskRenamed`: the stack under the
  new key, clips, references, rules and prop conditions reading the new name), `Duplicate` (the
  same layers), `Remove`; `StrataEdits` add/remove/move (`CoverageLayers.MoveStrata`; a stratum's
  references elsewhere are not renumbered, as height clips are not); `MapResize` (masks' layers gain
  nothing, the others continue their edge, alphas gain nothing).
- **Rules are rules layers** (`RulesSource`): `MapDocument.MaskRules` is now computed (per mask, its
  lowest rules layer's rules). `RuleEdits` take an owner (a mask's name or `stratum:N`) and an
  optional rules layer; `SetMaskRules` on a mask without one adds `Rules` (Replace, holding the
  mask, as a mask made from rules was); without its last rule the layer becomes painted, keeping
  what it made. `MaskRules.Order` gives stack keys; `CoverageLayerEdits.Generate` makes every rules
  layer of every stack (masks and strata) in that order, each from what the stacks it reads make by
  then (`GeneratedBefore` for a preview); `MaskRulesGenerator` applies them and follows. Its inputs
  are the rules layers' sources and what they read that rules do not make (`RuleInputs`).
- **One way to name a mask**: the key (`mask:forest`, `stratum:3`). `LayerStacks.KeyOf` turns any
  name (key, title `mask forest`/`stratum 3`, plain mask name) into it; `MaskOf` and
  `MaskRuleSources.Read` read plain names and keys alike, so prop rules, the props brush, height
  clips and mask references take any mask or stratum.
- **On disk**: `stacks/<key>.json` (`LayerStackDto`, unchanged), layers' PNGs in `stacks/<key>/`;
  no DTO changed. An older project's `masks/<name>.rules.json` loads as a rules layer holding the
  mask's PNG (when the mask has no stack file); the first save removes it. Reload reads a stack again
  when one of its files changes. An image layer reads its file (in the project, or anywhere when
  it is there at opening) and keeps no copy of a file in the project; Reload reads it again when the
  file changes (`CoverageImageLayersOf`, `ReloadCoverageImage`, through `CoverageLayerEdits.SetGrid`).
- **A new source**: its record and name in `LayerSources`, its fields in `LayerDto` and its read in
  `ReadStack`, a `CoverageLayerEdits.New...`, the masks or things it reads in `CoverageLayers.ReadsOf`
  when they decide the order, and a follow step (like rules: made again in the edit that changes what
  it reads). The window's side is one add choice and a `layers.add` source (the App guide, "Stacks of
  coverage"). The decal masks (below) are one: they read no mask, so `ReadsOf` leaves them out.

## Who writes a file: `.gen`

Format 7 (format 8 changed only the skybox: `MapProjectDto.CurrentFormatVersion`). The owner asked that a map maker can see on disk
which files the editor controls and which belong to other tools: a file the editor makes from other
things has `.gen` before its extension (`MapProjectLayout.Generated`, `IsGenerated`,
`WithoutGenerated`).

- **The editor's (`.gen`)**: the grid of a target a stack makes (`MapProjectLayout.GridFile(document,
  target)`, `IsMadeByStack`: `heightmap.gen.png` with height layers, `stratum-masks/stratum-N.gen.png`,
  `masks/<name>.gen.png`, `water/foam.gen.png` with a stack of coverage), a rules or decal-masks layer's
  grid (`StackLayerFile`, `StackLayerCoverageFile`, `IsGeneratedLayer`: `stacks/stratum-2/Hills.gen.png`)
  and the draft for World Machine and Gaea (`ProceduralFiles`). Written again whenever what they are
  made from changes; Reload skips them, so what another tool saves into one is not read back and is
  lost at the next save.
- **The map's own**: every other file (the grid of a target without a stack, a painted layer's
  values and alpha, `terrain-types.png`, the JSON): written when it is edited in the editor, read back
  when another tool saves into it.
- **A tool's**: the file an image layer reads (of the heightmap or a stack of coverage), named in the
  layer's `path`: never written by the editor, read again when it changes (in the project folder) or
  at opening. A file in the project keeps no copy; one outside it keeps its last reading in
  `stacks/<key>/<name>.png`.
- **Switching**: a save writes a grid under the name it has now and removes the file under the other
  (a stack added or ended); a layer's file under the other name goes with the layers' files the stack
  no longer has. Reload takes a plain file that is gone while its `.gen` file is there (and the other
  way round) as no change, so the editor's own renames are no edits.
- **Format 6** projects open unchanged: each grid and layer file is read under either name
  (`MapProjectStore.Existing`, `NamedEitherWay`, `ReadMasks` names a mask without `.gen`), and the
  next save renames them.
- **In words** (`ProjectFileNotes`): `OfLayer(target, layer)` and `OfResult(document, target)` say
  where a layer's values and a stack's grid are kept and who writes them; the App's stack control
  shows them under the list and the chosen layer.
- Tests: `GeneratedFilesTest`.

## Decals over the whole map, decal sets and decal masks

Wave 9C, user story D-1.

- **Decal sets** (`Editing/DecalSets`): textures named alike with a suffix each, `_albedo`,
  `_normals` (`_normal`), `_spec`, `_mask` (named `main`) and `_mask_<name>` (`PartOf`). `Of(entries)`
  finds them among the catalogue's decals (two parts or a mask), `OfMap(document)` among the map's
  own files, `Find` by name. A placed set is one `Decal` whose `Set` (`DecalSet`: name, normals,
  masks by name) carries the textures beyond its own (its albedo with the specular in slot 2, or its
  normals when it has no albedo): one selection, one inspector, one undo step, moved by every edit
  of decals. The export writes it as one decal per texture (`DecalSets.Expand` in `MapExporter`:
  the normals decal right after, the next free id, the same groups); masks are never exported:
  `DecalSets.UnexportedMasks` (pictures named `_mask` or `_mask_<name>` that no decal's texture,
  stratum or Lua file of the map names) are left out of `MapExporter`'s files and of
  `TextureExport.Pending`.
  The catalogue gives a set's textures the set's name as `Family` and masks the category `masks`.
  `decals.json` keeps `set` on such a decal (`DecalSetDto`, optional).
- **Map-wide decals** (`MapDocument.WideDecals`, `MapWideDecals`; `decals.json`'s optional
  `mapWide`, `MapWideDecalsDto`, absent for none): albedo (a file, with an optional specular),
  normals and shadow (generated, a file, or none) and the shadow's settings. Imported maps and older
  projects have none; a new map has the normals and shadow generated. A file is relative to the
  project, absolute, or a path into the map's own folder (named by the decal as it is).
  `MapWideDecalsExport.Apply(document, readFile)` (the editor's export step, `faf-map pack`,
  `EditedMap.Export(wideDecals: true)`) puts them under every decal as Kaali (kaali.v0004) has them:
  at (0, height at the corner, 0), scale (width, width, height), no turn, cut-off 10000; albedo
  (specular in slot 2), shadow (an albedo decal), normals; textures in `env/decals/` (`map_albedo`,
  `map_specular`, `map_lighting`, `map_normals`, a free name beside the map's own), a DDS file copied,
  anything else converted (at most 8192).
- **What the game reads** (`effects/terrain.fx`, `DecalsNormalsPS`, the same in the game's
  `effects.scd` and FAF's `effects.nx2`): a normals decal's x in alpha and z in green, turned by the
  decal's heading only, and red the weight with which it replaces the terrain's normal ("our blend
  mask is stored in the r channel"). Kaali's red grows with the slope as the square root of its sine
  (0.1 flat, 0.5 at 17 degrees, 0.9 at 55), so `MapWideDecalImages.Normals` writes that: 16 pixels an
  ogrid, the corners' normals blended. Its shadow decal is black with alpha 0 to 0.45, growing with
  the slope like ambient occlusion: `MapWideDecalImages.Shadow` writes the mean over eight
  directions of the horizon's sine within a radius (16 ogrids, steps growing), times a strength, at
  most the darkest (0.45), and optionally the sun's shadow (`Lighting.SunDirection`), 4 pixels an
  ogrid, from the corners' darkness blended. Generated and exported files are byte for byte Kaali's
  sizes (89 478 640 and 5 592 560 bytes for a map of 512).
- **DDS with mipmaps** (`DdsTexture.WriteMipmapped`, `DxtEncoder`): DXT5 and DXT1 written by our own
  encoder (colours along their principal axis, always four-colour; alpha's eight steps), each level
  the mean of two by two, down to one by one; header flags 0xa1007, caps 0x401008 as Kaali's.
  ImageMagick reads them back (it reads the first level only).
- **Generators** (`Generation/MapWideDecalGenerators`): `NormalDecalGenerator` and
  `ShadowDecalGenerator` make previews (at most 2048 on a side) as `DecodedTexture`; inputs the
  heightmap, and for the shadow its settings and (with sun shadows) the sun.
- **Decal-masks layers** (`DecalMasksSource(Mask, Sets, Height)`, `LayerSources.DecalMasksName`
  `decals`; `LayerDto`'s `decalMask`, `decalSets`, `height`): fit heights (the mask times `Height`)
  and coverage. `DecalMaskLayers.Draw` rasterizes the masks of the decals of the chosen sets (all
  when none) at each decal's place, turn and size (the mask's first row along the decal's z from its
  corner; the higher where decals overlap) over dirty tiles, reusing tiles that come out the same;
  `New`, `SetSource`, `MaskNamesOf`. `Follow(previous, next, pictures)` runs first in
  `EditSession.Record` and `EditTransaction`'s flush (before `CoverageLayers.Follow` and
  `HeightLayers.Follow`, which composite what changed): the decals of either version the other lacks
  (by reference: ids need not be unique) mark the tiles to draw again, in the heightmap's stack and
  every stack of coverage. The pictures come from `EditSession.DecalPictures`
  (`IDecalMaskPictures`; `GameMaskPictures`: the map's own files, else the workspace's assets, read
  once per version); without them the layers keep their grids, which the project saves.

### A map maker's own decals, and importing them (wave 12B)

- **`MapOwnAssets`**: the decals among the map's own files as `AssetEntry`s for the shelf and the
  picker: pictures in a folder named `decals`, and pictures a decal of the map names; key the game
  path without the leading slash and with the file's own extension (`AssetCatalog.KeyOf` would turn
  a `.png` into `.dds`, which the map's files are not), group `this map`, category by the set's
  part, a set's name as family. `DecalSets.Of` finds their sets as it finds the catalogue's.
- **Map-wide files from elsewhere**: the editor copies a slot's file from outside the project into
  the map's own files (`env/decals/map_albedo.png`, `map_specular`, `map_normals`, `map_lighting`)
  and the slot names the copy, so a project does not depend on a path of one computer.
- **Import** (`ImportedDecals.Recognise`, run by `EditedMap.ImportMap` and so by the window's import
  and `faf-map unpack`): decals over the whole map (at the corner, unturned, as large as the map,
  cut-off at least 5000, a texture of the map's own files) fill the slots as Kaali has them: the
  normals decal the normal slot, an albedo named for lighting or shadow the shadow slot, the other
  albedo the albedo slot with its second texture as the specular; they leave the list and their
  groups. An albedo decal of a set (`rock01_albedo`) directly followed by its normals decal
  (`rock01_normals`) with the same place, turn, size, distances and army is one decal carrying the
  set (with the masks the map's files have for it), as `DecalSets.Expand` wrote it. Kaali
  (kaali.v0004) imports with its three decals in the slots; HardFFA and Archsimkats Valley have no
  such pairs.

## Textures as DDS on export

Wave 10E, user story M-10. `TextureExport.Apply(document, out converted, cache)` (the editor's export
step `ddsTextures`, `faf-map pack`, `EditedMap.Export(ddsTextures: true)`) turns the map's own files
that are pictures of another format (`IsPicture`: PNG, JPEG, TGA, BMP, GIF, TIFF, WebP) into DDS for
the version an export writes, and points every reference at the DDS file: the strata's albedo and
normals, decals and their sets (normals and masks), the water's cube map, ramp, waves and wave
generators, the sky's background, cube maps and sky dome, and the paths in the map's `.lua` and
`.bp` files (after a slash, in any case). The project keeps its pictures.

- **What is converted** (`Pending`): every picture the document or a Lua file names, and every
  picture in a folder of the map (`env/`, `textures/`, a prop's texture found by its name); a picture
  at the top of the folder that nothing names (the vault's `.small.png`, a scenario preview) is no
  texture and stays. DDS files are left as they are; the game's and FAF's textures are only named.
- **How** (`TextureRole`, the strongest use when a file has two): the channels as the picture holds
  them, since the map's pictures are in the layout the game reads from DDS (as the converted assets
  are; a normals decal's x in alpha). `Normal` (strata normals, normals decals and sets' normals, the
  water's waves): DXT5 with mipmaps, as nearly every vault map has them. `Ramp` (the water's and the
  wave generators'): uncompressed A8R8G8B8 without mipmaps, as the game's `waterramp_tundra.dds`, so
  the gradient keeps every step. `Cube` (the cube maps): a DDS cube (caps2 0xfe00) of the strip's six
  faces (`CubeMaps.Decode`: a strip, else one picture on every side), DXT1 or DXT5 with mipmaps per
  face, one face after the other. `Plain`: DXT5 with mipmaps when any pixel is not opaque, DXT1 with
  mipmaps otherwise (`DdsTexture.WriteMipmapped`). A picture nothing names gets its role from its
  name (`normal`, `ramp`, `cube`). A TGA is read by its extension (it has no signature).
- **Names**: the picture's with `.dds`; when the map has a file of that name already, `_2`, `_3`, ...
- **Cache** (`TextureExportCache`, `TextureExport.SharedCache` within a process): DDS files by the
  SHA-256 of the picture and its role, at most 512 MB, the oldest dropped first; `ConvertedTexture.Cached`
  says which came from it. `FormatOf(dds)` reads how a DDS file is written (`DXT1 cube with mipmaps`).

## Height layers and curves

User stories T-5 and T-4 (`MapDocument.HeightStack`, `Editing/HeightLayers`, `HeightLayerEdits`,
`HeightCurves`): the heightmap's layer stack (above).

- `MapDocument.HeightStack`, lowest first, empty for a map without layers (its heightmap is simply
  its heights). With layers, `Terrain.Heightmap` is what they make, so everything that reads the
  heights (views, walkability, normal map, export) is unchanged. The base (`IsBase`, painted, the
  heights as they were when the stack began) is always the lowest layer, one per map, at full
  strength, unmasked, replacing; it cannot be moved or removed. `HeightLayerEdits.Bake` ends the
  stack: the heightmap keeps the sum.
- Its layers: painted (the sculpt tool's, shown as `Sculpt`), image (`Imported` in automation),
  curve, mask reference. `HeightLayerEdits.SetMask` sets the clip after checking the map has the
  mask. Per corner the weight is strength × clip (bilinear between the mask's pixels, any size) ×
  coverage.
- **The heightmap follows** (`HeightLayers.Follow(previous, next, sculptLayer)`): run by
  `EditSession` on every step and by `EditTransaction` on every read of its document. It adds up
  again only the tiles whose layer values, coverage or masks changed (all of them when a layer's
  settings or the stack changed); tiles that come out the same stay shared, and a sum equal to the
  previous one is the previous heightmap (so an edit that changes nothing is no step). A change of
  the heightmap itself (a brush through `EditTransaction.Heightmap`, any edit that sets it) is not
  the sum: it goes into `EditSession.SculptLayer` (the editor's chosen layer) so that the sum is
  what the brush made (a weaker or masked layer gets more; under a replacing layer it does not
  show), or, without a layer that can be sculpted (`SculptProblem`: locked, hidden, a curve, an
  import), is undone. Smooth, flatten and erode work on the sum.
- `SetCombine` of a sculpt layer between differences (add, subtract) and an absolute way (maximum,
  minimum, replace) converts its values over the layers below, and between add and subtract turns
  them round, so it keeps its look; to or from multiply the values stay. `HeightmapFiles.Import` and a `heightmap.png` another tool
  saves go into the base of a map with layers.
- **Curves** (`HeightCurve`: points with a height, `Profile` ridge, valley, road, cliff; width,
  falloff, closed, and an optional `Symmetry`: the layer then shapes the curve's images too,
  `HeightCurves.Parts`, an image in a mirrored part running the other way so a cliff's high side
  is mirrored; where parts overlap the one that shapes most wins, a road's by coverage; kept as
  the curve's `symmetry` in `layers.json`): a Catmull-Rom spline about an ogrid between samples (`HeightCurves.Spline`);
  `Rasterize` makes the layer's values (ridge `+h`, valley `-h`, cliff `+h` on the left of the
  direction of travel in the top view, all `Add`; a road `h` with coverage, `Replace`), full over
  the width (a cliff: a drop of an ogrid at the curve, full over the width on the high side) and
  a smooth fade over the falloff. `SetCurve` rasterizes again, reusing the tiles that come out the
  same, within the tiles the old and new curve reach; a new profile brings its way of combining.
- **Curve outlines** (wave 18, `Editing/CurveOutlines.cs`): `CurveOutlines.Of(curve, width, height)`
  gives the lines the window draws along a curve and its images: where the full width ends
  (`Edge`) and where the fade ends (`Fade`), from `HeightCurves.Samples` (so per-point values and
  variation count, edge noise does not); a cliff's high side, a fill's rim outside.
- **The curve's settings in full** (wave 17, the owner's chosen upgrades; `HeightCurve.cs`,
  `HeightCurves`): every curve has them; a new setting is a property at the end with a default.
  - *Per point* (`CurvePoint(X, Z, Height, Width?, FalloffLeft?, FalloffRight?, Strength?, Sharp)`):
    null is the curve's value (`IsPlain`: none set). Between points the values blend by a
    smoothstep. Strength (0 to 1, null 1) scales the profile (a road's or river's coverage). A sharp
    point makes a corner: the Catmull-Rom neighbour across it is the point itself.
  - *Heights between points* follow a monotone cubic (Fritsch and Carlson) for every curve: nothing
    lies outside the two points' heights, points of one height keep exactly that height (curves of
    one height make exactly what they made before; `HeightCurvesTest` compares with the old code,
    `LegacyHeightCurves` in the tests).
  - *Per curve*: `Falloff` (left, and right too) and `FalloffRight` (null: the same); `FalloffShape`
    (`Smooth`, `Linear`, `EaseIn`, `EaseOut`, `Terraced` with `TerraceSteps`; null: the profile's,
    `HeightCurves.ShapeOf`: ease out for a river, smooth otherwise; `HeightCurves.Fade(t, shape,
    steps)` is the curve of it), applied to the fade and to a cliff's face (a terraced face is a
    terraced cliff); `CliffFace` (the drop's width in ogrids; 1 and smooth is the cliff as it always
    was; the cliff fades back over its left falloff, its high side's); `RoadMode` (`Both` replace,
    `CutOnly` minimum, `FillOnly` maximum: `HeightCurves.CombineOf(curve)`, which `NewCurve` and
    `SetCurve` set as a profile does, unless the map maker chose another way); `Camber` (a road's or
    river's left edge that much higher than its right, across the width); edge noise
    `NoiseAmplitude` (ogrids, 0 none), `NoiseScale`, `NoiseSeed` (`HeightNoise` at the place moves
    the distance to the line before the fade, the signed distance of a cliff or a fill); variation
    along the curve as ranges, `HeightVariation` and `WidthVariation` (`CurveRange(Min, Max)`, added
    to the height in game units and the width in ogrids: -1 to 0 on a ridge of 5 gives 4 to 5) with
    `VariationScale` (ogrids along the curve), one-dimensional noise over the distance along the
    curve with the same seed; `LineOnly` (closed curves).
  - *Profiles*: `River` (appended) is a road that only cuts (minimum, coverage, absolute heights like
    a road's: `HeightCurves.IsAbsolute`), its banks easing out unless a shape is set.
  - *Closed curves fill* (`HeightCurve.Fills`: closed, not line only, three points or more): the
    inside (even and odd crossings per row on the sampled line) counts as on the curve, the outside
    fades over the falloff on the outside's side; a closed cliff raises its inside with the face at
    the line and no fade back. A fill has one height and strength, the means of its samples' along
    the curve: per-point heights do not tilt a fill. Edge noise makes the rim ragged.
  - *Symmetry*: the images are drawn from the curve's own samples turned or mirrored (a mirrored
    image runs the other way, so its left is the image of the curve's left), so they carry the same
    variation; each reads the edge noise at the original place (the inverse of its transform, as
    `HeightBrush.NoisePlace` does). Every part of a map is alike (no setting). Where parts overlap,
    the one that shapes most wins as before.
  - *Reading a curve* (for the window's node table, lines and grades): `HeightCurves.Samples(curve)`
    gives `CurveSample`s about an ogrid apart with every value there (height and width with the
    variation, both falloffs, strength, `Distance` along the spline, `Node`: the point's index at a
    point, else -1); `Spline` the same as points. `HeightCurves.Grades(curve)` gives per pair of
    points a `CurveGrade` (rise, run along the spline, percent, degrees, the steepest grade and where).
  - *Edits* (`HeightLayerEdits`, each through `SetCurve`, refused on a locked layer): `CurveOf`,
    `ReverseCurve` (`HeightCurves.Reversed`: the points in the other order, the falloffs left and
    right swapped, the curve's and each point's, for every profile but the cliff, whose high side
    flips; the camber's sign; variation then reads the distance from the other end),
    `SetCurvePoint(index, point)`, `EvenGrade(from, to)` (`HeightCurves.EvenGrade`: the points
    between get heights on a straight line by the distance along the spline), `SnapCurve(indices?)`
    (a road's or river's points get the height the layers below its layer make there, in the stack
    that holds it: an explicit snap, nothing follows the ground later).
  - *Cost*: the reach is per segment (widths, falloffs, the face and the noise's amplitude), the
    bands of rows and the reuse of tiles as before; a part's values are worked out in parallel per
    row, then merged. A ridge of about 650 ogrids on an 81 km map: 5 ms before, 6 ms now, 8 ms
    with noise and variation (`ALongCurveWithNoiseOnTheLargestMapStaysQuick`, debug).
  - *On disk* (format 9): the curve's `nodes` (the points with values of their own, by `index`),
    `falloffRight`, `falloffShape`, `terraceSteps`, `cliffFace`, `roadMode`, `camber`,
    `noiseAmplitude`, `noiseScale`, `noiseSeed`, `heightVariation` and `widthVariation` as
    `[min, max]`, `variationScale` (each left out at its default), and `lineOnly`, written for every
    closed curve: a closed curve without it (format 8 and older) opens as line only, so it keeps its
    look; new closed curves fill.
- **On disk** (format 6, `MapProjectStore.Stacks.cs`): `stacks/heightmap.json` (`LayerStackDto`:
  the target, the layers with name, `source`, combine, strength, clip, hidden, locked, base, and
  their source's fields: `offset`, `path`, `mask`/`threshold`/`invert`/`height`, `rules`, `curve`;
  absent without layers) and per layer except curves and mask references
  `stacks/heightmap/<name>.png` (16-bit, sample 0 at `offset`: -32768 steps for layers of
  differences, 0 otherwise) and `stacks/heightmap/<name>.coverage.png`; a coverage stack's layers
  are 8-bit PNGs of the target's size; a rules or decal-masks layer's are the editor's,
  `<name>.gen.png`. `SaveStack`/`ReadStack` work for any target. Load composites the layers again
  (the saved `heightmap.gen.png` is their composite, written by the editor). A save
  writes only the layers whose grids changed. Reload reads the stack again when a file of it
  changes, an image layer's file when that changes, and a changed `heightmap.png` into the base.
  **Older projects** (format 3 to 5) keep the layers as `layers/layers.json` (`HeightLayersDto`, kinds
  `Base`, `Sculpt`, `Imported`, `Curve`) and `layers/<name>.png`: they open unchanged (read into the
  stack in memory), and their first save writes `stacks/` and removes `layers/`. An image layer's
  file is read again whenever it changes, also for a layer added since the last save
  (`EditedMap.Reload` takes such a file as another tool's, `MapProjectStore.IsImageLayerFile`).
- **Cliff faces block walkability** (D3, `HeightCurve.BlocksWalkability`, `Editing/CliffFaces`,
  `HeightCurves.FaceCells`): a cliff curve with the flag (a cliff made by `terrain.addCurve` or
  `terrain.setCurve` has it; a curve read from a project without `blocksWalkability` has not; on disk
  the optional `blocksWalkability` of the curve in format 9) makes the ogrids under its face count as
  marked blocked. `FaceCells(curve, width, height)` is the drop widened by the face width, the
  curve's images through its symmetry, the edge noise included, a closed cliff's rim too. `CliffFaces`
  keeps the ogrids per curve (`ConditionalWeakTable`, curves are immutable) and merges those of the
  visible blocking curves of the final stack per call, remembering the last answer (`Grid`, `Count`,
  `Indices`, `KeyOf`). `WalkabilityMarks.TerrainTypes`, the walkability generator and the export
  block them unless a hand mark says open; they are never written into `WalkabilityMarks`, so
  editing the curve moves the face. `CliffFaces.Key` is an input of the walkability, navigation mesh
  and path network results (`GeneratedResults`), so a cliff edit makes them out of date and they
  regenerate by themselves while shown. The check `walkability-marks` counts them. Measured (4096
  ogrids, one cliff of 200 points with edge noise): 119 ms the first time, 0 cached, 93 ms for a
  second changed curve. Tests: `CliffFacesTest`.
- **Decals along a curve** (D2, `DecalEdits.AlongCurve(document, curve, pattern, DecalsAlongCurve, out
  added)`, `Editing/DecalEdits.AlongCurve.cs`): places decals once, as ordinary decals on top of the
  others; nothing ties them to the curve afterwards. `PlacesAlong(line, settings)` cuts the spline
  (`HeightCurves.Spline`) into equal pieces as close to `Spacing` as fits (left out: the decals'
  length, end to end; at least one) and puts a decal in the middle of each, its length (`Scale.Z`)
  along the direction of travel (heading `atan2(-dx, dz)`), moved by `Offset` (positive on the left
  in the top view, the side a cliff raises; `Alternate` flips every other), and by `Jitter` and
  `RandomTurn` drawn from `Random(Seed)`, three draws per decal whatever is used. The pattern (a
  `NewDecal` or `NewDecalSet`) gives the textures, type, set and distances, its size where the
  settings leave it out. A middle beside the map is left out. A curve with a `Symmetry` gets the
  images of its decals by `SymmetryEdits.MirrorObjects` (the exact image of each, its width along
  the image of its width; an image where one lies already, or beside the map, is left out), so they
  follow the same rules as mirrored placing; `added` lists the decals along the curve, then the images.

## Draft and final heightmaps

User story T-9 (wave 9A; `Editing/HeightStacks`, `Files/InMemory/HeightStackKind.cs`,
`ProceduralFiles`). The draft is the rough shape blocked out and the input of World Machine or Gaea;
the final starts from their output and is what the game gets.

- **The model**: `MapDocument.HeightStack` is the final (its composite is `Terrain.Heightmap`,
  which the export, walkability, the normal map, navigation and every generator read: the export
  always takes the final), `MapDocument.DraftStack` the draft, `DraftSamples` the draft's
  composite (null while both stacks hold the same layers in the same order, `MakeTheSame`: then
  `DraftHeightmap` is the heightmap itself, so a map that never parts costs nothing), `DraftHeightmap`
  the draft as a `Heightmap`. Both stacks are empty or both have a base; names are unique over
  both (`HeightStacks.NameProblem`, `FreeName`). `HeightStackKind` names a stack (`Final`, `Draft`),
  `LayerUse` a layer's membership (`Shared`, `Draft`, `Final`; `UseOf`).
- **Shared layers**: one layer, the same source, grids and settings in both, its own place and
  `Visible` in each. `HeightLayerEdits` finds a layer in either stack (`HeightStacks.Locate`, the
  final first) and makes a content edit (strength, combine, mask, lock, rename, curve, image read
  again) in both; `SetVisible` and `Move` take the stack (`kind`, the final by default); `Remove`
  takes it out of both; `HeightStacks.SetUse` changes membership (added on top, shown; a base keeps
  its use). `SetCombine` keeps a sculpt layer's look in the stack named.
- **New layers** (`HeightLayerEdits.Add(..., shown, use)`): into both while the final shares the
  draft's base (`HeightStacks.DefaultUse`: a map without a procedural step exports what was drawn),
  else into the stack shown. `Start` begins both stacks with one shared base.
- **The final's base** (`HeightStacks.UseAsFinalBase(document, samples, source)`): an image layer
  marked base (`Procedural base`, `FinalBaseName`) replaces the final's lowest layer; the old base
  stays the draft's (draft only). Read again whenever its file changes, as any image layer.
  `ShareDraftBase` makes the final start from the draft's base again; `SharesBase`, `FinalBase`.
- **Following** (`HeightLayers.Follow`, every step): `Normalized` (a final with layers gets a draft
  holding every layer; none without), a brush's change of the draft's samples goes into the draft's
  sculpting layer (`TakeChanges`), a brush's change of the heightmap into the final's, `Sync`
  carries changed layers into the other stack (unless that stack changed them in the same step), the
  final composites its dirty tiles, and the draft composites its own from its previous samples
  (which share every tile the two have in common, so switching uploads only what differs).
  `EditSession.Heights` says which heightmap brushes write; `EditTransaction.Heightmap` hands out the
  draft's samples for the draft (while the map has layers). `HeightLayers.SculptProblem(document,
  layer, kind)` refuses a layer the stack lacks.
- **On disk** (format 6, optional additions): `stacks/heightmap.json` lists every layer once (the
  final's in its order, then those only in the draft), each with `use` (`draft`, `final`; left out
  for both), and `draft`, the draft's order with each layer's `hidden` there (`DraftLayerDto`; left
  out while the two stacks are the same). A project without `draft` (every older one) opens with
  every layer in both: the same terrain. Shared layers load as one object in both stacks.
- **Files for World Machine and Gaea** (`ProceduralFiles`, all in `procedural/temp/`):
  `draft-heightmap.gen.png` (16-bit grey, (width + 1) by (height + 1), one step 1/128 game unit),
  `draft-heightmap.gen.r16` (the same samples, raw little-endian, row by row from the north-west),
  `draft-mask-<name>.gen.png` (a ticked mask for tools, 8-bit at its own size; an unticked one's file
  is removed); every other image file there is an output (`Outputs`, newest first; `IsInput` knows
  the editor's by `.gen`, and the names of format 6, which `WriteDraft` removes). `WriteDraft` writes
  them; the map's settings (`ProceduralSettings.DraftMasks`, `WriteDraftOnChange`; `map.json`'s
  `procedural.draftMasks`, `procedural.writeDraft`) say which masks and whether the editor writes them
  again whenever they change.
- `MapResize` keeps shared layers one object; `HeightmapFiles.Import` and a changed `heightmap.png`
  go into the final's base (the draft's too while shared).

## Generated results

- **The sky cube map** (L-1, wave 10A; `Generation/SkyCubeMaps`, `SkyboxSky`, `CubeFaces`, `Editing/Skyboxes`,
  `ProceduralSettings.SkyCube` as `SkyCubeSettings`, `procedural.skyCube` in `map.json`, `SkyCubeDto`): six faces
  seen from above the map's middle (`Eye`: the eye height over the terrain or water there). `CubeFaces` has the
  layout of Direct3D, OpenGL and the game's DDS cube maps (+x, -x, +y, -y, +z, -z; a face's pixel looks along
  `Direction(face, sc, tc)`), sampled by world direction, so the generated cube and the editor's and game's
  sampling agree. `SkyboxSky` draws the version 60 sky as the game's `effects/sky.fx` (`effects.scd`) does: the
  dome a sphere of `Scale` around `Position`, the height where a direction meets it mixing horizon into zenith
  between `HorizonHeight` and `ZenithHeight` (linear here; the engine shapes it with the game's
  `/textures/environment/horizonLookup.dds` and builds the dome as a flatter cap of a sphere, which this does
  not follow yet), the horizon's colour below the horizon; sky objects as squares facing the eye
  (`DecalVS`: the corner turned by `Rotation`, `Uv` with y from the atlas's bottom), alpha-blended in order, the
  glow brightening them; four cirrus layers, one channel each, at time 0; the sun as a disc. A map without a
  skybox gets one from its lighting (`Skyboxes.FromLighting`: the horizon the fog warmed by the sun, the sun's
  colour for a black fog; a deeper blue zenith). `SkyCubeMaps.DrawTerrain` follows each ray over the heightmap
  (a pyramid of the highest points), coloured by `TexturedPreview` from above, the water adding the reflected sky
  by FAF's Fresnel term; `Dds` writes DXT1 with mipmaps per face and caps2 0xfe00 (`DdsTexture.WriteCube`; the
  game's sky cubes are DXT1 of 512, its environment cubes carry mipmaps); `CubeMaps.Decode` reads it back.
  The skybox follows the engine's reader (the Map library's guide, "The binary format"): three cumulus
  texture paths where the FAF map editor reads a `MidRgbColor` (empty in every map), no `Clouds7` (the
  count of cartographic decals that follows the block), and always four cirrus layers
  (`Skyboxes.WithFourCirrusLayers`, filling from `DefaultCirrusLayers`, the engine's own table; format 8).
  **The version** (`Editing/ScmapVersions`): a map is written as 60 or 56 (`Set`; 53 keeps its version,
  its layers differ). `Skyboxes.GameDefault(document)` is the skybox the game gives a map whose file
  stores none (`CWldTerrainRes::Load` and `SkyDome::SetupHorizonAndCirrus` in Draiget/faf-re): over the
  middle, the radius the half diagonal over cos 72° (1171.58 for 512 ogrids, as the game's own maps
  store it), the bottom at the water or the lowest ground, the engine's colours, `cirrus001_512.dds`
  and its cirrus table; new maps, the sandbox and a map switched to 60 get it, so they look as before.
  The import keeps a map's version (an import wizard is to ask to switch to 60).
  `Skyboxes.ReadJson` reads the FAF map editor's skybox presets (`Data`, colours as r, g, b), moving texture paths
  of another map's folder into this one's when it has the file. Tests: `SkyCubeMapsTest` (with
  `assets/skyboxes/procedural-skybox.json`, 1196 sky objects).

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
also puts it in its place (`Apply`); the editor puts it into the current version without a step of
undo (`EditSession.Derive`: a derived result). Inputs read only what
a generator needs (`WaterLevel`, `DepositsAndAreas`, `MapType`), so a change of the water's colour
or a path node leaves walkability current.

Generators feed each other: `Outputs` names the parts of the document a generator's result
changes when put in place (as other generators' inputs name them: masks from rules change
`RuleInputs.PropRuleMasks`, props from rules `Props`, path networks `Markers`), and `Reads` the
generators whose last result it reads (path networks read walkability; `GeneratedResults.Generate`
hands it over through `Read` first, `HandResults` for a caller that generates elsewhere).
`GeneratedResults.Feeds(a, b)` and `InOrder()` give the order (a cycle keeps the order of
registration), and `Stale` lists in that order, so generating what is out of date one after the
other from the document each leaves (the editor's `generate.stale`) leaves nothing out of date.

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
- **The terrain shaders** (user story S-4, `Editing/TerrainShaderCatalog`): the techniques of FAF's
  `effects/terrain.fx` a map can name (`TTerrain`, `TTerrainXP`, `TTerrainXPExt`, `Terrain000` to
  `Terrain301`), each with how it blends (`Strata`: 4 or 8; `FullMaskRange`: a mask whole, or only its
  upper half as `saturate(mask * 2 - 1)`; `Stratum8IsData` and `UpperIsAlbedo`: whether stratum 8
  and the upper layer are textures or map-wide data) and the layers and settings it reads, from the
  layout comments in `terrain.fx`. `Of(name)` takes an unknown name as `TTerrainXP`.
- **The textured preview** (user story S-4, `Generation/TexturedPreview.Render(document, texture, size)`):
  the map from above, each pixel the layers blended as the shader says, the textures made smaller in
  steps of two and sampled at the step whose texel is as large as the ground a pixel covers (a
  texture is read at most 256 wide); lit as the 3D view lights (`CalculateXPLighting` without the
  specular part); under the water the ramp's colour at the depth over the ground (`terrain.fx`),
  then the surface colour by the depth clamped to the colour's near and far (FAF's `water2.fx`).
  A texture that cannot be read is grey (an upper layer covers nothing). The editor's preview
  generator uses it with the workspace's textures.
- **The normal map** (user story S-4, `Generation/TerrainImages.NormalMap`, `NormalMapGenerator`;
  the game makes its own from the heightmap and never reads this one, section "What the game ignores
  and what it derives"; a generated one is written in the engine's tiles):
  per pixel the terrain's normal at its centre (the four corners' normals blended), written as the
  FAF map editor writes it and the game reads it: x in alpha and z in green as 0.5 + n / 2, red and
  blue 0. Theta Passage's own normal map differs from the generated one by less than one step per
  channel on average.

- **Cancelling**: `IGenerator.Generate(document, CancellationToken)` (by default: refuses to start when
  cancelled, then runs to the end) lets the editor stop a run regenerating on its own; walkability
  passes the token into `NavGenerator.Generate`, which checks it between its steps.
- **The navigation mesh** (user story T-8, `Generation/NavigationMesh.cs`, `NavigationMeshGenerator`,
  id `navigationMesh`; the editor calls its regions "connected regions"): walkability's `MapNavigation`
  (its current result when there is one, `Reads` walkability) with, per layer, the regions largest first
  (`NavRegion`: cells, area, the starts in it, mass and hydrocarbon counts as the game assigns them). A
  start on ground no unit of the layer can leave belongs to the region within `StartReach` (8) ogrids,
  else to none (`StrandedOf`; HardFFA's eight starts crowd steep ground). On a 4096 map the regions take
  well under a second after walkability (`NavigationMeshTest.IsQuickEnoughOnTheLargestMaps`). Its inputs
  add `Spawns` to walkability's. FileFormats.Map's `NavPaths.FindRoute` (a route over a grid) is the
  Viewer's; the editor measures no routes.
- **The Procedural mode's settings** (`Files/InMemory/ProceduralSettings.cs`, `MapDocument.Procedural`,
  `map.json`'s optional `procedural`, `ProceduralDto`; absent for the defaults): `AutoGenerate` (ids that
  regenerate on their own), `NotOnExport` (ids the export leaves as they are), the draft's files, the
  path networks and the sky cube map. A `routes` written by an older editor is read and left out.
- **The workspace's defaults** (`Workspaces/ProceduralDefaults`, `workspace.json`'s optional `procedural`,
  `WorkspaceProceduralDto`: `autoGenerate`, `onExport`, each absent for the default and empty for none):
  what a map made or imported in the workspace regenerates on its own (`Default`: walkability, the
  navigation mesh, masks from rules) and which of `ExportChoices` (the normal map, the path networks) its
  export makes or writes (`Default`: the normal map). `ApplyTo(settings)` sets them on a map's settings (the
  path networks' choice only when on, so a map that never chose keeps "the first Generate turns it on"),
  `Of(settings)` takes a map's. `Workspace.Procedural`; `SandboxMap.Ensure` and `EditedMap.ImportMap(folder,
  defaults)` apply them, the editor's new maps too; a project opened keeps its own. Tests:
  `ProceduralDefaultsTest`.

## Rules: masks and props from the layers

User stories K-2 and P-4; generated only when asked, as the other results (F-7).

- **Mask rules** (`MapDocument.MaskRules`: a `MaskRuleSet` per mask name, `Files/InMemory/MaskRule`):
  an ordered list of `MaskRule`s on a value from 0 to 1 per pixel of the mask (its own size, each
  pixel at its centre). Slope (degrees, `PaintLimits.SlopeAt`), height (game units) and distance to
  water (ogrids, the exact Euclidean distance transform from the ground under the water level; a
  map without water is `MaskRules.Far` away everywhere) give 1 within `Min` to `Max` and fade to 0
  over `Soft`; a layer (`Source`: a mask's name or `stratum:N`, read between pixels, full from `Min`
  when above 0) and noise (`HeightNoise` at `Scale` ogrids, `Seed`) give their value. Each combines
  with what the rules before made (`Combine`: multiply, min, max, add, subtract, replace; the first
  rule is the start), after `Invert`. Blur (three box passes), grow and shrink (the largest or
  smallest value in a square, a queue per line), threshold and invert change what the rules before
  made. `MaskRules.GenerateAll` makes every mask with rules after the masks with rules it reads
  (`Order`; a circle is refused); `MaskRulesGenerator` (`masksFromRules`, an `IDocumentGenerator`)
  puts them in place. Its inputs are the heightmap, the water level, the strata, the rules and the
  masks without rules they read (`RuleInputs`: lists of parts compared by reference), so painting
  another mask or putting the result in place leaves it current.
- **Prop rules** (`MapDocument.PropRules`, `Files/InMemory/PropRule`): a name, a palette, layer
  conditions (`PropRuleCondition`: a layer covers at a threshold; every allowing one must, no
  forbidding one may), fade (the chance is the least allowing value), density per 100 square
  ogrids, spacing to every prop of the rules, clustering (`Noise`: patches where smooth noise is
  high; `Clusters`: copses around one centre per cell, as many cells as the cover asks), slope
  limits, out of the water, a seed and a `PropPlacement` (alignment by category, random turn,
  tilt, scale, sink). `PropRules.Generate` tries density times area / 100 places per rule (at most
  `MaxTries`), each drawn from the rule's seed, name and number (a change in one corner keeps the
  props elsewhere), kept apart by a shared `PropGrid`; later rules fill around earlier ones. Its
  props carry `Prop.Rule`; `PropRulesGenerator` (`propsFromRules`) replaces every prop with a rule
  (`RuleEdits.ReplaceRuleProps`, numbered after the props that stay) and never touches props
  placed by hand, which do not count for the rules' spacing. 20 000 props on a 20 km map take well
  under a second (`PropRulesTest`). On a map with a symmetry (`MapDocument.Symmetry`, an input of
  the generator) a rule that mirrors (`PropRule.Mirror`, on by default; `PropRuleDto.Mirror`
  written only when off) places each prop with its image in every other part (`ImagesOf`): a place
  is tried for one part's share of the density, and a prop goes only with all its images, each
  inside the map, allowed by the layers, the slope and the water where it lies and as far from
  every prop as the spacing asks; a place on an axis is its own image.
- **Edits** (`RuleEdits`): a mask's rules by place (`AddMaskRule`, `UpdateMaskRule`,
  `RemoveMaskRule`: without its last rule a mask is painted by hand again, `MoveMaskRule`,
  `SetMaskRules`); `MaskLayers.Rename` takes the rules along and points every rule that read the
  mask at the new name, `Remove` drops its rules. Prop rules by name, unique without case
  (`AddPropRule`; `UpdatePropRule`: a new name goes to its props; `RemovePropRule` with or without
  its props; `MovePropRule`; `KeepRuleProps`). A pasted prop is hand-placed (`ObjectEdits.Paste`).
- **On disk**: `masks/<name>.rules.json` (`MaskRulesDto`: only the parameters a rule's kind reads)
  beside the mask's PNG, which holds the last generated result; `prop-rules.json` (`PropRuleDto`);
  `rule` in `props.json` on the props a rule placed. All optional: a map without rules writes none
  of them, and an older project loads. Rules without their mask's PNG make an empty mask. `Reload`
  reads them again when another tool changes them. The export leaves the rules out and writes the
  props of rules as any other prop.

## Checks: problems of a map

User story F-14 (`Checks/`): an `IProblemCheck` (or a `ProblemCheck` made of a function) reads a
`ProblemContext` (the document; the workspace's catalogue and whether it has a file, the generated
results, the last walkability changes, the fingerprints of the unit presets: each may be missing) and yields `Problem`s (check, severity
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
- `steep-deposit` (warning): a mass or hydrocarbon deposit whose ground rises more than
  `MarkerEdits.MaxGroundVariation` over the square it needs (`MarkerEdits.Steepness`).
- `preset-changed` (a note): a group placed from a unit preset (U-9) whose preset's file changed
  since, or which the workspace has no preset of; reads the fingerprints the editor keeps
  (`ProblemContext.PresetFingerprint`, null for a name without a preset) and finds nothing without them.

## Unit templates from real maps

User story U-6. `Workspaces/UnitGroupScan` reads the `_save.lua` (and `_scenario.lua`) of every
map folder under some folders and classifies each group with units (`UnitGroupCategory`: a base
by its role groups `Defense`, `Factories`, `Energy`, `Mass`, `Misc`, `Engineers`, ... or by a
cluster of structures with a factory, a base role, defence, walls, wreckage, civilian, start,
scripting, other), the difficulty level of a campaign base (`_D1` inside `_D2` inside `_D3`), the
army's role (`ArmyRoles`: player, civilian, enemy), the faction, size and blueprints, and sums
the patterns of the names with numbers stripped; one-line Lua functions in a save's orders are
blanked first. `faf-map dev scan-units <folders...> --out scan.json` runs it.
`Workspaces/UnitTemplates` makes units presets from picks (`UnitTemplatePick`: map, army, group
path, role, wrecks, groups to leave out, a rectangle, blueprints, a radius about the median,
civilians only: civilian units without the civilian wall segments, `UnitGroupScan.IsCivilianWall`;
blueprints to leave out):
the subtree's units around their middle (a whole ogrid, so structures stay on the build grid),
in the base's role groups (`CanonicalRole`, `RoleOrder`), each on the terrain (y 0).
`faf-map dev unit-templates docs/map-editor/unit-template-picks.json <folders...> --out
src/FAForever.MapEditor.Project/Workspaces/ExampleUnitPresets` writes the shipped examples, which
`ExampleUnitPresets.Ensure` writes into a workspace's `presets/units` once (`.examples` names those
written; a copy equal to an earlier version, listed in `Earlier`, is updated; a copy equal to an
example that no longer ships, listed in `Removed`, is deleted; a changed one stays). The examples
are the civilians' only (owner's decision, October 2026): 26 settlements of the four building
styles, picked from the scan's civilian groups of 6 to 80 buildings and cut to one cluster with a
rectangle, `civilianOnly` stripping walls and defences, and 12 small wreck fields. Enemy bases and
players' starts are left to the map maker's own presets; `UnitPresets.ArmyFor` still places a
preset of any role. The scan itself is not committed: run it again into a scratch folder.
`Workspaces/UnitPresets` holds the preset records (`UnitPresetSettings` with the optional `Role`,
`Wrecks`, `Source`), `ArmyFor(document, role, x, z)` and `Place`. An enemy is never put in a
civilian army: on a map whose only extra armies are the civilians' (a new map's `ARMY_17` and
`NEUTRAL_CIVILIAN`) `ArmyFor` has none for it, and `Place` adds the extra army `EnemyArmy`
(`ARMY_9`, else the next free extra name; `ArmyOrNewFor` says which) in the same edit. The game's
folders, the coop maps and the vault are only read.
`Workspaces/UnitPalettes` (U-8) holds the palettes of the Units mode: the preset kind `palettes`, ten
slots per palette (`UnitPaletteSettings`, each `UnitPaletteSlot` a unit's id in lower case, a units
preset's name, or empty), `Read`, `SetSlot`, `ToPreset`, and the keys' order (`KeyLabel`, `SlotOfKey`:
1 to 9, then 0). `ExampleUnitPalettes.Ensure` writes twenty examples per faction (T1 base, T2,
defences, naval, civilian) into `presets/palettes` the way the example unit presets are written. The
user's settings keep the palette chosen last and the six units or presets placed last
(`UnitsSettingsDto`: `Palette`, `LastPlaced`).

## Tests

`tests/FAForever.MapEditor.Project.Tests` uses the three maps of the map library's tests (linked,
not copied). `GameContent` writes what the game reads from a save or scenario as text, without the
decoration the export derives; an imported and exported map must give the same text. A project
that is saved and loaded must export to the same bytes. Over the local vault copy, all 98 maps the
importer accepts (one folder holds two `.scmap` files) give the same game content after import and
export. `NormalMapTilesTest` makes a map stored in tiles from HardFFA (four tiles of 128) instead of
the game's 4096 maps, which are too large for assets.
