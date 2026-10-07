# Agent guide: tests/

One MSTest project per library in `src/`, named after it with `.Tests`. The repository-wide
guide is [`../AGENTS.md`](../AGENTS.md); what the replay tests pin down is explained in the
[parser guide](../src/FAForever.FileFormats.Replay/AGENTS.md).

| Project | Covers | Assets |
|---|---|---|
| `FAForever.FileFormats.Lua.Tests` | `LuaSourceParser`, `LuaDataFormatter`, `LuaTableReader` | none (inline Lua) |
| `FAForever.FileFormats.Blueprints.Tests` | `BlueprintParser`, `BlueprintIds`, `UnitNames`, `UnitSummary`, `UnitData`, `UnitBuildTree` | `.bp` files copied from the FA repo |
| `FAForever.FileFormats.Map.Tests` | `ScmapParser`, `MapSaveParser`, `MapScenarioParser`, `NavGenerator`, `NavPaths`, `ExtractorLayout`, `MapSymmetry`, `MapArchive`, `TerrainTypes` | one vault map per `.scmap` version (`.scmap`, `_save.lua`, `_scenario.lua`) |
| `FAForever.FileFormats.Replay.Tests` | loader, header, inputs, semantics, fingerprints | `.fafreplay` / `.scfareplay` files |

## Running

```sh
dotnet test FAForever.sln                                           # everything, as CI does
dotnet test tests/FAForever.FileFormats.Replay.Tests                # one project
dotnet test tests/FAForever.FileFormats.Replay.Tests --filter "FullyQualifiedName~ReplayFingerprintTest"
```

CI (`.github/workflows/test.yml`) runs on **Linux**: paths are case-sensitive, so a `[DataRow]`
path must match the file name exactly (`balthazar-01.SCFAReplay`, not `.scfareplay`).

## Shared setup

- `Directory.Build.props` adds the MSTest packages, `IsTestProject` and a global
  `using Microsoft.VisualStudio.TestTools.UnitTesting`.
- `Directory.Build.targets` copies everything under a project's `assets/` to the output directory.
  It is a targets file on purpose: in a props file the `None Update` would run before the SDK has
  created the items it updates, and nothing would be copied.
- A new test project's csproj therefore holds only its `ProjectReference`. Add it with
  `dotnet sln FAForever.sln add --solution-folder FileFormats <csproj>`.

## Conventions

- One test class per type under test, `<Type>Test`, in namespace `<Project>.Tests`.
- Prefer real data over hand-made fixtures: `[DataRow]` over the files in `assets/`, read by their
  relative path (`"assets/faforever/23225104.fafreplay"`), since the assets sit next to the dll.
- Tests never touch the network or the user's machine; everything they read is in `assets/`.
- New asset: drop the file under `assets/` (it is copied automatically), keep it small, and say in
  the commit where it came from (vault replay id, or FA repo path for blueprints). The benchmarks
  keep their own copies; they do not read these.

## The fingerprint test

`ReplayFingerprintTest` hashes the complete parsed output of every replay asset and checks that the
staged API matches a one-shot load. It is the safety net for performance work on the parser.

- A failure means the parsed output changed. If that was not intended, the change is a bug.
- If it was intended (a new field, a fix), update the expected hashes in the same commit, and say
  in the commit message why the output changed. The failure message shows the new hash.
- The hashed text contains full type names, so renaming a namespace or type changes every hash
  without changing the data. Verify such a rename by mapping the old names back before hashing.
- A new replay asset needs a row here as well as in the tests that use it.
