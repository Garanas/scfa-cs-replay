using FAForever.FileFormats.Replay;
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Replay.Tests
{
    [TestClass]
    public class ReplayEntitiesTest
    {
        private static Replay Load(string file)
        {
            return file.EndsWith(".fafreplay")
                ? ReplayLoader.LoadFAFReplayFromDisk(file)
                : ReplayLoader.LoadSCFAReplayFromDisk(file);
        }

        private static CommandUnits Units(params int[] ids) => new(ids.Length) { EntityIds = ids };

        private static ReplayInput.IssueCommand Order(int tick, CommandType type, int[] ids, bool queued = false, string blueprintId = "", bool position = true)
            => new(tick, 0, Units(ids), new CommandData(
                tick, type, position ? new CommandTarget.Position(tick, 0, tick) : new CommandTarget.None(), new CommandFormation.NoFormation(),
                blueprintId, new LuaData.Nil(), ClearQueue: !queued, 0, 0, 0, 0, 0, 0));

        [TestMethod]
        public void GuessesWhatEachEntityIs()
        {
            const int Commander = 0x00100000, Factory = 0x00100001, Engineer = 0x00100002, Extractor = 0x00100003, Tank = 0x00100004, Assistant = 0x00100005;

            List<ReplayEntity> entities = ReplaySemantics.GetEntities(
            [
                Order(10, CommandType.IssueBuildMobile, [Commander], blueprintId: "uab0101"),
                Order(20, CommandType.IssueBuildFactory, [Factory], blueprintId: "ual0105", position: false),
                Order(30, CommandType.IssueReclaim, [Engineer]),
                Order(40, CommandType.IssueUpgrade, [Extractor], blueprintId: "uab1202", position: false),
                Order(50, CommandType.IssueMove, [Tank]),
                Order(60, CommandType.IssueGuard, [Assistant], blueprintId: "uab0101"),
            ]);

            Dictionary<int, ReplayEntityKind> kinds = entities.ToDictionary(entity => entity.EntityId, entity => entity.Kind);
            Assert.AreEqual(ReplayEntityKind.Commander, kinds[Commander]);
            Assert.AreEqual(ReplayEntityKind.Factory, kinds[Factory]);
            Assert.AreEqual(ReplayEntityKind.Engineer, kinds[Engineer]);
            Assert.AreEqual(ReplayEntityKind.Structure, kinds[Extractor]);
            Assert.AreEqual(ReplayEntityKind.Unit, kinds[Tank]);
            Assert.AreEqual(ReplayEntityKind.Engineer, kinds[Assistant], "Assisting a structure under construction is engineer work.");
            Assert.AreEqual(1, entities.Single(entity => entity.EntityId == Factory).Army);
            Assert.AreEqual(3, entities.Single(entity => entity.EntityId == Extractor).Serial);
        }

        [TestMethod]
        public void ChainsFollowTheQueueAndBreakOnStopOrANewOrder()
        {
            const int Engineer = 0x00000002;

            ReplayEntity entity = ReplaySemantics.GetEntities(
            [
                Order(10, CommandType.IssueBuildMobile, [Engineer], blueprintId: "urb1103"),
                Order(10, CommandType.IssueBuildMobile, [Engineer], queued: true, blueprintId: "urb1103"),
                Order(20, CommandType.IssueReclaim, [Engineer], queued: true),
                Order(30, CommandType.IssueStop, [Engineer], position: false),
                Order(40, CommandType.IssueMove, [Engineer], queued: true),
                Order(50, CommandType.IssueAggressiveMove, [Engineer]),
            ]).Single();

            bool[] continues = Enumerable.Range(0, entity.Orders.Count).Select(entity.ContinuesChain).ToArray();
            CollectionAssert.AreEqual(new[] { false, true, true, false, false, false }, continues);
        }

        [TestMethod]
        public void SharesOneOrderAcrossTheSelectionAndStopsAtTheLimit()
        {
            List<ReplayEntity> entities = ReplaySemantics.GetEntities(
            [
                Order(10, CommandType.IssueMove, [1, 2, 3]),
                Order(200, CommandType.IssueMove, [1]),
            ], until: TimeSpan.FromSeconds(10));

            Assert.AreEqual(3, entities.Count);
            Assert.IsTrue(entities.All(entity => entity.Orders.Count == 1));
            Assert.AreSame(entities[0].Orders[0], entities[2].Orders[0]);
            Assert.AreEqual(3, entities[0].Orders[0].SelectionSize);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay")]
        [DataRow("assets/faforever/23225104.fafreplay")]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay")]
        public void EveryCommanderIsFoundAndBuildsFirst(string file)
        {
            Replay replay = Load(file);

            List<ReplayEntity> entities = ReplaySemantics.GetEntities(replay, TimeSpan.FromMinutes(10));

            List<ReplayEntity> commanders = entities.Where(entity => entity.Kind == ReplayEntityKind.Commander).ToList();
            int armiesWithOrders = entities.Select(entity => entity.Army).Distinct().Count();
            Assert.AreEqual(armiesWithOrders, commanders.Count, "One commander per army that gave orders.");
            Assert.IsTrue(commanders.All(commander => commander.Orders[0].CommandType == CommandType.IssueBuildMobile));
            Assert.IsTrue(entities.Any(entity => entity.Kind == ReplayEntityKind.Engineer));
            Assert.IsTrue(entities.Any(entity => entity.Kind == ReplayEntityKind.Factory));
        }
    }
}
