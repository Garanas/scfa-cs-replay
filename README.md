# Replay parser for Supreme Commander: Forged Alliance (Forever)

A small library to read and interpret replays of the game [Supreme Commander: Forged Alliance Forever](https://store.steampowered.com/app/9420/Supreme_Commander_Forged_Alliance/). It also supports the compressed replay format of [FAForever](https://faforever.com/). It is inspired by a similar [Java implementation](https://github.com/FAForever/faf-java-commons/blob/develop/faf-commons-data/src/main/java/com/faforever/commons/replay/ReplayLoader.java).

## Performance

We use the [BenchmarkDotNet](https://www.myget.org/feed/benchmarkdotnet/package/nuget/BenchmarkDotNet) library to generate basic statistics of the performance of the library as a whole. We do not generate statistics of individual functions since in practice you'll never call the individual functions - you'll always parse a replay as a single unit.

All files in `assets/faforever` are automatically part of the benchmark.

_Benchmark data from 2024/09/04_

| Method     | ReplayFile           | Mean      | Error      | StdDev    | Gen0      | Gen1      | Gen2      | Allocated |
|----------- |--------------------- |----------:|-----------:|----------:|----------:|----------:|----------:|----------:|
| LoadReplay | 23225104.fafreplay   | 51.920 ms | 56.8236 ms | 3.1147 ms | 1777.7778 | 1666.6667 | 1111.1111 |  57.92 MB |
| LoadReplay | 23225323.fafreplay   | 15.332 ms | 10.3524 ms | 0.5675 ms | 1062.5000 | 1046.8750 |  828.1250 |  23.71 MB |
| LoadReplay | 23225440.fafreplay   |  7.417 ms |  0.2265 ms | 0.0124 ms | 1117.1875 | 1109.3750 |  992.1875 |   9.77 MB |
| LoadReplay | 23225508.fafreplay   |  3.314 ms |  0.7076 ms | 0.0388 ms |  625.0000 |  621.0938 |  566.4063 |   5.64 MB |
| LoadReplay | 23225685.fafreplay   | 17.101 ms | 18.0378 ms | 0.9887 ms |  906.2500 |  875.0000 |  625.0000 |  25.61 MB |
| LoadReplay | TestC(...)eplay [24] | 15.803 ms | 32.3405 ms | 1.7727 ms |  750.0000 |  718.7500 |  500.0000 |  19.96 MB |

## References

The library attempts to use the latest and greatest of C#. This is made possible because of various books and online education content:

- [Zoran Horvat](https://www.youtube.com/@zoran-horvat)
- [dotnet](https://www.youtube.com/@dotnet)
- - [A Complete .NET Developer's Guide to Span with Stephen Toub](https://www.youtube.com/watch?v=5KdICNWOfEQ)
- [Raw coding](https://www.youtube.com/@RawCoding)
- - [Understanding .NET C# Heaps (Deep Dive)](https://www.youtube.com/watch?v=TnDRzHZbOio)

And relevant documentation:

- [BenchmarkDotNet](https://benchmarkdotnet.org/articles/overview.html)
