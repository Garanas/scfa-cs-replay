using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class PreviewPageTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static AssetEntry Entry(AssetKind kind, string key, string name) => new AssetEntry
        {
            Kind = kind,
            Key = key,
            Keys = [key],
            Name = name,
            Code = key,
            Group = "Other",
            Category = "other",
            Family = key,
            File = "",
        };

        /// <summary>
        /// A catalogue in which every prop of the map gives 10 mass and every wrecked unit costs 1000
        /// mass and keeps 0.9 of it, with an icon.
        /// </summary>
        private static AssetCatalog Catalog(MapDocument document, string icon) => AssetCatalog.Of([
            .. document.Props.Select(prop => prop.Blueprint).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(blueprint => Entry(AssetKind.Prop, AssetCatalog.KeyOf(blueprint), "prop") with { ReclaimMass = 10 }),
            .. document.AllUnits().Where(UnitWrecks.IsWreck).Select(unit => ReclaimValues.UnitId(unit.Unit.Blueprint)).Distinct()
                .Select(id => Entry(AssetKind.Unit, id, "Test unit " + id) with { MassCost = 1000, WreckageMass = 0.9, Icon = icon }),
        ]);

        private static string PageOf(IReadOnlyDictionary<string, byte[]> files) => Encoding.UTF8.GetString(files[PreviewPageFiles.Page]);

        [TestMethod]
        public void RunsNoScriptsAndEverySwitchHasItsInput()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);

            string page = PageOf(PreviewPageFiles.Render(document, null));

            Assert.IsFalse(page.Contains("<script", StringComparison.OrdinalIgnoreCase));
            HashSet<string> inputs = [.. Regex.Matches(page, "<input[^>]*id=\"([^\"]+)\"").Select(match => match.Groups[1].Value)];
            HashSet<string> labels = [.. Regex.Matches(page, "<label[^>]*for=\"([^\"]+)\"").Select(match => match.Groups[1].Value)];
            CollectionAssert.AreEquivalent(inputs.ToList(), labels.ToList(), "every switch is a label and its input");
            StringAssert.StartsWith(page, "<!doctype html>");
            StringAssert.Contains(page, "<meta charset=\"utf-8\">");
        }

        [TestMethod]
        public void EveryPictureThePageNamesIsWritten()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);
            string icon = Path.Combine(ProjectFolders.New("icon"), "icon.png");
            Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
            File.WriteAllBytes(icon, [1, 2, 3]);

            IReadOnlyDictionary<string, byte[]> files = PreviewPageFiles.Render(document, null, Catalog(document, icon));

            string page = PageOf(files);
            List<string> named = [.. Regex.Matches(page, "(?:src|href)=\"(assets/[^\"]+)\"").Select(match => match.Groups[1].Value)
                .Concat(Regex.Matches(page, "srcset=\"([^\"]+)\"").SelectMany(match => match.Groups[1].Value.Split(", ").Select(part => part.Split(' ')[0])))
                .Distinct()];
            Assert.IsTrue(named.Count > 21);
            foreach (string path in named)
            {
                Assert.IsTrue(files.ContainsKey(path), path);
            }
            Assert.IsTrue(files.Keys.Any(path => path.StartsWith("assets/icons/", StringComparison.Ordinal)), "the wrecks' icons");
        }

        [TestMethod]
        public void ShowsTheStartsAndTheWrecksWithTheirValue()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);
            int wrecks = document.AllUnits().Count(UnitWrecks.IsWreck);
            Assert.IsTrue(wrecks > 0, "Theta Passage keeps wrecks");

            string plain = PageOf(PreviewPageFiles.Render(document, null));
            string valued = PageOf(PreviewPageFiles.Render(document, null, Catalog(document, "")));

            Assert.AreEqual(document.Markers.Spawns.Length, Regex.Matches(plain, "class=\"mk mk-start\"").Count);
            Assert.AreEqual(wrecks, Regex.Matches(plain, "class=\"mk mk-wreck\"").Count);
            Assert.IsFalse(plain.Contains("All reclaim closest to it", StringComparison.Ordinal), "no reclaim without blueprints");
            Assert.IsFalse(plain.Contains(" value</span>", StringComparison.Ordinal));
            StringAssert.Contains(valued, "All reclaim closest to it");
            Assert.AreEqual(wrecks, Regex.Matches(valued, "High value").Count, "900 mass a wreck is high");
            StringAssert.Contains(valued, "Test unit ");
        }

        [TestMethod]
        public void CountsEveryPropAndWreckForTheNearestStartOnly()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);
            Spawn first = document.Markers.Spawns[0];
            Vector2 at = new Vector2(first.Position.X, first.Position.Z);
            Vector2 other = document.Markers.Spawns.Skip(1).Select(spawn => new Vector2(spawn.Position.X, spawn.Position.Z)).MinBy(place => Vector2.Distance(place, at));
            Vector2 toward = Vector2.Normalize(other - at);
            float apart = Vector2.Distance(at, other);
            Prop PropAt(int id, Vector2 place) => new Prop { Id = id, Blueprint = "/env/rock.bp", Position = new Vector3(place.X, 0, place.Y) };
            document = document with
            {
                Props =
                [
                    PropAt(1, at + toward * 20),                // near the first start
                    PropAt(2, at + toward * 45),                // far from it, when the starts are further apart
                    PropAt(3, at + toward * (apart / 2 - 1)),   // between the two, a little closer to the first
                    PropAt(4, other + toward * 5),              // at the other start
                ],
            };

            IReadOnlyList<StartSurroundings> around = StartSurroundings.Of(document, _ => 10, _ => 100);

            StartSurroundings mine = around[0];
            StartSurroundings theirs = around.Single(start => new Vector2(start.Start.Position.X, start.Start.Position.Z) == other);
            Assert.AreEqual(10, mine.ReclaimNear.Props);
            Assert.AreEqual(apart > 90 ? 20 : 30, mine.ReclaimFar.Props);
            Assert.AreEqual(30, mine.ReclaimClosest.Props, "the prop between the two counts for the nearer start only");
            Assert.AreEqual(10, theirs.ReclaimClosest.Props);
            Assert.AreEqual(40, around.Sum(start => start.ReclaimClosest.Props), "every prop once");
            Assert.AreEqual(100 * document.AllUnits().Count(UnitWrecks.IsWreck), around.Sum(start => start.ReclaimClosest.Wrecks), "every wreck once");
        }

        [TestMethod]
        [DataRow(99.9, WreckValue.Low)]
        [DataRow(100, WreckValue.Medium)]
        [DataRow(499, WreckValue.Medium)]
        [DataRow(500, WreckValue.High)]
        public void BandsAWrecksMass(double mass, WreckValue value) => Assert.AreEqual(value, ReclaimValues.ValueOf(mass));

        [TestMethod]
        [DataRow("uel0202", "uel0202")]
        [DataRow("/units/UEL0202/UEL0202_unit.bp", "uel0202")]
        public void NamesAUnitByItsId(string blueprint, string id) => Assert.AreEqual(id, ReclaimValues.UnitId(blueprint));
    }
}
