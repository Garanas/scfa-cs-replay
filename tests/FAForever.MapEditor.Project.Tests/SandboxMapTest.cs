using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public sealed class SandboxMapTest
    {
        /// <summary>
        /// A PNG of one grey pixel.
        /// </summary>
        private static readonly byte[] Pixel = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGNgAAAAAgABSK+kcQAAAABJRU5ErkJggg==");

        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        /// <summary>
        /// A few converted assets, as the conversion writes them: two stratum textures, two props, a
        /// decal and a civilian unit.
        /// </summary>
        private static void WriteAssets(Workspace workspace)
        {
            void Write(string path, string? text = null)
            {
                string file = Path.Combine(workspace.AssetsFolder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (text is null)
                {
                    File.WriteAllBytes(file, Pixel);
                }
                else
                {
                    File.WriteAllText(file, text);
                }
            }
            Write("env/evergreen/layers/eg_grass001_albedo.png");
            Write("env/evergreen/layers/eg_grass001_normal.png");
            Write("env/evergreen/layers/eg_rock_albedo.png");
            Write("env/evergreen/props/rocks/rock01_prop.json", """{ "id": "x", "reclaimMass": 20 }""");
            Write("env/evergreen/props/trees/pine06_prop.json", """{ "id": "x", "size": { "x": 1, "y": 9, "z": 1 } }""");
            Write("env/evergreen/decals/e_mud001_albedo.png");
            Write("units/uec1101/uec1101_unit.json", """{ "id": "UEC1101", "name": "UEF Residential Building", "faction": "Uef", "techLevel": 1, "categories": ["UEF", "STRUCTURE", "CIVILIAN"] }""");
        }

        [TestMethod]
        public void SettingUpAWorkspaceWritesAPlainSandboxThatOpensAndIsNotPublishable()
        {
            Workspace workspace = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/vault");

            EditedMap sandbox = EditedMap.OpenProject(SandboxMap.FolderOf(workspace));
            MapDocument document = sandbox.Document;
            Assert.IsFalse(document.IsPublishable);
            Assert.AreEqual(SandboxMap.Size, document.Width);
            Assert.AreEqual(2, document.Markers.Spawns.Length);
            Assert.AreEqual(3, document.Markers.PathNodes.Length);
            CollectionAssert.AreEqual(new[] { document.Markers.PathNodes[0].Id, document.Markers.PathNodes[2].Id },
                document.Markers.PathNodes[1].AdjacentTo.ToArray(), "the middle node links to both others");
            Assert.AreEqual(1, document.Markers.AiMarkers.Length);
            Assert.AreEqual(1, document.Markers.Areas.Length);
            Assert.AreEqual(3, document.Markers.Chains.Single().MarkerIds.Length);
            Assert.IsTrue(document.Props.IsEmpty, "without assets: no props");
            Assert.IsFalse(document.AllUnits().Any());
            Assert.IsTrue(document.Terrain.Layers.All(layer => layer.AlbedoPath.Length == 0));
            Assert.IsTrue(document.Water.HasWater);
        }

        [TestMethod]
        public void ThePlainSandboxIsMadeAgainFromTheAssetsOnceTheyAreConverted()
        {
            Workspace workspace = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/vault");
            WriteAssets(workspace);

            SandboxMap.Ensure(workspace);

            MapDocument document = EditedMap.OpenProject(SandboxMap.FolderOf(workspace)).Document;
            Assert.AreEqual("/env/evergreen/layers/eg_grass001_albedo.dds", document.Terrain.Layers[0].AlbedoPath, "the biome's textures, by their game paths");
            Assert.AreEqual("/env/evergreen/layers/eg_grass001_normal.dds", document.Terrain.Layers[0].NormalPath);
            Assert.AreEqual(2, document.Props.Length);
            Assert.AreEqual("/env/evergreen/props/rocks/rock01_prop.bp", document.Props[0].Blueprint);
            Assert.AreEqual("/env/evergreen/decals/e_mud001_albedo.dds", document.Decals.Single().Textures[0]);
            Assert.AreEqual(document.Decals[0].Id, document.DecalGroups.Single().DecalIds.Single());
            PlacedUnit unit = document.AllUnits().Single();
            Assert.AreEqual("uec1101", unit.Unit.Blueprint);
            Assert.AreEqual("NEUTRAL_CIVILIAN", unit.Army);
            CollectionAssert.AreEqual(new[] { "INITIAL" }, unit.Group.ToArray());
            Assert.IsFalse(document.IsPublishable);
        }

        [TestMethod]
        public void ASavedSandboxIsKeptAndADeletedOneComesBack()
        {
            Workspace workspace = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/vault");
            string folder = SandboxMap.FolderOf(workspace);
            EditedMap sandbox = EditedMap.OpenProject(folder);
            sandbox.Session.Edit("Move the spawn", document => document.UpdateMarker(document.Markers.Spawns[0].Id, marker => marker with { Position = marker.Position with { X = 100 } }));
            sandbox.Save();
            WriteAssets(workspace);

            SandboxMap.Ensure(workspace);
            Assert.AreEqual(100, EditedMap.OpenProject(folder).Document.Markers.Spawns[0].Position.X, "the person's sandbox is not overwritten");
            Assert.IsTrue(EditedMap.OpenProject(folder).Document.Props.IsEmpty);

            Directory.Delete(folder, recursive: true);
            SandboxMap.Ensure(workspace);
            Assert.AreEqual(2, EditedMap.OpenProject(folder).Document.Props.Length, "written again, from the assets");
        }

        [TestMethod]
        public void TheSandboxExportsToAMapFolderTheGameReads()
        {
            Workspace workspace = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/vault");
            WriteAssets(workspace);
            SandboxMap.Ensure(workspace);
            EditedMap sandbox = EditedMap.OpenProject(SandboxMap.FolderOf(workspace));

            string exported = sandbox.Export(ProjectFolders.New("maps"));

            MapDocument back = EditedMap.ImportMap(exported).Document;
            Assert.AreEqual(sandbox.Document.Markers.All.Count(), back.Markers.All.Count());
            Assert.AreEqual(2, back.Props.Length);
            Assert.AreEqual(1, back.AllUnits().Count());
            Assert.IsTrue(back.IsPublishable, "the game's files do not carry the mark: it belongs to the project");
        }

        [TestMethod]
        public void PublishableIsKeptByTheProjectAndTrueForOtherProjects()
        {
            string folder = ProjectFolders.New("project");
            EditedMap map = EditedMap.ImportMap(ProjectFolders.HardFfa);
            map.SaveAs(folder);
            Assert.IsTrue(EditedMap.OpenProject(folder).Document.IsPublishable);
            Assert.IsFalse(File.ReadAllText(Path.Combine(folder, "map.json")).Contains("publishable", StringComparison.OrdinalIgnoreCase), "left out while true");
        }
    }
}
