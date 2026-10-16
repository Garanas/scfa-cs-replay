using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// A height layer read from an image (the final's base from a procedural tool's output, user story
    /// T-3): the project keeps no copy of an image in its own folder, which opening reads again, and
    /// keeps one of an image elsewhere.
    /// </summary>
    [TestClass]
    public class ImageLayerCopiesTest
    {
        private const float Scale = 1f / 128;

        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static ushort[] Output(float height) => Enumerable.Repeat((ushort)(height / Scale), 257 * 257).ToArray();

        private static MapDocument WithBase(string path) =>
            HeightLayers.Recomposite(HeightStacks.UseAsFinalBase(NewMap.Create(new NewMapSettings { Size = 256, Height = 35 }), Output(20), path));

        private static string CopyOf(string project) => Path.Combine(project, MapProjectLayout.StackLayer("heightmap", HeightStacks.FinalBaseName));

        [TestMethod]
        public void KeepsNoCopyOfTheOutputInTheProject()
        {
            string project = ProjectFolders.New("image-own");
            MapDocument document = WithBase("procedural/temp/gaea.png");
            MapProjectStore.Save(document, project);
            Assert.IsTrue(File.Exists(CopyOf(project)), "without its file the heights are kept");

            // the procedural tool writes its output into the project: the next save drops the copy
            string output = Path.Combine(project, "procedural/temp/gaea.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            ProjectImages.WriteGray16(output, 257, 257, Output(20));
            MapProjectStore.Save(document, project, document);

            Assert.IsFalse(File.Exists(CopyOf(project)), "the output is the project's own file");
            MapDocument loaded = MapProjectStore.Load(project);
            Assert.AreEqual(20, loaded.Terrain.Heightmap.GetHeight(10, 10), 1e-3, "read from the output");

            // the output gone: the base starts from the heights the project saved
            File.Delete(output);
            MapDocument without = MapProjectStore.Load(project);
            Assert.AreEqual(20, without.Terrain.Heightmap.GetHeight(10, 10), 1e-3);
            Assert.AreEqual("procedural/temp/gaea.png", without.HeightStack.Layers[0].Path, "it still names its file, read when it comes back");
        }

        [TestMethod]
        public void KeepsACopyOfAnImageOutsideTheProject()
        {
            string outside = Path.Combine(ProjectFolders.New("image-outside"), "base.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
            ProjectImages.WriteGray16(outside, 257, 257, Output(20));
            string project = ProjectFolders.New("image-copy");

            MapProjectStore.Save(WithBase(outside), project);

            Assert.IsTrue(File.Exists(CopyOf(project)), "for when the file moves or the project goes to another computer");
        }
    }
}
