using System.Numerics;
using System.Text;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Armies and teams (user story M-4): the edits keep everything that names an army consistent,
    /// and teams survive the project and the game's files.
    /// </summary>
    [TestClass]
    public class ArmyEditsTest
    {
        private static MapDocument original = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        [TestMethod]
        public void APlayableArmyIsAddedWithASpawnAndAnExtraOneWithout()
        {
            MapDocument document = original.AddArmy(null, playable: true, new Vector2(40, 50), out string playable);
            Assert.AreEqual("ARMY_10", playable, "the next free ARMY_n: ARMY_9 is an extra army");
            Assert.AreEqual(playable, document.Scenario.PlayableArmies[^1]);
            Spawn spawn = document.Markers.Spawns.Single(s => s.Name == playable);
            Assert.AreEqual((40f, 50f), (spawn.Position.X, spawn.Position.Z));
            Assert.AreEqual(document.Terrain.Heightmap.HeightAt(40, 50), spawn.Position.Y, "on the terrain");
            CollectionAssert.AreEqual(new[] { UnitGroupEdits.InitialGroup }, document.FindArmy(playable)!.Units.Groups.Select(g => g.Name).ToArray());

            document = document.AddArmy("CIVILIANS", playable: false, null, out string extra);
            Assert.AreEqual("CIVILIANS", extra);
            Assert.AreEqual(extra, document.Scenario.ExtraArmies[^1]);
            Assert.IsFalse(document.Markers.Spawns.Any(s => s.Name == extra), "an extra army needs no spawn");

            Assert.ThrowsException<ArgumentException>(() => document.AddArmy("ARMY_1", true, null, out _), "taken");
            Assert.ThrowsException<ArgumentException>(() => document.AddArmy("two words", true, null, out _), "no army's name");
            Assert.ThrowsException<ArgumentException>(() => original.AddArmy("Mass 01", false, null, out _), "not an army's name");
        }

        [TestMethod]
        public void RenamingAnArmyRenamesItEverywhere()
        {
            MapDocument document = original
                .SetArmyFactions("ARMY_2", ["Aeon"])
                .SetTeam("ARMY_2", "North")
                .SetAlliance("ARMY_1", "ARMY_2", "ally");
            int spawnId = document.Markers.Spawns.Single(s => s.Name == "ARMY_2").Id;

            MapDocument renamed = document.RenameArmy("ARMY_2", "NORTH_1");

            Assert.IsNull(renamed.FindArmy("ARMY_2"));
            Assert.IsNotNull(renamed.FindArmy("NORTH_1"));
            Assert.AreEqual("NORTH_1", renamed.Scenario.PlayableArmies[1], "in its place");
            CollectionAssert.AreEqual(new[] { "aeon" }, renamed.Scenario.ArmyFactions["NORTH_1"].ToArray());
            Assert.AreEqual("North", renamed.TeamOf("NORTH_1"));
            Assert.AreEqual("Ally", renamed.FindArmy("ARMY_1")!.Alliances["NORTH_1"]);
            Assert.AreEqual("NORTH_1", renamed.FindMarker(spawnId)!.Name, "its spawn, which keeps its id");
            Assert.AreEqual(renamed.FindMarker(spawnId)!.Position, document.FindMarker(spawnId)!.Position);

            Assert.ThrowsException<ArgumentException>(() => document.RenameArmy("ARMY_2", "ARMY_3"));
            Assert.ThrowsException<ArgumentException>(() => document.RenameArmy("ARMY_2", "Mass_01x x"));
            Assert.ThrowsException<KeyNotFoundException>(() => document.RenameArmy("ARMY_99", "ARMY_100"));
        }

        [TestMethod]
        public void RemovingAnArmyRemovesItsUnitsSpawnAndEveryMention()
        {
            MapDocument document = original
                .AddUnit("ARMY_3", ["INITIAL"], new Unit { Name = "", Blueprint = "uec1101" })
                .SetTeam("ARMY_3", "South")
                .SetTeam("ARMY_4", "South")
                .SetArmyFactions("ARMY_3", ["uef", "cybran"])
                .SetAlliance("ARMY_3", "ARMY_1", "Enemy");

            MapDocument removed = document.RemoveArmy("ARMY_3");

            Assert.IsNull(removed.FindArmy("ARMY_3"));
            Assert.IsFalse(removed.AllUnits().Any(unit => unit.Army == "ARMY_3"), "its units go with it");
            Assert.IsFalse(removed.Scenario.PlayableArmies.Contains("ARMY_3"));
            Assert.IsFalse(removed.Scenario.ArmyFactions.ContainsKey("ARMY_3"));
            CollectionAssert.AreEqual(new[] { "ARMY_4" }, removed.Scenario.Teams.Single().Armies.ToArray());
            Assert.IsFalse(removed.FindArmy("ARMY_1")!.Alliances.ContainsKey("ARMY_3"));
            Assert.IsFalse(removed.Markers.Spawns.Any(s => s.Name == "ARMY_3"), "its spawn too");
            Assert.AreEqual(original.Markers.Spawns.Length - 1, removed.Markers.Spawns.Length);
        }

        [TestMethod]
        public void AnArmyBecomesExtraOrPlayable()
        {
            MapDocument extra = original.SetTeam("ARMY_8", "A").SetArmyFactions("ARMY_8", ["uef"]).SetArmyPlayable("ARMY_8", false);
            Assert.IsFalse(extra.IsPlayable("ARMY_8"));
            Assert.AreEqual("ARMY_8", extra.Scenario.ExtraArmies[^1]);
            Assert.IsTrue(extra.Scenario.Teams.IsEmpty, "out of its team, which is left empty and goes");
            Assert.IsFalse(extra.Scenario.ArmyFactions.ContainsKey("ARMY_8"));
            Assert.IsTrue(extra.Markers.Spawns.Any(s => s.Name == "ARMY_8"), "its spawn stays");
            Assert.ThrowsException<ArgumentException>(() => extra.SetTeam("ARMY_8", "A"));
            Assert.ThrowsException<ArgumentException>(() => extra.SetArmyFactions("ARMY_8", ["uef"]));

            MapDocument civilians = original.SetArmyPlayable("NEUTRAL_CIVILIAN", true, new Vector2(10, 20));
            Assert.AreEqual("NEUTRAL_CIVILIAN", civilians.Scenario.PlayableArmies[^1]);
            Assert.IsFalse(civilians.Scenario.ExtraArmies.Contains("NEUTRAL_CIVILIAN"));
            Assert.AreEqual(10, civilians.Markers.Spawns.Single(s => s.Name == "NEUTRAL_CIVILIAN").Position.X, "a playable army gets a spawn");
            Assert.AreSame(original, original.SetArmyPlayable("ARMY_1", true), "nothing changes");
        }

        [TestMethod]
        public void FactionsAlliancesAndTeams()
        {
            MapDocument limited = original.SetArmyFactions("ARMY_1", ["seraphim", "UEF"]);
            CollectionAssert.AreEqual(new[] { "uef", "seraphim" }, limited.Scenario.ArmyFactions["ARMY_1"].ToArray(), "in the game's order");
            Assert.IsFalse(limited.SetArmyFactions("ARMY_1", ["uef", "aeon", "cybran", "seraphim"]).Scenario.ArmyFactions.ContainsKey("ARMY_1"), "all four is no limit");
            Assert.IsFalse(limited.SetArmyFactions("ARMY_1", []).Scenario.ArmyFactions.ContainsKey("ARMY_1"));
            Assert.ThrowsException<ArgumentException>(() => original.SetArmyFactions("ARMY_1", ["nomads"]));

            MapDocument allied = original.SetAlliance("ARMY_1", "ARMY_2", "Ally");
            Assert.AreEqual("Ally", allied.FindArmy("ARMY_2")!.Alliances["ARMY_1"], "both ways");
            MapDocument oneWay = allied.SetAlliance("ARMY_1", "ARMY_2", null, mutual: false);
            Assert.IsFalse(oneWay.FindArmy("ARMY_1")!.Alliances.ContainsKey("ARMY_2"));
            Assert.AreEqual("Ally", oneWay.FindArmy("ARMY_2")!.Alliances["ARMY_1"]);
            Assert.ThrowsException<ArgumentException>(() => original.SetAlliance("ARMY_1", "ARMY_1", "Ally"));
            Assert.ThrowsException<ArgumentException>(() => original.SetAlliance("ARMY_1", "ARMY_2", "Friends"));

            MapDocument teams = original.SetTeam("ARMY_1", "North").SetTeam("ARMY_2", "South").SetTeam("ARMY_3", "North");
            CollectionAssert.AreEqual(new[] { "North", "South" }, teams.Scenario.Teams.Select(t => t.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "ARMY_1", "ARMY_3" }, teams.Scenario.Teams[0].Armies.ToArray());
            MapDocument moved = teams.SetTeam("ARMY_2", "North");
            Assert.AreEqual(1, moved.Scenario.Teams.Length, "South is left empty and goes");
            Assert.IsNull(moved.SetTeam("ARMY_1", null).TeamOf("ARMY_1"));
        }

        [TestMethod]
        public void TeamsAreExportedAfterTheFfaTeamAndImportedAgain()
        {
            MapDocument document = original.SetTeam("ARMY_1", "North").SetTeam("ARMY_2", "North").SetTeam("ARMY_3", "South");

            IReadOnlyDictionary<string, byte[]> files = MapExporter.ToBytes(document);
            string scenarioFile = files.Keys.Single(key => key.EndsWith("_scenario.lua", StringComparison.Ordinal));
            MapScenario scenario = MapScenarioParser.Parse(Encoding.Latin1.GetString(files[scenarioFile]));
            IReadOnlyList<MapTeam> written = scenario.Configurations.Single().Teams;
            Assert.AreEqual("FFA", written[0].Name, "the team the game's lobby reads comes first");
            CollectionAssert.AreEqual(original.Scenario.PlayableArmies.ToArray(), written[0].Armies.ToArray());
            CollectionAssert.AreEqual(new[] { "FFA", "North", "South" }, written.Select(t => t.Name).ToArray());
            CollectionAssert.AreEqual(original.Scenario.PlayableArmies.ToArray(), scenario.PlayerArmies.ToArray(), "the players' armies once each");

            string folder = ProjectFolders.New("teams");
            Directory.CreateDirectory(folder);
            foreach ((string name, byte[] bytes) in files)
            {
                File.WriteAllBytes(Path.Combine(folder, name), bytes);
            }
            MapDocument imported = MapImporter.ImportFolder(folder);
            CollectionAssert.AreEqual(original.Scenario.PlayableArmies.ToArray(), imported.Scenario.PlayableArmies.ToArray());
            CollectionAssert.AreEqual(new[] { "North", "South" }, imported.Scenario.Teams.Select(t => t.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "ARMY_1", "ARMY_2" }, imported.Scenario.Teams[0].Armies.ToArray());
            Assert.IsTrue(original.Scenario.Teams.IsEmpty, "a map of the vault has none");
        }

        [TestMethod]
        public void AProjectKeepsItsTeamsAndAnOldOneHasNone()
        {
            MapDocument document = original.SetTeam("ARMY_1", "North");
            string project = ProjectFolders.New("project");
            MapProjectStore.Save(document, project);
            MapDocument loaded = MapProjectStore.Load(project);
            Assert.AreEqual("North", loaded.TeamOf("ARMY_1"));

            // a project saved without teams (as every project before them) has no "teams" at all
            string plain = ProjectFolders.New("project");
            MapProjectStore.Save(original, plain);
            string text = string.Concat(Directory.EnumerateFiles(plain, "*.json", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.IsFalse(text.Contains("\"teams\"", StringComparison.Ordinal));
            Assert.IsTrue(MapProjectStore.Load(plain).Scenario.Teams.IsEmpty);
        }
    }
}
