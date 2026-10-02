using FAForever.Replay;

namespace FAForever.Replay.Test
{
    [TestClass]
    public class ReplayAnalysisTest
    {
        private static Replay Load(string file)
        {
            return file.EndsWith(".fafreplay")
                ? ReplayLoader.LoadFAFReplayFromDisk(file)
                : ReplayLoader.LoadSCFAReplayFromDisk(file);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 933.7)]
        [DataRow("assets/faforever/23225104.fafreplay", 3143.8)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", 2856.0)]
        public void GetDurationTest(string file, double expectedSeconds)
        {
            Replay replay = Load(file);
            Assert.AreEqual(expectedSeconds, ReplayAnalysis.GetDuration(replay).TotalSeconds, 0.001);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 9983, 2, 2161)]
        [DataRow("assets/faforever/23225104.fafreplay", 22758, 6, 6274)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", 5720, 2, 1827)]
        public void CountPlayerActionsTest(string file, int expectedTotal, int sourceId, int expectedForSource)
        {
            Replay replay = Load(file);

            Dictionary<int, int> actions = ReplayAnalysis.CountPlayerActions(replay);

            Assert.AreEqual(expectedTotal, actions.Values.Sum());
            Assert.AreEqual(expectedForSource, actions[sourceId]);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 2, 635, "xsb0101", 2.0)]
        [DataRow("assets/faforever/23225104.fafreplay", 6, 2165, "ueb0101", 3.3)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", 0, 434, "urb0101", 25.8)]
        public void GetBuildOrderTest(string file, int sourceId, int expectedEntries, string expectedFirstBlueprint, double expectedFirstSeconds)
        {
            Replay replay = Load(file);

            List<ReplayAnalysis.BuildOrderEntry> buildOrder = ReplayAnalysis.GetBuildOrder(replay, sourceId);

            Assert.AreEqual(expectedEntries, buildOrder.Count);
            Assert.AreEqual(expectedFirstBlueprint, buildOrder[0].BlueprintId);
            Assert.AreEqual(expectedFirstSeconds, buildOrder[0].Timestamp.TotalSeconds, 0.001);
            // Build orders are chronological by construction.
            for (int i = 1; i < buildOrder.Count; i++)
            {
                Assert.IsTrue(buildOrder[i].Timestamp >= buildOrder[i - 1].Timestamp);
            }
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 16, 12)]
        [DataRow("assets/faforever/23225104.fafreplay", 53, 8)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", 48, 5)]
        public void GetActionBucketsTest(string file, int expectedBuckets, int expectedSources)
        {
            Replay replay = Load(file);

            Dictionary<int, int[]> buckets = ReplayAnalysis.GetActionBuckets(replay);

            Assert.AreEqual(expectedSources, buckets.Count);
            foreach (int[] series in buckets.Values)
            {
                Assert.AreEqual(expectedBuckets, series.Length);
            }

            // Every action lands in exactly one bucket.
            Dictionary<int, int> actions = ReplayAnalysis.CountPlayerActions(replay);
            Assert.AreEqual(actions.Values.Sum(), buckets.Values.Sum(series => series.Sum()));
        }
    }
}
