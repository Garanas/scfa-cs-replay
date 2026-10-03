using FAForever.Replay;

namespace FAForever.Replay.Test
{
    [TestClass]
    public class ReplayInputFormatterTest
    {
        private static CommandData Command(CommandType type, CommandTarget target, string blueprintId = "", bool queued = false)
            => new(1, type, target, new CommandFormation.NoFormation(), blueprintId, new LuaData.Nil(), ClearQueue: !queued, 0, 0, 0, 0, 0, 0);

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
    }
}
