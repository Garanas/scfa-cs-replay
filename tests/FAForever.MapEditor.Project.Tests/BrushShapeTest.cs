using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class BrushShapeTest
    {
        private const float Scale = 1f / 128;

        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        /// <summary>
        /// A shape of 8 by 8 pixels whose top half is white.
        /// </summary>
        private static BrushShape TopHalf() => new BrushShape(8, 8, [.. Enumerable.Range(0, 64).Select(i => i < 32 ? 1f : 0f)]);

        private static TiledBuffer<ushort>.Builder Flat() => TiledBuffer<ushort>.Create(65, 65, 1280).ToBuilder();

        [TestMethod]
        public void RaisesWhereThePictureIsWhite()
        {
            TiledBuffer<ushort>.Builder samples = Flat();

            new HeightBrush { Radius = 8, Strength = 2, Shape = TopHalf() }.Apply(samples, Scale, 32, 32);

            Assert.AreEqual(12f, samples[32, 28] * Scale, "the top of the picture is north");
            Assert.AreEqual(1280, samples[32, 36]);
            Assert.AreEqual(12f, samples[26, 26] * Scale, "a shape fills the square, past the radius in its corners");
            Assert.AreEqual(1280, samples[32, 22], "nothing outside the square");
        }

        [TestMethod]
        public void TurnsClockwiseAsTheTopViewShowsIt()
        {
            TiledBuffer<ushort>.Builder samples = Flat();

            new HeightBrush { Radius = 8, Strength = 2, Shape = TopHalf(), Rotation = 90 }.Apply(samples, Scale, 32, 32);

            Assert.AreEqual(12f, samples[36, 32] * Scale, "the top of the picture turns to the east");
            Assert.AreEqual(1280, samples[28, 32]);
        }

        [TestMethod]
        public void PaintsAMaskWithItsShape()
        {
            TiledBuffer<byte>.Builder mask = TiledBuffer<byte>.Create(32, 32).ToBuilder();

            new MaskBrush { Radius = 8, Strength = 1, Shape = TopHalf(), Rotation = 180 }.Apply(mask, 64, 64, 32, 32);

            Assert.AreEqual(255, mask[16, 18], "turned half round, the white half is south");
            Assert.AreEqual(0, mask[16, 13]);
        }

        [TestMethod]
        public void KeepsTheValuesOfAGreyPictureAndTheBrightnessOfAColouredOne()
        {
            string folder = ProjectFolders.New("brush-values");
            string grey = Path.Combine(folder, "grey.png");
            ProjectImages.WriteGray16(grey, 2, 1, [32768, 65535]);
            string colour = Path.Combine(folder, "colour.png");
            ProjectImages.WriteRgba(colour, 2, 1, [128, 128, 128, 255, 255, 255, 255, 255]);

            Assert.AreEqual(0.5f, BrushShape.Read(grey).Values[0], 0.01f, "a value is a height: no conversion to linear light");
            Assert.AreEqual(0.5f, BrushShape.Read(colour).Values[0], 0.01f);
        }

        [TestMethod]
        public void ReadsAPictureAsItsBrightnessReducedAndStretched()
        {
            string folder = ProjectFolders.New("brush");
            string grey = Path.Combine(folder, "wide.png");
            ushort[] samples = new ushort[512 * 300];
            samples[150 * 512 + 256] = 30000;
            ProjectImages.WriteGray16(grey, 512, 300, samples);
            string colour = Path.Combine(folder, "colour.png");
            ProjectImages.WriteRgba(colour, 2, 1, [0, 0, 0, 255, 100, 100, 100, 255]);

            BrushShape wide = BrushShape.Read(grey);
            BrushShape small = BrushShape.Read(colour);

            Assert.AreEqual((256, 150), (wide.Width, wide.Height));
            Assert.AreEqual(1f, small.Sample(0.5f, 0), 1e-3f, "the brightest pixel is stretched to 1");
            Assert.AreEqual(0f, small.Sample(-0.5f, 0), 1e-3f);
            Assert.AreEqual(0f, small.Sample(0.5f, -1f), "a wide picture is centred in its square, nothing above it");
            Assert.ThrowsException<MapProjectException>(() => BrushShape.Read(Path.Combine(folder, "missing.png")));
        }
    }
}
