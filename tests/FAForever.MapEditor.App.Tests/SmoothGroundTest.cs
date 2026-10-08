using System.Numerics;
using FAForever.MapEditor.App.Rendering.Gpu;

namespace FAForever.MapEditor.App.Tests
{
    [TestClass]
    public sealed class SmoothGroundTest
    {
        private const int Size = 257;

        /// <summary>
        /// A plain at 20 with a needle of 200 every 16 ogrids.
        /// </summary>
        private static float Spiky(float x, float z) => (int)x % 16 == 0 && (int)z % 16 == 0 ? 200 : 20;

        private static SmoothGround SpikyGround()
        {
            float[] heights = new float[Size * Size];
            for (int z = 0; z < Size; z++)
            {
                for (int x = 0; x < Size; x++)
                {
                    heights[z * Size + x] = Spiky(x, z);
                }
            }
            return SmoothGround.FromHeights(Size, Size, heights);
        }

        [TestMethod]
        public void KeepsAFlatGroundAsItIs()
        {
            SmoothGround ground = SmoothGround.FromHeights(Size, Size, Enumerable.Repeat(37f, Size * Size).ToArray());

            Assert.AreEqual(37, ground.Height(100.3f, 7.9f, 8), 0.001);
            Assert.AreEqual(37, ground.Height(-50, 400, 900), 0.001, "outside the map: its edge");
        }

        [TestMethod]
        public void SpreadsSpikesOut()
        {
            SmoothGround ground = SpikyGround();

            // the needles add (200 - 20) / 256 to the average of every 16 by 16 square
            float expected = 20 + 180f / 256;
            for (float x = 40; x < 200; x += 3.7f)
            {
                Assert.AreEqual(expected, ground.Height(x, 100, 64), 0.5, $"at {x}");
            }
        }

        [TestMethod]
        public void TheCameraGlidesOverSpikesInsteadOfJumping()
        {
            SmoothGround ground = SpikyGround();
            EditorCamera camera = new EditorCamera(ground.Height, Spiky);
            camera.Reset(Size - 1, Size - 1);
            camera.Look(distance: 120, pitch: 60);

            List<float> heights = [];
            for (int i = 0; i < 40; i++)
            {
                camera.Pan(new Vector2(3, 0), 800);
                for (int frame = 0; frame < 10; frame++)
                {
                    camera.Update(1 / 60f);
                }
                heights.Add(camera.Eye.Y);
            }

            Assert.IsTrue(heights.Max() - heights.Min() < 2, $"the eye went from {heights.Min()} to {heights.Max()}");
        }

        [TestMethod]
        public void TheEyeStaysAboveTheTerrainItself()
        {
            // a tower of 300 where the eye would be (28 ogrids south of the pivot), which the blurred ground hardly knows of
            EditorCamera camera = new EditorCamera((_, _, _) => 20, (x, z) => Math.Abs(x - 100) < 5 && Math.Abs(z - 128) < 5 ? 300 : 20);
            camera.Reset(256, 256);

            camera.Look(x: 100, z: 100, distance: 40, yaw: 0, pitch: 45);

            Assert.IsTrue(camera.Eye.Y >= 302, $"the eye is at {camera.Eye}");
        }
    }
}
