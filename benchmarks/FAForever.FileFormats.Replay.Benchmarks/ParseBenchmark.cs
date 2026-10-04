
using BenchmarkDotNet.Attributes;

namespace FAForever.FileFormats.Replay.Benchmarks
{
    /// <summary>
    /// Measures the parsing stages on an already decompressed replay, separately for the header
    /// (scenario, lobby options) and the body (the input stream). The body is parsed through the
    /// staged API in batches, the way the Viewer does it.
    /// </summary>
    [MemoryDiagnoser]
    public class ParseBenchmark
    {
        /// <summary>
        /// The batch size that the Viewer uses (the default of <see cref="ReplayLoader.ProcessReplayStage(ReplayLoadingStage.AtInput, int)"/>).
        /// </summary>
        private const int BatchSize = 1000;

        [ParamsSource(nameof(ReplayFiles))]
        public string ReplayFile { get; set; } = "";

        public IEnumerable<string> ReplayFiles => ReplayAssets.List("faforever").Concat(ReplayAssets.List("scfa"));

        private byte[] Decompressed = [];

        private ReplayHeader ParsedHeader = null!;

        private long BodyOffset;

        [GlobalSetup]
        public void Setup()
        {
            Decompressed = ReplayAssets.ReadDecompressed(ReplayFile);

            // parse the header once, to know where the body starts
            if (ParseHeader() is not ReplayLoadingStage.WithScenario withScenario)
            {
                throw new InvalidOperationException($"Unable to parse the header of {ReplayFile}");
            }

            ParsedHeader = withScenario.Header;
            BodyOffset = withScenario.Stream.BaseStream.Position;
        }

        [Benchmark]
        public ReplayLoadingStage Header()
        {
            return ParseHeader();
        }

        [Benchmark]
        public ReplayBody Body()
        {
            MemoryStream stream = OpenDecompressed();
            stream.Position = BodyOffset;
            ReplayLoadingStage stage = new ReplayLoadingStage.WithScenario(new ReplayBinaryReader(stream), null, ParsedHeader);
            while (true)
            {
                switch (stage)
                {
                    case ReplayLoadingStage.WithScenario withScenario:
                        stage = ReplayLoader.ProcessReplayStage(withScenario, BatchSize);
                        break;

                    case ReplayLoadingStage.AtInput atInput:
                        stage = ReplayLoader.ProcessReplayStage(atInput, BatchSize);
                        break;

                    case ReplayLoadingStage.Complete complete:
                        return complete.Body;

                    default:
                        throw new InvalidOperationException($"Unexpected stage: {stage}");
                }
            }
        }

        /// <summary>
        /// Like the stream that decompression produces, this stream exposes its buffer.
        /// </summary>
        private MemoryStream OpenDecompressed()
        {
            return new MemoryStream(Decompressed, 0, Decompressed.Length, writable: false, publiclyVisible: true);
        }

        private ReplayLoadingStage ParseHeader()
        {
            return ReplayLoader.ProcessReplayStage(new ReplayLoadingStage.Decompressed(OpenDecompressed(), null));
        }
    }
}
