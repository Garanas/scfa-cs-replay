
using BenchmarkDotNet.Attributes;

namespace FAForever.FileFormats.Replay.Benchmarks
{
    /// <summary>
    /// Loads a FAForever replay end-to-end: metadata, decompression, header and body.
    /// </summary>
    [MemoryDiagnoser]
    public class FAForeverReplayBenchmark
    {
        [ParamsSource(nameof(ReplayFiles))]
        public string ReplayFile { get; set; } = "";

        public IEnumerable<string> ReplayFiles => ReplayAssets.List("faforever");

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
                return ReplayLoader.LoadFAFReplayFromMemory(stream);
            }
        }
    }
}
