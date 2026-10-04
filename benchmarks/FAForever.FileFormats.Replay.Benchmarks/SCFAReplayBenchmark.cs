
using BenchmarkDotNet.Attributes;

namespace FAForever.FileFormats.Replay.Benchmarks
{
    /// <summary>
    /// Loads an uncompressed SCFA replay end-to-end: header and body.
    /// </summary>
    [MemoryDiagnoser]
    public class SCFAReplayBenchmark
    {
        [ParamsSource(nameof(ReplayFiles))]
        public string ReplayFile { get; set; } = "";

        public IEnumerable<string> ReplayFiles => ReplayAssets.List("scfa");

        private byte[] Data = [];

        [GlobalSetup]
        public void Setup()
        {
            Data = ReplayAssets.Read(ReplayFile);
        }

        [Benchmark]
        public Replay LoadReplay()
        {
            using (MemoryStream stream = new MemoryStream(Data))
            {
                return ReplayLoader.LoadSCFAReplayFromStream(stream);
            }
        }
    }
}
