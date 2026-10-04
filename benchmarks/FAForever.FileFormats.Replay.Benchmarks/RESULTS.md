# Benchmark results

One section per optimisation step of the parser, so the effect of each step can be compared with
the previous one. Run with `dotnet run -c Release` from this directory (or `-- --filter "*<Benchmark>*"`
for a single phase) and copy Mean and Allocated from `BenchmarkDotNet.Artifacts/results/*-github.md`.

Note: production runs in the Blazor WebAssembly **interpreter**; these numbers are CoreCLR (RyuJIT).
Per-call overhead and allocations cost relatively more in the browser, so gains here are a lower bound.

Phases:

- **Decompress**: `DecompressBenchmark` (JSON metadata + decompression).
- **Header**: `ParseBenchmark.Header` (scenario, mods, armies; on decompressed bytes).
- **Body**: `ParseBenchmark.Body` (input stream through the staged API, batches of 1000, as the Viewer does).
- **End-to-end**: `FAForeverReplayBenchmark` / `SCFAReplayBenchmark` (`LoadFAFReplayFromMemory` / `LoadSCFAReplayFromStream`, one shot).

## Step 0: baseline (2026-10-03)

BenchmarkDotNet 0.14.0, .NET 10.0.3 (X64 RyuJIT AVX-512), Windows 11, DefaultJob.

| Replay | Size | Decompress | Header | Body | End-to-end |
|---|---:|---:|---:|---:|---:|
| `faforever/23225104.fafreplay` | 511 KB | 7.15 ms / 16,262 KB | 41.1 μs / 63.7 KB | 20.33 ms / 13,476 KB | 21.45 ms / 29,751 KB |
| `faforever/23225323.fafreplay` | 273 KB | 2.86 ms / 8,069 KB | 30.7 μs / 46.2 KB | 5.31 ms / 4,272 KB | 6.56 ms / 12,360 KB |
| `faforever/23225440.fafreplay` | 126 KB | 0.74 ms / 1,925 KB | 31.7 μs / 46.3 KB | 2.71 ms / 2,262 KB | 2.55 ms / 4,219 KB |
| `faforever/23225508.fafreplay` | 42 KB | 0.40 ms / 901 KB | 15.3 μs / 24.8 KB | 0.79 ms / 773 KB | 1.14 ms / 1,688 KB |
| `faforever/23225685.fafreplay` | 225 KB | 2.58 ms / 8,069 KB | 32.3 μs / 48.1 KB | 8.55 ms / 5,156 KB | 7.82 ms / 13,246 KB |
| `faforever/TestCommands01.fafreplay` | 190 KB | 1.18 ms / 3,974 KB | 53.6 μs / 84.2 KB | 7.04 ms / 5,245 KB | 5.50 ms / 9,283 KB |
| `faforever/gzip/22453414.fafreplay` (legacy zlib) | 6 KB | 45.2 μs / 94.8 KB | 54.6 μs / 64.3 KB | 4.1 μs / 14.9 KB | 105.3 μs / 173.4 KB |
| `scfa/laird-binary-01.SCFAReplay` | 11.0 MB | — | 72.7 μs / 96.0 KB | 172.15 ms / 108,051 KB | 176.56 ms / 105.57 MB |
| `scfa/laird-binary-02.SCFAReplay` | 6.7 MB | — | 69.2 μs / 90.4 KB | 83.22 ms / 53,377 KB | 81.45 ms / 52.19 MB |

Cells are Mean / Allocated.

Observations:

- **The header doesn't matter** (≤ 75 μs). All the time goes into the body, plus decompression for FAF replays.
- **Decompression is about a third of the end-to-end time for FAF replays** (7.2 of 21.5 ms for 23225104) and
  allocates more than the decompressed size (16 MB). The high Gen2 counts in the end-to-end runs come from
  the growing `MemoryStream` on the large object heap.
- **The body allocates about 10× its input size** (11 MB SCFA → 108 MB), with heavy Gen1/Gen2 activity.
  These are the allocations that steps 1–3 go after.
- Body (staged, batch 1000) and end-to-end (one shot) are close for the SCFA replays, so staging costs little.
  For the FAF replays, however, Body is *slower* than End-to-end minus Decompress (23225685: 8.55 ms vs
  ~5.2 ms; 23225440: 2.71 ms vs ~1.8 ms). The cause is still unknown, and the two code paths are almost
  identical. Possibly a GC/measurement effect (Body runs on a fresh non-expandable `MemoryStream`); to be
  investigated before attributing step results to the staged path.

## Steps 1, 2, 3, 5: screening with the short job (2026-10-03)

