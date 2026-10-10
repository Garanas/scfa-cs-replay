using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class NewMapTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        [TestMethod]
        [DataRow(256, "evergreen", true)]
        [DataRow(1024, "desert", false)]
        [DataRow(512, "red-barrens", true)]
        public void MakesAMapThatExportsAndImportsAsTheGameReadsIt(int size, string biome, bool water)
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Name = "My New Map!", Size = size, Height = 40, Water = water, WaterElevation = 25, Biome = biome, Players = 4 });

            Assert.AreEqual("my_new_map", document.Name);
            Assert.AreEqual(size, document.Width);
            Assert.AreEqual(40, document.Terrain.Heightmap.HeightAt(size / 3f, size / 2f), 0.01);
            Assert.AreEqual(water, document.Water.HasWater);
            Assert.AreEqual(20, document.Water.ElevationDeep);
            Assert.AreEqual(10, document.Water.ElevationAbyss);
            Assert.AreEqual(4, document.Markers.Spawns.Length);
            Assert.AreEqual("AREA_1", document.Markers.Areas[0].Name, "the playable area");
            Assert.AreEqual(10, document.Terrain.Layers.Length);
            Assert.AreEqual(8, document.Terrain.StratumMasks.Layers.Length);
            StringAssert.Contains(document.Terrain.Layers[1].AlbedoPath, biome == "red-barrens" ? "red barrens" : biome);

            string output = ProjectFolders.New("export");
            string folder = MapExporter.WriteFolder(document, output);
            Assert.AreEqual("my_new_map.v0001", Path.GetFileName(folder));
            MapDocument imported = MapImporter.ImportFolder(folder);

            Assert.AreEqual("My New Map!", imported.Scenario.Name);
            Assert.AreEqual(size, imported.Width);
            CollectionAssert.AreEqual(new[] { "ARMY_1", "ARMY_2", "ARMY_3", "ARMY_4" }, imported.Scenario.PlayableArmies.ToArray());
            CollectionAssert.AreEqual(document.Terrain.Heightmap.Samples.ToArray(), imported.Terrain.Heightmap.Samples.ToArray());
            CollectionAssert.AreEqual(document.Terrain.Layers.ToArray(), imported.Terrain.Layers.ToArray());
            Assert.AreEqual(document.Water with { Map = imported.Water.Map, FoamMask = imported.Water.FoamMask, FlatnessMask = imported.Water.FlatnessMask, DepthBiasMask = imported.Water.DepthBiasMask, WaveTextures = imported.Water.WaveTextures, WaveGenerators = imported.Water.WaveGenerators }, imported.Water);
            CollectionAssert.AreEqual(document.Water.WaveTextures.ToArray(), imported.Water.WaveTextures.ToArray());
            Assert.AreEqual(document.Lighting, imported.Lighting);
            Assert.AreEqual(4, imported.Markers.Spawns.Length);
            Assert.IsNotNull(imported.Script, "a skirmish map has a script");

            // the exported map gives the same files again: nothing was lost on the way
            Assert.AreEqual(GameContent.Save(MapExporter.Export(document).Save), GameContent.Save(MapExporter.Export(imported).Save));
            Assert.AreEqual(GameContent.Scenario(MapExporter.Export(document).Scenario), GameContent.Scenario(MapExporter.Export(imported).Scenario));
            Scmap scmap = ScmapParser.Parse(File.ReadAllBytes(Path.Combine(folder, "my_new_map.scmap")));
            Assert.AreEqual(56, scmap.Version);
        }

        [TestMethod]
        public void RefusesWhatTheGameCannotHold()
        {
            Assert.ThrowsException<ArgumentException>(() => NewMap.Create(new NewMapSettings { Size = 300 }));
            Assert.ThrowsException<ArgumentException>(() => NewMap.Create(new NewMapSettings { Biome = "moon" }));
            Assert.ThrowsException<ArgumentException>(() => NewMap.Create(new NewMapSettings { Height = 600 }));
            Assert.ThrowsException<ArgumentException>(() => NewMap.Create(new NewMapSettings { Players = 0 }));
        }

        [TestMethod]
        public void NamesTheFolderAfterTheMap()
        {
            Assert.AreEqual("theta_passage", NewMap.FolderNameOf("  Theta   Passage "));
            Assert.AreEqual("new_map", NewMap.FolderNameOf("???"));
        }

        [TestMethod]
        public void IsANewMapUntilSaved()
        {
            EditedMap map = EditedMap.New(NewMap.Create(new NewMapSettings()));
            Assert.IsNull(map.ProjectFolder);
            Assert.IsTrue(map.Session.IsModified, "closing it asks to save");
            map.SaveAs(ProjectFolders.New("new"));
            Assert.IsFalse(map.Session.IsModified);
            Assert.AreEqual(512, EditedMap.OpenProject(map.ProjectFolder!).Document.Width);
        }
    }
}
