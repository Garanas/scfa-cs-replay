# Replay parser for Supreme Commander: Forged Alliance (Forever)

A small library to read and interpret replays of the game [Supreme Commander: Forged Alliance Forever](https://store.steampowered.com/app/9420/Supreme_Commander_Forged_Alliance/). It also supports the compressed replay format of [FAForever](https://faforever.com/). It is inspired by a similar [Java implementation](https://github.com/FAForever/faf-java-commons/blob/develop/faf-commons-data/src/main/java/com/faforever/commons/replay/ReplayLoader.java).

## Performance

We use the [BenchmarkDotNet](https://www.myget.org/feed/benchmarkdotnet/package/nuget/BenchmarkDotNet) library to generate basic statistics of the performance of the library as a whole. We do not generate statistics of individual functions since in practice you'll never call the individual functions - you'll always parse a replay as a single unit.

All files in `assets/faforever` are automatically part of the benchmark.

_Benchmark data from 2024/09/04_

| Method     | ReplayFile           |      Mean |      Error |    StdDev |      Gen0 |      Gen1 |      Gen2 | Allocated |
| ---------- | -------------------- | --------: | ---------: | --------: | --------: | --------: | --------: | --------: |
| LoadReplay | 23225104.fafreplay   | 33.060 ms | 17.9827 ms | 0.9857 ms | 1200.0000 | 1133.3333 |  666.6667 |  43.58 MB |
| LoadReplay | 23225323.fafreplay   |  9.302 ms |  1.6260 ms | 0.0891 ms | 1156.2500 | 1140.6250 | 1000.0000 |  15.84 MB |
| LoadReplay | 23225440.fafreplay   |  3.208 ms |  0.1279 ms | 0.0070 ms |  417.9688 |  410.1563 |  335.9375 |   5.82 MB |
| LoadReplay | 23225508.fafreplay   |  1.200 ms |  0.1581 ms | 0.0087 ms |  273.4375 |  269.5313 |  248.0469 |   2.13 MB |
| LoadReplay | 23225685.fafreplay   | 11.330 ms |  1.3499 ms | 0.0740 ms | 1187.5000 | 1171.8750 | 1000.0000 |  17.79 MB |
| LoadReplay | TestC(...)eplay [24] | 10.611 ms |  8.0947 ms | 0.4437 ms | 1171.8750 | 1156.2500 |  984.3750 |  13.23 MB |

## References

The library attempts to use the latest and greatest of C#. This is made possible because of various books and online education content:

- [Zoran Horvat](https://www.youtube.com/@zoran-horvat)
- [dotnet](https://www.youtube.com/@dotnet)
- - [A Complete .NET Developer's Guide to Span with Stephen Toub](https://www.youtube.com/watch?v=5KdICNWOfEQ)
- [Raw coding](https://www.youtube.com/@RawCoding)
- - [Understanding .NET C# Heaps (Deep Dive)](https://www.youtube.com/watch?v=TnDRzHZbOio)

And relevant documentation:

- [BenchmarkDotNet](https://benchmarkdotnet.org/articles/overview.html)
- - [Diagnoses](https://benchmarkdotnet.org/articles/configs/diagnosers.html)
- - [About Memory Diagnoser](https://adamsitnik.com/the-new-Memory-Diagnoser/)

## Credits

With thanks to the following individuals:

- https://github.com/JoschiZ for casually helping a stranger with Blazor struggles on Discord
