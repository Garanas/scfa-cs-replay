using FAForever.FileFormats.Replay;
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Replay.Tests
{
    [TestClass]
    public class ReplayInputFormatterTest
    {
        private static CommandData Command(CommandType type, CommandTarget target, string blueprintId = "", bool queued = false)
            => new(1, type, target, new CommandFormation.NoFormation(), blueprintId, new LuaData.Nil(), ClearQueue: !queued, -1, -1, new CommandTarget.None(), [], 1, 1);

        [TestMethod]
        public void DescribesMoveCommand()
        {
            ReplayInput input = new ReplayInput.IssueCommand(0, 0,
                new CommandUnits(5),
                Command(CommandType.IssueMove, new CommandTarget.Position(512.4f, 0f, 299.6f)));

            Assert.AreEqual("Move → (512, 300) · 5 units", ReplayInputFormatter.Describe(input));
        }

        [TestMethod]
        public void DescribesQueuedBuildCommand()
        {
            ReplayInput input = new ReplayInput.IssueCommand(0, 0,
                new CommandUnits(3),
                Command(CommandType.IssueBuildMobile, new CommandTarget.Position(100f, 0f, 200f), "uab0101", queued: true));

            Assert.AreEqual("Build uab0101 → (100, 200) · 3 units (queued)", ReplayInputFormatter.Describe(input));
            Assert.AreEqual("uab0101", ReplayInputFormatter.TryGetBlueprintId(input));
        }

        [TestMethod]
        public void DescribesFactoryCommand()
        {
            ReplayInput input = new ReplayInput.IssueFactoryCommand(0, 0,
                new CommandUnits(2),
                Command(CommandType.IssueBuildFactory, new CommandTarget.None(), "uel0105"));

            Assert.AreEqual("Factory: Build uel0105 · 2 factories", ReplayInputFormatter.Describe(input));
        }

        [TestMethod]
        public void DescribesQueueAndPauseInputs()
        {
            Assert.AreEqual("Queue +1 (command #42)", ReplayInputFormatter.Describe(new ReplayInput.IncreaseCommandCount(0, 0, 42, 1)));
            Assert.AreEqual("Remove command #42 from queue", ReplayInputFormatter.Describe(new ReplayInput.RemoveCommandFromQueue(0, 0, 42, 7)));
            Assert.AreEqual("Paused the game", ReplayInputFormatter.Describe(new ReplayInput.RequestPause(0, 0)));
            Assert.AreEqual("Game ended", ReplayInputFormatter.Describe(new ReplayInput.EndGame(0, 0)));
        }

        [TestMethod]
        public void DescribesSimCallback()
        {
            LuaData.Table parameters = new(new Dictionary<string, LuaData> { ["Type"] = new LuaData.String("Move") });
            ReplayInput input = new ReplayInput.SimCallback(0, 0, "SpawnPing", parameters, new CommandUnits(0));

            Assert.AreEqual("Callback SpawnPing {Type='Move'}", ReplayInputFormatter.Describe(input));
            Assert.IsNull(ReplayInputFormatter.TryGetBlueprintId(input));
        }

        /// <summary>
        /// Smoke test: every input of a real replay gets a non-empty description without throwing.
        /// </summary>
        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay")]
        public void DescribesEveryInputOfARealReplay(string file)
        {
            Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);

            int withBlueprint = 0;
            foreach (ReplayInput input in replay.Body.UserInput)
            {
                string description = ReplayInputFormatter.Describe(input);
                Assert.IsFalse(string.IsNullOrWhiteSpace(description));
                if (ReplayInputFormatter.TryGetBlueprintId(input) is not null)
                {
                    withBlueprint++;
                }
            }

            Assert.IsTrue(withBlueprint > 1000, $"Expected plenty of blueprint-carrying inputs, found {withBlueprint}");
        }

        [TestMethod]
        [DataRow(CommandType.IssueMove, "Move")]
        [DataRow(CommandType.IssueBuildMobile, "Build")]
        [DataRow(CommandType.IssueNuke, "Launch nuke")]
        [DataRow(CommandType.IssueKillSelf, "Self-destruct")]
        [DataRow(CommandType.IssueAggressiveMove, "Aggressive move")]
        public void VerbTest(CommandType type, string expected)
        {
            Assert.AreEqual(expected, ReplayInputFormatter.Verb(type));
        }

        [TestMethod]
        public void DescribesTheCheatAndDebugMessages()
        {
            Assert.AreEqual("Warp entity #1048576 to (125, 191)", ReplayInputFormatter.Describe(
                new ReplayInput.WarpEntity(0, 0, 0x00100000, new ReplayOrientation(0.88f, 0f, 0.47f, 0f), 125.07f, 17.89f, 190.54f)));
            Assert.AreEqual("Set 2 cells of command #5 → (100, 300)", ReplayInputFormatter.Describe(
                new ReplayInput.SetCommandCells(0, 0, 5, [new CommandCell(1, 2), new CommandCell(3, 4)], 100f, 20f, 300f)));
            Assert.AreEqual("Lua: SetArmyStats", ReplayInputFormatter.Describe(
                new ReplayInput.ExecuteLuaInSim(0, 0, "SetArmyStats", new LuaData.Nil())));
            Assert.AreEqual("Unknown message 42 (3 bytes)", ReplayInputFormatter.Describe(
                new ReplayInput.Unknown(0, 0, (ReplayInputType)42, [1, 2, 3])));
            Assert.AreEqual("Unreadable WarpEntity message: too short", ReplayInputFormatter.Describe(
                new ReplayInput.Error(0, 0, ReplayInputType.WarpEntity, new InvalidDataException("too short"), [])));
        }
    }
}
