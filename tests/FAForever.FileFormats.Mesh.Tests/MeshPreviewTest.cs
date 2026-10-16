using System.Numerics;
using FAForever.FileFormats.Mesh;

namespace FAForever.FileFormats.Mesh.Tests
{
    [TestClass]
    public class MeshPreviewTest
    {
        /// <summary>
        /// A square of 2 by 2 standing up and facing the front (+z): two triangles, with texture
        /// coordinates over the whole of it, the tangent along x and the binormal along y.
        /// </summary>
        private static Scm Square(bool clockwise = false)
        {
            ScmVertex Corner(float x, float y)
            {
                Vector2 uv = new Vector2((x + 1) / 2, (1 - y) / 2);
                return new ScmVertex(new Vector3(x, y, 0), Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, uv, uv, 0);
            }
            ushort[] indices = clockwise ? [0, 2, 1, 0, 3, 2] : [0, 1, 2, 0, 2, 3];
            return new Scm { Bones = [], Vertices = [Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1)], Indices = indices, Information = [] };
        }

        /// <summary>
        /// A texture of one pixel.
        /// </summary>
        private static RgbaImage Pixel(byte r, byte g, byte b, byte a) => new RgbaImage(1, 1, [r, g, b, a]);

        private static byte Alpha(RgbaImage image, int x, int y) => image.Pixels[(y * image.Width + x) * 4 + 3];

