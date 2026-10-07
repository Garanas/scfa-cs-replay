# Agent guide: FAForever.FileFormats.Map

The files of a map folder in the vault (`/maps/<name>.v<version>/`). The repository-wide guide is
[`../../AGENTS.md`](../../AGENTS.md); the Lua evaluator it builds on is described in the
[Blueprints guide](../FAForever.FileFormats.Blueprints/AGENTS.md). It references only
`FAForever.FileFormats.Lua`.

| File | Reader | Result |
|---|---|---|
| `<name>.scmap` (binary) | `ScmapParser.Parse(bytes or stream)` | `Scmap`: heightmap, layers, lighting, water, decals, props, embedded DDS textures |
| `<name>_save.lua` | `MapSaveParser.Parse(text)` | `MapSave`: markers, areas, chains, armies with their unit groups |
| `<name>_scenario.lua` | `MapScenarioParser.Parse(text)` | `MapScenario`: name, size, file paths, configurations with teams and armies |

## The binary format

- The layout follows the FAF map editor's loader (`Assets/Scripts/HazardX SCMAP Code/Map.cs` in
  FAForever/FAForeverMapEditor). Versions 53 (Supreme Commander), 56 (Forged Alliance) and 60 (FAF
  map editor) exist; they differ in the cubemaps (53 stores one), the layers (53 stores a tileset
  and its own count of albedo/normal pairs, 56+ ten albedos then nine normal maps), the minimap
  colours (56+), the stratum masks (one or two) and the skybox (60).
- The parser rejects trailing bytes: a field it skips or misreads shows up as an error, not as
  garbage further on. Every map in a local vault copy of 100 maps (all three versions) parses.
- A prop starts with its blueprint path, then position, three rotation axes and scale. A decal's
  far cut-off comes before the near one.
- Unknown fields keep a neutral name (`ScmapMinimap.Unknown`, `Scmap.DecalHeader`); give them a
  meaning only with evidence.

## The Lua files

- They call the functions of `lua/dataInit.lua` in the FA repository (`STRING`, `FLOAT`,
  `BOOLEAN`, `VECTOR3`, `RECTANGLE`, `GROUP`, ...), mirrored in `MapLuaFunctions`. `GROUP` tags its
  table with `type = 'GROUP'`, which is how a unit group is told apart from a unit (whose `type`
  is its blueprint id).
- Markers come from `Scenario.MasterChain._MASTERCHAIN_.Markers`, the only chain the game reads
  (`lua/sim/ScenarioUtilities.lua`). Start positions are the markers named after an army.
- Co-op saves name the engine's `categories` in their platoon builders (`categories.ual0105`);
  `MapSaveParser` predefines each name the file uses as a string.
- The parsers read the tables with `LuaTableReader` from the Lua library; a reading helper that
  more than one library needs belongs there, not here.
- Every result keeps its full Lua table in `Raw` for the fields that have no property.

## The navigational mesh

`NavGenerator.Generate(scmap, save, playableArea)` ports `lua/sim/NavGenerator.lua` (FA repository):
per layer (land, water, hover, amphibious) the pathable ogrids, their regions (`NavLabel`) and the
mass and hydrocarbon markers in each. The constants (`MaxHeightDifference`, the depths, the cull
size) carry the Lua names; keep them in step with the file.

- It works on the ogrid grid instead of the game's quadtrees, which gives the same regions: the
  game's compression only turns mixed blocks of the threshold's size into unpathable ones
  (`ApplyCompression`; naval uses twice the threshold), and diagonal leaves only link through a
  pathable side neighbour. The known differences (label order, the first leaf's area counted
  twice, hydrocarbons counted as extractors) are listed on `NavGenerator`.
- The game samples the terrain type and the playable area at an ogrid's far corner; past the
  map's edge the terrain type reads as `Default`. That last part is an assumption about the
  engine: verify it in the game before relying on the outermost row and column.
- The playable area is the save file's first area when it is larger than 32 by 32 ogrids, on
  skirmish maps (`NavGenerator.FindPlayableArea`); otherwise the whole map. Map scripts are not run.
- There is no output of the game to compare with yet. The generator logs its per-layer counts
  (`NavLayerData`) in the game log; those of the test maps would make a good regression test.
- `TerrainTypes` is a copy of the type codes, names and `Blocking` flags of `lua/TerrainTypes.lua`
  as C# data; it names the FA commit it was copied from. Copy it again when the file changes.

## Routes and the layout of a map

What the Viewer's map page shows beyond the files themselves:

- `NavPaths`: distances and routes over a layer, in eight directions (a diagonal only past two
  pathable ogrids, the game's rule). `FindRoute` is an A* search; `NearestTwo` finds, in one search
  from all start positions, the two nearest ones for every ogrid, with a ring of buckets instead of
  a heap (steps of 1000 and 1414). Both matter in the browser: eight separate searches over a 20 km
  map took 15 s in the WebAssembly interpreter, this takes 0.4 s.
- `ExtractorLayout`: the roles of the mass spots after a guideline for map makers from the LOUD
  Discord (safe, expandable, raidable, contestable; at least 4, at least 3, 2 to 6 and 1 to 2 per
  player). `Measure` is the expensive part and runs once, on the layer that links the start positions
  and in blocks of the game's compression threshold (`NavGrid.Coarsen`); `Classify` runs per set of
  thresholds. The guideline names the roles, not the distances: the defaults (base radius 60 ogrids,
  contested within 15%, expansion spots within 25 ogrids) were checked with the owner on Theta Passage
  (4 safe, 0 expandable, 7 raidable, 1 contestable per player) and Glacier Valley.
- `MapSymmetry`: rotational or mirrored, from the start positions and resource markers (2 ogrids of tolerance).
- `MapArchive`: reads the vault's zip piece by piece: the directory from the end, then only the
  entries asked for, so the browser fetches the three map files with range requests and skips the
  textures. No ZIP64.
- The `.scmap` starts with the map's preview, a DDS of 256 by 256 pixels, uncompressed A8R8G8B8 in all
  100 maps of a local vault copy; the Viewer decodes it in `js/maps.js`.

## Tests

`tests/FAForever.FileFormats.Map.Tests/assets/maps/` holds one map per version, with only the
`.scmap`, `_save.lua` and `_scenario.lua` (no textures or scripts): Theta Passage - FAF version
(60, 5 km), HardFFA (56, 5 km) and the co-op mission SCCA_Coop_R01 (53, 10 km, with nested unit
groups, areas and chains). Keep new assets small: a 5 km map is about 1 MB.
