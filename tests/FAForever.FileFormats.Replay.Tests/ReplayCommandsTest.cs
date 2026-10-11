using FAForever.FileFormats.Replay;
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Replay.Tests
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
        [DataRow("assets/faforever/TestCommands01.fafreplay", "HeavyAntiMatterCannon")]
        [DataRow("assets/faforever/23225685.fafreplay", "LeftPod")]
        public void ReadsCommanderUpgrades(string file, string firstEnhancement)
        {
            Replay replay = Load(file);

            List<ReplayCommand> commands = ReplaySemantics.GetCommands(replay);

            Assert.AreEqual(firstEnhancement, commands.First(command => command.Enhancement is not null).Enhancement);
            Assert.IsTrue(commands.Where(command => command.Enhancement is not null).All(command => command.CommandType == CommandType.IssueScript),
                "Only script orders start an upgrade.");
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay")]
        [DataRow("assets/faforever/23225104.fafreplay")]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay")]
        public void EntityIdsCarryTheArmyAndTheCommanderBuildsFirst(string file)
        {
            Replay replay = Load(file);
            Dictionary<int, int> armyBySource = replay.Header.Armies
                .Select((army, index) => (army.SourceId, index))
                .Where(pair => pair.SourceId is not null)
                .ToDictionary(pair => pair.SourceId!.Value, pair => pair.index);
            HashSet<int> sourcesThatBuilt = [];

            foreach (ReplayInput input in replay.Body.UserInput)
            {
                (CommandUnits? units, CommandData? data) = input switch
                {
                    ReplayInput.IssueCommand command => (command.Units, command.Data),
                    ReplayInput.IssueFactoryCommand command => (command.Factories, command.Data),
                    _ => (null, null),
                };
                if (units is null || data is null || !armyBySource.TryGetValue(input.SourceId, out int army))
                {
                    continue;
                }

                Assert.AreEqual(units.UnitCount, units.EntityIds.Length);

                // An entity id is (army index << 20) | serial number.
                foreach (int id in units.EntityIds.Span)
                {
                    Assert.AreEqual(army, id >> 20, $"Entity 0x{id:X8} ordered by source {input.SourceId}");
                }

                // The first construction order of every army comes from serial 0: the commander.
                if (data.Type == CommandType.IssueBuildMobile && sourcesThatBuilt.Add(input.SourceId))
                {
                    CollectionAssert.AreEqual(new[] { army << 20 }, units.EntityIds.ToArray());
                }
            }

            Assert.IsTrue(sourcesThatBuilt.Count >= 2);
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
                blueprintId, new LuaData.Nil(), false, -1, -1, new CommandTarget.None(), [], 1, 1);

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

        private static List<(int SourceId, CommandData Data)> Orders(Replay replay)
            => replay.Body.UserInput
                .Select(input => input switch
                {
                    ReplayInput.IssueCommand command => (input.SourceId, command.Data),
                    ReplayInput.IssueFactoryCommand command => (input.SourceId, command.Data),
                    _ => (input.SourceId, null!),
                })
                .Where(order => order.Data is not null)
                .ToList();

        /// <summary>
        /// A formation is a script index, an orientation (a unit quaternion that turns about the
        /// vertical axis) and a scale of 1.
        /// </summary>
        [TestMethod]
        [DataRow("assets/faforever/23225104.fafreplay", 9542)]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 4226)]
        [DataRow("assets/scfa/balthazar-03.SCFAReplay", 3999)]
        public void FormationsAreAQuaternionAndAScaleTest(string file, int expectedFormations)
        {
            List<CommandFormation.Formation> formations = Orders(Load(file))
                .Select(order => order.Data.Formation)
                .OfType<CommandFormation.Formation>()
                .ToList();

            Assert.AreEqual(expectedFormations, formations.Count);
            foreach (CommandFormation.Formation formation in formations)
            {
                ReplayOrientation q = formation.Orientation;
                Assert.AreEqual(1.0, Math.Sqrt(q.W * q.W + q.X * q.X + q.Y * q.Y + q.Z * q.Z), 0.001, formation.ToString());
                Assert.AreEqual(0f, q.X, 0.001f);
                Assert.AreEqual(0f, q.Z, 0.001f);
                Assert.AreEqual(1f, formation.Scale);
                Assert.IsTrue(formation.ScriptIndex >= 0);
                Assert.IsTrue(Math.Abs(q.GetHeading()) <= 2 * Math.PI);
            }
        }

        /// <summary>
        /// The fields every measured order leaves at their defaults: no second target, no cells, a
        /// max count and count of 1. A coordinating command id appears only on attack orders and is a
        /// command of the same source; an insert-before id is a command of the same source too.
        /// </summary>
        [TestMethod]
        [DataRow("assets/faforever/23225104.fafreplay", 8, 7)]
        [DataRow("assets/faforever/23225323.fafreplay", 5, 7)]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 1, 6)]
        [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay", 0, 12)]
        public void CoordinatedAttacksAndQueueInsertionsTest(string file, int expectedCoordinated, int expectedInsertions)
        {
            List<(int SourceId, CommandData Data)> orders = Orders(Load(file));

            foreach ((int sourceId, CommandData data) in orders)
            {
                Assert.AreEqual(sourceId, data.Identifier >> 24, "A command id starts with its source.");
                Assert.IsInstanceOfType<CommandTarget.None>(data.SecondTarget);
                Assert.AreEqual(0, data.Cells.Count);
                Assert.AreEqual((1, 1), (data.MaxCount, data.Count));
            }

            List<(int SourceId, CommandData Data)> coordinated = orders.Where(order => order.Data.CoordinatingCommandId != -1).ToList();
            Assert.AreEqual(expectedCoordinated, coordinated.Count);
            Assert.IsTrue(coordinated.All(order => order.Data.Type == CommandType.IssueAttack));
            Assert.IsTrue(coordinated.All(order => order.Data.CoordinatingCommandId >> 24 == order.SourceId));

            List<(int SourceId, CommandData Data)> insertions = orders.Where(order => order.Data.InsertBeforeCommandId != -1).ToList();
            Assert.AreEqual(expectedInsertions, insertions.Count);
            Assert.IsTrue(insertions.All(order => order.Data.InsertBeforeCommandId >> 24 == order.SourceId));
        }
    }
}
