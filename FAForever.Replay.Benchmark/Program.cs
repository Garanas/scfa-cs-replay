using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace FAForever.Replay.Benchmark
{
    internal class Program
    {
        /// <summary>
        /// Without arguments all benchmarks run. Pass BenchmarkDotNet arguments to run a subset, e.g.
        /// `dotnet run -c Release -- --filter *ParseBenchmark.Body*` or `--filter *Decompress*`.
        /// </summary>
        static void Main(string[] args)
        {
            // keep the full replay path in the summary, e.g. "faforever/TestCommands01.fafreplay"
            var config = ManualConfig.Create(DefaultConfig.Instance)
                .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(40));
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args.Length == 0 ? ["--filter", "*"] : args, config);
        }
    }
}
