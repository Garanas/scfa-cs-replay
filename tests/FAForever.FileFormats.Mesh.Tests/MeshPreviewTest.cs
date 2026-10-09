using System.Numerics;
using FAForever.FileFormats.Mesh;

namespace FAForever.FileFormats.Mesh.Tests
{
    [TestClass]
    public class MeshPreviewTest
    {
        /// <summary>
        /// A square of 2 by 2 standing up and facing the front (+z): two triangles, with texture
        /// coordinates over the whole of it.
        /// </summary>
        private static Scm Square(bool clockwise = false)
        {
            ScmVertex Corner(float x, float y) => new ScmVertex(new Vector3(x, y, 0), Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, new Vector2((x + 1) / 2, (1 - y) / 2), Vector2.Zero, 0);
            ushort[] indices = clockwise ? [0, 2, 1, 0, 3, 2] : [0, 1, 2, 0, 2, 3];
            return new Scm { Bones = [], Vertices = [Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1)], Indices = indices, Information = [] };
        }

        private static byte Alpha(RgbaImage image, int x, int y) => image.Pixels[(y * image.Width + x) * 4 + 3];

        private static (byte R, byte G, byte B) Colour(RgbaImage image, int x, int y)
        {
            int i = (y * image.Width + x) * 4;
            return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2]);
        }

        private static MeshPreviewOptions Front => new MeshPreviewOptions { Size = 32, Yaw = 0, Pitch = 0, Exposure = null };

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FillsThePictureWithTheMeshFromEitherSide(bool clockwise)
        {
            RgbaImage image = MeshPreview.Render(Square(clockwise), null, Front);

            Assert.AreEqual(32, image.Width);
            Assert.AreEqual(32, image.Height);
            Assert.AreEqual(255, Alpha(image, 16, 16));
            Assert.AreEqual(255, Alpha(image, 3, 3));
            // a margin stays transparent
            Assert.AreEqual(0, Alpha(image, 0, 0));
            Assert.AreEqual(0, Alpha(image, 31, 16));
        }

        [TestMethod]
        public void DrawsTheAlbedoTheRightWayUp()
        {
            // the top half of the albedo red, the bottom half blue: texture coordinates start at the top
            byte[] red = [255, 0, 0, 255], blue = [0, 0, 255, 255];
            RgbaImage albedo = new RgbaImage(1, 8, [.. red, .. red, .. red, .. red, .. blue, .. blue, .. blue, .. blue]);

            RgbaImage image = MeshPreview.Render(Square(), albedo, Front);

            (byte r, _, byte b) = Colour(image, 16, 4);
            Assert.IsTrue(r > 100 && b < 10, $"top {r} {b}");
            (r, _, b) = Colour(image, 16, 27);
            Assert.IsTrue(b > 100 && r < 10, $"bottom {r} {b}");
        }

        [TestMethod]
        public void CutsOutWhereTheAlbedoIsTransparent()
        {
            // the left pixel of the albedo opaque, the right one transparent
            RgbaImage albedo = new RgbaImage(2, 1, [255, 255, 255, 255, 255, 255, 255, 0]);

            RgbaImage cut = MeshPreview.Render(Square(), albedo, Front with { Alpha = PreviewAlpha.Cutout });
            RgbaImage solid = MeshPreview.Render(Square(), albedo, Front);

            Assert.AreEqual(255, Alpha(cut, 5, 16));
            Assert.AreEqual(0, Alpha(cut, 26, 16));
            Assert.AreEqual(255, Alpha(solid, 26, 16));
        }

        [TestMethod]
        public void BrightensADarkMesh()
        {
            RgbaImage albedo = new RgbaImage(1, 1, [20, 20, 20, 255]);

            byte dark = Colour(MeshPreview.Render(Square(), albedo, Front), 16, 16).R;
            byte bright = Colour(MeshPreview.Render(Square(), albedo, Front with { Exposure = 0.4f }), 16, 16).R;

            Assert.IsTrue(bright > dark * 3, $"{dark} to {bright}");
        }

        [TestMethod]
        [DataRow(ScmParserTest.Ual0101Lod0)]
        [DataRow(ScmParserTest.Max)]
        public void DrawsAMeshOfTheGame(string path)
        {
            RgbaImage image = MeshPreview.Render(ScmParser.Parse(File.ReadAllBytes(path)), null, new MeshPreviewOptions { Size = 64 });

            int drawn = Enumerable.Range(0, 64 * 64).Count(i => image.Pixels[i * 4 + 3] > 0);
            Assert.IsTrue(drawn > 64 * 64 / 10, $"{drawn} pixels");
            Assert.AreEqual(0, Alpha(image, 0, 0));
        }

        [TestMethod]
        [DataRow("UndulatingNormalMappedAlpha", PreviewAlpha.Cutout)]
        [DataRow("TMeshAlpha", PreviewAlpha.Cutout)]
        [DataRow("Unit", PreviewAlpha.Opaque)]
        [DataRow(null, PreviewAlpha.Opaque)]
        public void CutsOutForTheShadersWithAlpha(string? shader, PreviewAlpha expected)
        {
            Assert.AreEqual(expected, MeshPreview.AlphaOf(shader));
        }
    }
}
