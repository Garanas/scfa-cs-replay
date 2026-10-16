using System.Text;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Units placed as wrecks (user story U-5): a group named <c>WRECKAGE</c>, as the game and the
    /// vault's maps keep them.
    /// </summary>
    [TestClass]
    public class UnitWrecksTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        /// <summary>Vault maps with wrecks in an extra army and the default script (read only).</summary>
        private static string Vault => ProjectFolders.VaultMaps;

        private static MapDocument WithUnits()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 256 });
            foreach ((string name, float x) in new[] { ("UNIT_1", 40f), ("UNIT_2", 60f), ("UNIT_3", 80f) })
            {
                document = document.AddUnit("NEUTRAL_CIVILIAN", [UnitGroupEdits.InitialGroup], new Unit { Name = name, Blueprint = "uel0307", Position = new(x, 35, 40) });
            }
            return document;
        }

        [TestMethod]
        public void WrecksGoIntoTheArmysWreckageGroupAndBack()
        {
            MapDocument document = WithUnits().SetWrecks(["UNIT_1", "UNIT_2"], wreck: true, out int count);

            Assert.AreEqual(2, count);
            CollectionAssert.AreEquivalent(new[] { "UNIT_1", "UNIT_2" }, document.Wrecks().ToArray());
            Assert.AreEqual(2, document.FindGroup("NEUTRAL_CIVILIAN", [UnitWrecks.GroupName])!.Units.Length);
            Assert.AreEqual("NEUTRAL_CIVILIAN", document.FindUnit("UNIT_1")!.Army, "each stays in its army");

            document.SetWrecks(["UNIT_1"], wreck: true, out int again);
            Assert.AreEqual(0, again, "a wreck stays a wreck");

            document = document.SetWrecks(["UNIT_2"], wreck: false, out int back);
            Assert.AreEqual(1, back);
            Assert.IsFalse(document.Wrecks().Contains("UNIT_2"));
            CollectionAssert.AreEqual(new[] { UnitGroupEdits.InitialGroup }, document.FindUnit("UNIT_2")!.Group.ToArray());
        }

        [TestMethod]
        public void ANestedWreckageGroupIsTheOneTheGameFinds()
        {
            MapDocument document = WithUnits().AddGroup("NEUTRAL_CIVILIAN", ["INITIAL"], UnitWrecks.GroupName, out _);
            CollectionAssert.AreEqual(new[] { "INITIAL", "WRECKAGE" }, document.WreckagePath("NEUTRAL_CIVILIAN").ToArray());
            document = document.SetWrecks(["UNIT_3"], wreck: true, out _);
            CollectionAssert.AreEqual(new[] { "INITIAL", "WRECKAGE" }, document.FindUnit("UNIT_3")!.Group.ToArray());
            Assert.IsTrue(document.Wrecks().Contains("UNIT_3"));
        }

        [TestMethod]
        public void TheExportWritesTheWreckageGroupAsMapsDo()
        {
            MapDocument document = WithUnits().SetWrecks(["UNIT_1"], wreck: true, out _);
            IReadOnlyDictionary<string, byte[]> files = MapExporter.ToBytes(document);
            string save = Encoding.Latin1.GetString(files.Single(file => file.Key.EndsWith("_save.lua", StringComparison.Ordinal)).Value);

            StringAssert.Contains(save, "WRECKAGE");
            MapSave parsed = MapSaveParser.Parse(save);
            MapUnitGroup wreckage = parsed.Armies.Single(army => army.Name == "NEUTRAL_CIVILIAN").Units.Groups.Single(group => group.Name == "WRECKAGE");
            Assert.AreEqual("UNIT_1", wreckage.Units.Single().Name);
            CollectionAssert.Contains(document.Scenario.ExtraArmies.ToArray(), "NEUTRAL_CIVILIAN", "an extra army: created in every game");

            string folder = MapExporter.WriteFolder(document, ProjectFolders.New("wrecks"));
            MapDocument again = MapImporter.ImportFolder(folder);
            CollectionAssert.AreEquivalent(new[] { "UNIT_1" }, again.Wrecks().ToArray());
        }

        [TestMethod]
        public void WrecksOfAPlayableArmyAreAWarning()
        {
            MapDocument document = WithUnits();
            Assert.AreEqual(0, MapChecks.WrecksInPlayableArmies(new ProblemContext(document)).Count());
            document = document.AddUnit("ARMY_1", [UnitWrecks.GroupName], new Unit { Name = "UNIT_9", Blueprint = "uel0307", Position = new(50, 35, 50) });

            Problem problem = MapChecks.WrecksInPlayableArmies(new ProblemContext(document)).Single();

            Assert.AreEqual(ProblemSeverity.Warning, problem.Severity);
            Assert.AreEqual(ObjectRef.Unit("UNIT_9"), problem.Thing);
            StringAssert.Contains(problem.Message, "ARMY_1");
        }

        [TestMethod]
        [DataRow("abhor.v0004", "ARMY_9")]
        [DataRow("setons_clutch_-_faf_version.v0004", "ARMY_9,NEUTRAL_CIVILIAN")]
        [DataRow("sweepwing_sanctum.v0003", "NEUTRAL_CIVILIAN")]
        [DataRow(ProjectFolders.ThetaPassage, "NEUTRAL_CIVILIAN")]
        public void AVaultMapsWrecksSurviveImportAndExport(string map, string armies)
        {
            string folder = map.StartsWith("assets", StringComparison.Ordinal) ? map : Path.Combine(Vault, map);
            if (!Directory.Exists(folder))
            {
                Assert.Inconclusive($"The local vault copy has no {map}");
            }
            string file = Path.GetFileNameWithoutExtension(Directory.GetFiles(folder, "*.scmap").Single());

            MapDocument document = MapImporter.ImportFolder(folder);
            PlacedUnit[] wrecks = [.. document.AllUnits().Where(UnitWrecks.IsWreck)];
            Assert.AreNotEqual(0, wrecks.Length, "the map has wrecks");
            Assert.IsTrue(wrecks.All(unit => armies.Split(',').Contains(unit.Army)), string.Join(", ", wrecks.Select(unit => unit.Army).Distinct()));
            Assert.AreEqual(0, MapChecks.WrecksInPlayableArmies(new ProblemContext(document)).Count());

            IReadOnlyDictionary<string, byte[]> exported = MapExporter.ToBytes(document);
            string original = File.ReadAllText(Path.Combine(folder, file + "_save.lua"), Encoding.Latin1);
            Assert.AreEqual(
                GameContent.Save(MapSaveParser.Parse(original)),
                GameContent.Save(MapSaveParser.Parse(Encoding.Latin1.GetString(exported[file + "_save.lua"]))),
                "the same game content, wrecks included");
            CollectionAssert.AreEquivalent(document.Wrecks().ToArray(), MapImporter.ImportFolder(MapExporter.WriteFolder(document, ProjectFolders.New("wrecks"))).Wrecks().ToArray());
        }
    }
}
