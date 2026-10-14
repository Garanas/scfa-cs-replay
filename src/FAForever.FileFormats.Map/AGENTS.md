# Agent guide: FAForever.FileFormats.Map

The files of a map folder in the vault (`/maps/<name>.v<version>/`). The repository-wide guide is
[`../../AGENTS.md`](../../AGENTS.md); the Lua evaluator it builds on is described in the
[Blueprints guide](../FAForever.FileFormats.Blueprints/AGENTS.md). It references only
`FAForever.FileFormats.Lua`.

| File | Reader | Result |
|---|---|---|
| `<name>.scmap` (binary) | `ScmapParser.Parse(bytes or stream)`, written by `ScmapWriter.Write` | `Scmap`: heightmap, layers, lighting, water, decals, props, embedded DDS textures |
| `<name>_save.lua` | `MapSaveParser.Parse(text)`, written by `MapSaveWriter.Write` | `MapSave`: markers, areas, chains, armies with their unit groups |
| `<name>_scenario.lua` | `MapScenarioParser.Parse(text)`, written by `MapScenarioWriter.Write` | `MapScenario`: name, size, file paths, configurations with teams and armies |

## The binary format

The source is the engine's reader as reconstructed in [Draiget/faf-re](https://github.com/Draiget/faf-re)
(a C++20 rebuild of the Forged Alliance engine with the binary's addresses), **commit 2ba55b2**; every
statement below names the file it rests on, paths relative to that repository. Checked against 163
local maps (103 of a vault copy, 60 of the game). The FAF map editor's loader
(`Assets/Scripts/HazardX SCMAP Code/Map.cs` in FAForever/FAForeverMapEditor) has the same layout under
other names; where it differs, the engine wins.

- **Versions.** 53 (Supreme Commander), 56 and 60 (Forged Alliance) occur; 60 is what the engine
  itself saves (`kTerrainSaveVersion`, `src/sdk/moho/sim/CWldMap.cpp`): the game's own maps and those
  of the FAF map editor. The engine reads every version from 51 on; the parser and writer accept 53,
  56 and 60 (`ScmapParser.SupportedVersions`) and use the engine's version gates where they apply.
- The parser rejects trailing bytes: a field it skips or misreads shows up as an error, not as
  garbage further on (the engine reads nothing after the props and does not check).
- `ScmapWriter.Write(scmap)` is the reverse of the parser: a parsed map writes back to the same
  bytes (all 163 local maps, and the test assets in `ScmapWriterTest`). The one field the parser does
  not keep is the terrain's size in the preview's header, which equals the heightmap's in every map.
  If a map ever differs in a field written as a constant, keep the field in `Scmap` rather than
  guessing. The writer checks that the map fits its version and the engine's reader before it writes
  anything: the arrays follow the map's size, ten layers from 54 on and six before, two stratum masks
  from 54 on, exactly two texture slots per decal, a skybox from 58 on, nine numbers per decal of the
  strategic view.

### Block by block

`CWldMap::MapLoad` (`src/sdk/moho/sim/CWldMap.cpp`) reads the header, the preview, the terrain
(`CWldTerrainRes::Load`, same file) and the props (`CWldProps::Load`, same file), in this order:

| Block | What it holds | Engine source |
|---|---|---|
| Header | `Map\x1a`, file version 2 | `CWldMap::MapLoad` |
| Preview header | `0xbeeffeed` (optional to the engine), chunk version (2), the terrain's size (2 floats), a name in UTF-16 up to a zero word (`Scmap.PreviewName`), a count of (tag, value) `uint32` pairs (`PreviewMetadata`); the engine discards name and pairs. Empty and 0 in every map | `RWldMapPreviewChunk::Load` (`CWldMap.cpp`) |
| Preview | size + DDS (256 by 256, A8R8G8B8 in every map); size 0 shows `/textures/engine/b_fails_to_load.dds` | same |
| Terrain header | version, width, height, height scale; `(w + 1) * (h + 1)` `uint16` heights | `CWldTerrainRes::Load` |
| Composite byte | from **54**: whether the terrain shader is composite (`CompositeTerrainShader`). The engine sets it on save from the shader's `usage` annotation in `terrain.fx` (`ResolveTerrainCompositeShaderUsage`) and falls back to `TTerrain` for a shader that is not composite; 0 in every map, `TTerrainXP` maps included | `CWldMap.cpp` |
| Shader, background, sky cube | three strings | `CWldTerrainRes::Load` |
| Environment cubemaps | before **55** one string (named `<default>`); from 55 a count of (name, texture) | same |
| Lighting | 23 floats: multiplier, sun direction, ambience, sun colour, shadow fill, specular (4), bloom, fog colour (3), fog start, fog end | same; meaning of the fog floats from `WRenViewport::FogOn` (`src/sdk/moho/app/WxRuntimeTypes.cpp`) |
| Water | a flag (any byte but 0 is water; written 1), elevation, deep, abyss; then 20 floats, cube map, ramp, four repeats, four (movement, texture) | `CWaterShaderProperties::Load` (`src/sdk/moho/terrain/water/CWaterShaderProperties.cpp`) |
| Wave generators | count; each two strings, 13 floats, and from **52** four more | `WaveSystem::Load`, `WaveGenerator::LoadSerializedState` (`src/sdk/moho/terrain/water/WaveSystem.cpp`) |
| Strategic view | from **56**: contour bands (`ContourInterval`) and five colours (else the engine's defaults: 20 bands); from **57** the imager elevation offset | `CWldTerrainRes::Load` |
| Strata | before **54**: a discarded shader name (`Tileset`, `No Tileset`), a discarded `uint32` (`StoredLayerCount`, 6 in every map; the FAF map editor reads it as the layer count), then always six (albedo, normal, albedo size, normal size); from 54: ten (albedo, size), then nine (normal, size) | `CWldTerrainRes::LoadTexturing` (`CWldMap.cpp`) |
| Decals | the next decal id and next group id (`NextDecalId`, `NextDecalGroupId`), count, decals (id, type, a count of (length, text) slots, scale, position, rotation, far cut-off, near cut-off, army), count, groups (id, name, count, decal ids) | `CDecalManager::Load` (`src/sdk/moho/terrain/splat/CWldSplat.cpp`), `CWldTerrainDecal::DecalLoad` (`src/sdk/moho/render/CWldTerrainDecal.cpp`), `CDecalGroup::ReadFromStream` (`src/sdk/moho/render/CDecalGroup.cpp`) |
| Normal map | tile width and height (`NormalMapTileWidth`/`Height`: the map's size, at most 2048), count, tiles (size + DDS), row by row; **the engine seeks past the tiles unread** | `CWldTerrainRes::Load` (faf-re calls it a "legacy skip-list"), written by `CWldTerrainRes::Save` |
| Stratum masks | from **54** two (size + DDS); before 54 a count (1 in every map) of masks, the first serving as both | `CWldTerrainRes::Load` |
| Water map | a count (1 in every map; `ExtraWaterMaps` holds any after the first) of (size + DDS); the engine uses the first | same |
| Water masks | foam, flatness, depth bias: `(w / 2) * (h / 2)` bytes each, **from the heightmap's size** | same |
| Terrain types | `w * h` bytes | same; `STIMap` (`src/sdk/moho/sim/STIMap.cpp`) |
| Legacy strings | before **53** two discarded strings | same |
| Skybox | from **58** (before: a dome from the map's size, `SkyDome::SetupHorizonAndCirrus`) | `SkyDome::Load` (`src/sdk/moho/render/SkyDome.cpp`) |
| Strategic view decals | from **59**: a count of batches; each a technique (from **60**; before, `Decal`), a texture, a count of decals of nine floats (`CartographicDecals`, meaning not recovered); none in every map | `Cartographic::ReadDecals`, `CartographicDecalBatch::Read` (`src/sdk/moho/render/Cartographic.cpp`) |
| Props | count; each blueprint path, position, three rotation rows, scale (3 floats) | `CWldProps::Load` (`CWldMap.cpp`) |

- **The skybox** follows `SkyDome::Load`: after the sky objects come three strings, the cumulus
  clouds' textures (empty in every map known; the FAF map editor reads them as three bytes it calls
  `MidRgbColor`), and the cirrus layers are always four (the count before them is ignored by the
  engine and written as 4). The count of strategic view decal batches after the skybox is what the FAF
  map editor reads as `Clouds7`.
- **The normal map's tiles.** The water masks and the terrain types are sized from the heightmap,
  never from the two numbers before the normal map. Those are the size of a tile, which the engine
  caps at 2048: the game's 4096 maps (`SCMP_029`, `SCMP_030`, `X1MP_012`) store 2048 by 2048 and four
  DXT5 tiles. A version 53 vault map of 4096 (`SCMP_haz09fa`) stores 4096 and one tile, so the writer
  writes what `Scmap` holds rather than computing it; a new map gets `min(size, 2048)`
  (`Scmap.MaxNormalMapTile`), what the engine's `Save` writes. No other local map stores a size other
  than its own (the normal map's DDS itself may be smaller: some FAF maps store one of half or a quarter
  of the map's size).
- A decal's far cut-off comes before the near one; its texture slots are always two in the engine
  (`mNames[2]`; `DecalLoad` writes each slot it reads, so more than two overrun the decal; `DecalSave`
  writes two); every local decal has two (194,523).
- Decal types (`CWldTerrainDecalTYPETypeInfo.h`): 0 undefined, 1 albedo, 2 normals, 3 water mask,
  4 water albedo, 5 water normals, 6 glow, 7 alpha normals, 8 glow mask, 9 albedo XP. Local maps use
  0, 1, 2, 4, 6, 7 and 8.
- Fields keep a neutral name until there is evidence of their meaning; the evidence goes into the doc
  comment and this guide with its source.

### What the game does with the data

What the engine does after reading (all from faf-re, same commit; CONFIRMED in its code unless marked
INFERRED; none of it tested in the game yet):

- **The normal map is made, not read.** `CWldTerrainRes::Finalize` calls `InitNormalMap`
  (`CWldMap.cpp`): tiles of `min(w, 2048)` by `min(h, 2048)`, `ceil(w / tile) * ceil(h / tile)` of
  them, each a DXT5 sheet filled from the heightfield (`UpdateNormalMap`, rows along z outside, x
  inside; x of the normal in the alpha block, z in green). The stored tiles are for other tools only.
  faf-re notes (`WxRuntimeTypes.cpp`) that its rebuild lacked the `Finalize` call site and added it;
  the skip in `Load` is from the binary.
- **The water map is rewritten** from the heightmap: `Finalize` → `EnterEditMode` → `UpdateWaterMap`
  → `RebuildWaterMapRect` (`CWldMap.cpp`) sets every texel: alpha the foam mask, red the flatness
  mask, green the depth below the surface over the range to the abyss where any corner is under
  water, blue 255 where any corner is above it. The foam and flatness masks matter; the depth bias
  mask is read and saved but nothing in faf-re uses it beyond its getter (INFERRED unused). New masks
  default to foam 0, flatness 255, depth bias 127 (`CreateWaterMasks`). Without water the engine
  saves the elevations as -10000; many FAF maps keep 17.5 with the flag off, which is no water.
- **Stratum masks** are copied into sheets of `w / 2` by `h / 2` in `Finalize`; FAF maps that store
  them at the map's full size work, so the copy tolerates the mismatch (INFERRED). The game's maps use
  `w / 2`, the safe choice for a writer.
- **Layers** repeat `mapSize / size` times over the map (`CStratumMaterial::SetSize`,
  `src/sdk/moho/terrain/StratumMaterial.cpp`): the scales are world units per repeat; an empty path is
  skipped. The defaults (`StratumMaterial::StratumMaterial`) are evergreen layers at 4 and the upper
  `macrotexture000_albedo.dds` at 128.
- **Props** (`CWldProps::Load`): the three rotation rows become a quaternion, normalised, so skew and
  scale in them are lost; the three scale floats are read into ignored locals, and `CWldProps::Save`
  writes 1, 1, 1. Two vault maps store other scales (dddx_rpg_survival, sandstorm).
- **Decals.** The position is a corner: the footprint is the parallelogram from it along
  `(scale.x cos ry, scale.x sin ry)` and `(-scale.z sin ry, scale.z cos ry)`; only the turn around y
  shapes it (`ProjectDecalBoundsXZ`, `CWldTerrainDecal.cpp`). The cut-offs fade (`GetLODAlpha`, with
  `ren_DecalFadeFraction` 0.75 from `src/sdk/moho/misc/RuntimeTuningGlobals.cpp`): with a near cut-off
  above 0 the decal fades in from 0.75 of it up to it and is unseen closer, and the far one does not
  fade it; otherwise it fades out from 0.75 of the far cut-off to it. The far cut-off is also the key
  of its dissolve in the spatial database. A new decal's default cut-off is the diagonal of its
  footprint's bounds times `ren_DecalAlbedoLodCutoff` (albedo, water albedo, glow mask) or
  `ren_DecalNormalLodCutoff`, both 1 (`ComputeCutoffLOD`). The spatial bounds span heights 0 to 128
  whatever the terrain (INFERRED: a decal on ground above 128 may be culled).
- **Decal counters** (`CWldSplat.cpp`): a decal or group made at run time takes `NextDecalId++` or
  `NextDecalGroupId++` (a group named `Group_<n>`). Five FAF editor maps store 0 or too small values
  (arctic_refuge, open_palms, setons_clutch, DualGap_Adaptive, the cgn maps); a decal made at run time
  then reuses an id in the id lookup, harmless for play (INFERRED).
- **The strategic view** (`Cartographic::InitializeTerrain`, `InitializeTerrainTextures`,
  `Cartographic.cpp`): `ContourInterval` is the number of equal contour bands over the land above the
  deep water line, clamped to 1 to 100 (`kCartographicMaxTopographicBands`); FAF maps of version 56
  store 0 (one band), the game's maps 20 or 24. The five colours make a ramp of 256 texels: colour 0 at
  or below the deep water elevation, colour 2 between it and the surface, 3 to 4 blended over the land
  (without water the whole range). The imager elevation offset (`mImagerElevationOffset`) lifts the
  borders of the map imager above the terrain's middle height (`MapImager::UpdateMeshStances`,
  `src/sdk/moho/render/MapImager.cpp`); the game's maps store -19.5 to 6.3, FAF maps 0.
- **Wave generators** (`WaveSystem.cpp`): `FrameCount` is the ramp's value scale, `StripCount` the
  texture selection range (one of N strips at random), `Period*` the interval between waves, `Scale*`
  their size at the start and end, `Rotation` the angle.
- **Terrain types**: outside the map the type is 1, Default (`STIMap::GetTerrainType`), the
  assumption `NavGenerator` makes; a new map is filled with 1.
- **The preview's size floats** equal the terrain's size in every map.

### Other tools' habits seen in the files

- **The FAF map editor** names the cumulus strings `MidRgbColor` (three bytes, which equals three
  empty strings), the strategic view decal count `Clouds7`, and the decal counters `Unknown7` and
  `Unknown8`; it reads and writes `StoredLayerCount` as the number of layers. Its maps store `ContourInterval` 0 (v56),
  the imager offset 0, the decal counters sometimes 0, stratum masks at the map's full size, normal maps
  smaller than the map (256 by 256 on a 512 map) and water maps smaller than `w / 2` (64 by 64 on a 512
  map).
- **The game's own maps** store the 4096 normal map in four tiles of 2048, `ContourInterval` 20 or 24
  and imager offsets other than 0.
- Values checked across the local maps: the composite byte 0 everywhere, the preview name empty and no
  pairs, the version 53 layer number and mask count 6 and 1, one water map, no strategic view decals,
  the water flag 0 or 1.

### Where not to trust faf-re

- It is a **rebuild with FAF additions**, not a pure decompilation: `MeshRenderer::SetDistanceFog` in
  `FogOn` is marked "FAF:", and the `Finalize` call site was added by the rebuild
  (`WxRuntimeTypes.cpp`).
- **Wrong labels**: `CWldTerrainRes` names the five fog floats `mFogStartDistance, mFogCutoffDistance,
  mFogMinClamp, mFogMaxClamp, mFogCurveExponent`, but its own `WRenViewport::FogOn` uses the first three
  as the fog's colour and the last two as its start and end (our names, `FogColor`, `FogStart`,
  `FogEnd`, are right); the normal map block is called a "legacy skip-list"; comments in `CWldMap.cpp`
  say "24 streamed floats" where the code reads 23.
- **Stale comments**: `Cartographic.cpp` says `CWldTerrainRes::Load` is "not yet recovered"; it is in
  `CWldMap.cpp`.
- **Suspect code**: `CartographicDecalBatch::Write` writes `sizeof` raw bytes of the string objects,
  which cannot match `Read`; trust `Read` for the layout. The ramp thresholds of the strategic view
  give colour 0 and colour 1 the same threshold, so colour 1 (`ContourColor`, 0xff707070 in every map)
  is never chosen there; one of them is likely mis-recovered: its meaning is uncertain.
- **Unrecovered**: `SkyDome::GetEffect` returns nothing (`SkyDome.cpp`), the shoreline is an opaque
  blob (`src/sdk/moho/terrain/water/Shoreline.cpp`), some decal fields are still `mUnknown94`,
  `mUnknownA0` (the latter filters decals out of `Save`), and the nine numbers of a strategic view decal
  have no names.
- The water shader's reader was corrected once in faf-re (a comment in `CWaterShaderProperties.cpp`);
  every block above was checked here against the local maps (a walk of the files lines up byte for
  byte), so prefer that check to faf-re's comments.

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
- `MapSaveWriter` and `MapScenarioWriter` are the reverse of the parsers: what they write parses
  back to the same content (the test assets, and all 99 saves and scenarios of the local vault copy;
  `MapDump` in the tests compares them). The typed properties are the source; `Raw` supplies only
  the keys without a property (a marker's `amount`, a scenario's `norushoffsetX_ARMY_1`). The save
  writer wraps the fields of markers and areas as the map editors do (`MapLuaFunctions.Wrap`).
- The platoons, orders and platoon builders of co-op saves are not kept: the writer leaves them
  empty. Only four co-op missions of the vault fill them, and no map maker edits them.
- The `next_*_id` counters of a save belong to the map editors, not the game: the writer keeps the
  stored value and raises it past the names in use (`UNIT_302` gives at least 303).

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
  map's edge the terrain type reads as `Default`. The engine does so in faf-re
  (`STIMap::GetTerrainType`, "What the game does with the data" above); not yet seen in the game.
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
- `MapSymmetry`: the kinds (`MapSymmetryKind`) are those that map a rectangle or a square onto
  itself, the turns for 3 and 5 to 8 players (for maps played inside a circle) and their mirrored
  versions: rotational (2 to 8 parts; 2 is the point symmetry, the same as a point mirrored through
  the middle), the four mirrors, both mirrors (`MirroredBothWays`, 4 parts, any rectangle), both
  diagonals (4, a square) and `Kaleidoscope` (count turns each mirrored, 4 to 16 parts; four turns
  is the eight-way symmetry of a square, two the same as both mirrors). `Detect(area, positions)`
  finds, from the start positions and resource markers (2 ogrids of tolerance), the kind with the
  most parts under which every position has a counterpart (the diagonals and turns other than half
  a circle only on a square), so a map with a base in each corner is both mirrors, not the half turn.
  It also mirrors (the map editor's symmetry, user story F-12): `Transforms(kind, count, area)` (or
  `width, height` for the whole map) gives one `MapSymmetryTransform` per part, the identity first
  (rotational: turned clockwise in the top view around the area's middle; a mirror: two; the
  dihedral kinds each turn followed by its mirror, `MapSymmetryTransform.Mirror` and `Then`),
  which maps a point, a position (its height kept), a direction, a yaw and a rotation matrix;
  `Images` the points, `Guides` the lines between the parts (a mirrored kind's a line per mirror,
  at most 8, what the editor's 3D overlay holds; none for the point symmetry, whose guide is its
  middle, `Point`). The area (`MapSymmetryArea`) is the whole map or
  a playable area off its middle. Orientations follow `Matrix4x4.CreateRotationY`: a yaw faces
  `(sin, cos)` on (x, z), a matrix's rows are an object's own axes, its z axis where it faces; a
  mirror keeps a rotation a rotation (it turns the object's own x axis around).
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

The maps of 4096 by 4096 (the game's `SCMP_029`, `SCMP_030`, `X1MP_012`, over 100 MB each) are no
assets: `ScmapParserTest` writes HardFFA with a normal map of four tiles of 128 to test the sizing of
the grids from the heightmap. To check every local map, parse and write back each `.scmap` of the
vault copy and the game's `maps` folder with a throwaway program (`#:project` on this library); all
163 parse and write back to the same bytes (2026-10-11).
