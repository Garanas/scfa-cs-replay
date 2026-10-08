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
prop (whose id exists in memory only) are mapped by hand in `Files/MapProjectMapping.cs`.

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
- Each stratum mask texture holds four layers, one per channel (red, green, blue, alpha): layer 1
  to 4 in the first, 5 to 8 in the second. A project keeps one grey PNG per layer.

## Tests

`tests/FAForever.MapEditor.Project.Tests` uses the three maps of the map library's tests (linked,
not copied). `GameContent` writes what the game reads from a save or scenario as text, without the
decoration the export derives; an imported and exported map must give the same text. A project
that is saved and loaded must export to the same bytes. Over the local vault copy, all 98 maps the
importer accepts (one folder holds two `.scmap` files) give the same game content after import and
export.
