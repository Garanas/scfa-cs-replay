using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class HeightBrushTest
    {
        private const float Scale = 1f / 128;

        /// <summary>
        /// A flat heightmap of 64 by 64 ogrids at 10 game units.
        /// </summary>
        private static TiledBuffer<ushort>.Builder Flat() => TiledBuffer<ushort>.Create(65, 65, 1280).ToBuilder();

        [TestMethod]
        public void RaisesMostAtTheCentreAndNothingPastTheRadius()
        {
            TiledBuffer<ushort>.Builder samples = Flat();

            new HeightBrush { Mode = HeightBrushMode.Raise, Radius = 8, Strength = 2 }.Apply(samples, Scale, 32, 32);

            Assert.AreEqual(12f, samples[32, 32] * Scale);
            Assert.IsTrue(samples[36, 32] > 1280 && samples[36, 32] < samples[34, 32]);
            Assert.AreEqual(1280, samples[40, 32]);
            Assert.AreEqual(1280, samples[0, 0]);
        }

        [TestMethod]
        public void LowersNoFurtherThanZero()
        {
            TiledBuffer<ushort>.Builder samples = Flat();

            new HeightBrush { Mode = HeightBrushMode.Lower, Radius = 4, Strength = 100 }.Apply(samples, Scale, 10, 10);

            Assert.AreEqual(0, samples[10, 10]);
        }

        [TestMethod]
        public void FlattensTowardsItsTarget()
        {
            TiledBuffer<ushort>.Builder samples = Flat();

            new HeightBrush { Mode = HeightBrushMode.Flatten, Radius = 4, Strength = 1, Target = 20 }.Apply(samples, Scale, 10, 10);

            Assert.AreEqual(20f, samples[10, 10] * Scale);
        }

        [TestMethod]
        public void SmoothsASpike()
        {
            TiledBuffer<ushort>.Builder samples = Flat();
            samples[32, 32] = 1280 + 900;

            new HeightBrush { Mode = HeightBrushMode.Smooth, Radius = 4, Strength = 1 }.Apply(samples, Scale, 32, 32);

            Assert.AreEqual(1280 + 100, samples[32, 32], "the mean of the spike and its eight flat neighbours");
            Assert.IsTrue(samples[33, 32] > 1280 && samples[33, 32] < 1280 + 100, "a neighbour rises towards the spike, less than the centre");
        }

        [TestMethod]
        public void StaysInsideTheHeightmapAtItsEdge()
        {
            TiledBuffer<ushort>.Builder samples = Flat();

            new HeightBrush { Mode = HeightBrushMode.Raise, Radius = 8, Strength = 1 }.Apply(samples, Scale, 0, 64);
            new HeightBrush { Mode = HeightBrushMode.Smooth, Radius = 8, Strength = 1 }.Apply(samples, Scale, 64, 0);

            Assert.AreEqual(11f, samples[0, 64] * Scale);
        }
    }
}
