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
  duplicate copies before mirroring the copies). `Checks/SymmetryChecks` is the
  `not-symmetric` check, given the transforms by the caller.
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

**Generated path networks** (user story R-3, `Generation/PathNetworks`, `PathNetworkGenerator`, an
`IDocumentGenerator` with walkability's inputs): per layer the AI's graph from the navigational
mesh (land, water, amphibious; air on a grid over the playable area). The map is cut into squares of
the spacing (`DefaultSpacing`: a sixteenth of the map, 16 to 64 ogrids); each 4-connected part of
passable ground in a square that covers a sixteenth of it gets a node at its ogrid furthest from
blocked ground (to a quarter of the spacing), nearest the square's centre; parts that touch across a
square's edge are linked, with nodes along the way through them where the straight line would cross
blocked ground (`CanWalk`: every ogrid a line passes, both beside a corner, of the start's region);
diagonal squares link where walkable and not linked through a neighbour; last every region is made
one network (pieces joined along `NavPaths.FindRoute`). The map's own nodes of a layer stay unless
`Replace`: they are nodes from the start with their links, a part that holds one takes it, and two
that reach each other get no new way, so generating again adds nothing. `Apply` names new nodes as
the map editors do (`LandPN`, `WaterPN`, `AmphPN`, `AirPN`, going on from the highest number), puts
them on the terrain and links both ways; replacing takes the layer's nodes out of chains too.
`PathNetworksTest` checks on the test maps that no link crosses blocked ground and every region is
one network.

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
The editor writes neither the options nor the script; civilians by the number of players are not
part of the convention.

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
- **Where an asset comes from** (F-8, `AssetOrigins`): `Read(workspace)` reads the lists of files
  (never their content) of the archives the manifest names, laid over each other as the converter
  did, and `Of(entry)` answers an `AssetSource`: the game's (`env.scd`), FAF's (`env.nx2`, with
  `ReplacesGame` when the game has the file too), the workspace's own (brushes) or the map's own
  (`/maps/...`); `Unknown` when the archives moved. FAF's `.nx2` hold every blueprint, so every
  prop and unit is FAF's by its blueprint; textures and decals are mostly the game's.
- **Missing assets** (F-8, `MissingAssets.Find(document, catalog)`): what a map uses that neither
  the catalogue nor the map's own files have, with how often and the check that finds it too
  (`missing-texture`, `missing-model`), most used first.
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
- `faf-map workspace init|convert|status` does the same without the editor. `faf-map workspace scratch
  <folder> --from <workspace> [--settings <file>]` makes a scratch workspace for test editors
  (`ScratchWorkspace`: its own projects, presets, copied brushes and a vault inside it, the other
  workspace's assets through a directory junction on Windows, a symbolic link elsewhere, a copy
  when neither works); `remove-scratch` deletes it, the junction first, since .NET does not delete
  a junction inside a recursive delete. Converting assets in a scratch workspace would replace the
  linked ones.
- **The sandbox** (`Workspaces/SandboxMap`, user story F-5): `projects/Sandbox`, a map of 256
  ogrids with two armies and one of every kind of thing (spawns, mass, hydrocarbon, three land path
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
  `PresetLibrary`), a folder per kind (lower-case letters, digits and dashes: `brush`, `lighting`,
  `water`, `units`, `markers`, `props-palette`) and a file per preset (`PresetDto`: format version,
  kind, name, settings). The settings are a JSON object whose shape the feature of the kind owns:
  `Preset.Of(kind, name, record)` writes a record of its own as the editor writes JSON, and
  `preset.Read<T>()` reads it back. The file name is the name with the characters a file name
  cannot hold made dashes; the name itself is in the file. The library keeps nothing in memory:
  every call reads the folder, so presets other tools or editors write show at once; `List` leaves
  out a file it cannot read and names it.

## New maps, heightmaps, strata and areas

- `NewMap.Create(NewMapSettings)` (user story M-2) makes a version 56 map from scratch, as the
  FAF map editor's new map: one of the game's sizes (`NewMap.Sizes`, 256 to 4096 ogrids), flat at
  a height, water with its deep and abyss elevations (5 and 15 below unless given), the strata
  textures of a biome (`NewMap.Biomes`, static data: lower layer, four strata, the macro texture
  as upper layer), spawns on a circle, `AREA_1` over the whole map, the extra armies
  `ARMY_17` and `NEUTRAL_CIVILIAN`, the skirmish script (`NewMap.DefaultScript`), the FAF editor's
  default lighting and water. Masks and the water's images are half the map's size, as in the
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

## Height layers and curves

User stories T-5 and T-4 (`Files/InMemory/HeightLayer.cs`, `Editing/HeightLayers`, `HeightLayerEdits`,
`HeightCurves`).

- `MapDocument.HeightLayers`, lowest first, empty for a map without them (its heightmap is simply
  its heights). With layers, `Terrain.Heightmap` is what they add up to, so everything that reads
  the heights (views, walkability, normal map, export) is unchanged. The base (`Kind.Base`, the
  heights as they were when the stack began) is always the lowest layer, one per map, at full
  strength, unmasked, replacing; it cannot be moved or removed. `HeightLayerEdits.Bake` ends the
  stack: the heightmap keeps the sum.
- A layer: name (a file name, unique without case), kind (`Base`, `Sculpt`, `Imported`, `Curve`),
  `Combine` (`Add` differences; `Maximum`, `Minimum`, `Replace` absolute heights), `Strength` 0 to
  1, `Mask` (`mask:<name>` for a mask for tools, `stratum:<n>`; a missing mask counts as none, a
  renamed mask for tools is followed), `Visible`, `Locked`, `Values` in game units at the
  heightmap's corners (its tiles are the heightmap's tiles) and an optional `Coverage` (0 to 255:
  a road's soft edge, the part of a replacing sculpt layer that was sculpted). Per corner the
  weight is strength × mask (bilinear between the mask's pixels, any size) × coverage.
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
- `SetCombine` of a sculpt layer between adding and an absolute way converts its values over the
  layers below, so it keeps its look. `HeightmapFiles.Import` and a `heightmap.png` another tool
  saves go into the base of a map with layers.
- **Curves** (`HeightCurve`: points with a height, `Profile` ridge, valley, road, cliff; width,
  falloff, closed): a Catmull-Rom spline about an ogrid between samples (`HeightCurves.Spline`);
  `Rasterize` makes the layer's values (ridge `+h`, valley `-h`, cliff `+h` on the left of the
  direction of travel in the top view, all `Add`; a road `h` with coverage, `Replace`), full over
  the width (a cliff: a drop of an ogrid at the curve, full over the width on the high side) and
  a smooth fade over the falloff. `SetCurve` rasterizes again, reusing the tiles that come out the
  same, within the tiles the old and new curve reach; a new profile brings its way of combining.
- **On disk**: `layers/layers.json` (`HeightLayersDto`; absent without layers) and per layer except
  curves `layers/<name>.png` (16-bit, sample 0 at `offset`: -32768 steps for adding layers, 0
  otherwise) and `layers/<name>.coverage.png`. Load adds the layers up again (the saved
  `heightmap.png` is their sum, written by the editor). A save writes only the layers whose values
  changed. Reload reads `layers/` again, an imported layer's `Source` (relative to the project when
  inside it) when that file changes, and a changed `heightmap.png` into the base. Old projects have
  no layers.

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
- **The normal map** (user story S-4, `Generation/TerrainImages.NormalMap`, `NormalMapGenerator`):
  per pixel the terrain's normal at its centre (the four corners' normals blended), written as the
  FAF map editor writes it and the game reads it: x in alpha and z in green as 0.5 + n / 2, red and
  blue 0. Theta Passage's own normal map differs from the generated one by less than one step per
  channel on average.

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
  under a second (`PropRulesTest`).
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
