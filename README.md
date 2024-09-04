# Replay parser for Supreme Commander: Forged Alliance (Forever)

A small library to read and interpret replays of the game [Supreme Commander: Forged Alliance Forever](https://store.steampowered.com/app/9420/Supreme_Commander_Forged_Alliance/). It also supports the compressed replay format of [FAForever](https://faforever.com/). It is inspired by a similar [Java implementation](https://github.com/FAForever/faf-java-commons/blob/develop/faf-commons-data/src/main/java/com/faforever/commons/replay/ReplayLoader.java).

## Performance

We use the [BenchmarkDotNet](https://www.myget.org/feed/benchmarkdotnet/package/nuget/BenchmarkDotNet) library to generate basic statistics of the performance of the library as a whole. We do not generate statistics of individual functions since in practice you'll never call the individual functions - you'll always parse a replay as a single unit. 

All files in `assets/faforever` are automatically part of the benchmark.

_Benchmark data from 2024/09/04_

| LoadReplay | 23225104.fafreplay   | 55.013 ms | 82.8102 ms | 4.5391 ms |
| LoadReplay | 23225323.fafreplay   | 15.456 ms | 32.1467 ms | 1.7621 ms |
| LoadReplay | 23225440.fafreplay   |  6.265 ms |  0.3651 ms | 0.0200 ms |
| LoadReplay | 23225508.fafreplay   |  3.304 ms |  1.1702 ms | 0.0641 ms |
| LoadReplay | 23225685.fafreplay   | 18.886 ms | 19.5360 ms | 1.0708 ms |

## References

The library attempts to use the latest and greatest of C#. This is made possible because of various books and online education content:

- [Zoran Horvat](https://www.youtube.com/@zoran-horvat)

And relevant documentation:

- [BenchmarkDotNet](https://benchmarkdotnet.org/articles/overview.html)
