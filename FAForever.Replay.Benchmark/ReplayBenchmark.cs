﻿
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

namespace FAForever.Replay.Benchmark
{
    [ShortRunJob]
    [MemoryDiagnoser]
    public class ReplayBenchmark
    {
        private static readonly string DirectoryWithReplays = "assets/faforever";

        private Dictionary<string, byte[]> AllReplays = new Dictionary<string, byte[]>();

        [ParamsSource(nameof(ReplayFiles))]

        public string ReplayFile { get; set; } = "";

        /// <summary>
        /// Create a list of file names from the directory with replays. We use just the file name as an identifier because we store the replay data in memory during setup.
        /// </summary>
        public IEnumerable<string> ReplayFiles => Directory.GetFiles(ReplayBenchmark.DirectoryWithReplays).Select(e => Path.GetFileName(e)).AsEnumerable();


        [GlobalSetup]
        public void Setup()
        {
            // Store all replay files in memory so that disk IO are not part of the benchmark.
            // Note that we use the file name as an identifier, which matches with the benchmark parameter.
            Directory.GetFiles(ReplayBenchmark.DirectoryWithReplays).ToList().ForEach(file => AllReplays.Add(Path.GetFileName(file), File.ReadAllBytes(file)));
        }

        [Benchmark]
        public void LoadReplay()
        {
            byte[] replayData = AllReplays[ReplayFile];
            using (MemoryStream stream = new MemoryStream(replayData))
            {
                ReplayLoader.LoadFAFReplayFromMemory(stream);
            }
        }
    }
}
