# Replay parser for Supreme Commander: Forged Alliance (Forever)

A small library to read and interpret replays of the game [Supreme Commander: Forged Alliance Forever](https://store.steampowered.com/app/9420/Supreme_Commander_Forged_Alliance/). It also supports the compressed replay format of [FAForever](https://faforever.com/). It is inspired by a similar [Java implementation](https://github.com/FAForever/faf-java-commons/blob/develop/faf-commons-data/src/main/java/com/faforever/commons/replay/ReplayLoader.java).

## Performance

We use the [BenchmarkDotNet](https://www.myget.org/feed/benchmarkdotnet/package/nuget/BenchmarkDotNet) library to generate basic statistics of the performance of the library as a whole. We do not generate statistics of individual functions since in practice you'll never call the individual functions - you'll always parse a replay as a single unit. 

All files in `assets/faforever` are automatically part of the benchmark.

_Benchmark data from 2024/09/01_

| Method      | ReplayFile         | Mean      | Error    | StdDev   | Median    |
|------------ |------------------- |----------:|---------:|---------:|----------:|
| LoadReplays | 23225104.fafreplay | 167.85 ms | 3.353 ms | 3.293 ms | 166.50 ms |
| LoadReplays | 23225323.fafreplay |  62.32 ms | 1.244 ms | 3.590 ms |  63.38 ms |
| LoadReplays | 23225440.fafreplay |  16.34 ms | 0.176 ms | 0.147 ms |  16.33 ms |
| LoadReplays | 23225508.fafreplay |  11.28 ms | 0.222 ms | 0.364 ms |  11.28 ms |
| LoadReplays | 23225685.fafreplay |  64.65 ms | 1.419 ms | 4.182 ms |  64.79 ms |

## References

The library attempts to use the latest and greatest of C#. This is made possible because of various books and online education content:

- [Zoran Horvat](https://www.youtube.com/@zoran-horvat)

And relevant documentation:

- [BenchmarkDotNet](https://benchmarkdotnet.org/articles/overview.html)
