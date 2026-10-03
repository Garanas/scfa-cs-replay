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

## Step 0 — baseline (2026-10-03)

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

## Steps 1, 2, 3, 5 — screening with the short job (2026-10-03)

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
