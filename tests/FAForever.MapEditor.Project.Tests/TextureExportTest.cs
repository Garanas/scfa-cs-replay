using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class TextureExportTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg", ".tga", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

        private static byte[] Png(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
        {
            byte[] rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    (byte r, byte g, byte b, byte a) = pixel(x, y);
                    int at = (y * width + x) * 4;
                    (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) = (r, g, b, a);
                }
            }
            using MagickImage image = new MagickImage(rgba, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));
            return image.ToByteArray(MagickFormat.Png);
        }

        private static byte[] Opaque(int size) => Png(size, size, (x, y) => ((byte)(x * 4), (byte)(y * 4), 90, 255));

        private static byte[] WithAlpha(int size) => Png(size, size, (x, y) => (128, (byte)(y * 4), 0, (byte)(x * 4)));

        /// <summary>
        /// A new map (without map-wide decals) whose strata, decals, decal set, water and sky name
        /// its own pictures through a folder of another name, as an imported map does, with a script
        /// that names one, a DDS stratum, and the vault's preview at the top of the folder.
        /// </summary>
        private static MapDocument WithOwnPictures(out byte[] dds)
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Name = "Pictures", Size = 256 }) with { WideDecals = MapWideDecals.None };
            const string own = "/maps/PICTURES.v0003/";
            dds = DdsTexture.WriteMipmapped(16, 16, DdsCompression.Dxt1, new byte[16 * 16 * 4]);
            ImmutableSortedDictionary<string, ImmutableArray<byte>> files = document.Files
                .SetItem("env/layers/rock_albedo.png", [.. Opaque(64)])
                .SetItem("env/layers/rock_normal.png", [.. WithAlpha(64)])
                .SetItem("env/layers/dirt_albedo.dds", ImmutableCollectionsMarshal.AsImmutableArray(dds))
                .SetItem("env/decals/splat.png", [.. WithAlpha(32)])
                .SetItem("env/decals/rock01_albedo.png", [.. Opaque(32)])
                .SetItem("env/decals/rock01_normals.png", [.. WithAlpha(32)])
                .SetItem("env/water/ramp.png", [.. Png(128, 1, (x, _) => ((byte)x, (byte)(2 * x), 200, 255))])
                .SetItem("env/sky/cube.png", [.. Png(6 * 16, 16, (x, y) => ((byte)(x / 16 * 40), (byte)(y * 8), 50, 255))])
                .SetItem("env/props/lamp_albedo.png", [.. Opaque(16)])
                .SetItem("pictures.small.png", [.. Opaque(16)])
                .SetItem("env/scripted.lua", [.. MapImporter.LuaEncoding.GetBytes($"decal = '{own}env/decals/splat.png'\n")]);

            ImmutableArray<TerrainLayer> layers = document.Terrain.Layers
                .SetItem(1, document.Terrain.Layers[1] with { AlbedoPath = own + "env/layers/rock_albedo.png", NormalPath = own + "env/layers/rock_normal.png" })
                .SetItem(2, document.Terrain.Layers[2] with { AlbedoPath = own + "env/layers/dirt_albedo.dds" });
            Decal splat = new Decal { Id = 0, Type = DecalType.Albedo, Textures = [own + "env/decals/splat.png", ""], Scale = new(8, 8, 8), Position = new(20, 0, 20), OwnerArmy = -1, CutOffLod = 1000 };
            Decal set = new Decal
            {
                Id = 1, Type = DecalType.Albedo, Textures = [own + "env/decals/rock01_albedo.png", ""], Scale = new(8, 8, 8), Position = new(40, 0, 40), OwnerArmy = -1, CutOffLod = 1000,
                Set = new DecalSet { Name = own + "env/decals/rock01", Normals = own + "env/decals/rock01_normals.png" },
            };
            return document with
            {
                Files = files,
                Terrain = document.Terrain with { Layers = layers },
                Decals = [splat, set],
                Water = document.Water with { Ramp = own + "env/water/ramp.png", Cubemap = own + "env/sky/cube.png" },
                Sky = document.Sky with { Cubemap = own + "env/sky/cube.png" },
            };
        }

        private static string Format(byte[] dds) => TextureExport.FormatOf(dds);

        [TestMethod]
        public void AMapWithPngTexturesExportsOnlyDdsWithItsReferencesPointedAtThem()
        {
            MapDocument document = WithOwnPictures(out byte[] dds);
            string output = ProjectFolders.New("dds-export");

            MapDocument exported = TextureExport.Apply(document, out IReadOnlyList<ConvertedTexture> converted, new TextureExportCache());
            string folder = MapExporter.WriteFolder(exported, output);

            Assert.AreEqual(8, converted.Count, string.Join(", ", converted.Select(texture => texture.Source)));
            Assert.IsFalse(converted.Any(texture => texture.Cached));
            string[] pictures = [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
                .Where(path => PictureExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))];
            CollectionAssert.AreEqual(new[] { "pictures.small.png" }, pictures, "only the vault's preview stays a picture");
            CollectionAssert.AreEqual(dds, File.ReadAllBytes(Path.Combine(folder, "env/layers/dirt_albedo.dds")), "a DDS is copied as it is");

            // read back as the game's files
            MapDocument back = MapImporter.ImportFolder(folder);
            string root = $"/maps/{document.Folder}/";
            Assert.AreEqual(root + "env/layers/rock_albedo.dds", back.Terrain.Layers[1].AlbedoPath);
            Assert.AreEqual(root + "env/layers/rock_normal.dds", back.Terrain.Layers[1].NormalPath);
            Assert.AreEqual(root + "env/layers/dirt_albedo.dds", back.Terrain.Layers[2].AlbedoPath);
            CollectionAssert.AreEquivalent(
                new[] { root + "env/decals/splat.dds", root + "env/decals/rock01_albedo.dds", root + "env/decals/rock01_normals.dds" },
                back.Decals.Select(decal => decal.Textures[0]).ToArray(), "the set's normals are a decal of their own");
            Assert.AreEqual(root + "env/water/ramp.dds", back.Water.Ramp);
            Assert.AreEqual(root + "env/sky/cube.dds", back.Water.Cubemap);
            Assert.AreEqual(root + "env/sky/cube.dds", back.Sky.Cubemap);
            StringAssert.Contains(File.ReadAllText(Path.Combine(folder, "env/scripted.lua")), root + "env/decals/splat.dds");

            // the formats the game reads, and the pictures as they were
            byte[] Read(string path) => File.ReadAllBytes(Path.Combine(folder, path));
            Assert.AreEqual("DXT1 with mipmaps", Format(Read("env/layers/rock_albedo.dds")), "opaque");
            Assert.AreEqual("DXT5 with mipmaps", Format(Read("env/layers/rock_normal.dds")), "a normal map");
            Assert.AreEqual("DXT5 with mipmaps", Format(Read("env/decals/splat.dds")), "with alpha");
            Assert.AreEqual("DXT5 with mipmaps", Format(Read("env/decals/rock01_normals.dds")));
            Assert.AreEqual("DXT1 with mipmaps", Format(Read("env/props/lamp_albedo.dds")), "named by nothing, in a folder of the map");
            Assert.AreEqual("A8R8G8B8", Format(Read("env/water/ramp.dds")), "a ramp keeps every step");
            Assert.AreEqual("DXT1 cube with mipmaps", Format(Read("env/sky/cube.dds")));

            DecodedTexture albedo = GameTextures.Decode(Read("env/layers/rock_albedo.dds"));
            DecodedTexture original = GameTextures.Decode(Opaque(64));
            Assert.AreEqual(64, albedo.Width);
            Assert.IsTrue(albedo.Rgba.Zip(original.Rgba).Average(pair => Math.Abs(pair.First - pair.Second)) < 3);
            DecodedTexture normal = GameTextures.Decode(Read("env/layers/rock_normal.dds"));
            Assert.IsTrue(normal.Rgba.Zip(GameTextures.Decode(WithAlpha(64)).Rgba).Average(pair => Math.Abs(pair.First - pair.Second)) < 3, "the channels as the picture holds them");
            DecodedTexture ramp = GameTextures.Decode(Read("env/water/ramp.dds"));
            CollectionAssert.AreEqual(GameTextures.Decode(document.Files["env/water/ramp.png"].ToArray()).Rgba, ramp.Rgba, "pixel for pixel");
            DecodedTexture cube = CubeMaps.Decode(Read("env/sky/cube.dds"));
            Assert.AreEqual(6 * 16, cube.Width);
            Assert.AreEqual(16, cube.Height);
            Assert.AreEqual(200, cube.Rgba[(8 * 96 + 5 * 16 + 8) * 4], 4, "the sixth face is the sixth of the strip");
        }

        [TestMethod]
        public void LeavesTheProjectsDocumentAloneAndAMapOfDdsAsItIs()
        {
            MapDocument document = WithOwnPictures(out _);
            TextureExport.Apply(document, out _, new TextureExportCache());
            Assert.IsTrue(document.Files.ContainsKey("env/layers/rock_albedo.png"), "the document is immutable");

            MapDocument plain = NewMap.Create(new NewMapSettings { Name = "Plain", Size = 256 });
            Assert.AreSame(plain, TextureExport.Apply(plain, out IReadOnlyList<ConvertedTexture> none, new TextureExportCache()));
            Assert.AreEqual(0, none.Count);
            Assert.AreEqual(0, TextureExport.Pending(plain).Count);
        }

        [TestMethod]
        public void AnExportThatFollowsAnotherTakesItsDdsFilesFromTheCache()
        {
            MapDocument document = WithOwnPictures(out _);
            TextureExportCache cache = new TextureExportCache();

            MapDocument first = TextureExport.Apply(document, out _, cache);
            MapDocument second = TextureExport.Apply(document, out IReadOnlyList<ConvertedTexture> again, cache);

            Assert.IsTrue(again.All(texture => texture.Cached));
            foreach (ConvertedTexture texture in again)
            {
                Assert.AreSame(ImmutableCollectionsMarshal.AsArray(first.Files[texture.Target]), ImmutableCollectionsMarshal.AsArray(second.Files[texture.Target]));
            }

            // a changed picture is converted again
            MapDocument changed = document with { Files = document.Files.SetItem("env/decals/splat.png", [.. Opaque(24)]) };
            TextureExport.Apply(changed, out IReadOnlyList<ConvertedTexture> third, cache);
            Assert.AreEqual("env/decals/splat.png", third.Single(texture => !texture.Cached).Source);
        }

        [TestMethod]
        public void GivesTheDdsFileAnotherNameWhenTheMapHasOneOfItsName()
        {
            MapDocument document = WithOwnPictures(out byte[] dds);
            document = document with { Files = document.Files.SetItem("env/layers/rock_albedo.dds", ImmutableCollectionsMarshal.AsImmutableArray(dds)) };

            MapDocument exported = TextureExport.Apply(document, out _, new TextureExportCache());

            Assert.AreSame(dds, ImmutableCollectionsMarshal.AsArray(exported.Files["env/layers/rock_albedo.dds"]));
            Assert.IsTrue(exported.Files.ContainsKey("env/layers/rock_albedo_2.dds"));
            Assert.AreEqual("/maps/PICTURES.v0003/env/layers/rock_albedo_2.dds", exported.Terrain.Layers[1].AlbedoPath);
        }

        [TestMethod]
        public void PointsTheSkyDomeAndTheEnvironmentCubeMapsAtTheirDdsFiles()
        {
            MapDocument document = WithOwnPictures(out _);
            const string own = "/maps/PICTURES.v0003/";
            document = document with
            {
                Files = document.Files.SetItem("env/sky/atlas.jpg", [.. Opaque(32)]).SetItem("env/sky/cirrus.tga", [.. TgaOf(Opaque(32))]),
                Sky = document.Sky with
                {
                    EnvironmentCubemaps = [new Cubemap { Name = "<default>", Texture = own + "env/sky/cube.png" }],
                    Skybox = new Skybox { Albedo = own + "env/sky/atlas.jpg", Glow = own + "env/sky/atlas.jpg", CirrusTexture = own + "env/sky/cirrus.tga" },
                },
            };

            MapDocument exported = TextureExport.Apply(document, out _, new TextureExportCache());

            Assert.AreEqual(own + "env/sky/cube.dds", exported.Sky.EnvironmentCubemaps[0].Texture);
            Assert.AreEqual(own + "env/sky/atlas.dds", exported.Sky.Skybox!.Albedo);
            Assert.AreEqual(own + "env/sky/atlas.dds", exported.Sky.Skybox.Glow);
            Assert.AreEqual(own + "env/sky/cirrus.dds", exported.Sky.Skybox.CirrusTexture);
            Assert.AreEqual(32, GameTextures.Decode(exported.Files["env/sky/cirrus.dds"].ToArray()).Width, "a TGA is read too");
        }

        [TestMethod]
        public void PackingAProjectConvertsItsPictures()
        {
            MapDocument document = WithOwnPictures(out _);
            string project = ProjectFolders.New("dds-project");
            EditedMap.New(document).SaveAs(project);
            List<ConvertedTexture> heard = [];

            string folder = EditedMap.OpenProject(project).Export(ProjectFolders.New("dds-pack"), wideDecals: true, ddsTextures: true, converted: heard.AddRange);

            Assert.AreEqual(8, heard.Count);
            Assert.IsFalse(Directory.EnumerateFiles(Path.Combine(folder, "env"), "*.png", SearchOption.AllDirectories).Any());
            Assert.IsTrue(File.Exists(Path.Combine(project, "files", "env", "layers", "rock_albedo.png")) || Directory.EnumerateFiles(project, "rock_albedo.png", SearchOption.AllDirectories).Any(),
                "the project keeps its pictures");
        }

        private static byte[] TgaOf(byte[] png)
        {
            using MagickImage image = new MagickImage(png);
            return image.ToByteArray(MagickFormat.Tga);
        }
    }
}
