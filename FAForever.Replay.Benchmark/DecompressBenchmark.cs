
using BenchmarkDotNet.Attributes;

namespace FAForever.Replay.Benchmark
{
    /// <summary>
    /// Measures the first two stages of a FAForever replay: reading the JSON metadata and decompressing the body (zstd, or legacy base64 + zlib).
    /// </summary>
    [MemoryDiagnoser]
    public class DecompressBenchmark
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
        public ReplayLoadingStage Decompress()
        {
            ReplayLoadingStage stage = ReplayLoader.ProcessReplayStage(new ReplayLoadingStage.NotStarted(new MemoryStream(Data)));
            if (stage is not ReplayLoadingStage.WithMetadata withMetadata)
            {
                throw new InvalidOperationException($"Unexpected stage: {stage}");
            }

            return ReplayLoader.ProcessReplayStage(withMetadata);
        }
    }
}
