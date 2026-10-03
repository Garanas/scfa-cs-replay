
namespace FAForever.Replay.Benchmark
{
    /// <summary>
    /// Access to the replays in the assets directory. Benchmarks read their replay into memory
    /// during setup so that disk IO is not part of the measurement. A replay is identified by its
    /// path relative to the assets directory, e.g. "faforever/23225104.fafreplay".
    /// </summary>
    public static class ReplayAssets
    {
        private static readonly string Root = "assets";

        public static IEnumerable<string> List(string directory)
        {
            return Directory.GetFiles(Path.Combine(Root, directory), "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(Root, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal);
        }

        public static byte[] Read(string replay)
        {
            return File.ReadAllBytes(Path.Combine(Root, replay));
        }

        public static bool IsFAForeverReplay(string replay)
        {
            return Path.GetExtension(replay).Equals(".fafreplay", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the uncompressed replay: the decompressed body of a FAForever replay, or the file as-is for a SCFA replay.
        /// </summary>
        public static byte[] ReadDecompressed(string replay)
        {
            byte[] data = Read(replay);
            if (!IsFAForeverReplay(replay))
            {
                return data;
            }

            ReplayLoadingStage stage = ReplayLoader.ProcessReplayStage(new ReplayLoadingStage.NotStarted(new MemoryStream(data)));
            if (stage is ReplayLoadingStage.WithMetadata withMetadata)
            {
                stage = ReplayLoader.ProcessReplayStage(withMetadata);
            }

            if (stage is not ReplayLoadingStage.Decompressed decompressed)
            {
                throw new InvalidOperationException($"Unable to decompress {replay}: {stage}");
            }

            return decompressed.Stream.ToArray();
        }
    }
}
