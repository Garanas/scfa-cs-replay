using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class TerrainTypesTest
    {
        [TestMethod]
        public void HoldsTheTypesOfTheGame()
        {
            Assert.AreEqual(59, TerrainTypes.All.Count);
            Assert.AreEqual(TerrainTypes.All.Count, TerrainTypes.All.Select(type => type.TypeCode).Distinct().Count());
            CollectionAssert.AreEqual(new[] { 9, 230 }, TerrainTypes.All.Where(type => type.Blocking).Select(type => type.TypeCode).ToList());
            Assert.AreEqual("Lava01", TerrainTypes.Get(230).Name);
        }

        [TestMethod]
        public void ReadsAnUnknownCodeAsDefault()
        {
            Assert.AreEqual("Default", TerrainTypes.Get(0).Name);
            Assert.IsFalse(TerrainTypes.Get(255).Blocking);
        }
    }
}
