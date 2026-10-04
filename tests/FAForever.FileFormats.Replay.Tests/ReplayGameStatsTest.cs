using FAForever.FileFormats.Replay;
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Replay.Tests
{
    [TestClass]
    public class ReplayGameStatsTest
    {
        private static Replay Load(string file)
        {
            return file.EndsWith(".fafreplay")
                ? ReplayLoader.LoadFAFReplayFromDisk(file)
                : ReplayLoader.LoadSCFAReplayFromDisk(file);
        }

        private static ReplayInput Report(int tick, string message)
        {
            LuaData.Table parameters = new(new Dictionary<string, LuaData>
            {
                ["From"] = new LuaData.Number(1),
                ["Message"] = new LuaData.String(message),
            });
            return new ReplayInput.SimCallback(tick, 0, "ModeratorEvent", parameters, new CommandUnits(0));
        }

        [TestMethod]
        [DataRow("assets/faforever/23225104.fafreplay")]
        [DataRow("assets/faforever/23225323.fafreplay")]
        [DataRow("assets/faforever/23225440.fafreplay")]
        [DataRow("assets/faforever/23225508.fafreplay")]
        [DataRow("assets/faforever/23225685.fafreplay")]
        public void ReadsTheStatsOfEveryArmy(string file)
        {
            Replay replay = Load(file);
            ReplayGameStats? stats = ReplaySemantics.GetGameStats(replay);

            Assert.IsNotNull(stats);
            List<string?> armyNames = replay.Header.Armies
                .Where(army => army.Civilian != true)
                .Select(army => army.PlayerName)
                .ToList();
            CollectionAssert.AreEquivalent(armyNames, stats.Armies.Select(army => army.Name).ToList());

            foreach (ReplayArmyStats army in stats.Armies)
            {
                Assert.IsTrue(army.Faction is >= 1 and <= 5, $"{army.Name}: faction {army.Faction}");
                Assert.IsTrue(army.General.Built.Count >= 1, $"{army.Name} built nothing");
                Assert.IsTrue(army.Units.ContainsKey("land"), $"{army.Name} has no land tally");
                Assert.AreEqual(1, army.Units["cdr"].Built, $"{army.Name}: one ACU");
            }
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay")]
        [DataRow("assets/faforever/zstd/22338092.fafreplay")]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay")]
        public void IsNullWithoutAReport(string file)
        {
            Assert.IsNull(ReplaySemantics.GetGameStats(Load(file)));
        }

        [TestMethod]
        public void UndoesTheZeroDksonInsertsAfterTheDecimalPoint()
        {
            // As written by the game: 1415.1 → "1415.01", 0.5 → "0.05"; integers are untouched.
            const string Json = """{"stats":[{"name":"Jip","faction":1,"Defeated":1415.01,"general":{"score":100,"currentunits":0.05,"kills":{"mass":10.025}}}]}""";

            ReplayArmyStats army = ReplayGameStatsReader.Parse(Json)!.Single();

            Assert.AreEqual(1415.1, army.Defeated!.Value, 1e-9);
            Assert.AreEqual(0.5, army.General.CurrentUnits, 1e-9);
            Assert.AreEqual(10.25, army.General.Kills.Mass, 1e-9);
            Assert.AreEqual(100, army.General.Score);
        }

        [TestMethod]
        public void ReadsDecimalsAsIsWhenTheEncoderIsFixed()
        {
            const string Json = """{"stats":[{"name":"Jip","general":{"score":12.5,"currentunits":0.05}}]}""";

            ReplayArmyStats army = ReplayGameStatsReader.Parse(Json)!.Single();

            Assert.AreEqual(12.5, army.General.Score, 1e-9);
            Assert.AreEqual(0.05, army.General.CurrentUnits, 1e-9);
        }

        [TestMethod]
        public void ReadsArmiesKeyedByIndexWhenThereAreHoles()
        {
            const string Json = """{"stats":{"3":{"name":"Third"},"1":{"name":"First"}}}""";

            IReadOnlyList<ReplayArmyStats>? armies = ReplayGameStatsReader.Parse(Json);

            CollectionAssert.AreEqual(new[] { "First", "Third" }, armies!.Select(army => army.Name).ToArray());
        }

        [TestMethod]
        public void StripsTheMessageAroundTheJson()
        {
            string message = ReplayGameStatsReader.MessagePrefix + """{"stats":[]},'""";

            Assert.AreEqual("""{"stats":[]}""", ReplayGameStatsReader.GetJson(message));
            Assert.IsNull(ReplayGameStatsReader.GetJson("GpgNetSend with command 'GameEnded' and data ''"));
        }

        [TestMethod]
        public void SkipsReportsThatDoNotParse()
        {
            string valid = ReplayGameStatsReader.MessagePrefix + """{"stats":[{"name":"Jip","type":"Human","general":{"score":5}}]},'""";

            ReplayGameStats? stats = ReplaySemantics.GetGameStats(
            [
                Report(100, "Created a ping of type 'alert'"),
                Report(200, ReplayGameStatsReader.MessagePrefix + """{"stats":[{"name":"Jip","gen""" + "'"),
                Report(201, ReplayGameStatsReader.MessagePrefix + "not json at all,'"),
                Report(202, ReplayGameStatsReader.MessagePrefix + """{"stats":"nope"},'"""),
                Report(203, ReplayGameStatsReader.MessagePrefix + """{"stats":[{"name":5,"general":{"score":"x","kills":[]},"units":[],"resources":7}]},'"""),
                Report(204, valid),
            ]);

            // The mistyped report at 203 still parses (every field falls back), so it wins.
            Assert.IsNotNull(stats);
            Assert.AreEqual(TimeSpan.FromSeconds(20.3), stats.Timestamp);
            ReplayArmyStats army = stats.Armies.Single();
            Assert.IsNull(army.Name);
            Assert.AreEqual(0, army.General.Score);
            Assert.AreEqual(0, army.Units.Count);
            Assert.AreEqual(0, army.Resources.MassIn.Total);
        }
    }
}