Measured with `--job short` (3 iterations; error margins are wide, so only large effects count) on
`--filter "*ParseBenchmark.Body*" "*DecompressBenchmark*" "*SCFAReplayBenchmark*"`, each step on its own
branch from `09c3f07`, against a short-job baseline of that same commit. Mean / Allocated.

| Benchmark | Baseline | Step 1 (strings) | Step 2 (Lua allocs) | Step 1+3 (interning) | Step 5 (zstd) | Combined |
|---|---:|---:|---:|---:|---:|---:|
| Decompress 23225104 | 4.87 ms / 16.3 MB | — | — | — | 2.09 ms / 6.4 MB | 2.11 ms / 6.4 MB |
| Decompress 23225685 | 2.81 ms / 8.1 MB | — | — | — | 0.80 ms / 2.5 MB | 0.78 ms / 2.5 MB |
| Body 23225104 | 22.2 ms / 13.5 MB | 14.9 ms / 13.5 MB | 18.1 ms / 11.5 MB | 12.5 ms / 10.2 MB | — | 9.7 ms / 8.2 MB |
| Body 23225685 | 8.47 ms / 5.2 MB | 6.29 ms / 5.2 MB | 6.07 ms / 4.2 MB | 5.55 ms / 4.3 MB | — | 4.25 ms / 3.4 MB |
| Body TestCommands01 | 7.95 ms / 5.2 MB | 6.11 ms / 5.2 MB | 5.27 ms / 4.3 MB | 4.79 ms / 4.3 MB | — | 3.93 ms / 3.3 MB |
| Body laird-binary-01 | 171 ms / 108 MB | 156 ms / 108 MB | 125 ms / 89 MB | 164 ms / 88 MB | — | 90 ms / 68 MB |
| End-to-end laird-binary-01 | 169 ms / 106 MB | 164 ms / 117 MB | 127 ms / 87 MB | 176 ms / 97 MB | — | 93 ms / 78 MB |
| End-to-end laird-binary-02 | 92 ms / 52 MB | 53 ms / 59 MB | 56 ms / 44 MB | 54 ms / 48 MB | — | 32 ms / 40 MB |

Notes:

- Step 1 adds one copy for streams that do not expose their buffer (`new MemoryStream(bytes)`), which is
  visible in the SCFA end-to-end allocations; decompressed replays expose their buffer and need no copy.
- Step 3's timing on SCFA is within the noise of the short job; its value is mostly fewer allocations
  (fewer, shared strings), which matters more in the WebAssembly interpreter than here.
- Step 5, first attempt, was *slower*: FAForever replays do not store the decompressed size in the zstd
  frame header, `GetDecompressedSize` then returns an upper bound (6.03 MB vs 5.83 MB actual), and the
  exact-size check made it always fall back. Using the bound as the buffer size fixed it.
- Before merging into the main line: re-run the full (default job) suite for a definitive table.

## Steps 1, 2, 3, 5 combined: full run (2026-10-03)

`perf/combined` (`1c73040`), DefaultJob, same machine and settings as the step 0 baseline. Mean / Allocated,
with the change against the baseline.

Caveat: this run took 39 minutes instead of ~15 and several cases show an elevated standard deviation
(6–10%), so the machine was busy at times. Allocations are deterministic and reliable; for timings, small
differences (< ~10%) are not meaningful. The cleanest cases (low deviation) are TestCommands01, 23225685
and the laird-binary-01 body.

| Replay | Decompress | Header | Body | End-to-end |
|---|---:|---:|---:|---:|
| `faforever/23225104.fafreplay` | 2.63 ms / 6,405 KB (2.7×, −61%) | 26.9 μs / 57.0 KB | 13.37 ms / 8,203 KB (1.5×, −39%) | 11.63 ms / 14,604 KB (1.8×, −51%) |
| `faforever/23225323.fafreplay` | 0.99 ms / 2,712 KB (2.9×, −66%) | 16.9 μs / 45.0 KB | 4.86 ms / 3,077 KB (1.1×, −28%) | 4.18 ms / 5,796 KB (1.6×, −53%) |
| `faforever/23225440.fafreplay` | 0.80 ms / 1,283 KB (≈, −33%) | 18.6 μs / 44.9 KB | 2.31 ms / 1,598 KB (1.2×, −29%) | 1.90 ms / 2,927 KB (1.3×, −31%) |
| `faforever/23225508.fafreplay` | 0.33 ms / 687 KB (1.2×, −24%) | 11.4 μs / 29.5 KB | 0.94 ms / 559 KB (≈, −28%) | 1.17 ms / 1,254 KB (≈, −26%) |
| `faforever/23225685.fafreplay` | 0.87 ms / 2,535 KB (3.0×, −69%) | 17.6 μs / 46.5 KB | 4.26 ms / 3,392 KB (2.0×, −34%) | 4.36 ms / 5,934 KB (1.8×, −55%) |
| `faforever/TestCommands01.fafreplay` | 0.67 ms / 2,117 KB (1.8×, −47%) | 25.3 μs / 70.6 KB | 3.92 ms / 3,338 KB (1.8×, −36%) | 4.08 ms / 5,493 KB (1.3×, −41%) |
| `faforever/gzip/22453414.fafreplay` | 55.7 μs / 94.9 KB (unchanged path) | 24.3 μs / 73.1 KB | 4.1 μs / 14.2 KB | 96.4 μs / 181.4 KB |
| `scfa/laird-binary-01.SCFAReplay` | — | 30.6 μs / 88.8 KB | 91.11 ms / 68,166 KB (1.9×, −37%) | 120.09 ms / 77.58 MB (1.5×, −27%) |
| `scfa/laird-binary-02.SCFAReplay` | — | 34.9 μs / 85.4 KB | 45.28 ms / 33,798 KB (1.8×, −37%) | 44.48 ms / 39.77 MB (1.8×, −24%) |

