using FAForever.FileFormats.Mesh;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public sealed class AssetPreviewTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        /// <summary>
        /// The mesh of <see cref="GameFiles.Mesh"/> as the conversion writes it, and a red albedo.
        /// </summary>
        private static AssetModel Model(string? shader = null, bool albedo = true)
        {
            string folder = Directory.CreateDirectory(ProjectFolders.New("preview")).FullName;
            string mesh = Path.Combine(folder, "rock_lod0.glb");
            File.WriteAllBytes(mesh, GlbWriter.Write(ScmParser.Parse(GameFiles.Mesh("rock"))));
            string texture = Path.Combine(folder, "rock_albedo.png");
            if (albedo)
            {
                using MagickImage red = new MagickImage(MagickColors.Red, 1024, 1024);
                File.WriteAllBytes(texture, red.ToByteArray(MagickFormat.Png));
            }
            return new AssetModel(mesh, texture, shader);
        }

        private static IEnumerable<(byte R, byte G, byte B, byte A)> Pixels(RgbaImage image) =>
            Enumerable.Range(0, image.Width * image.Height).Select(i => (image.Pixels[i * 4], image.Pixels[i * 4 + 1], image.Pixels[i * 4 + 2], image.Pixels[i * 4 + 3]));

        [TestMethod]
        public void DrawsTheMeshWithItsAlbedo()
        {
            RgbaImage image = AssetPreview.Render(Model(), 48);

            Assert.AreEqual((48, 48), (image.Width, image.Height));
            var drawn = Pixels(image).Where(pixel => pixel.A == 255).ToList();
            Assert.IsTrue(drawn.Count > 100, $"{drawn.Count} pixels");
            Assert.IsTrue(drawn.All(pixel => pixel.R > 100 && pixel.G == 0 && pixel.B == 0), "red");
            Assert.AreEqual(0, image.Pixels[3], "the background is transparent");
        }

        [TestMethod]
        public void DrawsAMeshWithoutAlbedoGrey()
        {
            RgbaImage image = AssetPreview.Render(Model(albedo: false), 48);

            Assert.IsTrue(Pixels(image).Where(pixel => pixel.A == 255).All(pixel => pixel.R == pixel.G && pixel.G == pixel.B && pixel.R > 0));
        }

        [TestMethod]
        public void RejectsAMeshThatIsNone()
        {
            AssetModel model = Model();
            File.WriteAllText(model.Mesh, "not a mesh");

            Assert.ThrowsException<GlbFormatException>(() => AssetPreview.Render(model, 48));
        }
    }
}
