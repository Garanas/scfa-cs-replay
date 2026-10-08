using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class MapProjectStoreTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static string Saved(string map)
        {
            string project = ProjectFolders.New("project");
            MapProjectStore.Save(MapImporter.ImportFolder(map), project);
            return project;
        }

        [TestMethod]
        [DataRow(ProjectFolders.ThetaPassage)]
        [DataRow(ProjectFolders.HardFfa)]
        [DataRow(ProjectFolders.CoopR01)]
        public void LoadsWhatItSaved(string map)
        {
            MapDocument document = MapImporter.ImportFolder(map);
            string project = ProjectFolders.New("project");

            MapProjectStore.Save(document, project);
            MapDocument loaded = MapProjectStore.Load(project);

            // the same document makes the same files, byte for byte
            IReadOnlyDictionary<string, byte[]> expected = MapExporter.ToBytes(document);
            IReadOnlyDictionary<string, byte[]> actual = MapExporter.ToBytes(loaded);
            CollectionAssert.AreEquivalent(expected.Keys.ToList(), actual.Keys.ToList());
            foreach ((string path, byte[] bytes) in expected)
            {
                CollectionAssert.AreEqual(bytes, actual[path], path);
            }
        }

        [TestMethod]
        public void WritesTheFilesOfAProject()
        {
            string project = Saved(ProjectFolders.ThetaPassage);

            string[] files = Directory.GetFiles(project, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(project, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "armies.json",
                "decals.json",
                "heightmap.png",
                "map.json",
                "markers.json",
                "normal-map.png",
                "preview.png",
                "props.json",
                "stratum-masks/stratum-1.png",
                "stratum-masks/stratum-2.png",
                "stratum-masks/stratum-3.png",
                "stratum-masks/stratum-4.png",
                "stratum-masks/stratum-5.png",
                "stratum-masks/stratum-6.png",
                "stratum-masks/stratum-7.png",
                "stratum-masks/stratum-8.png",
                "terrain-types.png",
                "water/depth-bias.png",
                "water/flatness.png",
                "water/foam.png",
                "water/water-map.png",
            }, files);
        }

        [TestMethod]
        public void WritesJsonAMapMakerCanRead()
        {
            string project = Saved(ProjectFolders.ThetaPassage);

            string markers = File.ReadAllText(Path.Combine(project, "markers.json")).ReplaceLineEndings("").Replace(" ", "");
            StringAssert.Contains(markers, "{\"army\":\"ARMY_1\",\"position\":{\"x\":14.5,\"y\":5.98474,\"z\":241.5}}");
            StringAssert.Contains(markers, "\"name\":\"Mass08\",\"kind\":\"Mass\",\"position\":{\"x\":14.5,\"y\":5.98474,\"z\":31.5}}");
            Assert.IsFalse(markers.Contains("\"orientation\""), "a marker that is not turned leaves its orientation out");

            string map = File.ReadAllText(Path.Combine(project, "map.json")).ReplaceLineEndings("").Replace(" ", "");
            StringAssert.Contains(map, "\"deepWaterColor\":\"#");
            StringAssert.Contains(map, "\"normalMapFormat\":\"Dxt5\"");
        }

        [TestMethod]
        public void ReadsARawHeightmap()
        {
            string project = Saved(ProjectFolders.HardFfa);
            ushort[] samples = MapProjectStore.Load(project).Terrain.Heightmap.Samples.ToArray();
            byte[] raw = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, raw, 0, raw.Length);
            File.Delete(Path.Combine(project, MapProjectLayout.Heightmap));
            File.WriteAllBytes(Path.Combine(project, MapProjectLayout.HeightmapRaw), raw);

            CollectionAssert.AreEqual(samples, MapProjectStore.Load(project).Terrain.Heightmap.Samples.ToArray());

            File.WriteAllBytes(Path.Combine(project, MapProjectLayout.HeightmapRaw), raw[..^2]);
            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => MapProjectStore.Load(project));
            StringAssert.Contains(exception.Message, "257 by 257");
        }

        [TestMethod]
        public void LeavesALayerWithoutAMaskUnused()
        {
            string project = Saved(ProjectFolders.HardFfa);
            File.Delete(Path.Combine(project, MapProjectLayout.StratumMask(8)));

            StratumMasks masks = MapProjectStore.Load(project).Terrain.StratumMasks;

            Assert.IsTrue(masks.Layers[7].ToArray().All(value => value == 0));
        }

        [TestMethod]
        public void NamesAnImageOfTheWrongSize()
        {
            string project = Saved(ProjectFolders.HardFfa);
            ProjectImages.WriteGray8(Path.Combine(project, MapProjectLayout.TerrainTypes), 100, 100, new byte[100 * 100]);

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => MapProjectStore.Load(project));

            StringAssert.Contains(exception.Message, MapProjectLayout.TerrainTypes);
            StringAssert.Contains(exception.Message, "100 by 100 pixels, expected 256 by 256");
        }

        [TestMethod]
        public void NamesAFieldThatIsMissing()
        {
            string project = Saved(ProjectFolders.HardFfa);
            string markers = Path.Combine(project, MapProjectLayout.Markers);
            File.WriteAllText(markers, File.ReadAllText(markers).Replace("\"spawns\"", "\"spawn\""));

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => MapProjectStore.Load(project));

            StringAssert.Contains(exception.Message, MapProjectLayout.Markers);
            StringAssert.Contains(exception.Message, "spawns");
        }

        [TestMethod]
        public void RefusesAFolderThatIsNotAProject()
        {
            string folder = ProjectFolders.New("folder");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() =>
                MapProjectStore.Save(MapImporter.ImportFolder(ProjectFolders.HardFfa), folder));

            StringAssert.Contains(exception.Message, "holds no project");
        }

        [TestMethod]
        public void RemovesTheFilesTheDocumentNoLongerHas()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            string project = ProjectFolders.New("project");
            MapProjectStore.Save(document with
            {
                Files = document.Files.Add("env/rock.dds", [1, 2, 3]),
                Script = ImmutableArray.Create((byte)'-', (byte)'-'),
            }, project);

            MapProjectStore.Save(document with { Script = null }, project);

            Assert.IsFalse(File.Exists(Path.Combine(project, "files", "env", "rock.dds")));
            Assert.IsFalse(File.Exists(Path.Combine(project, MapProjectLayout.Script)));
        }
    }
}