        private static (byte R, byte G, byte B) Colour(RgbaImage image, int x, int y)
        {
            int i = (y * image.Width + x) * 4;
            return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2]);
        }

        private static MeshPreviewOptions Front => new MeshPreviewOptions { Size = 32, Yaw = 0, Pitch = 0 };

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FillsThePictureWithTheMeshFromEitherSide(bool clockwise)
        {
            RgbaImage image = MeshPreview.Render(Square(clockwise), new PreviewTextures(), Front);

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

            RgbaImage image = MeshPreview.Render(Square(), new PreviewTextures(albedo), Front);

            (byte r, _, byte b) = Colour(image, 16, 4);
            Assert.IsTrue(r > 100 && b < 10, $"top {r} {b}");
            (r, _, b) = Colour(image, 16, 27);
            Assert.IsTrue(b > 100 && r < 10, $"bottom {r} {b}");
        }

        [TestMethod]
        public void CutsOutWhereTheAlbedoIsNotAboveTheAlphaTest()
        {
            // the left half of the albedo opaque, the right half at exactly a half
            byte[] opaque = [255, 255, 255, 255], half = [255, 255, 255, 128];
            RgbaImage albedo = new RgbaImage(4, 1, [.. opaque, .. opaque, .. half, .. half]);

            RgbaImage cut = MeshPreview.Render(Square(), new PreviewTextures(albedo), Front with { AlphaTest = 128 / 255f });
            RgbaImage solid = MeshPreview.Render(Square(), new PreviewTextures(albedo), Front);

            Assert.AreEqual(255, Alpha(cut, 5, 16));
            Assert.AreEqual(0, Alpha(cut, 22, 16), "the test is greater than, as the game's");
            Assert.AreEqual(255, Alpha(solid, 22, 16));
        }

        [TestMethod]
        [DataRow(PreviewShader.Uef)]
        [DataRow(PreviewShader.Aeon)]
        [DataRow(PreviewShader.Cybran)]
        public void PaintsTheTeamColourWhereTheSpecularMapMarksIt(PreviewShader shader)
        {
            PreviewTextures marked = new PreviewTextures(Pixel(128, 128, 128, 255), Specular: Pixel(0, 0, 0, 255));
            MeshPreviewOptions options = Front with { Shader = shader };

            (byte r, _, byte b) = Colour(MeshPreview.Render(Square(), marked, options with { TeamColour = new Vector3(1, 0, 0) }), 16, 16);
            Assert.IsTrue(r > b + 20, $"red army: {r} {b}");
            (r, _, b) = Colour(MeshPreview.Render(Square(), marked, options with { TeamColour = new Vector3(0, 0, 1) }), 16, 16);
            Assert.IsTrue(b > r + 20, $"blue army: {r} {b}");
        }

        [TestMethod]
        public void PaintsTheSeraphimTeamColourWhereTheAlbedoMarksIt()
        {
            MeshPreviewOptions options = Front with { Shader = PreviewShader.Seraphim, TeamColour = new Vector3(1, 0, 0) };

            (byte r, _, byte b) = Colour(MeshPreview.Render(Square(), new PreviewTextures(Pixel(40, 40, 40, 255)), options), 16, 16);
            Assert.IsTrue(r > b + 20, $"marked: {r} {b}");
            (r, _, b) = Colour(MeshPreview.Render(Square(), new PreviewTextures(Pixel(40, 40, 40, 0)), options), 16, 16);
            Assert.IsTrue(b >= r, $"silver: {r} {b}");
        }

        [TestMethod]
        public void TurnsTheLightByTheNormalMap()
        {
            // the sun is above the camera: a normal tilted up along the binormal (green) is lit more
            // than one tilted down; a flat one (a half in green and alpha) lies between
            PreviewTextures Tilted(byte green) => new PreviewTextures(Pixel(128, 128, 128, 255), Normals: Pixel(0, green, 0, 128));
            MeshPreviewOptions options = Front with { Shader = PreviewShader.NormalMapped };

            byte up = Colour(MeshPreview.Render(Square(), Tilted(230), options), 16, 16).R;
            byte flat = Colour(MeshPreview.Render(Square(), Tilted(128), options), 16, 16).R;
            byte down = Colour(MeshPreview.Render(Square(), Tilted(25), options), 16, 16).R;

            Assert.IsTrue(up > flat && flat > down, $"{up} {flat} {down}");
        }

        [TestMethod]
        [DataRow(ScmParserTest.Ual0101Lod0)]
        [DataRow(ScmParserTest.Max)]
        public void DrawsAMeshOfTheGame(string path)
        {
            RgbaImage image = MeshPreview.Render(ScmParser.Parse(File.ReadAllBytes(path)), new PreviewTextures(), new MeshPreviewOptions { Size = 64, Shader = PreviewShader.Aeon });

            int drawn = Enumerable.Range(0, 64 * 64).Count(i => image.Pixels[i * 4 + 3] > 0);
            Assert.IsTrue(drawn > 64 * 64 / 10, $"{drawn} pixels");
            Assert.AreEqual(0, Alpha(image, 0, 0));
        }

        [TestMethod]
        [DataRow("Unit", PreviewShader.Uef, null)]
        [DataRow("UEFNavy", PreviewShader.Uef, null)]
        [DataRow("Aeon", PreviewShader.Aeon, null)]
        [DataRow("Insect", PreviewShader.Cybran, null)]
        [DataRow("SeraphimNavy", PreviewShader.Seraphim, null)]
        [DataRow("NormalMappedTerrain", PreviewShader.NormalMapped, null)]
        [DataRow("UndulatingNormalMappedAlpha", PreviewShader.NormalMapped, 0x80)]
        [DataRow("TMeshAlpha", PreviewShader.NormalMapped, 0x80)]
        [DataRow("TMeshNoNormals", PreviewShader.VertexNormal, 0x23)]
        [DataRow("VertexNormal", PreviewShader.VertexNormal, 0x23)]
        [DataRow("Simple", PreviewShader.Uef, null)]
        [DataRow("Team", PreviewShader.Uef, null)]
        [DataRow("", PreviewShader.Uef, null)]
        [DataRow(null, PreviewShader.Uef, null)]
        public void ShadesAsTheGamesShader(string? name, PreviewShader shader, int? alphaRef)
        {
            Assert.AreEqual((shader, alphaRef / 255f), MeshPreview.ShaderOf(name));
        }

        [TestMethod]
        [DataRow(null, "Unit")]
        [DataRow("", "Unit")]
        [DataRow("TMeshAlpha", "NormalMappedAlpha")]
        [DataRow("TMeshNoNormals", "VertexNormal")]
        [DataRow("TMeshNoLighting", "Flat")]
        [DataRow("TMeshGlow", "NormalMappedGlow")]
        [DataRow("Simple", "Unit")]
        [DataRow("Team", "Unit")]
        [DataRow("Aeon", "Aeon")]
        [DataRow("NormalMappedTerrain", "NormalMappedTerrain")]
        public void NamesTheShaderAsTheEngine(string? name, string resolved)
        {
            // faf-re, src/sdk/moho/mesh/ShaderDictionary.cpp
            Assert.AreEqual(resolved, MeshPreview.ShaderName(name));
        }
    }
}
