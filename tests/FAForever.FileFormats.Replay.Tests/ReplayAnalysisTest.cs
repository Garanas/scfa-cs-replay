using FAForever.FileFormats.Replay;

namespace FAForever.FileFormats.Replay.Tests
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
        [DataRow("assets/faforever/TestCommands01.fafreplay", 7373, 2, 1495)]
        [DataRow("assets/faforever/23225104.fafreplay", 15611, 6, 3508)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", 3357, 2, 1135)]
        public void CountPlayerActionsTest(string file, int expectedTotal, int sourceId, int expectedForSource)
        {
            Replay replay = Load(file);

            Dictionary<int, int> actions = ReplayAnalysis.CountPlayerActions(replay);

            Assert.AreEqual(expectedTotal, actions.Values.Sum());
            Assert.AreEqual(expectedForSource, actions[sourceId]);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 9983, 2, 2161)]
        [DataRow("assets/faforever/23225104.fafreplay", 22758, 6, 6274)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", 5720, 2, 1827)]
        public void CountPlayerOrdersTest(string file, int expectedTotal, int sourceId, int expectedForSource)
        {
            Replay replay = Load(file);

            Dictionary<int, int> orders = ReplayAnalysis.CountPlayerOrders(replay);

            Assert.AreEqual(expectedTotal, orders.Values.Sum());
            Assert.AreEqual(expectedForSource, orders[sourceId]);

            // Orders never undercut actions: a batch holds at least one order.
            Dictionary<int, int> actions = ReplayAnalysis.CountPlayerActions(replay);
            foreach ((int source, int actionCount) in actions)
            {
                Assert.IsTrue(orders[source] >= actionCount);
            }
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

        [TestMethod]
        [DataRow("assets/faforever/23225104.fafreplay", 8, 0, 665.3f, 355.7f)]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 12, 0, 236.1f, 759.7f)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", 5, 0, 984.7f, 679.1f)]
        public void GetEstimatedSpawnPositionsTest(string file, int expectedSources, int sourceId, float expectedX, float expectedZ)
        {
            Replay replay = Load(file);

            Dictionary<int, ReplayAnalysis.MapPosition> spawns = ReplayAnalysis.GetEstimatedSpawnPositions(replay);

            Assert.AreEqual(expectedSources, spawns.Count);
            Assert.AreEqual(expectedX, spawns[sourceId].X, 0.1f);
            Assert.AreEqual(expectedZ, spawns[sourceId].Z, 0.1f);

            // Every estimate lies within the bounds of the map.
            ReplayScenarioMap map = replay.Header.Scenario.Map;
            foreach (ReplayAnalysis.MapPosition position in spawns.Values)
            {
                Assert.IsTrue(position.X >= 0 && position.X <= map.SizeX);
                Assert.IsTrue(position.Z >= 0 && position.Z <= map.SizeZ);
            }
        }

        [TestMethod]
        [DataRow("assets/faforever/23225104.fafreplay", 18, 474.9, 1, 2, 6, 1.0, 0.0)]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 4, 289.7, 11, 12, 6, 0.25, 0.0)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", 8, 873.0, 0, 1, 7, 0.6411764621734619, 0.0)]
        public void GetResourceTransfersTest(string file, int expectedCount, double expectedFirstSeconds, int expectedSourceId, int expectedFrom, int expectedTo, double expectedMass, double expectedEnergy)
        {
            Replay replay = Load(file);

            List<ReplayResourceTransfer> transfers = ReplaySemantics.GetResourceTransfers(replay);

            Assert.AreEqual(expectedCount, transfers.Count);
            ReplayResourceTransfer first = transfers[0];
            Assert.AreEqual(expectedFirstSeconds, first.Timestamp.TotalSeconds, 0.001);
            Assert.AreEqual(expectedSourceId, first.SourceId);
            Assert.AreEqual(expectedFrom, first.FromArmy);
            Assert.AreEqual(expectedTo, first.ToArmy);
            Assert.AreEqual(expectedMass, first.MassRatio, 0.0001);
            Assert.AreEqual(expectedEnergy, first.EnergyRatio, 0.0001);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 6013, 5604, 2.0, 1, 797.5f, 257.5f)]
        [DataRow("assets/faforever/23225104.fafreplay", 12931, 12124, 3.3, 6, 920.5f, 93.5f)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", 3622, 3528, 3.1, 3, 25.5f, 664.5f)]
        public void GetMapEventsTest(string file, int expectedTotal, int expectedCommands, double expectedFirstSeconds, int expectedFirstSource, float expectedFirstX, float expectedFirstZ)
        {
            Replay replay = Load(file);

            List<ReplayMapEvent> events = ReplaySemantics.GetMapEvents(replay);

            Assert.AreEqual(expectedTotal, events.Count);
            Assert.AreEqual(expectedCommands, events.Count(e => e.Kind == ReplayMapEventKind.Command));

            // In all three replays the opening order is building a UEF land factory.
            ReplayMapEvent first = events[0];
            Assert.AreEqual(expectedFirstSeconds, first.Timestamp.TotalSeconds, 0.001);
            Assert.AreEqual(expectedFirstSource, first.SourceId);
            Assert.AreEqual(ReplayMapEventKind.Command, first.Kind);
            Assert.AreEqual(CommandType.IssueBuildMobile, first.CommandType);
            Assert.AreEqual(expectedFirstX, first.X, 0.001f);
            Assert.AreEqual(expectedFirstZ, first.Z, 0.001f);
            Assert.AreEqual("ueb0101", first.BlueprintId);

            // Raw clicks can land marginally outside the play area (the game clamps them,
            // the replay stores the click), hence the small margin.
            const float margin = 8f;
            ReplayScenarioMap map = replay.Header.Scenario.Map;
            foreach (ReplayMapEvent mapEvent in events)
            {
                Assert.IsTrue(mapEvent.BlueprintId is null or { Length: > 0 });
                if (map.SizeX is { } sizeX && map.SizeZ is { } sizeZ)
                {
                    Assert.IsTrue(mapEvent.X >= -margin && mapEvent.X <= sizeX + margin, $"X {mapEvent.X} outside the map");
                    Assert.IsTrue(mapEvent.Z >= -margin && mapEvent.Z <= sizeZ + margin, $"Z {mapEvent.Z} outside the map");
                }
            }

            // The extraction preserves the tick order of the body.
            for (int i = 1; i < events.Count; i++)
            {
                Assert.IsTrue(events[i].Timestamp >= events[i - 1].Timestamp);
            }
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 23, 10, 12, 1)]
        [DataRow("assets/faforever/23225104.fafreplay", 17, 7, 9, 0)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", 8, 2, 5, 1)]
        public void GetSessionEventsTest(string file, int expectedTotal, int expectedLeft, int expectedResumed, int expectedEnded)
        {
            Replay replay = Load(file);

            List<ReplaySessionEvent> events = ReplaySemantics.GetSessionEvents(replay);

            Assert.AreEqual(expectedTotal, events.Count);
            Assert.AreEqual(expectedLeft, events.Count(e => e.Kind == ReplaySessionEventKind.PlayerLeft));
            // FAF clients emit a RequestResume on startup, so resumes outnumber pauses.
            Assert.AreEqual(expectedResumed, events.Count(e => e.Kind == ReplaySessionEventKind.Resumed));
            Assert.AreEqual(expectedEnded, events.Count(e => e.Kind == ReplaySessionEventKind.GameEnded));

            for (int i = 1; i < events.Count; i++)
            {
                Assert.IsTrue(events[i].Timestamp >= events[i - 1].Timestamp);
            }
        }
    }
}
