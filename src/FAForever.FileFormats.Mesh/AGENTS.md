# Agent guide: FAForever.FileFormats.Mesh

The game's meshes (`.scm`, version 5): `ScmParser` reads one into an `Scm` record, `ScmWriter`
writes it back, `GlbWriter` and `GlbReader` turn it into glTF 2.0 (`.glb`) and back, and
`MeshPreview` draws it into a small picture on the processor, shaded as the game's
`effects/mesh.fx`. The repository-wide guide is [`../../AGENTS.md`](../../AGENTS.md), the rules for
every FileFormats library are in [`../AGENTS.md`](../AGENTS.md) (pure managed code, it may run in
WebAssembly). The map editor converts the game's meshes with this library
([`../FAForever.MapEditor.Project/AGENTS.md`](../FAForever.MapEditor.Project/AGENTS.md), "The
workspace and the game's assets") and draws them in its 3D view
([`../FAForever.MapEditor.App/AGENTS.md`](../FAForever.MapEditor.App/AGENTS.md), "Props, units and
markers").

## Where the knowledge comes from

- **The engine**: [Draiget/faf-re](https://github.com/Draiget/faf-re), a C++20 reconstruction of the
  Forged Alliance engine from its binary, read at **commit `2ba55b2`** (2026-10-08). Paths below are
  relative to its `src/sdk/`: `moho/resource/SScmFile.h` and `.cpp` (the file's layout),
  `moho/resource/ResourceFactory.cpp` (loading), `moho/resource/RScmResource.cpp` (bounds),
  `moho/resource/RScaResource.h` and `.cpp` (animations), `moho/animation/CAniSkel.cpp`,
  `CAniPose.cpp`, `CAnimationManipulator.cpp`, `moho/mesh/Mesh.h`, `Mesh.cpp`, `MeshBatch.cpp`,
  `HardwareMeshBatch.cpp`, `ShaderDictionary.cpp`,
  `moho/resource/blueprints/RMeshBlueprint.cpp` and `RMeshBlueprintLODTypeInfo.cpp`,
  `gpg/gal/MeshVertex.h` and `.cpp`, `moho/entity/Prop.cpp`, `UserEntity.cpp`. faf-re is a
  reconstruction: see "Where not to trust faf-re" below.
- **The game's data**, checked against the engine's reading with throwaway scripts (2026-10-11):
  every `.scm` and `.sca` of the game's `units.scd`, `env.scd`, `meshes.scd`, `projectiles.scd` and
  `props.scd` (1906 meshes, 474 animations) and of FAF's `units.nx2`, `env.nx2`, `meshes.nx2` and
  `projectiles.nx2` (605 meshes, 139 animations), and `effects/mesh.fx` of the game's `effects.scd`
  and FAF's `effects.nx2`. "All meshes" below means those 2510 version-5 meshes.
- **The tools' headers**: the header files GPG released with the game's tools
  (https://ftp.zx.net.nz/pub/Game-Files/SupCom/util/) and the Blender exporter
  (Solstice245/scstudio), which the parser was first written from.

## The `.scm` file as the engine reads it

Little endian throughout. The loader rejects only a file shorter than its 0x30-byte header
(`ResourceFactory.cpp`, the `.scm` factory: `byteCount < 0x30`): it checks no magic number, no
version, no section name and no offset. `ScmParser` checks more (the magic, version 5, offsets,
indices and parents inside the file) but reads the same fields.

### Header (0x30 bytes; `NAME` follows at 0x3C, the bone names at 0x40)

| Offset | Field | The engine (`SScmFile.h`) |
|---|---|---|
| 0x00 | `MODL` | not read (`mUnknown00`) |
| 0x04 | version, 5 | not read |
| 0x08 | offset of the bones | read (`mBoneTableOffset`) |
| 0x0C | skinned bone count (`Scm.SkinnedBoneCount`) | read (`mSkinBoneCount`): the bones of the skinning palette and the bones the render pose interpolates (`MeshBatch.cpp`, `CAniPose.cpp`, `Mesh.cpp` `UpdateInterpolatedFields`); the bones after them are attachment points (`total - skinned`) |
| 0x10 | offset of the vertices | read |
| 0x14 | offset of extra vertex data (`VEXT`) | not read; 0 in all meshes |
| 0x18 | vertex count | read |
| 0x1C | offset of the indices | read, copied verbatim into a 16-bit index buffer (`HardwareMeshBatch.cpp`) |
| 0x20 | index count | read; triangles are count / 3 |
| 0x24 | offset of the notes (`INFO`) | not read (`mUnknown24`) |
| 0x28 | size of the notes | not read |
| 0x2C | bone count | read (`mBoneTotalCount`) |

- **The bone names are read one after the other from byte 0x40** (`SScmFile.cpp`, `GetBoneNames`),
  as many zero-terminated strings as there are bones; the name offset in each bone record is
  ignored. All meshes have their names there and their offsets equal to the sequential ones.
  `ScmParser` reads them the engine's way, `ScmWriter` always writes them at 0x40.
- **Section names** (`SKEL`, `VTXL`, `TRIS`, `INFO`, the four bytes before each section) are never
  read; `ScmParser` ignores them too.
- **The skinned bone count**: the game's exporter stores one more than the highest bone a vertex
  uses (`ScmWriter.WeightedBoneCount`), equal in all 1905 game meshes. It is never below that in any
  mesh. The engine shares **80** palette slots between the instances of a draw
  (`Mesh.h`, `kPaletteCapacity = 0x50`); a unit mesh with more skinned bones gets an instance budget
  of 0, and the original binary then hangs or writes past the palette (`MeshBatch.cpp`, which
  guards against it; see "Where not to trust faf-re"). The largest: 58 in the game
  (`UEL0401_lod0`), 64 in FAF (`URB0101_LOD0`). `ScmWriter.MaxSkinnedBones` refuses more.

### Bone record (0x6C = 108 bytes, `CAniSkel.cpp`, the skeleton's constructor)

| Offset | Field (`ScmBone`) | The engine |
|---|---|---|
| 0x00 | `RestPoseInverse`, 4 × 4 floats row by row | rows 0 to 2: a 3 × 3 basis, made a quaternion (`MatrixColumnsToQuatCanonical`); row 3 xyz: a translation. Together the inverse bind transform used by skinning (`HardwareMeshBatch.cpp`) and the per-bone bounds. The fourth column is ignored, scale or shear in the 3 × 3 is lost. |
| 0x40 | `Position`, 3 floats | the rest pose's local translation (parent space) |
| 0x4C | `Rotation`, w x y z | the rest pose's local rotation |
| 0x5C | offset of the name | not read |
| 0x60 | `Parent`, -1 for the root | read |
| 0x64 | `FirstVertex` | not read (`mUnknown64`); the game's exporter stores the index of the bone's first vertex |
| 0x68 | `TriangleCount` | not read; the game's exporter stores the bone's number of triangles |

- `FirstVertex` and `TriangleCount` are identified from the data: the game's exporter sorts the
  vertices and triangles by bone (`Bone0`), and these match in all 1904 game meshes with bones and
  the 257 FAF meshes that fill them in. They only matter for writing back to the same bytes.
- **A parent comes before its children**: `CAniPose::SetWorldTransform` invalidates the pose in one
  forward pass, testing the parent's dirty flag (`CAniPose.cpp`). All meshes keep to it;
  `ScmWriter` refuses a bone before its parent.
- **Conventions** (checked on the data): composing the local `Position` and `Rotation` from the root
  to a bone with `System.Numerics` (row vectors, `Matrix4x4.CreateFromQuaternion`) gives the inverse
  of `RestPoseInverse` in all meshes but 27 of the game's and about 20 of FAF's; so `GlbWriter` writes
  `Rotation` as a node's rotation and `RestPoseInverse` as its inverse bind matrix.
- Bones are found by name with a case-sensitive binary search (`CAniSkel.cpp`, `FindBoneIndex`): the
  Lua scripts' bone names and an animation's tracks must match the case.

### Vertex (0x44 = 68 bytes, `SScmFile.h`; the shader inputs in `gpg/gal/MeshVertex.h`)

| Offset | Field (`ScmVertex`) | Shader input |
|---|---|---|
| 0x00 | `Position` | POSITION (w = 1) |
| 0x0C | `Normal` | NORMAL |
| 0x18 | `Tangent` | TANGENT |
| 0x24 | `Binormal` | BINORMAL |
| 0x30 | `Uv0` | TEXCOORD0.xy: the albedo and the specular (team colour) map |
| 0x38 | `Uv1` | TEXCOORD0.zw: the normal map |
| 0x40 | `Bone0` to `Bone3`, one byte each | BLENDINDICES; every skinning site of `mesh.fx` reads only `boneIndex[0]` |

- The order matches the engine's (`HardwareMeshBatch.cpp`, the vertex copy). The file holds plain
  floats; on the card the engine prefers half floats when the device allows instancing and
  float16 (`MeshVertex.cpp`, `Float16HardwareVertexFormatterD3D9` first): about three significant
  digits, 0.03 ogrid at 32 ogrids from the mesh's origin.
- `Bone1` to `Bone3` are 0 in all meshes and never read: a vertex moves with one bone, weight 1.
- Positions are in the mesh's space in the rest pose.

### Indices, notes, padding

- 16-bit indices, a triangle list. The front winds counter-clockwise in a right-handed reading of
  the coordinates: (b − a) × (c − a) points along the vertex normals in 99.9% of the triangles
  (1 218 052 against 1 290 in the game, 557 821 against 1 147 in FAF). `mesh.fx` sets no cull mode.
- `INFO` (the exporter's notes) is never read; 4 FAF meshes have none.
- The game's exporter pads with 0xC5 to multiples of 32; other exporters pad with 0x58 or 0x00 and
  261 FAF meshes are not aligned to 32. The engine does not care. `ScmWriter` writes the game's
  layout, so a mesh of the game writes back to the same bytes (all 1905 of the game's and 261 of
  FAF's); the others read back the same.
- **At most 65,535 vertices** (`ScmWriter.MaxVertices`): an unskinned (prop) batch holds
  `0xFFFF / vertexCount` instances (`MeshBatch.cpp`), none at 65,536.

## How the engine uses a mesh

- **Props are drawn unskinned, units skinned** (`UserEntity.cpp`, `CreateMeshInstance(IsUserUnit())`).
  A prop is one instance matrix (with per-axis scale) and its vertices as stored (the unskinned
  branch of `HardwareMeshBatch.cpp`), so drawing the raw vertices, as the editor does, is exactly
  the game's. A unit's palette entry is the posed composite transform times the inverse bind
  (`RestPoseInverse` as a quaternion and a translation) times the instance's scale
  (`HardwareMeshBatch.cpp`); hidden bones are parked far below the world. Bones past the skinned
  count are not in the palette.
- **The rest pose**: the pose starts at each bone's local transform (`CAniPose.cpp`), positions
  times the pose's scale. Where `RestPoseInverse` is not the inverse of the local chain (27 game
  meshes, about 20 FAF ones; on a bone with vertices up to 0.4 ogrid in `UEB2305_lod0`, 5.2 in FAF's
  `UAA0203_lod1`), a posed unit differs from its raw vertices. The editor draws raw vertices.
- **Scale**: a prop's or unit's `Display.UniformScale` of its own blueprint (`Prop.cpp`, `Unit.cpp`);
  the mesh blueprint's `UniformScale` is used only by the map imager (`MapImager.cpp`).
- **Bounds**: the box around every vertex of LOD0, also unreferenced ones; `mSize` = 1.2 × the
  box's largest side (`RScmResource.cpp`). The culling sphere is centred on the entity's position
  (not the box's centre), radius max(scale) × `mSize` / 2 (`Mesh.cpp`); the oriented box is the
  box scaled and turned with the instance. Per-bone boxes in bone space use only `Bone0` and start
  at 0, so they hold the bone's origin (`CAniSkel.cpp`, `UpdateBoneBounds`); a vertex bone past the
  bone count logs "Encoutered bad SCM file" and asserts.
- **Levels of detail** (`Mesh.cpp`, `Mesh::ComputeLOD` and the render pass before it): the first
  LOD whose cutoff reaches the distance; a cutoff of 0 or less shows that LOD at every distance; the
  last LOD (marked `useDissolve` by `Mesh::Load`) fades out over `ren_MeshDissolve` past its cutoff
  and is hidden beyond. The distance is the **view depth** (the entity's position dotted with the
  camera's plane), not the distance to the eye. A missing `LODCutoff` is **1000**
  (`RMeshBlueprint.cpp`, the constructor of `RMeshBlueprintLOD`). LOD1 and further use LOD0's
  skeleton, their bones matched to LOD0's **by name**, an unmatched one to bone 0 (`MeshBatch.cpp`).
- **Names a mesh blueprint fills in** (`RMeshBlueprint.cpp`, `RMeshBlueprintLOD::Init`): the prefix is
  the blueprint's path up to its **last `_`** (`ExtractMeshFamilyPrefix`); then per LOD n, when the
  blueprint names none and the file exists, `<prefix>_lod<n>.scm`, `_albedo.dds`, `_normalsTS.dds`,
  `_SpecTeam.dds` and `_lookup.dds` (no default for `SecondaryName`). A name the blueprint gives is
  completed relative to the blueprint's folder.
- **Fields of a mesh blueprint** (`RMeshBlueprintLODTypeInfo.cpp`): per LOD `MeshName`, `AlbedoName`,
  `NormalsName`, `SpecularName`, `LookupName`, `SecondaryName`, `ShaderName`, `LODCutoff` (1000),
  `Scrolling`, `Occlude`, `Silhouette`; per mesh `IconFadeInZoom`, `SortOrder` (draw order, smallest
  first), `UniformScale`, `StraddleWater`. The sim takes the first LOD whose mesh loads as the
  entity's mesh.
- **Textures bound per LOD** (`Mesh.cpp`): `albedoTexture`, `normalsTexture`, `specularTexture`,
  `lookupTexture`, `secondaryTexture`, `environmentTexture`, and globally `dissolveTexture`
  (`/textures/environment/dissolve.dds`), `anisotropicTexture`, `insectTexture`, `waterRamp` and the
  shadow map. What each channel means lives in `mesh.fx`, which `MeshPreview` follows per shader.
- **Normal maps** (`effects/mesh.fx`, `ComputeNormal`): sampled at `Uv1` (every `normalsSampler` read
  uses `texcoord0.zw`: 19 in the game's file, 29 in FAF's; albedo and specular use `Uv0`; `Uv1`
  differs from `Uv0` in 219 game and 132 FAF meshes). The shader builds
  `float3x3(binormal, tangent, normal)`: the map's **green (x) tilts along the binormal**, its
  **alpha (y) along the tangent**, z = sqrt(1 − x² − y²). In the game's meshes the tangent points
  against the texture's u and the binormal against v (dot products −0.94 and −0.89 on the test
  meshes; the glTF tangent `GlbWriter` writes points along u in 95.5% of all triangles).
  `MeshPreview` reads the normal map this way.
- **Shader names** (`ShaderDictionary.cpp`, `ResolveShaderAnnotationName`): an empty name is `Unit`;
  legacy names are renamed (`TMeshAlpha` → `NormalMappedAlpha`, `Simple` and `Team` → `Unit`,
  `TMeshNoNormals` → `VertexNormal`, `TMeshNoLighting` → `Flat`, `TMeshGlow` → `NormalMappedGlow`,
  `TMeshTerrain` → `NormalMappedTerrain`, and the effect shaders); the engine's lookup is
  case-sensitive. `MeshPreview.ShaderName` does the same (ignoring case), `MeshPreview.ShaderOf`
  chooses its formula after it. The technique's `parameter` and `cartographicTechnique`
  annotations are read too (`MeshBatch.cpp`, `Mesh.cpp`).

## The `.sca` animation file

Not parsed by this library yet; fully understood (all 613 files agree), so a parser is about 60
lines. faf-re's `SScaHeader` (`RScaResource.h`) names +0x08 and +0x10 wrongly; the manipulator's
own struct (`CAnimationManipulator.cpp`) is right.

```
0x00 'ANIM'
0x04 version: 5 (below 5 the engine swaps the quaternion's lanes on load, RScaResource.cpp)
0x08 frame count
0x0C duration (float, seconds)
0x10 track (bone) count
0x14 offset of NAME: the track names back to back, zero-terminated, in track order
0x18 offset of LINK: an int32 parent per track (the engine does not read it)
0x1C offset of DATA (each offset points just past its four-letter name)
0x20 frame size = 8 + 28 × tracks (the engine works it out itself)
DATA: 28 bytes of root delta (position 3 floats, quaternion w x y z), not used by the manipulator,
      then the frames: float time, uint32 flags, then per track position (3 floats) and
      quaternion (w x y z)
```

- Checked in every file: the names `NAME`, `LINK`, `DATA`, the frame size, the file ending right
  after the last frame, the first time 0, flags 0.
- The engine ignores the frames' times: it samples `frame = (frames − 1) / duration × t` and blends
  the two frames around it (lerp of the position, nlerp of the quaternion)
  (`CAnimationManipulator.cpp`).
- Tracks are matched to the skeleton by name (`SetAnimationResource`); a key is the bone's local
  (parent-space) transform; positions are multiplied by the pose's scale; in additive mode a key is
  taken relative to the rest pose's local transform.
- Time advances 0.1 × rate a tick (10 ticks a second), scaled by the unit's speed / `MaxSpeed` × 10
  when a goal unit is set.

## Other tools' meshes: what to expect

- **`VRTX` for `VTXL`**: four FAF meshes name their vertex section `VRTX`:
  `units/UES0103/UES0103_LOD0.scm` (UEF frigate), `units/UES0201/UES0201_LOD0.scm` (UEF destroyer),
  `units/DEL0204/DEL0204_lod0.scm` and `units/XSA0402/XSA0402_lod1.scm` (all in `units.nx2`). The
  game draws them; a parser that checks section names skips them (the editor showed those units
  from a coarser LOD). `assets/meshes/faf/xsa0402_lod1.scm` is one of them.
- **A skinned bone count of all bones**: 23 FAF meshes (among them the four `VRTX` ones,
  `UES0304_LOD0`: 26 where its vertices use 9, `UEB3102_LOD0`, `XSB2109_lod0`, `UAS0203_LOD0`) and
  the 3ds Max community exporter (`assets/meshes/3dsmax/srl0310_lod3.scm`: 36 where 28 are used)
  store the number of all bones, the game's exporter one more than the highest used, the Blender
  exporter (`assets/meshes/blender/url0302_lod1.scm`) the same as the game's. `Scm.SkinnedBoneCount`
  keeps what the file says.
- **Rest-pose matrices of the Blender "SupCom-Exporter 4.0"**: 28 FAF meshes carry numbers in the
  ignored fourth column of `RestPoseInverse` (`env/Aeon/Props/Aeon_Dock_lod1.scm`: (0, 0, 6, 1)),
  17 have a 3 × 3 that is not a rotation (`env/Evergreen/Props/Rocks/SeaRock02_lod0.scm`, rows up to
  2.2 long). The engine keeps the rotation and translation only, and props are drawn unskinned, so
  the game does not show it; a glTF viewer that skins such a `GlbWriter` file distorts the mesh.
- **Tangents**: the Blender exporter leaves some tangents at length 0, the 3ds Max one writes some
  that are not a number (`GlbWriter` replaces them by a direction across the normal); 24 FAF meshes
  have a tangent frame of the other handedness (N × T · B positive for most vertices).
- **Layout**: padding of 0x58 or 0x00, sections not aligned to 32, no `INFO` (above). One mesh of the
  game is version 3 (`props.scd`: `props/RPLACEHOLDER/RPLACEHOLDER_LOD0.scm`); `ScmParser` rejects
  it, the engine would read it as version 5.
- **Animations**: in 94 FAF `.sca` files (Blender) the last frame's time differs from the duration,
  which the engine ignores.

## Where not to trust faf-re

- `SScmFile.h` leaves the magic and version (`mUnknown00`), +0x14 and the notes' offset and size
  (`mUnknown24`) unnamed, and `SScmBoneRecord::mUnknown5C` (the name's offset) and `mUnknown64`
  (`FirstVertex`, `TriangleCount`): unused by the engine, known from the data.
- `RScaResource.h` names the header wrong (`boneCount` and `keysPerBone` are the frame and track
  counts).
- `ANI_DumpSkeleton` (`CAniSkel.cpp`) reads bone fields at offsets that match no known layout;
  faf-re keeps it as it is and calls it unresolved.
- `HardwareMeshBatch.cpp` holds debug code that is not the game's (`[REMAP]` and `[MIXTRI]` dumps
  with `OutputDebugStringA`, a "TEMPORARY SWITCH" that reads `nohide.on`), "FAF divergence" guards
  (`MeshBatch.cpp`, the palette budget) and a bone-palette texture path the original binary does not
  have.
- `MeshBatch::Initialize` assumes the `parameter` annotation defaults to 0 ("no visible default
  literal").
- `ren_MeshDissolve` and `ren_MeshDissolveCutoff` are 0.0 in the reconstruction (`Mesh.cpp`); the
  game's defaults are not known.
- The LOD distance was itself wrong in faf-re until a recent fix (the comment in `Mesh.cpp` about the
  quaternion's lane order); the plane-depth reading comes from the binary, but treat exact signs
  with care.
- faf-re notes several quaternion "convention mismatch" sites (`CAniSkel.cpp`); this library's
  conventions were checked against the data instead (the bone record, above).

## Rules for this library

- **Mirror the engine, and say where from**: a doc comment that reproduces the engine names faf-re,
  its commit and the file, as this guide does. Re-check after a new faf-re commit.
- **Read as the engine reads**: what the engine ignores, `ScmParser` ignores (section names, name
  offsets); what it keeps that the engine ignores (`FirstVertex`, `TriangleCount`, the notes, the
  stored skinned count) is there to write the same bytes back.
- **Write what the engine can draw**: `ScmWriter` refuses what the engine cannot use (the limits
  above). A mesh of the game writes back to the same bytes (`ScmWriterTest`).
- **glTF's conventions in the `.glb`**: `GlbWriter` writes what glTF defines (tangents along u, the
  bitangent's side in w, one joint of weight 1), `GlbReader` turns it back, so the round trip is
  exact (`GlbReaderTest`). A glTF of another tool: the heaviest joint becomes `Bone0`, joints of 8 or
  16 bits, weights as floats or normalised integers.
- Every change has a test over the real meshes in
  `tests/FAForever.FileFormats.Mesh.Tests/assets/meshes/` (`forged-alliance/` from the game, which
  write back to the same bytes; `blender/`, `3dsmax/` and `faf/` from other exporters).

## Open items

- **The last LOD's fade-out**: the game fades the last LOD over `ren_MeshDissolve` past its cutoff
  and hides it beyond; the default of `ren_MeshDissolve` is not known (0.0 in faf-re). The editor
  keeps the last LOD in sight.
- **Inverse bind matrices of the Blender exporter's meshes**: `GlbWriter` writes `RestPoseInverse` as
  it is, so the round trip stays exact; writing it as the engine reads it (the 3 × 3 as a rotation,
  row 3) would make the 45 FAF meshes skin right in glTF viewers but lose the original numbers.
- **The mesh blueprint's names in the editor** (`AssetBlueprints` of MapEditor.Project): its prefix
  is the blueprint's path without `_<letters>.bp`, the engine's up to the last `_` (the same for the
  game's blueprints); it adds a LOD0 when a blueprint lists none (the engine makes no LOD then, FA's
  Lua may add one) and drops a LOD whose mesh is missing (the engine keeps the slot and draws
  nothing in its range). No `LookupName` or `SecondaryName` yet.
- **Units in their rest pose**: the editor draws a unit's raw vertices; the game skins them with the
  local chain and `RestPoseInverse`, which differ slightly in a few meshes (above), and an `.sca`
  reader would let the editor show idle poses.
- **Derive `FirstVertex` and `TriangleCount`** in `ScmWriter` when the vertices are sorted by bone, so
  a mesh read from glTF writes as the game's exporter would.
