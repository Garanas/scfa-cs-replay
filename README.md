# Replay parser for Supreme Commander: Forged Alliance (Forever)

A small library to read and interpret replays of the game [Supreme Commander: Forged Alliance Forever](https://store.steampowered.com/app/9420/Supreme_Commander_Forged_Alliance/). It also supports the compressed replay format of [FAForever](https://faforever.com/). It is inspired by a similar [Java implementation](https://github.com/FAForever/faf-java-commons/blob/develop/faf-commons-data/src/main/java/com/faforever/commons/replay/ReplayLoader.java).

## Performance

We use the [BenchmarkDotNet](https://www.myget.org/feed/benchmarkdotnet/package/nuget/BenchmarkDotNet) library to generate basic statistics of the performance of the library as a whole. We do not generate statistics of individual functions since in practice you'll never call the individual functions - you'll always parse a replay as a single unit.

All files in `assets/faforever` are automatically part of the benchmark.

_Benchmark data from 2024/09/04_

| ReplayFile           | Mean      | Error     | StdDev    | Gen0      | Gen1      | Gen2      | Allocated |
|--------------------- |----------:|----------:|----------:|----------:|----------:|----------:|----------:|
| 23225104.fafreplay   | 17.290 ms | 0.3406 ms | 0.2844 ms | 1218.7500 | 1187.5000 |  968.7500 |  29.06 MB |
| 23225323.fafreplay   |  5.238 ms | 0.0730 ms | 0.0683 ms | 1078.1250 | 1062.5000 | 1000.0000 |  12.07 MB |
| 23225440.fafreplay   |  2.438 ms | 0.0205 ms | 0.0192 ms |  539.0625 |  535.1563 |  496.0938 |   4.12 MB |
| 23225508.fafreplay   |  1.056 ms | 0.0205 ms | 0.0182 ms |  261.7188 |  259.7656 |  248.0469 |   1.65 MB |
| 23225685.fafreplay   |  6.138 ms | 0.0548 ms | 0.0512 ms | 1093.7500 | 1085.9375 | 1000.0000 |  12.94 MB |

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
