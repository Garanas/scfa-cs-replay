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
        /// The mesh of <see cref="GameFiles.Mesh"/> as the conversion writes it, with textures of
        /// one colour each.
        /// </summary>
        private static AssetModel Model(string? shader = null, MagickColor? albedo = null, MagickColor? specular = null)
        {
            string folder = Directory.CreateDirectory(ProjectFolders.New("preview")).FullName;
            string mesh = Path.Combine(folder, "rock_lod0.glb");
            File.WriteAllBytes(mesh, GlbWriter.Write(ScmParser.Parse(GameFiles.Mesh("rock"))));
            string? Texture(string name, MagickColor? colour)
            {
                string file = Path.Combine(folder, name);
                if (colour is not null)
                {
                    using MagickImage image = new MagickImage(colour, 1024, 1024);
                    File.WriteAllBytes(file, image.ToByteArray(MagickFormat.Png));
                }
                return file;
            }
            return new AssetModel(mesh, Texture("rock_albedo.png", albedo), Texture("rock_normalsts.png", null), Texture("rock_specteam.png", specular), shader);
        }

        private static List<(byte R, byte G, byte B, byte A)> Drawn(RgbaImage image) =>
            [.. Enumerable.Range(0, image.Width * image.Height)
                .Select(i => (image.Pixels[i * 4], image.Pixels[i * 4 + 1], image.Pixels[i * 4 + 2], image.Pixels[i * 4 + 3]))
                .Where(pixel => pixel.Item4 == 255)];

        [TestMethod]
        public void DrawsTheMeshWithItsAlbedo()
        {
            RgbaImage image = AssetPreview.Render(Model(albedo: MagickColors.Red), 48);

            Assert.AreEqual((48, 48), (image.Width, image.Height));
            var drawn = Drawn(image);
            Assert.IsTrue(drawn.Count > 100, $"{drawn.Count} pixels");
            Assert.IsTrue(drawn.All(pixel => pixel.R > 100 && pixel.G == 0 && pixel.B == 0), "red");
            Assert.AreEqual(0, image.Pixels[3], "the background is transparent");
        }

        [TestMethod]
        public void DrawsAMeshWithoutTexturesGrey()
        {
            // the normal map named is not there: left out, as the albedo
            RgbaImage image = AssetPreview.Render(Model(), 48);

            Assert.IsTrue(Drawn(image).All(pixel => pixel.R == pixel.G && Math.Abs(pixel.B - pixel.R) < 16 && pixel.R > 0));
        }

        [TestMethod]
        public void DrawsAUnitInTheTeamColourWhereItsSpecularMapMarksIt()
        {
            // the specular map's alpha is the team colour's mask; the preview's army is blue
            RgbaImage marked = AssetPreview.Render(Model("Unit", MagickColors.Gray, new MagickColor("#000000FF")), 48);
            RgbaImage plain = AssetPreview.Render(Model("Unit", MagickColors.Gray, new MagickColor("#00000000")), 48);

            // the metal reflects the sky, which is blue too, but less
            double blue = Drawn(marked).Average(pixel => pixel.B - pixel.R);
            double metal = Drawn(plain).Average(pixel => pixel.B - pixel.R);
            Assert.IsTrue(blue > metal + 30, $"{blue} against {metal}");
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
