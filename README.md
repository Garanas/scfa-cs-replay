# Replay parser for Supreme Commander: Forged Alliance (Forever)

A small library to read and interpret replays of the game [Supreme Commander: Forged Alliance Forever](https://store.steampowered.com/app/9420/Supreme_Commander_Forged_Alliance/). It also supports the compressed replay format of [FAForever](https://faforever.com/). It is inspired by a similar [Java implementation](https://github.com/FAForever/faf-java-commons/blob/develop/faf-commons-data/src/main/java/com/faforever/commons/replay/ReplayLoader.java).

## Performance

We use the [BenchmarkDotNet](https://www.myget.org/feed/benchmarkdotnet/package/nuget/BenchmarkDotNet) library to generate basic statistics of the performance of the library as a whole. We do not generate statistics of individual functions since in practice you'll never call the individual functions - you'll always parse a replay as a single unit.

All files in `assets/faforever` are automatically part of the benchmark.

_Benchmark data from 2024/09/04_

| ReplayFile           | Mean      | Error      | StdDev    | Gen0      | Gen1      | Gen2      | Allocated |
|--------------------- |----------:|-----------:|----------:|----------:|----------:|----------:|----------:|
| 23225104.fafreplay   | 21.533 ms |  6.0310 ms | 0.3306 ms | 1031.2500 | 1000.0000 |  718.7500 |  33.15 MB |
| 23225323.fafreplay   |  7.328 ms |  1.7732 ms | 0.0972 ms | 1109.3750 | 1101.5625 | 1000.0000 |  13.66 MB |
| 23225440.fafreplay   |  2.623 ms |  1.2323 ms | 0.0675 ms |  394.5313 |  386.7188 |  339.8438 |   4.59 MB |
| 23225508.fafreplay   |  1.081 ms |  0.1809 ms | 0.0099 ms |  265.6250 |  263.6719 |  248.0469 |   1.79 MB |
| 23225685.fafreplay   |  8.515 ms |  2.9728 ms | 0.1629 ms | 1125.0000 | 1109.3750 | 1000.0000 |  14.43 MB |

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
