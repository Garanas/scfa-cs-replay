using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Height stamps (user story T-2): a picture pressed into the terrain, and a part of the map
    /// taken as one.
    /// </summary>
    [TestClass]
    public class HeightStampTest
    {
        private const float Scale = 1f / 128;

        /// <summary>
        /// A flat heightmap of 64 by 64 ogrids at 10 game units.
        /// </summary>
        private static TiledBuffer<ushort>.Builder Flat() => TiledBuffer<ushort>.Create(65, 65, 1280).ToBuilder();

        /// <summary>
        /// A picture of 8 by 8 pixels, white in its left half and grey in its right.
        /// </summary>
        private static BrushShape Halves() => new BrushShape(8, 8, [.. Enumerable.Range(0, 64).Select(i => i % 8 < 4 ? 1f : 0.5f)]);

        [TestMethod]
        public void RaisesByThePicturesValueAtOnceAndNothingOutsideIt()
        {
            TiledBuffer<ushort>.Builder samples = Flat();

            new HeightStamp { Shape = Halves(), Radius = 8, Height = 10, Edge = 0 }.Apply(samples, Scale, 32, 32);

            Assert.AreEqual(20, samples[28, 32] * Scale, 0.01, "white: the whole height at once");
            Assert.AreEqual(15, samples[36, 32] * Scale, 0.01, "grey: half of it");
            Assert.AreEqual(10, samples[41, 32] * Scale, 0.01, "outside the square");
        }

        [TestMethod]
        public void TurnsAndMirrorsThePicture()
        {
            TiledBuffer<ushort>.Builder turned = Flat();
            new HeightStamp { Shape = Halves(), Radius = 8, Height = 10, Edge = 0, Rotation = 90 }.Apply(turned, Scale, 32, 32);
            // a quarter turn clockwise in the top view brings the white left half to the top (north, -z)
            Assert.AreEqual(20, turned[32, 28] * Scale, 0.01);
            Assert.AreEqual(15, turned[32, 36] * Scale, 0.01);

            TiledBuffer<ushort>.Builder mirrored = Flat();
            new HeightStamp { Shape = Halves(), Radius = 8, Height = 10, Edge = 0, Mirrored = true }.Apply(mirrored, Scale, 32, 32);
            Assert.AreEqual(15, mirrored[28, 32] * Scale, 0.01, "flipped left to right");
            Assert.AreEqual(20, mirrored[36, 32] * Scale, 0.01);
        }

        [TestMethod]
        public void TheEdgeFadesOutTowardsTheSides()
        {
            TiledBuffer<ushort>.Builder samples = Flat();
            BrushShape white = new BrushShape(8, 8, [.. Enumerable.Repeat(1f, 64)]);

            new HeightStamp { Shape = white, Radius = 8, Height = 10, Edge = 0.5f, Mode = HeightStampMode.Lower }.Apply(samples, Scale, 32, 32);

            Assert.AreEqual(0, samples[32, 32] * Scale, 0.01, "full in the middle");
            float near = samples[39, 32] * Scale;
            Assert.IsTrue(near > 0.5f && near < 10, $"faded near the side: {near}");
        }

        [TestMethod]
        public void APartOfTheMapTakenAsAStampPutsItBackWhenSet()
        {
            TiledBuffer<ushort>.Builder samples = Flat();
            new HeightBrush { Mode = HeightBrushMode.Raise, Radius = 6, Strength = 7 }.Apply(samples, Scale, 20, 20);
            new HeightBrush { Mode = HeightBrushMode.Lower, Radius = 3, Strength = 4 }.Apply(samples, Scale, 24, 18);
            Heightmap heights = new Heightmap { Scale = Scale, Samples = samples.ToBuffer() };

            HeightStampCapture capture = HeightStampCapture.Of(heights, 12, 12, 28, 28);
            Assert.AreEqual((17, 17), (capture.Width, capture.Height));
            Assert.AreEqual(17, capture.Highest, 0.05);
            Assert.IsTrue(capture.Lowest < 10, "the dip");

            // saved and read again as a brush picture, pressed on flat ground elsewhere
            string file = Path.Combine(ProjectFolders.New("stamp"), "hill.png");
            capture.Save(file);
            BrushShape shape = BrushShape.Read(file);
            TiledBuffer<ushort>.Builder elsewhere = Flat();
            new HeightStamp
            {
                Shape = shape, Mode = HeightStampMode.Set, Radius = 17 / 2f, Height = capture.Highest - capture.Lowest, Base = capture.Lowest, Edge = 0,
            }.Apply(elsewhere, Scale, 44, 40);
            for (int dz = -8; dz <= 8; dz++)
            {
                for (int dx = -8; dx <= 8; dx++)
                {
                    Assert.AreEqual(samples[20 + dx, 20 + dz] * Scale, elsewhere[44 + dx, 40 + dz] * Scale, 0.02, $"at {dx}, {dz}");
                }
            }
            Assert.AreEqual(10, elsewhere[44 + 10, 40] * Scale, 0.01, "nothing outside it");
        }

        [TestMethod]
        public void RefusesATinyPart()
        {
            Heightmap heights = new Heightmap { Scale = Scale, Samples = Flat().ToBuffer() };
            Assert.ThrowsException<ArgumentException>(() => HeightStampCapture.Of(heights, 5, 5, 5, 9));
        }
    }
}
