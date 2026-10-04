# Agent guide: benchmarks/

`FAForever.FileFormats.Replay.Benchmarks` measures the replay parser with BenchmarkDotNet. The
repository-wide guide is [`../AGENTS.md`](../AGENTS.md); the parser itself is described in the
[parser guide](../src/FAForever.FileFormats.Replay/AGENTS.md).

## Running

Always in Release, from `benchmarks/FAForever.FileFormats.Replay.Benchmarks/`:

```sh
dotnet run -c Release                                         # everything (takes a while)
dotnet run -c Release -- --filter "*ParseBenchmark.Body*"     # one phase
dotnet run -c Release -- --filter "*ParseBenchmark.Body*" --job short   # quick screening
dotnet run -c Release -- --filter "*ParseBenchmark.Body*" --inProcess   # see below
```

- Use `--inProcess` while a git worktree under `.claude/worktrees/` holds a second copy of this
  project: the default toolchain then refuses to build the generated project.
- Close other heavy work (a running `dotnet watch`, a browser build) while measuring.
- Results land in `BenchmarkDotNet.Artifacts/` (gitignored).

## Phases

| Benchmark | Measures |
|---|---|
| `DecompressBenchmark` | JSON metadata + decompression of `.fafreplay` files |
| `ParseBenchmark.Header` | Scenario, mods, armies, on decompressed bytes |
| `ParseBenchmark.Body` | The input stream through the staged API, in batches as the Viewer does |
| `FAForeverReplayBenchmark` / `SCFAReplayBenchmark` | End to end, one-shot load |

Each class takes its replays from `ReplayAssets.List(...)` via `[ParamsSource]` and reads them into
memory in `[GlobalSetup]`, so disk IO is never measured. Keep it that way for new benchmarks.

## Assets

`assets/` holds its own copies, separate from the test assets: a spread of FAF replays (small to
large, one legacy zlib) and two large SCFA replays (`laird-binary-*`, 7 and 11 MB) that dominate
any regression. A new file under `assets/` is picked up automatically by every benchmark of its
kind; adding one changes the tables in `RESULTS.md`, so mention it there.

## Logging results

Every optimisation step gets a section in `RESULTS.md`, the history of the parser's performance:

- Title with the step and the date, the environment line (BenchmarkDotNet, .NET, CPU, OS), and the
  exact command used.
- A table per replay with Mean / Allocated, copied from `BenchmarkDotNet.Artifacts/results/*-github.md`.
- Measure before and after back to back, on the same machine, the same way; numbers from different
  sessions are not comparable.
- A few observations: what got faster, what did not, and why.

## Rules

- These numbers are CoreCLR; production runs in the WebAssembly interpreter, where allocations and
  per-call overhead cost relatively more. Treat a gain here as a lower bound, and an allocation
  increase as a real cost.
- An optimisation is only done when `ReplayFingerprintTest` is still green: faster but different
  output is a bug (see `../tests/AGENTS.md`).
