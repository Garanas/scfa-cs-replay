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

## Tests

`tests/FAForever.FileFormats.Map.Tests/assets/maps/` holds one map per version, with only the
`.scmap`, `_save.lua` and `_scenario.lua` (no textures or scripts): Theta Passage - FAF version
(60, 5 km), HardFFA (56, 5 km) and the co-op mission SCCA_Coop_R01 (53, 10 km, with nested unit
groups, areas and chains). Keep new assets small: a 5 km map is about 1 MB.
