using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The version of the <c>.scmap</c> a map is written as: new maps and the sandbox are 60 with the
    /// skybox the game would give them, and a map switches between 56 and 60.
    /// </summary>
    [TestClass]
    public class ScmapVersionsTest
    {
        [TestMethod]
        [DataRow(256, 585.7907f)]
        [DataRow(512, 1171.5814f)]
        [DataRow(1024, 2343.1628f)]
        public void TheGamesDefaultDomeHasTheSizeOfTheGamesOwnMaps(int size, float scale)
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = size });

            Skybox skybox = Skyboxes.GameDefault(document);

            Assert.AreEqual(scale, skybox.Scale, 0.01f, "as SCMP_012, SCMP_007 and SCMP_001 store it");
            Assert.AreEqual(new Vector3(size / 2f, 0, size / 2f), skybox.Position);
            Assert.AreEqual(Skyboxes.DefaultStartAngle, skybox.SubtractHeight);
            Assert.AreEqual(scale * 0.1536f, skybox.ZenithHeight, 0.01f);
            CollectionAssert.AreEqual(Skyboxes.DefaultCirrusLayers, skybox.CirrusLayers);
            Assert.IsTrue(skybox.Planets.IsEmpty);
        }

        [TestMethod]
        public void TheDomesBottomIsAtTheWaterElseTheLowestGround()
        {
            MapDocument wet = NewMap.Create(new NewMapSettings { Size = 256, Water = true, WaterElevation = 17.5f });
            MapDocument dry = NewMap.Create(new NewMapSettings { Size = 256, Water = false, Height = 30 });

            Assert.AreEqual(17.5f, Skyboxes.GameDefault(wet).HorizonHeight);
            Assert.AreEqual(30, Skyboxes.GameDefault(dry).HorizonHeight, 0.01f);
        }

        [TestMethod]
        public void ANewMapIsVersion60WithTheGamesDefaultSkyboxAndExportsIt()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 256 });

            Assert.AreEqual(60, document.Terrain.ScmapVersion);
            Assert.IsNotNull(document.Sky.Skybox);
            Scmap read = ScmapParser.Parse(ScmapWriter.Write(MapExporter.Export(document).Scmap));
            Assert.AreEqual(60, read.Version);
            Assert.AreEqual(document.Sky.Skybox.Scale, read.Skybox!.Scale);
            Assert.AreEqual(4, read.Skybox.CirrusLayers.Count);
        }

        [TestMethod]
        public void TheSandboxIsVersion60WithASkybox()
        {
            MapDocument sandbox = SandboxMap.Build(AssetCatalog.Empty);

            Assert.AreEqual(60, sandbox.Terrain.ScmapVersion);
            Assert.IsNotNull(sandbox.Sky.Skybox);
        }

        [TestMethod]
        public void AMapSwitchesBetween56And60KeepingItsSkybox()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 256 });
            MapDocument older = ScmapVersions.Set(document with { Sky = document.Sky with { Skybox = null } }, 56);
            Assert.AreEqual(56, older.Terrain.ScmapVersion);
            Assert.IsNull(older.Sky.Skybox);

            MapDocument newer = ScmapVersions.Set(older, 60);
            Assert.AreEqual(60, newer.Terrain.ScmapVersion);
            Assert.AreEqual(Skyboxes.GameDefault(older), newer.Sky.Skybox, "the dome the game gave it");

            MapDocument back = ScmapVersions.Set(newer, 56);
            Assert.AreSame(newer.Sky.Skybox, back.Sky.Skybox, "kept in the project, unused by the game");
            Assert.AreSame(back, ScmapVersions.Set(back, 56), "the same version changes nothing");
            Assert.ThrowsException<ArgumentException>(() => ScmapVersions.Set(back, 58));
        }

        [TestMethod]
        public void AVersion53MapKeepsItsVersion()
        {
            EditedMap map = EditedMap.ImportMap(ProjectFolders.CoopR01);
            Assert.AreEqual(53, map.Document.Terrain.ScmapVersion);

            Assert.ThrowsException<InvalidOperationException>(() => ScmapVersions.Set(map.Document, 60));
        }
    }
}
