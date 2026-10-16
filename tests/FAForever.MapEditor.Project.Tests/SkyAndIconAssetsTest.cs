using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The sky's cube maps and the units' icons of the workspace (user stories L-1, U-1), reading one
    /// asset again and the missing cube maps (F-8), the terrain shaders and the textured preview (S-4).
    /// </summary>
    [TestClass]
    public sealed class SkyAndIconAssetsTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static void Png(string file, int width, int height, MagickColor colour)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using MagickImage image = new MagickImage(colour, (uint)width, (uint)height);
            File.WriteAllBytes(file, image.ToByteArray(MagickFormat.Png));
        }

        private static string Assets()
        {
            string assets = ProjectFolders.New("assets");
            Png(Path.Combine(assets, "textures/environment/skycube_evergreen01a.png"), 24, 4, MagickColors.SkyBlue);
            Png(Path.Combine(assets, "textures/environment/envcube_desert01a.png"), 24, 4, MagickColors.SandyBrown);
            Png(Path.Combine(assets, "textures/environment/defaultbackground.png"), 4, 4, MagickColors.Black);
            Png(Path.Combine(assets, "textures/engine/watercubemap.png"), 24, 4, MagickColors.Navy);
            Png(Path.Combine(assets, "textures/ui/common/icons/units/uel0101_icon.png"), 4, 4, MagickColors.Green);
            Png(Path.Combine(assets, "textures/ui/common/icons/units/land_up.png"), 4, 4, MagickColors.DarkGreen);
            Png(Path.Combine(assets, "textures/ui/common/icons/units/sea_up.png"), 4, 4, MagickColors.DarkBlue);
            Directory.CreateDirectory(Path.Combine(assets, "units/uel0101"));
            File.WriteAllText(Path.Combine(assets, "units/uel0101/uel0101_unit.json"), """{ "id": "UEL0101", "name": "Snoop", "faction": "Uef", "techLevel": 1, "categories": ["UEF", "LAND"] }""");
            Directory.CreateDirectory(Path.Combine(assets, "units/uel0102"));
            File.WriteAllText(Path.Combine(assets, "units/uel0102/uel0102_unit.json"), """{ "id": "UEL0102", "name": "Tank", "faction": "Uef", "categories": ["UEF", "LAND"], "buildIcon": "sea" }""");
            return assets;
        }

        [TestMethod]
        public void ListsTheCubeMapsByWhatTheyAreFor()
        {
            AssetCatalog catalog = AssetCatalog.Load(Assets());

            AssetEntry sky = catalog.Find("/textures/environment/SkyCube_Evergreen01a.dds")!;
            Assert.AreEqual((AssetKind.Cubemap, "environment", "sky"), (sky.Kind, sky.Group, sky.Category));
            Assert.AreEqual("reflection", catalog.Find("/textures/environment/envcube_desert01a.dds")!.Category);
            Assert.AreEqual(("engine", "water"), (catalog.Find("/textures/engine/watercubemap.dds")!.Group, catalog.Find("/textures/engine/watercubemap.dds")!.Category));
            Assert.IsNull(catalog.Find("/textures/environment/defaultbackground.dds"), "a picture that is no strip of six faces is no cube map");
        }

        [TestMethod]
        public void AUnitHasTheGamesIcon()
        {
            string assets = Assets();
            AssetCatalog catalog = AssetCatalog.Load(assets);

            Assert.AreEqual(Path.Combine(assets, AssetCatalog.UnitIconFolder, "uel0101_icon.png"), catalog.Find("uel0101")!.Icon);
            Assert.IsNull(catalog.Find("uel0102")!.Icon, "a unit the game has no icon of");
            // what the build menu draws behind the icon: the blueprint's General.Icon, else land
            Assert.AreEqual(Path.Combine(assets, AssetCatalog.UnitIconFolder, "land_up.png"), catalog.Find("uel0101")!.IconBackground);
            Assert.AreEqual(Path.Combine(assets, AssetCatalog.UnitIconFolder, "sea_up.png"), catalog.Find("uel0102")!.IconBackground);
        }

        [TestMethod]
        public void ReadsOneAssetAgain()
        {
            string assets = Assets();
            AssetCatalog catalog = AssetCatalog.Load(assets);
            AssetEntry other = catalog.Find("uel0102")!;
            File.WriteAllText(Path.Combine(assets, "units/uel0101/uel0101_unit.json"), """{ "id": "UEL0101", "name": "Snoop Mark II", "faction": "Uef", "techLevel": 2, "categories": ["UEF", "LAND"] }""");

            (AssetCatalog again, AssetEntry? entry) = catalog.Reload("UEL0101", assets);

            Assert.AreEqual("Snoop Mark II", entry!.Name);
            Assert.AreEqual(2, again.Find("uel0101")!.TechLevel);
            Assert.AreSame(other, again.Find("uel0102"), "the other assets are kept as they were");
            Assert.AreEqual(catalog.Entries.Count, again.Entries.Count);
            Assert.AreSame(catalog, again.ReloadedFrom);
            CollectionAssert.AreEquivalent(new[] { "uel0101" }, again.ReloadedKeys.ToArray());
            Assert.AreEqual("Snoop", catalog.Find("uel0101")!.Name, "the catalogue read before is unchanged");

            File.Delete(Path.Combine(assets, "units/uel0101/uel0101_unit.json"));
            (AssetCatalog gone, AssetEntry? none) = again.Reload("uel0101", assets);
            Assert.IsNull(none);
            Assert.IsNull(gone.Find("uel0101"), "an asset whose file is gone is left out");
            Assert.ThrowsException<ArgumentException>(() => gone.Reload("uel0101", assets));
        }

        [TestMethod]
        public void ReadsACubeMapAgain()
        {
            string assets = Assets();
            AssetCatalog catalog = AssetCatalog.Load(assets);
            Png(Path.Combine(assets, "textures/environment/skycube_evergreen01a.png"), 48, 8, MagickColors.SkyBlue);

            (_, AssetEntry? entry) = catalog.Reload("textures/environment/skycube_evergreen01a.dds", assets);

            Assert.AreEqual("6 of 8 × 8 pixels", entry!.Facts.Single().Value);
        }

        [TestMethod]
        public void TheCubeMapsAMapNamesAreMissingOnlyFromAWorkspaceThatHasCubeMaps()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            AssetCatalog catalog = AssetCatalog.Load(Assets());
            document = document with { Sky = document.Sky with { Cubemap = "/textures/environment/SkyCube_Evergreen01a.dds", EnvironmentCubemaps = [new Cubemap { Name = "<default>", Texture = "/textures/environment/none.dds" }] } };

            IReadOnlyList<MissingAsset> missing = MissingAssets.Find(document, catalog);
            MissingAsset reflection = missing.Single(asset => asset.Kind == AssetKind.Cubemap && asset.Path.Contains("none"));
            Assert.AreEqual("missing-texture", reflection.Check);
            Assert.IsFalse(missing.Any(asset => asset.Path.Contains("skycube_evergreen01a")), "the workspace has the sky");

            IReadOnlyList<Problem> problems = [.. new ProblemChecks().Run(new ProblemContext(document, catalog)).Where(problem => problem.Check == "missing-texture")];
            Assert.IsTrue(problems.Any(problem => problem.Message.StartsWith("The cube map units reflect (<default>)") && problem.Message.Contains("/textures/environment/none.dds")));

            // a workspace converted before the cube maps were knows none of them: nothing is missing
            Assert.IsFalse(MissingAssets.Find(document, AssetCatalog.Empty).Any(asset => asset.Kind == AssetKind.Cubemap));
        }

        [TestMethod]
        public void KnowsTheTerrainShadersAndWhatTheyRead()
        {
            Assert.AreEqual(4, TerrainShaderCatalog.Of("TTerrain").Strata);
            Assert.IsFalse(TerrainShaderCatalog.Of("tterrainxp").FullMaskRange, "the name in any case");
            Assert.IsTrue(TerrainShaderCatalog.Of("Terrain000").FullMaskRange);
            Assert.IsTrue(TerrainShaderCatalog.Of("Terrain250").Stratum8IsData);
            Assert.IsFalse(TerrainShaderCatalog.Of("Terrain250").UpperIsAlbedo);
            Assert.IsNull(TerrainShaderCatalog.Find("MyShader"));
            Assert.AreEqual("TTerrainXP", TerrainShaderCatalog.Of("MyShader").Name, "an unknown one blends as TTerrainXP");
            Assert.AreEqual(TerrainShaderCatalog.All.Length, TerrainShaderCatalog.All.Select(shader => shader.Name).Distinct().Count());
        }

        [TestMethod]
        public void ThePreviewShowsTheStrataTextures()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa) with { Water = MapImporter.ImportFolder(ProjectFolders.HardFfa).Water with { HasWater = false } };
            // the lower layer red, the first stratum blue over the whole map, the others none
            TiledBuffer<byte> full = TiledBuffer<byte>.Create(document.Terrain.StratumMasks.Width, document.Terrain.StratumMasks.Height, 255);
            TiledBuffer<byte> empty = TiledBuffer<byte>.Create(document.Terrain.StratumMasks.Width, document.Terrain.StratumMasks.Height, 0);
            document = document with
            {
                Terrain = document.Terrain with
                {
                    Shader = "TTerrainXP",
                    StratumMasks = document.Terrain.StratumMasks with { Layers = [.. document.Terrain.StratumMasks.Layers.Select((_, i) => i == 0 ? full : empty)] },
                },
                Lighting = document.Lighting with { SunColor = new System.Numerics.Vector3(0), SunAmbience = new System.Numerics.Vector3(1), ShadowFillColor = new System.Numerics.Vector3(0), Multiplier = 1 },
            };
            string lower = document.Terrain.Layers[0].AlbedoPath;
            string first = document.Terrain.Layers[1].AlbedoPath;
            DecodedTexture? Texture(string path) =>
                path == lower ? Solid(255, 0, 0) : path == first ? Solid(0, 0, 255) : null;

            TiledBuffer<Color32> preview = TexturedPreview.Render(document, Texture, 64);
            Color32 middle = preview[32, 32];
            Assert.IsTrue(middle.Blue > 200 && middle.Red < 40, $"the first stratum covers the map: {middle}");

            // half its mask: TTerrainXP counts only the upper half, so nothing of it shows; Terrain000 the whole range
            TiledBuffer<byte> half = TiledBuffer<byte>.Create(full.Width, full.Height, 128);
            document = document with { Terrain = document.Terrain with { StratumMasks = document.Terrain.StratumMasks with { Layers = [.. document.Terrain.StratumMasks.Layers.Select((_, i) => i == 0 ? half : empty)] } } };
            Color32 halfMask = TexturedPreview.Render(document, Texture, 64)[32, 32];
            Assert.IsTrue(halfMask.Red > 200 && halfMask.Blue < 10, $"TTerrainXP: {halfMask}");
            Color32 wholeRange = TexturedPreview.Render(document with { Terrain = document.Terrain with { Shader = "Terrain000" } }, Texture, 64)[32, 32];
            Assert.IsTrue(wholeRange.Red is > 100 and < 160 && wholeRange.Blue is > 100 and < 160, $"Terrain000: {wholeRange}");
        }

        [TestMethod]
        public void AnUncompressedCubeMapWithoutAlphaKeepsItsFacesApartFromTheirSmallerLevels()
        {
            // as skycube_desert01.dds: 32 bits a pixel, DDPF_RGB without DDPF_ALPHAPIXELS, a chain of levels per face
            const int size = 4;
            const int levels = 3;
            int chain = (16 + 4 + 1) * 4;
            byte[] dds = new byte[128 + 6 * chain];
            void Put(int at, uint value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(at), value);
            "DDS "u8.CopyTo(dds);
            Put(4, 124);
            Put(8, 0x1 | 0x2 | 0x4 | 0x1000 | 0x20000);
            Put(12, size);
            Put(16, size);
            Put(28, levels);
            Put(76, 32);
            Put(80, 0x40);
            Put(88, 32);
            Put(92, 0x00ff0000);
            Put(96, 0x0000ff00);
            Put(100, 0x000000ff);
            Put(104, 0xff000000);
            Put(112, 0x200 | 0xfc00);
            for (int face = 0; face < 6; face++)
            {
                for (int i = 0; i < chain / 4; i++)
                {
                    // the first level of a face its own red, the smaller levels white
                    Put(128 + face * chain + i * 4, i < size * size ? (uint)((face * 40 + 10) << 16) : 0x00ffffffu);
                }
            }

            DecodedTexture strip = CubeMaps.Decode(dds);

            Assert.AreEqual((6 * size, size), (strip.Width, strip.Height));
            for (int face = 0; face < 6; face++)
            {
                byte[] pixels = CubeMaps.Face(strip, face);
                Assert.IsTrue(Enumerable.Range(0, size * size).All(i => pixels[i * 4] == face * 40 + 10 && pixels[i * 4 + 1] == 0 && pixels[i * 4 + 3] == 255), $"face {face} whole and of its own colour");
            }
        }

        private static DecodedTexture Solid(byte red, byte green, byte blue)
        {
            byte[] rgba = new byte[8 * 8 * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (red, green, blue, 255);
            }
            return new DecodedTexture(8, 8, rgba);
        }
    }
}
