using System.Buffers.Binary;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class MapWideDecalsTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        /// <summary>
        /// A new map of 256 ogrids with a cliff: flat at 20 west of x = 128, a ramp of 45 degrees over 16
        /// ogrids, flat at 36 east of it.
        /// </summary>
        private static MapDocument Cliff()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Name = "Cliff", Size = 256 });
            Heightmap heights = document.Terrain.Heightmap;
            int size = heights.Width + 1;
            ushort[] samples = new ushort[size * size];
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    samples[z * size + x] = (ushort)MathF.Round(Math.Clamp(20 + (x - 128), 20, 36) / heights.Scale);
                }
            }
            return document with { Terrain = document.Terrain with { Heightmap = heights with { Samples = TiledBuffer<ushort>.FromArray(size, size, samples) } } };
        }

        private static byte[] Picture(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
        {
            byte[] rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    (byte r, byte g, byte b, byte a) = pixel(x, y);
                    rgba[(y * width + x) * 4] = r;
                    rgba[(y * width + x) * 4 + 1] = g;
                    rgba[(y * width + x) * 4 + 2] = b;
                    rgba[(y * width + x) * 4 + 3] = a;
                }
            }
            return rgba;
        }

        [TestMethod]
        public void WritesDxt5WithAFullChainOfMipmapsThatReadsBack()
        {
            byte[] rgba = Picture(64, 32, (x, y) => ((byte)(x * 4), (byte)(y * 8), 40, (byte)(255 - x * 4)));

            byte[] dds = DdsTexture.WriteMipmapped(64, 32, DdsCompression.Dxt5, rgba);

            Assert.AreEqual(0xa1007, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(8)), "flags as Kaali's");
            Assert.AreEqual(7, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(28)), "64 by 32 down to 1 by 1");
            Assert.AreEqual(0x401008, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(108)));
            Assert.AreEqual("DXT5", System.Text.Encoding.ASCII.GetString(dds, 84, 4));
            // 16 by 8 blocks, then 8 by 4, 4 by 2, 2 by 1, 1, 1, 1 of 16 bytes
            Assert.AreEqual(128 + (128 + 32 + 8 + 2 + 1 + 1 + 1) * 16, dds.Length);

            DecodedTexture back = GameTextures.Decode(dds);
            Assert.AreEqual(64, back.Width);
            Assert.AreEqual(32, back.Height);
            double error = rgba.Zip(back.Rgba).Average(pair => Math.Abs(pair.First - pair.Second));
            Assert.IsTrue(error < 3, $"a mean error of {error:0.00} per channel");

            // the second level is the picture halved, compressed: it reads back as the halved picture
            (int w, int h, byte[] half) = DxtEncoder.Halve(64, 32, rgba);
            byte[] second = DdsTexture.WriteMipmapped(w, h, DdsCompression.Dxt5, half);
            CollectionAssert.AreEqual(second[128..(128 + 32 * 16)], dds[(128 + 128 * 16)..(128 + 160 * 16)]);
            DecodedTexture halfBack = GameTextures.Decode(second);
            Assert.IsTrue(half.Zip(halfBack.Rgba).Average(pair => Math.Abs(pair.First - pair.Second)) < 3);
        }

        [TestMethod]
        public void WritesDxt1ForTheSpecular()
        {
            byte[] rgba = Picture(16, 16, (x, _) => ((byte)(x * 16), (byte)(x * 16), (byte)(x * 16), 255));

            byte[] dds = DdsTexture.WriteMipmapped(16, 16, DdsCompression.Dxt1, rgba);

            Assert.AreEqual("DXT1", System.Text.Encoding.ASCII.GetString(dds, 84, 4));
            Assert.AreEqual(128 + (16 + 4 + 1 + 1 + 1) * 8, dds.Length);
            DecodedTexture back = GameTextures.Decode(dds);
            double error = rgba.Zip(back.Rgba).Average(pair => Math.Abs(pair.First - pair.Second));
            Assert.IsTrue(error < 4, $"a mean error of {error:0.00} per channel");
        }

        [TestMethod]
        public void SizesTheTexturesAsKaali()
        {
            Assert.AreEqual((8192, 8192), MapWideDecalImages.SizeOf(512, 512, MapWideDecalImages.DetailPixelsPerOgrid));
            Assert.AreEqual((2048, 2048), MapWideDecalImages.SizeOf(512, 512, MapWideDecalImages.ShadowPixelsPerOgrid));
            Assert.AreEqual((8192, 8192), MapWideDecalImages.SizeOf(4096, 4096, MapWideDecalImages.DetailPixelsPerOgrid), "capped");
            Assert.AreEqual((8192, 4096), MapWideDecalImages.SizeOf(1024, 512, MapWideDecalImages.DetailPixelsPerOgrid), "in the map's proportions");
            Assert.AreEqual((4096, 4096), MapWideDecalImages.SizeOf(256, 256, MapWideDecalImages.DetailPixelsPerOgrid));
        }

        [TestMethod]
        public void BlendsTheNormalsDecalByTheSlope()
        {
            DecodedTexture normals = MapWideDecalImages.Normals(Cliff().Terrain.Heightmap, 256, 256);

            byte[] Pixel(int x, int z) => normals.Rgba.AsSpan((z * 256 + x) * 4, 4).ToArray();
            // flat: the normal points up, nothing replaces the terrain's
            CollectionAssert.AreEqual(new byte[] { 0, 128, 0, 128 }, Pixel(40, 100));
            // the 45 degree slope rising east: the normal leans west (alpha below the middle), red the
            // square root of the sine of 45 degrees
            byte[] slope = Pixel(136, 100);
            Assert.AreEqual(MathF.Round(MathF.Sqrt(MathF.Sin(MathF.PI / 4)) * 255), slope[0], 1);
            Assert.AreEqual(128, slope[1], 1, "no slope to the south");
            Assert.AreEqual(0, slope[2]);
            Assert.AreEqual(MathF.Round((0.5f - 0.5f / MathF.Sqrt(2)) * 255), slope[3], 1);
        }

        [TestMethod]
        public void DarkensTheFootOfTheCliffAndNotTheOpenGround()
        {
            float[] darkness = MapWideDecalImages.Darkness(Cliff().Terrain.Heightmap, ShadowDecalSettings.Default, Vector3.Normalize(new Vector3(1, 1, 0)));
            int size = 257;

            Assert.AreEqual(0, darkness[100 * size + 40], 1e-6, "open flat ground sees the whole sky");
            float foot = darkness[100 * size + 128];
            Assert.IsTrue(foot > 0.05f && foot <= 0.45f, $"the foot of the cliff: {foot}");
            Assert.AreEqual(0, darkness[100 * size + 200], 1e-6, "the top is open");
            Assert.IsTrue(darkness.All(value => value <= ShadowDecalSettings.Default.Darkest));
        }

        [TestMethod]
        public void CastsTheSunsShadowWhenAsked()
        {
            Heightmap heights = Cliff().Terrain.Heightmap;
            // a low sun in the east: the cliff (rising east) hides it from the ground at its foot and west of it
            Vector3 sun = Vector3.Normalize(new Vector3(1, 0.2f, 0));
            float[] open = MapWideDecalImages.Darkness(heights, ShadowDecalSettings.Default, sun);
            float[] shaded = MapWideDecalImages.Darkness(heights, ShadowDecalSettings.Default with { SunShadows = true }, sun);

            Assert.AreEqual(0, open[100 * 257 + 100], 1e-6);
            Assert.AreEqual(0.45f, shaded[100 * 257 + 100], 1e-6, "16 game units of cliff throw a shadow 80 ogrids long at this sun");
            Assert.AreEqual(0, shaded[100 * 257 + 20], 1e-6, "beyond the shadow");
            Assert.AreEqual(0, shaded[100 * 257 + 200], 1e-6, "on top of the cliff");
        }

        [TestMethod]
        public void ExportsTheDecalsAsKaali()
        {
            string folder = ProjectFolders.New("wide");
            Directory.CreateDirectory(folder);
            using (MagickImage albedo = new MagickImage(MagickColors.SaddleBrown, 512, 512))
            {
                File.WriteAllBytes(Path.Combine(folder, "albedo.png"), albedo.ToByteArray(MagickFormat.Png));
            }
            using (MagickImage specular = new MagickImage(MagickColors.Gray, 128, 128))
            {
                File.WriteAllBytes(Path.Combine(folder, "specular.png"), specular.ToByteArray(MagickFormat.Png));
            }
            MapDocument document = Cliff();
            document = document with { WideDecals = document.WideDecals with { Albedo = MapWideSlot.Of("albedo.png"), Specular = "specular.png" } };

            MapDocument exported = MapWideDecalsExport.Apply(document, path => File.ReadAllBytes(Path.Combine(folder, path)), out IReadOnlyList<MapWideWritten> written);
            MapGameFiles files = MapExporter.Export(exported);

            Assert.AreEqual(4, written.Count, "albedo, specular, shadow, normals");
            IReadOnlyList<ScmapDecal> decals = files.Scmap.Decals;
            Assert.AreEqual(document.Decals.Length + 3, decals.Count);
            string root = "/maps/cliff.v0001/env/decals/";
            ScmapDecal albedoDecal = decals[0], shadowDecal = decals[1], normalsDecal = decals[2];
            Assert.AreEqual((int)DecalType.Albedo, albedoDecal.Type);
            CollectionAssert.AreEqual(new[] { root + "map_albedo.dds", root + "map_specular.dds" }, albedoDecal.Textures.ToArray());
            Assert.AreEqual((int)DecalType.Albedo, shadowDecal.Type);
            CollectionAssert.AreEqual(new[] { root + "map_lighting.dds", "" }, shadowDecal.Textures.ToArray());
            Assert.AreEqual((int)DecalType.Normals, normalsDecal.Type);
            CollectionAssert.AreEqual(new[] { root + "map_normals.dds", "" }, normalsDecal.Textures.ToArray());
            foreach (ScmapDecal decal in decals.Take(3))
            {
                Assert.AreEqual(new Vector3(256, 256, 256), decal.Scale);
                Assert.AreEqual(new Vector3(0, 20, 0), decal.Position);
                Assert.AreEqual(Vector3.Zero, decal.Rotation);
                Assert.AreEqual(10000, decal.CutOffLod);
                Assert.AreEqual(-1, decal.OwnerArmy);
            }

            // the textures among the map's files: DXT5 with mipmaps, the specular DXT1, Kaali's resolutions
            (int Width, int Height, string Format, int Levels) Header(string path)
            {
                byte[] dds = files.Files[path];
                return (BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(16)), BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(12)),
                    System.Text.Encoding.ASCII.GetString(dds, 84, 4), BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(28)));
            }
            Assert.AreEqual((512, 512, "DXT5", 10), Header("env/decals/map_albedo.dds"), "a chosen file keeps its size");
            Assert.AreEqual((128, 128, "DXT1", 8), Header("env/decals/map_specular.dds"));
            Assert.AreEqual((1024, 1024, "DXT5", 11), Header("env/decals/map_lighting.dds"), "4 pixels an ogrid");
            Assert.AreEqual((4096, 4096, "DXT5", 13), Header("env/decals/map_normals.dds"), "16 pixels an ogrid");

            // the shadow is black with its darkness in alpha
            DecodedTexture shadow = GameTextures.Decode(files.Files["env/decals/map_lighting.dds"]);
            Assert.IsTrue(Enumerable.Range(0, shadow.Width * shadow.Height).All(i => shadow.Rgba[i * 4] < 8 && shadow.Rgba[i * 4 + 1] < 8 && shadow.Rgba[i * 4 + 2] < 8));
            Assert.IsTrue(shadow.Rgba.Where((_, i) => i % 4 == 3).Max() > 20, "the cliff's foot is dark");
        }

        [TestMethod]
        public void LeavesAMapWithoutMapWideDecalsAlone()
        {
            MapDocument document = Cliff() with { WideDecals = MapWideDecals.None };

            Assert.AreSame(document, MapWideDecalsExport.Apply(document, _ => null, out IReadOnlyList<MapWideWritten> written));
            Assert.AreEqual(0, written.Count);
        }

        [TestMethod]
        public void NamesTheMapsOwnFileAsItIs()
        {
            MapDocument document = Cliff();
            byte[] dds = DdsTexture.WriteMipmapped(8, 8, DdsCompression.Dxt5, new byte[8 * 8 * 4]);
            document = document with
            {
                Files = document.Files.SetItem("env/decals/own_normals.dds", [.. dds]),
                WideDecals = new MapWideDecals { Normal = MapWideSlot.Of("/maps/cliff.v0001/env/decals/own_normals.dds") },
            };

            MapDocument exported = MapWideDecalsExport.Apply(document, _ => null, out IReadOnlyList<MapWideWritten> written);

            Assert.AreEqual("/maps/cliff.v0001/env/decals/own_normals.dds", exported.Decals[0].Textures[0]);
            Assert.AreEqual(DecalType.Normals, exported.Decals[0].Type);
            Assert.AreEqual(document.Files.Count, exported.Files.Count, "nothing written");
            Assert.AreEqual("the map's own file", written.Single().How);
        }

        [TestMethod]
        public void RefusesAFileItCannotRead()
        {
            MapDocument document = Cliff() with { WideDecals = new MapWideDecals { Albedo = MapWideSlot.Of("gone.png") } };

            Assert.ThrowsException<MapProjectException>(() => MapWideDecalsExport.Apply(document, _ => null, out _));
        }

        [TestMethod]
        public void KeepsTheSettingsInTheProject()
        {
            MapDocument document = Cliff() with
            {
                WideDecals = new MapWideDecals
                {
                    Albedo = MapWideSlot.Of("procedural/temp/albedo.png"),
                    Specular = "procedural/temp/spec.png",
                    Normal = MapWideSlot.None,
                    Shadow = MapWideSlot.Generated,
                    ShadowSettings = new ShadowDecalSettings { Radius = 24, Strength = 1.5f, Darkest = 0.3f, SunShadows = true },
                },
            };
            string project = ProjectFolders.New("wide-project");

            MapProjectStore.Save(document, project);
            MapDocument loaded = MapProjectStore.Load(project);

            Assert.AreEqual(document.WideDecals, loaded.WideDecals);
            Assert.IsTrue(File.ReadAllText(Path.Combine(project, "decals.json")).Contains("\"mapWide\""));
        }

        [TestMethod]
        public void StartsANewMapWithTheNormalAndShadowGeneratedAndAnImportedOneWithout()
        {
            Assert.AreEqual(MapWideDecals.NewMap, NewMap.Create(new NewMapSettings()).WideDecals);
            Assert.AreEqual(MapWideDecals.None, MapImporter.ImportFolder(ProjectFolders.ThetaPassage).WideDecals);
        }

        [TestMethod]
        public void LeavesNoMapWideKeyInAProjectWithout()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);
            string project = ProjectFolders.New("plain");

            MapProjectStore.Save(document, project);

            Assert.IsFalse(File.ReadAllText(Path.Combine(project, "decals.json")).Contains("mapWide"));
        }
    }
}
