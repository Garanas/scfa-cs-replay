using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class TiledBufferTest
    {
        private static ushort[] Counting(int count) => [.. Enumerable.Range(0, count).Select(i => (ushort)i)];

        [TestMethod]
        [DataRow(1, 1)]
        [DataRow(64, 64)]
        [DataRow(257, 257)]
        [DataRow(130, 65)]
        public void HoldsTheValuesItIsGiven(int width, int height)
        {
            ushort[] values = Counting(width * height);

            TiledBuffer<ushort> buffer = TiledBuffer<ushort>.FromArray(width, height, values);

            CollectionAssert.AreEqual(values, buffer.ToArray());
            Assert.AreEqual(values[(height - 1) * width + width - 1], buffer[width - 1, height - 1]);
            Assert.AreEqual((width + 63) / 64, buffer.TilesX);
        }

        [TestMethod]
        public void CopiesOnlyTheTilesItChanges()
        {
            TiledBuffer<ushort> before = TiledBuffer<ushort>.FromArray(257, 257, Counting(257 * 257));
            TiledBuffer<ushort>.Builder builder = before.ToBuilder();

            builder[100, 10] = 7;
            builder[101, 11] = 8;
            TiledBuffer<ushort> after = builder.ToBuffer();

            Assert.AreEqual(7, after[100, 10]);
            Assert.AreEqual(10 * 257 + 100, before[100, 10], "the earlier version keeps its values");
            CollectionAssert.AreEqual(new[] { (1, 0) }, after.ChangedTiles(before).ToList());
        }

        [TestMethod]
        public void KeepsABuiltBufferWhenTheBuilderGoesOn()
        {
            TiledBuffer<byte>.Builder builder = TiledBuffer<byte>.Create(100, 100).ToBuilder();
            builder[5, 5] = 1;
            TiledBuffer<byte> first = builder.ToBuffer();

            builder[5, 5] = 2;
            TiledBuffer<byte> second = builder.ToBuffer();

            Assert.AreEqual(1, first[5, 5]);
            Assert.AreEqual(2, second[5, 5]);
        }

        [TestMethod]
        public void BuildsTheSameBufferWhenNothingChanged()
        {
            TiledBuffer<byte> buffer = TiledBuffer<byte>.Create(100, 100);
            TiledBuffer<byte>.Builder builder = buffer.ToBuilder();

            Assert.AreSame(buffer, builder.ToBuffer());
            builder[0, 0] = 1;
            TiledBuffer<byte> changed = builder.ToBuffer();
            Assert.AreSame(changed, builder.ToBuffer());
        }

        [TestMethod]
        public void SharesOneTileWhileItIsFilledWithOneValue()
        {
            TiledBuffer<byte> buffer = TiledBuffer<byte>.Create(1024, 1024, 3);
            TiledBuffer<byte>.Builder builder = buffer.ToBuilder();

            builder[0, 0] = 9;
            TiledBuffer<byte> changed = builder.ToBuffer();

            Assert.AreEqual(9, changed[0, 0]);
            Assert.AreEqual(3, changed[64, 0], "the other tiles still share the filled one");
            Assert.AreEqual(3, buffer[0, 0]);
        }

        [TestMethod]
        public void RefusesAPlaceOutsideIt()
        {
            TiledBuffer<byte> buffer = TiledBuffer<byte>.Create(10, 10);

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => buffer[10, 0]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => buffer.ToBuilder()[0, -1] = 1);
            Assert.ThrowsException<ArgumentException>(() => TiledBuffer<byte>.FromArray(10, 10, new byte[99]));
        }
    }
}
