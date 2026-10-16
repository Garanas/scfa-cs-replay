using System.Text;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class MapExporterTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static string Text(byte[] bytes) => Encoding.Latin1.GetString(bytes);

        private static string Original(string map, string file) => File.ReadAllText(Path.Combine(map, file), Encoding.Latin1);

        [TestMethod]
        [DataRow(ProjectFolders.ThetaPassage)]
        [DataRow(ProjectFolders.HardFfa)]
        [DataRow(ProjectFolders.CoopR01)]
        public void ExportsAnImportedMapWithTheSameContent(string map)
        {
            string file = Path.GetFileNameWithoutExtension(Directory.GetFiles(map, "*.scmap").Single());

            IReadOnlyDictionary<string, byte[]> exported = MapExporter.ToBytes(MapImporter.ImportFolder(map));

            CollectionAssert.AreEquivalent(
                Directory.GetFiles(map).Select(Path.GetFileName).ToList(),
                exported.Keys.ToList(),
                "the same files, under the same names");

            Assert.AreEqual(
                GameContent.Save(MapSaveParser.Parse(Original(map, file + "_save.lua"))),
                GameContent.Save(MapSaveParser.Parse(Text(exported[file + "_save.lua"]))));
            Assert.AreEqual(
                GameContent.Scenario(MapScenarioParser.Parse(Original(map, file + "_scenario.lua"))),
                GameContent.Scenario(MapScenarioParser.Parse(Text(exported[file + "_scenario.lua"]))));

            // the .scmap is the same but for the encoding of its textures and the derived decal counters
            Scmap original = ScmapParser.Parse(File.ReadAllBytes(Path.Combine(map, file + ".scmap")));
            Scmap written = ScmapParser.Parse(exported[file + ".scmap"]);
            Scmap withTextures = original with
            {
                Preview = written.Preview,
                NormalMaps = written.NormalMaps,
                StratumMasks = written.StratumMasks,
                WaterMap = written.WaterMap,
                NextDecalId = written.NextDecalId,
                NextDecalGroupId = written.NextDecalGroupId,
            };
            Assert.AreEqual(
                (original.Decals.Count == 0 ? 0 : original.Decals.Max(d => d.Id) + 1, original.DecalGroups.Count == 0 ? 0 : original.DecalGroups.Max(g => g.Id) + 1),
                (written.NextDecalId, written.NextDecalGroupId));
            CollectionAssert.AreEqual(ScmapWriter.Write(withTextures), exported[file + ".scmap"]);

            // the uncompressed textures keep every pixel, the compressed ones nearly every
            AssertSamePixels(original.Preview, written.Preview, 0);
            for (int i = 0; i < original.StratumMasks.Count; i++)
            {
                AssertSamePixels(original.StratumMasks[i], written.StratumMasks[i], 0);
            }
            AssertSamePixels(original.NormalMaps[0], written.NormalMaps[0], 0.1);
            AssertSamePixels(original.WaterMap, written.WaterMap, 0.1);
        }

        private static void AssertSamePixels(byte[] expected, byte[] actual, double meanError)
        {
            (int width, int height, TextureFormat format, byte[] expectedRgba) = DdsTexture.Read(expected, "expected");
            (int actualWidth, int actualHeight, TextureFormat actualFormat, byte[] actualRgba) = DdsTexture.Read(actual, "actual");
            Assert.AreEqual((width, height, format), (actualWidth, actualHeight, actualFormat));

            double error = expectedRgba.Zip(actualRgba, (a, b) => Math.Abs(a - b)).Average();
            Assert.IsTrue(error <= meanError, $"a mean error of {error} per channel");
        }

        [TestMethod]
        public void SortsTheMarkersByWhatTheyAre()
        {
            MarkerSet theta = MapImporter.ImportFolder(ProjectFolders.ThetaPassage).Markers;
            MarkerSet coop = MapImporter.ImportFolder(ProjectFolders.CoopR01).Markers;

            CollectionAssert.IsSubsetOf(new[] { "ARMY_1", "ARMY_2" }, theta.Spawns.Select(s => s.Army).ToList());
            Assert.AreEqual(24, theta.Resources.Count(r => r.Kind == ResourceKind.Mass));
            Assert.AreEqual(2, theta.Resources.Count(r => r.Kind == ResourceKind.Hydrocarbon));
            Assert.AreEqual(139, theta.PathNodes.Count(n => n.Layer == PathLayer.Land));
            Assert.AreEqual(256, theta.PathNodes.Count(n => n.Layer == PathLayer.Air));
            Assert.IsTrue(theta.PathNodes.All(n => n.AdjacentTo.Length > 0));

            Assert.AreEqual(36, coop.Resources.Length);
            Assert.AreEqual(1, coop.WeatherDefinitions.Length);
            Assert.AreEqual(7, coop.WeatherGenerators.Length);
            Assert.AreEqual(2, coop.AiMarkers.Count(m => m.Type == "Rally Point"));
        }

        [TestMethod]
        public void KeepsTheNoRushOffsetsPerArmy()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);

            Assert.AreEqual(8, document.Markers.Spawns.Count(spawn => spawn.NoRushOffset is not null));
            Assert.AreEqual(-35, document.Markers.Spawns.Single(spawn => spawn.Army == "ARMY_1").NoRushOffset!.Value.X);
            CollectionAssert.AreEqual(new[] { "ARMY_9", "NEUTRAL_CIVILIAN" }, document.Scenario.ExtraArmies.ToList());
            Assert.AreEqual(8, document.Scenario.PlayableArmies.Length);
        }

        [TestMethod]
        public void WritesTheDecorationOfAMarkersType()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);

            string save = Text(MapExporter.ToBytes(document)["theta_passage_-_faf_version_save.lua"]);

            StringAssert.Contains(save, """
                                ['Mass 08'] = {
                                    color = STRING( 'ff808080' ),
                                    size = FLOAT( 1 ),
                                    resource = BOOLEAN( true ),
                                    amount = FLOAT( 100 ),
                                    prop = STRING( '/env/common/props/markers/M_Blank_prop.bp' ),
                                    type = STRING( 'Mass' ),
                                    orientation = VECTOR3( 0, 0, 0 ),
                                    position = VECTOR3( 14.5, 5.98474, 31.5 ),
                                },
                """.ReplaceLineEndings("\n"));
        }

        [TestMethod]
        public void PointsThePathsAtTheNewVersion()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.CoopR01) with { Version = 21 };

            IReadOnlyDictionary<string, byte[]> exported = MapExporter.ToBytes(document);

            Assert.AreEqual("SCCA_Coop_R01.v0021", document.Folder.ToString());
            MapScenario scenario = MapScenarioParser.Parse(Text(exported["SCCA_Coop_R01_scenario.lua"]));
            Assert.AreEqual(21, scenario.MapVersion);
            Assert.AreEqual("/maps/SCCA_Coop_R01.v0021/SCCA_Coop_R01.scmap", scenario.Map);
            Assert.AreEqual("/maps/SCCA_Coop_R01.v0021/SCCA_Coop_R01_save.lua", scenario.Save);

            string save = Text(exported["SCCA_Coop_R01_save.lua"]);
            StringAssert.Contains(save, "plans = '/maps/SCCA_Coop_R01.v0021/SCCA_Coop_R01_playerplanlist.lua'");
            Assert.IsFalse(save.Contains(".v0020"), "every path names the new version");
        }

        [TestMethod]
        public void KeepsTheScriptAndTheOtherFiles()
        {
            string map = Path.Combine(ProjectFolders.New("map"), "my_map.v0003");
            Directory.CreateDirectory(Path.Combine(map, "env", "layers"));
            foreach (string file in Directory.GetFiles(ProjectFolders.HardFfa))
            {
                File.Copy(file, Path.Combine(map, Path.GetFileName(file).Replace("HardFFA", "my_map")));
            }
            string scenario = Path.Combine(map, "my_map_scenario.lua");
            File.WriteAllText(scenario, File.ReadAllText(scenario).Replace("HardFFA.v0001/HardFFA", "my_map.v0003/my_map").Replace("map_version=1", "map_version=3"));
            File.WriteAllText(Path.Combine(map, "my_map_script.lua"), "local plans = '/maps/my_map.v0003/plans.lua'\n");
            File.WriteAllBytes(Path.Combine(map, "env", "layers", "rock.dds"), [1, 2, 3]);

            MapDocument document = MapImporter.ImportFolder(map) with { Version = 4 };
            IReadOnlyDictionary<string, byte[]> exported = MapExporter.ToBytes(document);

            Assert.AreEqual("my_map", document.Name);
            Assert.AreEqual("local plans = '/maps/my_map.v0004/plans.lua'\n", Text(exported["my_map_script.lua"]));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, exported["env/layers/rock.dds"]);
        }

        [TestMethod]
        public void WritesTwoTextureSlotsPerDecal()
        {
            // the engine's decal holds two textures; a decal with one (the sandbox's) gets an empty second
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);
            document = document with { Decals = document.Decals.SetItem(0, document.Decals[0] with { Textures = ["/env/rock.dds"] }) };

            Scmap scmap = MapExporter.Export(document).Scmap;

            CollectionAssert.AreEqual(new[] { "/env/rock.dds", "" }, scmap.Decals[0].Textures.ToArray());
            Assert.IsTrue(scmap.Decals.All(decal => decal.Textures.Count == 2));
        }

        [TestMethod]
        public void WritesTheFolderOfTheVersion()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            string output = ProjectFolders.New("output");

            string folder = MapExporter.WriteFolder(document, output);

            Assert.AreEqual(Path.Combine(output, "HardFFA.v0001"), folder);
            Assert.AreEqual(3, Directory.GetFiles(folder).Length);
            Assert.AreEqual(2226, ScmapParser.Parse(File.ReadAllBytes(Path.Combine(folder, "HardFFA.scmap"))).Props.Count);
        }

        [TestMethod]
        public void NamesWhatIsMissing()
        {
            string map = ProjectFolders.New("map");
            Directory.CreateDirectory(map);
            File.Copy(Path.Combine(ProjectFolders.HardFfa, "HardFFA.scmap"), Path.Combine(map, "HardFFA.scmap"));

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => MapImporter.ImportFolder(map));

            StringAssert.Contains(exception.Message, "0 files named *_scenario.lua");
        }
    }
}
