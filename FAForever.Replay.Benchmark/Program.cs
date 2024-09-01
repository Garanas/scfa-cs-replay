using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace FAForever.Replay.Benchmark
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var config = ManualConfig.Create(DefaultConfig.Instance);
            BenchmarkRunner.Run<ReplayBenchmark>(config);
        }
    }
}
