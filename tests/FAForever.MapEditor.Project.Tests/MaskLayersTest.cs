using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The masks for tools (user story K-1): named, in the project as <c>masks/&lt;name&gt;.png</c>,
    /// read again when another tool changes them, never exported.
    /// </summary>
    [TestClass]
    public class MaskLayersTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapDocument HardFfa() => MapImporter.ImportFolder(ProjectFolders.HardFfa);

        private static MapDocument Painted(MapDocument document, string name, float x, float z)
        {
            TiledBuffer<byte>.Builder mask = document.Masks[name].ToBuilder();
            new MaskBrush { Radius = 6, Strength = 1, Falloff = BrushFalloff.Sharp }.Apply(mask, document.Width, document.Height, x, z);
            return document with { Masks = document.Masks.SetItem(name, mask.ToBuffer()) };
        }

        [TestMethod]
        public void AddsRenamesAndRemovesMasks()
        {
            MapDocument document = HardFfa();
            Assert.AreEqual(0, document.Masks.Count, "an imported map has none");

            document = MaskLayers.Add(document, "rivers");
            document = MaskLayers.Add(document, "forest edge", half: true);

            Assert.AreEqual(document.Width, document.Masks["rivers"].Width, "the map's size");
            Assert.AreEqual(document.Width / 2, document.Masks["forest edge"].Width, "half of it");
            Assert.AreEqual(0, document.Masks["rivers"][10, 10], "empty");
            Assert.IsNotNull(MaskLayers.Problem(document, "Rivers"), "names differ by more than case: they are file names");
            Assert.IsNotNull(MaskLayers.Problem(document, "a/b"));
            Assert.IsNotNull(MaskLayers.Problem(document, " padded"));
            Assert.IsNull(MaskLayers.Problem(document, "Rivers", except: "rivers"), "a rename to another case is fine");
            Assert.ThrowsException<ArgumentException>(() => MaskLayers.Add(document, "rivers"));
            Assert.AreEqual("Mask", MaskLayers.FreeName(document));
            Assert.AreEqual("rivers 2", MaskLayers.FreeName(document, "rivers"));

            TiledBuffer<byte> rivers = document.Masks["rivers"];
            document = MaskLayers.Rename(document, "rivers", "water");
            Assert.AreSame(rivers, document.Masks["water"], "the same pixels under the new name");
            Assert.IsFalse(document.Masks.ContainsKey("rivers"));

            document = MaskLayers.Remove(document, "water");
            CollectionAssert.AreEqual(new[] { "forest edge" }, document.Masks.Keys.ToArray());
            Assert.ThrowsException<ArgumentException>(() => MaskLayers.Remove(document, "water"));
        }

        [TestMethod]
        public void SavesAndLoadsTheMasks()
        {
            MapDocument document = Painted(MaskLayers.Add(MaskLayers.Add(HardFfa(), "rivers"), "forest", half: true), "rivers", 40, 50);
            string project = ProjectFolders.New("masks");

            MapProjectStore.Save(document, project);
            MapDocument loaded = MapProjectStore.Load(project);

            Assert.IsTrue(File.Exists(Path.Combine(project, "masks", "rivers.png")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "masks", "forest.png")));
            CollectionAssert.AreEqual(new[] { "forest", "rivers" }, loaded.Masks.Keys.ToArray());
            CollectionAssert.AreEqual(document.Masks["rivers"].ToArray(), loaded.Masks["rivers"].ToArray());
            Assert.AreEqual(255, loaded.Masks["rivers"][40, 50]);
            Assert.AreEqual(document.Width / 2, loaded.Masks["forest"].Width);
        }

        [TestMethod]
        public void LoadsAProjectWithoutMasks()
        {
            string project = ProjectFolders.New("masks");
            MapProjectStore.Save(HardFfa(), project);

            Assert.IsFalse(Directory.Exists(Path.Combine(project, "masks")), "no folder until there is a mask");
            Assert.AreEqual(0, MapProjectStore.Load(project).Masks.Count);
        }

        [TestMethod]
        public void WritesOnlyTheMasksThatChangedAndRemovesTheOnesThatWent()
        {
            MapDocument document = MaskLayers.Add(MaskLayers.Add(HardFfa(), "rivers"), "forest");
            string project = ProjectFolders.New("masks");
            MapProjectStore.Save(document, project);
            string rivers = Path.Combine(project, "masks", "rivers.png");
            string forest = Path.Combine(project, "masks", "forest.png");
            DateTime old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(rivers, old);
            File.SetLastWriteTimeUtc(forest, old);

            MapDocument changed = Painted(document, "forest", 100, 100);
            MapProjectStore.Save(changed, project, previous: document);
            Assert.AreEqual(old, File.GetLastWriteTimeUtc(rivers), "an untouched mask is not written again");
            Assert.AreNotEqual(old, File.GetLastWriteTimeUtc(forest));

            MapDocument renamed = MaskLayers.Rename(MaskLayers.Remove(changed, "rivers"), "forest", "Forest");
            MapProjectStore.Save(renamed, project, previous: changed);
            CollectionAssert.AreEqual(new[] { "Forest.png" }, Directory.GetFiles(Path.Combine(project, "masks")).Select(Path.GetFileName).ToArray(),
                "a removed mask's file goes; a mask renamed to another case gets its file under the new name");
            Assert.AreEqual(255, MapProjectStore.Load(project).Masks["Forest"][100, 100]);
        }

        [TestMethod]
        public void ReadsAMaskAgainWhenAnotherToolChangesIt()
        {
            MapDocument document = MaskLayers.Add(HardFfa(), "rivers", half: true);
            string project = ProjectFolders.New("masks");
            MapProjectStore.Save(document, project);
            Assert.AreSame(document, MapProjectStore.Reload(document, project, ["masks/rivers.png"]), "the file holds what the document holds");

            // another tool paints the mask and adds one
            MapDocument other = Painted(document, "rivers", 60, 60);
            other = MaskLayers.Add(other, "erosion");
            MapProjectStore.Save(other, project, previous: document);

            MapDocument reloaded = MapProjectStore.Reload(document, project, ["masks/rivers.png", "masks/erosion.png"]);
            Assert.AreEqual(255, reloaded.Masks["rivers"][30, 30]);
            Assert.IsTrue(reloaded.Masks.ContainsKey("erosion"), "a new file is a new mask");

            File.Delete(Path.Combine(project, "masks", "erosion.png"));
            Assert.IsFalse(MapProjectStore.Reload(reloaded, project, ["masks/erosion.png"]).Masks.ContainsKey("erosion"), "a removed file takes its mask away");
        }

        [TestMethod]
        public void RefusesAMaskOfAnotherSize()
        {
            MapDocument document = MaskLayers.Add(HardFfa(), "rivers");
            string project = ProjectFolders.New("masks");
            MapProjectStore.Save(document, project);
            // a mask of a quarter of the map's size, written by another tool
            MapDocument quarter = document with { Masks = document.Masks.SetItem("rivers", TiledBuffer<byte>.Create(document.Width / 4, document.Height / 4)) };
            MapProjectStore.Save(quarter, project, previous: document);

            MapProjectException refused = Assert.ThrowsException<MapProjectException>(() => MapProjectStore.Load(project));
            StringAssert.Contains(refused.Message, "rivers.png");
            Assert.ThrowsException<MapProjectException>(() => MapProjectStore.Reload(document, project, ["masks/rivers.png"]));
        }

        [TestMethod]
        public void LeavesTheMasksOutOfTheExport()
        {
            MapDocument document = HardFfa();
            MapDocument withMask = Painted(MaskLayers.Add(document, "rivers"), "rivers", 40, 40);

            IReadOnlyDictionary<string, byte[]> expected = MapExporter.ToBytes(document);
            IReadOnlyDictionary<string, byte[]> actual = MapExporter.ToBytes(withMask);

            CollectionAssert.AreEquivalent(expected.Keys.ToList(), actual.Keys.ToList());
            foreach ((string path, byte[] bytes) in expected)
            {
                CollectionAssert.AreEqual(bytes, actual[path], path);
            }
        }
    }
}
