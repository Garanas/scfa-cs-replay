# Agent guide: src/ (the shipped code)

Everything under `src/` ships: the libraries and the hosted app. Tests, benchmarks and the sandbox
live next to this folder. The repository-wide guide is [`../AGENTS.md`](../AGENTS.md); four projects
here have their own guide, read it before working in that project:

| Project | Guide | What it is |
|---|---|---|
| `FAForever.FileFormats.Lua` | [Blueprints guide](FAForever.FileFormats.Blueprints/AGENTS.md) | Lua values and the data-only Lua evaluator |
| `FAForever.FileFormats.Blueprints` | [its guide](FAForever.FileFormats.Blueprints/AGENTS.md) | Blueprint files, blueprint ids, unit names, factions |
| `FAForever.FileFormats.Replay` | [its guide](FAForever.FileFormats.Replay/AGENTS.md) | The replay parser (benchmarked, fingerprint-tested) |
| `FAForever.Vault.Viewer` | [its guide](FAForever.Vault.Viewer/AGENTS.md) | Blazor WebAssembly app |
| `FAForever.Vault.Server` | the root guide | Static host, OAuth token proxy, link previews |

## Dependencies point one way

```
Vault.Server → Vault.Viewer → FileFormats.Replay → FileFormats.Blueprints → FileFormats.Lua
```

- A `FileFormats` library never references `Vault`, and a lower library never references a higher
  one. When two libraries need the same type, it moves down (that is why `Faction` lives in
  Blueprints and `LuaData` in Lua).
- The Server only hosts: it does not reference the libraries for logic, and must hold no secrets
  or session state.

## Rules for the FileFormats libraries

- **They run in the browser.** The Viewer executes them in the WebAssembly interpreter: single
  threaded, no filesystem. Keep them pure managed code: no native dependencies, no `Task.Run`, no
  threads. Entry points take text, bytes or a stream; helpers that read from disk
  (`ReplayLoader.*FromDisk`) are conveniences for desktop callers and must stay thin wrappers.
- **Data in, records out.** Public results are immutable records; logic is small pure static
  functions (`ReplaySemantics`, `ReplayAnalysis`, `BlueprintIds`). No global mutable state.
- **Mirror the game, and say where from.** When code reproduces game behaviour or data, the doc
  comment names the source in the FA repository (e.g. `lua/system/Blueprints.lua`), so it can be
  re-checked after a game update.
- **Document public members** with XML doc comments, as the existing code does.
- Every behaviour change needs a test in the matching `tests/` project (see `../tests/AGENTS.md`).

## Conventions

- Folder name = project name = assembly name = root namespace. A file is named after its main
  type; small companions (an enum, a delegate, an exception) may share it.
- Libraries use block-scoped namespaces (`namespace X { ... }`), the Viewer file-scoped ones.
  Match the file you are in.
- Build settings shared by all projects (target framework, nullable, implicit usings) live in
  `../Directory.Build.props`; a csproj only lists its packages and references. Package versions
  live only in `../Directory.Packages.props`.

## Adding a library

1. `src/FAForever.FileFormats.<Name>/FAForever.FileFormats.<Name>.csproj` with only its references.
2. `dotnet sln FAForever.sln add --solution-folder FileFormats <csproj>`.
3. A test project in `tests/` (see its guide) and, if it has non-obvious rules, an `AGENTS.md` here
   with a `CLAUDE.md` containing `@AGENTS.md` next to it.
4. If the Viewer references it, add its csproj to the restore layer of the `Dockerfile` (the
   restore runs before `src/` is copied, so a missing project fails the image build only).
5. List it in the table above, in the root guide and in `README.md`.
