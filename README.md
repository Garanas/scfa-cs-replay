# Replay parser for Supreme Commander: Forged Alliance (Forever)

A small library to read and interpret replays of the game [Supreme Commander: Forged Alliance Forever](https://store.steampowered.com/app/9420/Supreme_Commander_Forged_Alliance/). It also supports the compressed replay format of [FAForever](https://faforever.com/). It is inspired by a similar [Java implementation](https://github.com/FAForever/faf-java-commons/blob/develop/faf-commons-data/src/main/java/com/faforever/commons/replay/ReplayLoader.java).

## Performance

We use the [BenchmarkDotNet](https://www.myget.org/feed/benchmarkdotnet/package/nuget/BenchmarkDotNet) library to generate basic statistics of the performance of the library as a whole. We do not generate statistics of individual functions since in practice you'll never call the individual functions - you'll always parse a replay as a single unit.

All files in `assets/faforever` are automatically part of the benchmark.

_Benchmark data from 2024/09/04_

| ReplayFile           | Mean      | Error      | StdDev    | Gen0      | Gen1      | Gen2      | Allocated |
|--------------------- |----------:|-----------:|----------:|----------:|----------:|----------:|----------:|
| 23225104.fafreplay   | 25.320 ms | 14.8758 ms | 0.8154 ms | 1031.2500 | 1000.0000 |  687.5000 |  33.21 MB |
| 23225323.fafreplay   |  7.563 ms |  0.9807 ms | 0.0538 ms | 1109.3750 | 1101.5625 | 1000.0000 |  13.71 MB |
| 23225440.fafreplay   |  2.916 ms |  0.4022 ms | 0.0220 ms |  402.3438 |  394.5313 |  347.6563 |   4.64 MB |
| 23225508.fafreplay   |  1.166 ms |  0.2228 ms | 0.0122 ms |  265.6250 |  255.8594 |  248.0469 |    1.8 MB |
| 23225685.fafreplay   |  9.123 ms |  1.6959 ms | 0.0930 ms | 1125.0000 | 1109.3750 | 1000.0000 |  14.48 MB |

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
