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
- Lua plugins (Lua-CSharp, later) get a facade over the session, never the document: reads give
  values, writes happen inside a transaction, so each plugin run is one undo step and a failing
  plugin changes nothing.

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
- `ProjectWatcher` reports the changed files of a folder once it has been quiet for a moment
  (tools save in bursts), on a pool thread. `faf-map pack --watch` opens the project once and
  reloads only what changed before each export.

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
  `UnitBlueprintDto`, read with FileFormats.Blueprints by `AssetBlueprints`), meshes as they are;
  scripts, animations and the rest are left out. The levels of detail get the names the engine
  fills in (`<name>_lod<n>.scm`, `_albedo.dds`, `_normalsts.dds`, `_specteam.dds`), and a file the
  archives lack is left out. It writes into `assets.new/` and replaces `assets/` when done; a stopped
  conversion goes on where it was. For game version 3839: 32 s, 2.9 GB, 6256 textures, 2188 meshes,
  563 props, 606 units.
- `AssetCheck` tells whether the assets are current: converted by this editor version (and
  `AssetConverter.FormatVersion`, raise it when the output changes) from the archives as they are.
  Only FAF's `.nx2` are checked; same size and time is unchanged, otherwise the SHA-256 decides.
- `Workspace.FindAsset("/env/Evergreen/layers/Eg_Grass001_albedo.dds")` gives the converted file.
- `GameTextures.Find(path, document, workspace)` finds a texture a map names as the game does: a
  path into `/maps/<any folder>/` is one of the map's own files (`MapDocument.Files`), any other
  one of the workspace's assets. Its `Key` is the same for every spelling of the path and its
  `Version` changes with the file, so a view can keep one copy per texture and reload it.
- `faf-map workspace init|convert|status` does the same without the editor.

## Tests

`tests/FAForever.MapEditor.Project.Tests` uses the three maps of the map library's tests (linked,
not copied). `GameContent` writes what the game reads from a save or scenario as text, without the
decoration the export derives; an imported and exported map must give the same text. A project
that is saved and loaded must export to the same bytes. Over the local vault copy, all 98 maps the
importer accepts (one folder holds two `.scmap` files) give the same game content after import and
export.
