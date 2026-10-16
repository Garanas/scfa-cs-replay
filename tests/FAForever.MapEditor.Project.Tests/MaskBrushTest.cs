using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class MaskBrushTest
    {
        [TestMethod]
        public void PaintsMostAtTheCentreAndNothingPastTheRadius()
        {
            TiledBuffer<byte>.Builder mask = TiledBuffer<byte>.Create(256, 256).ToBuilder();

            new MaskBrush { Radius = 8, Strength = 1 }.Apply(mask, 256, 256, 100, 100);

            Assert.IsTrue(mask[99, 99] > 240, "full at the centre");
            Assert.IsTrue(mask[103, 99] > 0 && mask[103, 99] < mask[99, 99]);
            Assert.AreEqual(0, mask[110, 99]);
        }

        [TestMethod]
        public void PaintsAMaskOfHalfTheMapsSizeWhereTheMapSays()
        {
            TiledBuffer<byte>.Builder mask = TiledBuffer<byte>.Create(128, 128).ToBuilder();

            new MaskBrush { Radius = 8, Strength = 1 }.Apply(mask, 256, 256, 200, 40);

            Assert.IsTrue(mask[99, 19] > 200, "ogrid (200, 40) is pixel (100, 20) of the mask");
            Assert.AreEqual(0, mask[50, 50]);
        }

        [TestMethod]
        public void ErasesAndBuildsUpBitByBit()
        {
            TiledBuffer<byte>.Builder mask = TiledBuffer<byte>.Create(64, 64, 255).ToBuilder();
            MaskBrush erase = new MaskBrush { Radius = 4, Strength = 0.5f, Erase = true };

            erase.Apply(mask, 64, 64, 32, 32);
            byte once = mask[31, 31];
            erase.Apply(mask, 64, 64, 32, 32);

            Assert.IsTrue(once < 255 && once > 64, "half way in one dab");
            Assert.IsTrue(mask[31, 31] < once, "further in the next");
        }
    }
}
