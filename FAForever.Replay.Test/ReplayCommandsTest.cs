using FAForever.Replay;

namespace FAForever.Replay.Test
{
    [TestClass]
    public class ReplayCommandsTest
    {
        private static Replay Load(string file)
        {
            return file.EndsWith(".fafreplay")
                ? ReplayLoader.LoadFAFReplayFromDisk(file)
                : ReplayLoader.LoadSCFAReplayFromDisk(file);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay")]
        [DataRow("assets/faforever/23225104.fafreplay")]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay")]
        public void KeepsEveryUnitAndFactoryCommand(string file)
        {
            Replay replay = Load(file);

            List<ReplayCommand> commands = ReplaySemantics.GetCommands(replay);

            int expected = replay.Body.UserInput.Count(input => input is ReplayInput.IssueCommand or ReplayInput.IssueFactoryCommand);
            Assert.AreEqual(expected, commands.Count);
            Assert.IsTrue(commands.Any(command => command.Position is null), "Some orders have no position (e.g. factory queues).");
            Assert.IsTrue(commands.Any(command => command.CommandType == CommandType.IssueBuildFactory && command.BlueprintId is not null && command.Position is null),
                "Factory build orders keep their blueprint, without a position.");
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 300)]
        [DataRow("assets/faforever/23225104.fafreplay", 340)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", 82)]
        public void ResolvesEveryQueueChangeToItsOrder(string file, int expectedChanges)
        {
            Replay replay = Load(file);

            List<ReplayQueueChange> changes = ReplaySemantics.GetQueueChanges(replay);

            Assert.AreEqual(expectedChanges, changes.Count);
            Assert.IsTrue(changes.All(change => change.BlueprintId is not null), "Every queue change resolves to a construction order.");
            Assert.IsTrue(changes.Any(change => change.CommandType == CommandType.IssueBuildFactory));
        }

        [TestMethod]
        public void QueueChangesAreSignedAndMatchedPerSource()
        {
            CommandData Order(int identifier, string blueprintId) => new(
                identifier, CommandType.IssueBuildFactory, new CommandTarget.None(), new CommandFormation.NoFormation(),
                blueprintId, new LuaData.Nil(), false, 0, 0, 0, 0, 0, 0);

            List<ReplayQueueChange> changes = ReplaySemantics.GetQueueChanges(
            [
                new ReplayInput.IssueCommand(10, 1, new CommandUnits(1), Order(7, "uel0105")),
                new ReplayInput.IssueCommand(11, 2, new CommandUnits(1), Order(7, "url0105")),
                new ReplayInput.IncreaseCommandCount(20, 1, 7, 2),
                new ReplayInput.DecreaseCommandCount(30, 2, 7, 1),
                new ReplayInput.IncreaseCommandCount(40, 1, 99, 1),
            ]);

            Assert.AreEqual(3, changes.Count);
            Assert.AreEqual((2, "uel0105"), (changes[0].Delta, changes[0].BlueprintId));
            Assert.AreEqual((-1, "url0105"), (changes[1].Delta, changes[1].BlueprintId));
            Assert.IsNull(changes[2].BlueprintId, "An unknown command identifier stays unresolved.");
            Assert.IsNull(changes[2].CommandType);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay")]
        [DataRow("assets/faforever/23225104.fafreplay")]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay")]
        public void PositionsMatchTheMapEvents(string file)
        {
            Replay replay = Load(file);

            List<(TimeSpan, int, CommandType, float, float)> fromCommands = ReplaySemantics.GetCommands(replay)
                .Where(command => command.Position is not null)
                .Select(command => (command.Timestamp, command.SourceId, command.CommandType, command.Position!.X, command.Position.Z))
                .ToList();
            List<(TimeSpan, int, CommandType, float, float)> fromMapEvents = ReplaySemantics.GetMapEvents(replay)
                .Where(mapEvent => mapEvent.Kind is ReplayMapEventKind.Command or ReplayMapEventKind.FactoryCommand)
                .Select(mapEvent => (mapEvent.Timestamp, mapEvent.SourceId, mapEvent.CommandType, mapEvent.X, mapEvent.Z))
                .ToList();

            CollectionAssert.AreEqual(fromMapEvents, fromCommands);
        }
    }
}
