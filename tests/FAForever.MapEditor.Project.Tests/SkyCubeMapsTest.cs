using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class SkyCubeMapsTest
    {
        private const string SkyboxJson = "assets/skyboxes/procedural-skybox.json";

        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static readonly Vector3 Horizon = new Vector3(1, 0.5f, 0);
        private static readonly Vector3 Zenith = new Vector3(0, 0, 1);

        /// <summary>
        /// A flat map of 256 ogrids at height 10, without water, with a sky from orange at the horizon
        /// to blue at the zenith.
        /// </summary>
        private static MapDocument Flat(Skybox? skybox = null, bool water = false, float waterElevation = 20)
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Name = "Sky", Size = 256, Height = 10, Water = water, WaterElevation = waterElevation });
            return document with { Sky = document.Sky with { Skybox = skybox ?? Sky() } };
        }

        private static Skybox Sky(params Planet[] planets) => Skyboxes.FromLighting(NewMap.Create(new NewMapSettings { Size = 256 })) with
        {
            HorizonColor = Horizon,
            ZenithColor = Zenith,
            HorizonHeight = 0,
            ZenithHeight = 100,
            Scale = 600,
            Position = new Vector3(128, 0, 128),
            Albedo = planets.Length > 0 ? "atlas" : "",
            Planets = [.. planets],
        };

        private static readonly SkyCubeSettings NoTerrain = new SkyCubeSettings { Resolution = 512, IncludeTerrain = false, SunDisc = false, EyeHeight = 0 };

        private static Vector3 At(DecodedTexture strip, int face, int column, int row)
        {
            int at = (row * strip.Width + face * strip.Height + column) * 4;
            return new Vector3(strip.Rgba[at], strip.Rgba[at + 1], strip.Rgba[at + 2]) / 255f;
        }

        private static void AreClose(Vector3 expected, Vector3 actual, float within, string what) =>
            Assert.IsTrue(Vector3.Distance(expected, actual) <= within, $"{what}: expected {expected}, got {actual}");

        /// <summary>
        /// An atlas of 64 by 64 in four quarters: red top left, green top right, blue bottom left,
        /// white bottom right; opaque.
        /// </summary>
        private static DecodedTexture Atlas()
        {
            byte[] rgba = new byte[64 * 64 * 4];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    (byte r, byte g, byte b) = (x < 32, y < 32) switch
                    {
                        (true, true) => ((byte)255, (byte)0, (byte)0),
                        (false, true) => ((byte)0, (byte)255, (byte)0),
                        (true, false) => ((byte)0, (byte)0, (byte)255),
                        _ => ((byte)255, (byte)255, (byte)255),
                    };
                    int at = (y * 64 + x) * 4;
                    (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) = (r, g, b, 255);
                }
            }
            return new DecodedTexture(64, 64, rgba);
        }

        [TestMethod]
        public void EachFaceLooksAlongItsAxisAsDirect3DLaysThemOut()
        {
            Vector3[] axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
            for (int face = 0; face < 6; face++)
            {
                AreClose(axes[face], CubeFaces.Direction(face, 0, 0), 1e-6f, $"the middle of {CubeFaces.Names[face]}");
            }
            // the first row of a side face looks up, its left column (+x: towards +z) as Direct3D has it
            Assert.IsTrue(CubeFaces.PixelDirection(CubeFaces.PositiveX, 256, 0, 512).Y > 0.7f);
            Assert.IsTrue(CubeFaces.PixelDirection(CubeFaces.PositiveX, 0, 256, 512).Z > 0.7f);
            Assert.IsTrue(CubeFaces.PixelDirection(CubeFaces.PositiveZ, 511, 256, 512).X > 0.7f, "+z: right is +x");
            Assert.IsTrue(CubeFaces.PixelDirection(CubeFaces.NegativeZ, 511, 256, 512).X < -0.7f, "-z: right is -x");
            Assert.IsTrue(CubeFaces.PixelDirection(CubeFaces.PositiveY, 256, 511, 512).Z > 0.7f, "+y: its last row towards +z");
            Assert.IsTrue(CubeFaces.PixelDirection(CubeFaces.NegativeY, 256, 511, 512).Z < -0.7f, "-y: its last row towards -z");
        }

        [TestMethod]
        public void TheFaceOfADirectionIsTheOneThatLooksThatWay()
        {
            Random random = new Random(7);
            for (int i = 0; i < 2000; i++)
            {
                int face = random.Next(6);
                float sc = (float)(random.NextDouble() * 2 - 1);
                float tc = (float)(random.NextDouble() * 2 - 1);
                int found = CubeFaces.FaceOf(CubeFaces.Direction(face, sc, tc) * 3.5f, out float s, out float t);
                Assert.AreEqual(face, found);
                Assert.AreEqual(sc, s, 1e-5f);
                Assert.AreEqual(tc, t, 1e-5f);
            }
        }

        [TestMethod]
        public void TheSkyGoesFromTheHorizonToTheZenith()
        {
            DecodedTexture strip = SkyCubeMaps.Render(Flat(), NoTerrain, _ => null);

            Assert.AreEqual(6 * 512, strip.Width);
            Assert.AreEqual(512, strip.Height);
            AreClose(Zenith, At(strip, CubeFaces.PositiveY, 256, 256), 0.01f, "straight up");
            AreClose(Horizon, At(strip, CubeFaces.PositiveX, 256, 257), 0.02f, "at the horizon");
            AreClose(Horizon, At(strip, CubeFaces.NegativeY, 256, 256), 0.01f, "below the horizon, without the terrain");
            // up the side face the sky turns from the horizon's colour to the zenith's
            float lower = At(strip, CubeFaces.PositiveZ, 256, 250).Z;
            float higher = At(strip, CubeFaces.PositiveZ, 256, 230).Z;
            Assert.IsTrue(higher > lower && lower > 0.05f, $"bluer higher up: {lower} then {higher}");
        }

        [TestMethod]
        public void APlanetShowsWhereItIsWithItsPartOfTheAtlasTheRightWayUp()
        {
            // far to the south (+z), level with the eye; the atlas's top half (uv y from the bottom)
            Planet planet = new Planet { Position = new Vector3(128, 0, 128 + 5000), Scale = new Vector2(1000, 1000), Uv = new Vector4(0, 0.5f, 1, 0.5f) };
            MapDocument document = Flat(Sky(planet));

            DecodedTexture strip = SkyCubeMaps.Render(document, NoTerrain with { EyeHeight = 0 }, path => path == "atlas" ? Atlas() : null);

            // the eye stands on the ground (height 10): the planet's middle is slightly below the face's
            Vector3 left = At(strip, CubeFaces.PositiveZ, 230, 256);
            Vector3 right = At(strip, CubeFaces.PositiveZ, 282, 256);
            AreClose(new Vector3(1, 0, 0), left, 0.05f, "red on the left (west)");
            AreClose(new Vector3(0, 1, 0), right, 0.05f, "green on the right (east)");
            // no planet on the opposite face
            AreClose(Horizon, At(strip, CubeFaces.NegativeZ, 256, 257), 0.05f, "nothing to the north");
        }

        [TestMethod]
        public void APlanetTurnedHalfWayShowsItsPictureUpsideDown()
        {
            Planet planet = new Planet { Position = new Vector3(128 + 5000, 10, 128), Scale = new Vector2(1000, 1000), Rotation = MathF.PI, Uv = new Vector4(0, 0, 0.5f, 1) };
            DecodedTexture strip = SkyCubeMaps.Render(Flat(Sky(planet)), NoTerrain, path => path == "atlas" ? Atlas() : null);

            // the atlas's left half (red over blue), turned: blue on top, red below
            AreClose(new Vector3(0, 0, 1), At(strip, CubeFaces.PositiveX, 256, 230), 0.05f, "blue above");
            AreClose(new Vector3(1, 0, 0), At(strip, CubeFaces.PositiveX, 256, 282), 0.05f, "red below");
        }

        [TestMethod]
        public void TheSunShowsAsADiscWhereItIs()
        {
            MapDocument document = Flat();
            document = document with { Lighting = document.Lighting with { SunDirection = Vector3.Normalize(new Vector3(1, 1, 0)) } };

            DecodedTexture with = SkyCubeMaps.Render(document, NoTerrain with { SunDisc = true }, _ => null);
            DecodedTexture without = SkyCubeMaps.Render(document, NoTerrain, _ => null);

            // 45 degrees up towards +x: on the +x face, halfway up from its middle row to the top
            CubeFaces.FaceOf(new Vector3(1, 1, 0), out float sc, out float tc);
            int column = (int)((sc + 1) / 2 * 512);
            int row = (int)((tc + 1) / 2 * 512);
            int face = CubeFaces.FaceOf(new Vector3(1, 1, 0), out _, out _);
            Vector3 disc = At(with, face, Math.Min(column, 511), Math.Min(row, 511));
            Assert.IsTrue(disc.X > 0.9f && disc.Y > 0.9f && disc.Z > 0.8f, $"white-hot: {disc}");
            Assert.IsTrue(At(without, face, Math.Min(column, 511), Math.Min(row, 511)).Y < 0.6f);
            AreClose(At(without, CubeFaces.NegativeX, 256, 100), At(with, CubeFaces.NegativeX, 256, 100), 0.001f, "away from the sun");
        }

        [TestMethod]
        public void TheCirrusLaysItsColourOverTheSky()
        {
            byte[] white = Enumerable.Repeat((byte)255, 8 * 8 * 4).ToArray();
            Skybox sky = Sky() with
            {
                CirrusTexture = "cirrus",
                CirrusMultiplier = 0.5f,
                CirrusColor = new Vector3(1, 1, 1),
                CirrusLayers = [new CirrusLayer { Frequency = new Vector2(0.001f), Direction = new Vector2(1, 0) }],
            };

            DecodedTexture strip = SkyCubeMaps.Render(Flat(sky), NoTerrain, path => path == "cirrus" ? new DecodedTexture(8, 8, white) : null);

            AreClose(Vector3.Lerp(Zenith, Vector3.One, 0.5f), At(strip, CubeFaces.PositiveY, 256, 256), 0.01f, "half white over the zenith");
        }

        [TestMethod]
        public void BelowTheHorizonLiesTheMap()
        {
            MapDocument document = Flat();
            SkyCubeSettings settings = NoTerrain with { IncludeTerrain = true, EyeHeight = 40 };

            DecodedTexture strip = SkyCubeMaps.Render(document, settings, _ => null);

            Vector3 down = At(strip, CubeFaces.NegativeY, 256, 256);
            Assert.IsTrue(Vector3.Distance(down, Horizon) > 0.2f, $"the ground, not the sky: {down}");
            Assert.IsTrue(Math.Abs(down.X - down.Y) < 0.1f && Math.Abs(down.Y - down.Z) < 0.1f, $"the grey of a map without textures, lit: {down}");
            // the sky above stays as it was
            AreClose(Zenith, At(strip, CubeFaces.PositiveY, 256, 256), 0.01f, "straight up");
            Assert.AreEqual(new Vector3(128, 50, 128), SkyCubeMaps.Eye(document, settings));
        }

        [TestMethod]
        public void HighGroundHidesTheSkyBehindIt()
        {
            // a wall 200 high along the east edge
            MapDocument document = Flat();
            Heightmap heights = document.Terrain.Heightmap;
            int size = heights.Width + 1;
            ushort[] samples = new ushort[size * size];
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    samples[z * size + x] = (ushort)MathF.Round((x > 230 ? 200 : 10) / heights.Scale);
                }
            }
            document = document with { Terrain = document.Terrain with { Heightmap = heights with { Samples = TiledBuffer<ushort>.FromArray(size, size, samples) } } };

            DecodedTexture strip = SkyCubeMaps.Render(document, NoTerrain with { IncludeTerrain = true, EyeHeight = 40 }, _ => null);

            // a little above the horizon towards the east: the wall; towards the west: the sky
            Vector3 east = At(strip, CubeFaces.PositiveX, 256, 200);
            Vector3 west = At(strip, CubeFaces.NegativeX, 256, 200);
            Assert.IsTrue(Vector3.Distance(east, west) > 0.2f, $"the wall {east} against the sky {west}");
            Assert.IsTrue(west.Z > east.Z, "the sky is bluer");
        }

        [TestMethod]
        public void TheWaterReflectsTheSky()
        {
            MapDocument document = Flat(water: true, waterElevation: 20);
            SkyCubeSettings settings = NoTerrain with { IncludeTerrain = true, EyeHeight = 40 };

            DecodedTexture strip = SkyCubeMaps.Render(document, settings, _ => null);

            Assert.AreEqual(60, SkyCubeMaps.Eye(document, settings).Y, "above the water");
            DecodedTexture dull = SkyCubeMaps.Render(document with { Water = document.Water with { SkyReflection = 0 } }, settings, _ => null);
            // the reflection is stronger at a slant than straight down (Fresnel)
            float down = Vector3.Distance(At(strip, CubeFaces.NegativeY, 256, 256), At(dull, CubeFaces.NegativeY, 256, 256));
            float slant = Vector3.Distance(At(strip, CubeFaces.PositiveZ, 256, 400), At(dull, CubeFaces.PositiveZ, 256, 400));
            Assert.IsTrue(down > 0.01f, $"some reflection straight down: {down}");
            Assert.IsTrue(slant > down, $"more at a slant: {slant} against {down}");
        }

        [TestMethod]
        public void TheCubeMapIsWrittenAsADdsCubeThatReadsBackFaceByFace()
        {
            // each face a colour of its own
            Vector3[] colours = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 1, 0), new(0, 1, 1), new(1, 0, 1)];
            const int size = 64;
            byte[] rgba = new byte[6 * size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < 6 * size; x++)
                {
                    Vector3 c = colours[x / size] * (0.4f + 0.6f * y / size);
                    int at = (y * 6 * size + x) * 4;
                    (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) = ((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), 255);
                }
            }
            DecodedTexture strip = new DecodedTexture(6 * size, size, rgba);

            byte[] dds = SkyCubeMaps.Dds(strip);

            Assert.IsTrue(CubeMaps.IsCubeMap(dds));
            Assert.AreEqual(0xfe00, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(112)), "a cube with all six faces, as the game's");
            Assert.AreEqual(0x401008, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(108)));
            Assert.AreEqual(7, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(28)), "64 down to 1");
            Assert.AreEqual("DXT1", System.Text.Encoding.ASCII.GetString(dds, 84, 4));
            // per face 16 by 16 blocks, then 8, 4, 2, 1, 1, 1 of 8 bytes
            Assert.AreEqual(128 + 6 * (256 + 64 + 16 + 4 + 1 + 1 + 1) * 8, dds.Length);

            DecodedTexture back = CubeMaps.Decode(dds);
            Assert.AreEqual(6 * size, back.Width);
            Assert.AreEqual(size, back.Height);
            for (int face = 0; face < 6; face++)
            {
                AreClose(At(strip, face, 32, 10), At(back, face, 32, 10), 0.05f, $"face {CubeFaces.Names[face]} near its top");
                AreClose(At(strip, face, 32, 60), At(back, face, 32, 60), 0.05f, $"face {CubeFaces.Names[face]} near its bottom");
            }
        }

        [TestMethod]
        public void AGeneratedCubeMapRoundTripsThroughCubeMaps()
        {
            DecodedTexture strip = SkyCubeMaps.Render(Flat(), NoTerrain with { IncludeTerrain = true, EyeHeight = 40 }, _ => null);

            DecodedTexture back = CubeMaps.Decode(SkyCubeMaps.Dds(strip));

            double error = strip.Rgba.Zip(back.Rgba).Average(pair => Math.Abs(pair.First - pair.Second));
            Assert.IsTrue(error < 3, $"a mean error of {error:0.00} per channel");
            AreClose(Zenith, At(back, CubeFaces.PositiveY, 256, 256), 0.03f, "up stays up");
        }

        [TestMethod]
        public void AMapWithoutASkyboxGetsOneFromItsLighting()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 256 });
            document = document with
            {
                Sky = document.Sky with { Skybox = null },
                Lighting = document.Lighting with { FogColor = new Vector3(0.8f, 0.7f, 0.6f), SunColor = new Vector3(1.2f, 1.1f, 0.9f) },
            };

            Skybox sky = SkyCubeMaps.SkyOf(document);

            Assert.AreEqual(new Vector3(128, 0, 128), sky.Position);
            Assert.IsTrue(sky.Planets.IsEmpty && sky.CirrusLayers.IsEmpty);
            Assert.IsTrue(sky.HorizonColor.X > sky.HorizonColor.Z, $"a warm horizon from the fog and sun: {sky.HorizonColor}");
            Assert.IsTrue(sky.ZenithColor.Z > sky.ZenithColor.X, $"a blue zenith: {sky.ZenithColor}");
            Assert.IsTrue(sky.ZenithHeight > sky.HorizonHeight);

            DecodedTexture strip = SkyCubeMaps.Render(document, NoTerrain, _ => null);
            AreClose(sky.ZenithColor, At(strip, CubeFaces.PositiveY, 256, 256), 0.02f, "its zenith straight up");

            // a black fog takes the sun's colour
            Skybox dark = Skyboxes.FromLighting(document with { Lighting = document.Lighting with { FogColor = Vector3.Zero } });
            Assert.IsTrue(dark.HorizonColor.X > 0.3f, $"from the sun: {dark.HorizonColor}");
        }

        [TestMethod]
        public void ReadsTheSkyboxPresetsOfTheFafMapEditor()
        {
            string json = File.ReadAllText(SkyboxJson);

            Skybox sky = Skyboxes.ReadJson(json);

            Assert.AreEqual(1196, sky.Planets.Length);
            Assert.AreEqual(4, sky.CirrusLayers.Length);
            Assert.AreEqual(new Vector3(256, 0, 256), sky.Position);
            Assert.AreEqual(1171.5814f, sky.Scale, 1e-3f);
            Assert.AreEqual(-50, sky.HorizonHeight);
            Assert.AreEqual(256, sky.ZenithHeight);
            Assert.AreEqual(0.1862f, sky.HorizonColor.X, 1e-4f);
            Assert.AreEqual("/maps/blackstar.v0004/env/decals/sky_objects_albedo.dds", sky.Albedo);
            Assert.AreEqual(new Vector3(-2500, 350, -5000), sky.Planets[0].Position);
            Assert.AreEqual(-0.2f, sky.Planets[0].Rotation, 1e-6f);
            Assert.AreEqual(new Vector2(1900, 1900), sky.Planets[0].Scale);
            Assert.AreEqual(new Vector4(0, 0.5f, 0.5f, 0.5f), sky.Planets[0].Uv);
            Assert.AreEqual(0.24f, sky.CirrusLayers[0].Speed, 1e-6f);
            Assert.AreEqual(1e-4f, sky.CirrusLayers[0].Frequency.X, 1e-9f);
            Assert.AreEqual(2.5f, sky.CirrusMultiplier);

            // the textures of another map's folder move into this one's when it has them
            Skybox moved = Skyboxes.ReadJson(json, "mine.v0001", path => path == "env/decals/aurora.dds");
            Assert.AreEqual("/maps/mine.v0001/env/decals/aurora.dds", moved.CirrusTexture);
            Assert.AreEqual("/maps/blackstar.v0004/env/decals/sky_objects_albedo.dds", moved.Albedo, "a file the map lacks keeps its path");
        }

        [TestMethod]
        public void TheRealSkyRendersWithoutItsTexturesAndQuickly()
        {
            Skybox sky = Skyboxes.ReadJson(File.ReadAllText(SkyboxJson));
            MapDocument document = Flat(sky);
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            DecodedTexture strip = SkyCubeMaps.Render(document, new SkyCubeSettings { Resolution = 512 }, path => path.Contains("albedo") ? Atlas() : null);

            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
            Assert.AreEqual(512, strip.Height);
        }

        [TestMethod]
        public void TheSettingsAreKeptInTheProjectAndTheResolutionIsAPowerOfTwo()
        {
            Assert.AreEqual(512, SkyCubeSettings.ValidResolution(100));
            Assert.AreEqual(1024, SkyCubeSettings.ValidResolution(1000));
            Assert.AreEqual(2048, SkyCubeSettings.ValidResolution(1600));
            Assert.AreEqual(1024, SkyCubeSettings.ValidResolution(1500));
            Assert.AreEqual(2048, SkyCubeSettings.ValidResolution(9000));

            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            SkyCubeSettings sky = new SkyCubeSettings { Resolution = 2048, EyeHeight = 12.5f, IncludeTerrain = false, SunDisc = false };
            string project = ProjectFolders.New("skycube");

            MapProjectStore.Save(document with { Procedural = document.Procedural with { SkyCube = sky } }, project);
            MapDocument loaded = MapProjectStore.Load(project);

            Assert.AreEqual(sky, loaded.Procedural.SkyCube);
            Assert.IsFalse(loaded.Procedural.IsDefault);
            Assert.IsTrue((document.Procedural with { SkyCube = SkyCubeSettings.Default }).IsDefault);
        }

        [TestMethod]
        public void TheCubeMapLiesAmongTheMapsOwnFiles()
        {
            MapDocument document = Flat();

            Assert.AreEqual($"/maps/{document.Folder}/env/cubemaps/skycube_generated.dds", SkyCubeMaps.GamePath(document));
            MapDocument with = document with { Files = document.Files.SetItem(SkyCubeMaps.FilePath, ImmutableArray.Create(SkyCubeMaps.Dds(SkyCubeMaps.Render(document, NoTerrain, _ => null)))) };
            Assert.IsNotNull(GameTextures.FindCube(SkyCubeMaps.GamePath(with), with, null), "the editor finds it as the map's own cube map");
        }
    }
}
