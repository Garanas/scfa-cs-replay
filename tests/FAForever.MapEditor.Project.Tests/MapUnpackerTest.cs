namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class MapUnpackerTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        [TestMethod]
        public void WritesTheFilesOfAProject()
        {
            string project = ProjectFolders.New("project");

            MapUnpacker.Unpack(ProjectFolders.ThetaPassage, project);

            string[] files = Directory.GetFiles(project, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(project, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "decals.json",
                "heightmap.png",
                "map.json",
                "map/theta_passage_-_faf_version_save.lua",
                "map/theta_passage_-_faf_version_scenario.lua",
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

            MapProjectManifest manifest = MapProjectJson.Read<MapProjectManifest>(Path.Combine(project, MapProjectLayout.Manifest));
            Assert.AreEqual(60, manifest.ScmapVersion);
            Assert.AreEqual(256, manifest.Width);
            Assert.AreEqual(ScmapTextureFormat.Dxt5, manifest.NormalMapFormat);
            Assert.AreEqual((257, 257), ProjectImages.Size(Path.Combine(project, MapProjectLayout.Heightmap)));
        }

        [TestMethod]
        public void WritesVectorsAsArrays()
        {
            string project = ProjectFolders.New("project");

            MapUnpacker.Unpack(ProjectFolders.ThetaPassage, project);

            string props = File.ReadAllText(Path.Combine(project, MapProjectLayout.Props));
            StringAssert.Contains(props, "\"blueprint\": \"/env/redrocks/props/thetabridge01_prop.bp\"");
            StringAssert.Contains(props.ReplaceLineEndings("").Replace(" ", ""), "\"position\":[143.5,4.3535156,143.5]");
        }

        [TestMethod]
        public void KeepsFourLayersInAVersion53Map()
        {
            string project = ProjectFolders.New("project");

            MapUnpacker.Unpack(ProjectFolders.CoopR01, project);

            Assert.AreEqual(4, Directory.GetFiles(Path.Combine(project, MapProjectLayout.StratumMasks)).Length);
        }

        [TestMethod]
        public void RefusesAProjectFolderThatIsNotEmpty()
        {
            string project = ProjectFolders.New("project");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "notes.txt"), "mine");

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => MapUnpacker.Unpack(ProjectFolders.HardFfa, project));

            StringAssert.Contains(exception.Message, "not empty");
        }

        [TestMethod]
        public void RefusesAFolderWithoutAMap()
        {
            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() =>
                MapUnpacker.Unpack("assets", ProjectFolders.New("project")));

            StringAssert.Contains(exception.Message, "0 .scmap files");
        }
    }
}
