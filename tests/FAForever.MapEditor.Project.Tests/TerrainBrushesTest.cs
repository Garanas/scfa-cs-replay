using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The brushes on the terrain's grids beyond raising, lowering, smoothing and flattening (user
    /// stories T-1, T-6, S-1, W-2, K-1).
    /// </summary>
    [TestClass]
    public class TerrainBrushesTest
    {
        private const float Scale = 1f / 128;

        /// <summary>
        /// A flat heightmap of 64 by 64 ogrids at 10 game units.
        /// </summary>
        private static TiledBuffer<ushort>.Builder Flat() => TiledBuffer<ushort>.Create(65, 65, 1280).ToBuilder();

        private static float Height(TiledBuffer<ushort>.Builder samples, int x, int z) => samples[x, z] * Scale;

        [TestMethod]
        public void SetsTheHeightWhereverASharpBrushWorks()
        {
            TiledBuffer<ushort>.Builder samples = Flat();

            new HeightBrush { Mode = HeightBrushMode.SetHeight, Radius = 6, Strength = 0.01f, Target = 30, Falloff = BrushFalloff.Sharp }.Apply(samples, Scale, 32, 32);

            Assert.AreEqual(30f, Height(samples, 32, 32), "all the way, whatever the strength");
            Assert.AreEqual(30f, Height(samples, 37, 32), "as much at the edge as at the centre");
            Assert.AreEqual(10f, Height(samples, 39, 32), "nothing past the radius");
        }

        [TestMethod]
        public void SetsTheHeightPartlyWhereASmoothBrushFades()
        {
            TiledBuffer<ushort>.Builder samples = Flat();

            new HeightBrush { Mode = HeightBrushMode.SetHeight, Radius = 8, Target = 30 }.Apply(samples, Scale, 32, 32);

            Assert.AreEqual(30f, Height(samples, 32, 32));
            Assert.IsTrue(Height(samples, 38, 32) > 10 && Height(samples, 38, 32) < 30);
        }

        [TestMethod]
        public void FadesAsTheFalloffSays()
        {
            float Raised(BrushFalloff falloff, int x)
            {
                TiledBuffer<ushort>.Builder samples = Flat();
                new HeightBrush { Mode = HeightBrushMode.Raise, Radius = 8, Strength = 8, Falloff = falloff }.Apply(samples, Scale, 32, 32);
                return Height(samples, x, 32) - 10;
            }

            Assert.AreEqual(4f, Raised(BrushFalloff.Linear, 36), 0.01f, "half way out, half as much");
            Assert.AreEqual(4f, Raised(BrushFalloff.Smooth, 36), 0.01f, "the bell is at half at half the radius");
            Assert.IsTrue(Raised(BrushFalloff.Smooth, 34) > Raised(BrushFalloff.Linear, 34), "the bell is fuller near the centre");
            Assert.AreEqual(8f, Raised(BrushFalloff.Sharp, 39), 0.01f, "sharp: all of it up to the edge");
            Assert.AreEqual(0f, Raised(BrushFalloff.Sharp, 40), 0.01f);
        }

        [TestMethod]
        public void AddsTheSameNoiseForTheSameSeed()
        {
            TiledBuffer<ushort>.Builder first = Flat();
            TiledBuffer<ushort>.Builder again = Flat();
            TiledBuffer<ushort>.Builder other = Flat();
            HeightBrush noise = new HeightBrush { Mode = HeightBrushMode.Noise, Radius = 16, Strength = 4, NoiseScale = 4, Seed = 7, Falloff = BrushFalloff.Sharp };

            noise.Apply(first, Scale, 32, 32);
            noise.Apply(again, Scale, 32, 32);
            (noise with { Seed = 8 }).Apply(other, Scale, 32, 32);

            float[] heights = [.. Enumerable.Range(20, 25).SelectMany(x => Enumerable.Range(20, 25).Select(z => Height(first, x, z)))];
            Assert.IsTrue(heights.Any(h => h > 10.5f) && heights.Any(h => h < 9.5f), "up and down");
            Assert.IsTrue(heights.All(h => h >= 6 && h <= 14), "by at most the strength");
            Assert.IsTrue(Enumerable.Range(20, 25).All(x => first[x, 30] == again[x, 30]), "the same seed, the same noise");
            Assert.IsTrue(Enumerable.Range(20, 25).Any(x => first[x, 30] != other[x, 30]), "another seed, other noise");
            Assert.AreEqual(1280, first[0, 0], "nothing outside the brush");
        }

        [TestMethod]
        public void ErodesACliffAndKeepsTheEarth()
        {
            // a cliff of 20 game units between x 31 and 32
            TiledBuffer<ushort>.Builder samples = Flat();
            for (int z = 0; z < 65; z++)
            {
                for (int x = 0; x < 32; x++)
                {
                    samples[x, z] = 1280 + 20 * 128;
                }
            }
            double Earth() => Enumerable.Range(0, 65).Sum(x => Enumerable.Range(0, 65).Sum(z => (double)samples[x, z]));
            double before = Earth();
            HeightBrush erode = new HeightBrush { Mode = HeightBrushMode.Erode, Radius = 8, Strength = 1 };

            for (int i = 0; i < 20; i++)
            {
                erode.Apply(samples, Scale, 32, 32);
            }

            Assert.IsTrue(Height(samples, 31, 32) < 29, "the top of the cliff wears down");
            Assert.IsTrue(Height(samples, 32, 32) > 11, "the foot fills up");
            Assert.AreEqual(30f, Height(samples, 31, 10), "nothing outside the brush");
            Assert.AreEqual(before, Earth(), before * 1e-4, "the earth slides, it does not vanish");
        }

        [TestMethod]
        public void LeavesGentleSlopesToErosion()
        {
            TiledBuffer<ushort>.Builder samples = Flat();
            for (int x = 0; x < 65; x++)
            {
                for (int z = 0; z < 65; z++)
                {
                    samples[x, z] = (ushort)(1280 + x * 32);  // a quarter of a game unit per ogrid
                }
            }
            ushort[] before = samples.ToBuffer().ToArray();

            new HeightBrush { Mode = HeightBrushMode.Erode, Radius = 8, Strength = 1 }.Apply(samples, Scale, 32, 32);

            CollectionAssert.AreEqual(before, samples.ToBuffer().ToArray());
        }

        [TestMethod]
        public void ScalesTheHeightsAroundABase()
        {
            TiledBuffer<ushort>.Builder samples = Flat();
            samples[32, 32] = 20 * 128;
            samples[33, 32] = 5 * 128;

            new HeightBrush { Mode = HeightBrushMode.Scale, Radius = 8, Factor = 2, Target = 10, Falloff = BrushFalloff.Sharp }.Apply(samples, Scale, 32, 32);

            Assert.AreEqual(30f, Height(samples, 32, 32), "10 above the base becomes 20 above it");
            Assert.AreEqual(0f, Height(samples, 33, 32), "5 below it becomes 10 below");
            Assert.AreEqual(10f, Height(samples, 34, 32), "the base stays");
        }

        [TestMethod]
        public void KeepsWhatItWritesWithinTheLimits()
        {
            TiledBuffer<ushort>.Builder samples = Flat();
            samples[20, 20] = 40 * 128;

            new HeightBrush { Mode = HeightBrushMode.Raise, Radius = 30, Strength = 20, MaxHeight = 15 }.Apply(samples, Scale, 32, 32);
            Assert.AreEqual(15f, Height(samples, 32, 32), "raised no higher than the limit");
            Assert.AreEqual(40f, Height(samples, 20, 20), "a height above it already is not cut down");

            new HeightBrush { Mode = HeightBrushMode.Lower, Radius = 30, Strength = 20, MinHeight = 12 }.Apply(samples, Scale, 32, 32);
            Assert.AreEqual(12f, Height(samples, 32, 32), "lowered no deeper than the limit");
        }

        [TestMethod]
        public void PaintsAMaskOnlyWithinTheLimitsOfSlopeAndHeight()
        {
            // flat at 10 west of x 32, a slope of 45 degrees up to the east from there
            TiledBuffer<ushort>.Builder samples = Flat();
            for (int x = 32; x < 65; x++)
            {
                for (int z = 0; z < 65; z++)
                {
                    samples[x, z] = (ushort)(1280 + (x - 32) * 128);
                }
            }
            Heightmap terrain = new Heightmap { Scale = Scale, Samples = samples.ToBuffer() };
            MaskBrush flatOnly = new MaskBrush { Radius = 20, Strength = 1, Falloff = BrushFalloff.Sharp, Limits = new PaintLimits(MaxSlope: 20) };

            TiledBuffer<byte>.Builder mask = TiledBuffer<byte>.Create(64, 64).ToBuilder();
            flatOnly.Apply(mask, 64, 64, 32, 32, terrain);
            Assert.AreEqual(255, mask[25, 32], "flat ground is painted");
            Assert.AreEqual(0, mask[40, 32], "the slope is not");

            mask = TiledBuffer<byte>.Create(64, 64).ToBuilder();
            (flatOnly with { Limits = new PaintLimits(MinHeight: 15, MaxHeight: 20) }).Apply(mask, 64, 64, 40, 32, terrain);
            Assert.AreEqual(0, mask[35, 32], "below the lowest height");
            Assert.AreEqual(255, mask[38, 32], "between the heights");
            Assert.AreEqual(0, mask[45, 32], "above the highest");

            mask = TiledBuffer<byte>.Create(64, 64).ToBuilder();
            flatOnly.Apply(mask, 64, 64, 32, 32);
            Assert.AreEqual(255, mask[40, 32], "without the heights the limits do not apply");
        }

        [TestMethod]
        public void SetsCodesWithoutBlending()
        {
            TiledBuffer<byte>.Builder types = TiledBuffer<byte>.Create(64, 64, 1).ToBuilder();

            new CodeBrush { Radius = 8, Value = 150, Threshold = 0.5f, Falloff = BrushFalloff.Smooth }.Apply(types, 64, 64, 32, 32);

            Assert.AreEqual(150, types[31, 31], "the code itself at the centre");
            Assert.AreEqual(150, types[34, 31], "and where the brush works at least half");
            Assert.AreEqual(1, types[37, 31], "the old code where it works less");
            Assert.IsTrue(types.ToBuffer().ToArray().All(code => code is 1 or 150), "no code in between");
        }

        [TestMethod]
        public void PaintsTheGridsOfADocumentInOneStep()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            document = MaskLayers.Add(document, "rivers", half: true);
            EditSession session = new EditSession(document);
            byte grass = (byte)TerrainTypes.All.First(type => type.Name == "Vegetation02").TypeCode;

            using (EditTransaction stroke = session.Begin("Paint"))
            {
                new CodeBrush { Radius = 4, Value = grass }.Apply(stroke.Grid(PaintTarget.TerrainTypes), document.Width, document.Height, 100, 100);
                new MaskBrush { Radius = 4, Strength = 1, Falloff = BrushFalloff.Sharp }.Apply(stroke.Grid(PaintTarget.Water(WaterMask.Foam)), document.Width, document.Height, 100, 100);
                new MaskBrush { Radius = 4, Strength = 1, Falloff = BrushFalloff.Sharp }.Apply(stroke.Grid(PaintTarget.Mask("rivers")), document.Width, document.Height, 100, 100);
                stroke.Commit();
            }

            MapDocument painted = session.Document;
            Assert.AreEqual(grass, painted.Terrain.Types[100, 100]);
            Assert.AreEqual(255, painted.Water.FoamMask[50, 50], "half the map's size");
            Assert.AreEqual(255, painted.Masks["rivers"][50, 50]);
            Assert.AreSame(document.Water.FlatnessMask, painted.Water.FlatnessMask, "the other masks are untouched");
            Assert.AreEqual("Paint", session.UndoDescription, "one step");
            session.Undo();
            Assert.AreSame(document, session.Document);
        }

        [TestMethod]
        public void ExportsThePaintedTerrainTypes()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            byte lava = (byte)TerrainTypes.All.First(type => type.Name == "Lava01").TypeCode;
            TiledBuffer<byte>.Builder types = document.Terrain.Types.ToBuilder();
            new CodeBrush { Radius = 3, Value = lava }.Apply(types, document.Width, document.Height, 60, 70);
            document = document with { Terrain = document.Terrain with { Types = types.ToBuffer() } };

            IReadOnlyDictionary<string, byte[]> files = MapExporter.ToBytes(document);
            Scmap scmap = ScmapParser.Parse(files.Single(file => file.Key.EndsWith(".scmap", StringComparison.Ordinal)).Value);

            Assert.AreEqual(lava, scmap.TerrainTypes[70 * scmap.Width + 60], "the scmap holds a type per ogrid, row by row");
            CollectionAssert.AreEqual(document.Terrain.Types.ToArray(), scmap.TerrainTypes);
        }
    }
}