Observations:

- **Allocations drop by 25–69% across the board**; decompression and the body both roughly halve or better
  for the larger replays.
- **The header is about 2× faster** (41 → 27 μs for 23225104), but it was never significant.
- The legacy gzip path is unchanged; its 45 → 56 μs is noise (it shares no code with these changes).
- End-to-end for laird-binary-01 (120 ms) is notably more than Body (91 ms) plus the one-off buffer copy.
  The same gap is visible for the FAF replays in the baseline (see step 0). Still unexplained; worth a
  closer look together with the GC behaviour (Gen2 counts) of the one-shot path.
- A rerun on a quiet machine would firm up the timings, especially 23225104 and 23225323, whose body
  results (13.4 / 4.9 ms) are slower than the screening run (9.7 / 3.4 ms).

## Step 6: entity ids of command selections (2026-10-03)

Not an optimisation but a feature: the parser used to skip the entity ids of every command
selection (`CommandUnits`); it now keeps them, for the per-unit analysis of the Build order tab.
Body only, `--filter "*ParseBenchmark.Body*" --inProcess` (the in-process toolchain, because a
second copy of the benchmark project in a git worktree under `.claude/worktrees/` makes the default
toolchain refuse to build). Before and after were measured the same way, back to back.

| Replay | Before | Ids, a slice per selection | Ids, repeated selections shared |
|---|---:|---:|---:|
| `faforever/23225104.fafreplay` | 12.30 ms / 8,203 KB | 17.64 ms / 12,961 KB | 11.33 ms / 9,214 KB |
| `faforever/23225323.fafreplay` | 4.39 ms / 3,077 KB | 5.55 ms / 4,623 KB | 5.04 ms / 3,322 KB |
| `faforever/23225440.fafreplay` | 2.54 ms / 1,599 KB | 2.58 ms / 2,196 KB | 2.29 ms / 1,698 KB |
| `faforever/23225508.fafreplay` | 0.89 ms / 559 KB | 0.91 ms / 917 KB | 0.89 ms / 594 KB |
| `faforever/23225685.fafreplay` | 5.42 ms / 3,392 KB | 6.29 ms / 4,756 KB | 5.30 ms / 3,748 KB |
| `faforever/TestCommands01.fafreplay` | 4.89 ms / 3,338 KB | 5.79 ms / 4,274 KB | 4.57 ms / 3,558 KB |
| `faforever/gzip/22453414.fafreplay` | 6.0 μs / 14.2 KB | 5.8 μs / 23.1 KB | 6.1 μs / 30.5 KB¹ |
| `scfa/laird-binary-01.SCFAReplay` | 92.53 ms / 68,168 KB | 93.34 ms / 71,377 KB | 99.98 ms / 68,781 KB |
| `scfa/laird-binary-02.SCFAReplay` | 40.79 ms / 33,799 KB | 42.16 ms / 36,493 KB | 45.07 ms / 34,383 KB |

¹ Before the first chunk was made lazy (a body without commands now allocates no chunk).

Observations:

- **Storing every selection as is costs up to +58% allocations** (23225104): players give dozens of
  orders to the same group of units, and each order repeated the ids.
- **Sharing a selection equal to the previous one of the same source** brings that down to +6–12%,
  and the ids are copied in one block from the buffer (`ReplayBinaryReader.ReadInt32s`) instead of
  one `ReadInt32` per id. Timings are back to the baseline within noise for the FAF replays; the
  laird replays (+6–10%) have standard deviations of that order.
- An entity id is `(army index << 20) | serial`; serial 0 is the commander, and its first order
  is always the first construction order (pinned by `ReplayCommandsTest`).
