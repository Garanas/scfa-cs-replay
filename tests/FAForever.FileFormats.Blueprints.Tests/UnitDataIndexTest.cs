using FAForever.FileFormats.Blueprints;

namespace FAForever.FileFormats.Blueprints.Tests
{
    [TestClass]
    public class UnitDataIndexTest
    {
        private static readonly UnitDataIndex Index = UnitDataIndex.Empty
            .With(3801, "3801.json")
            .With(3810, "3810.json")
            .With(3811, "3810.json")
            .With(3839, "3839.json");

        [TestMethod]
        [DataRow(3839, 3839)]
        [DataRow(3811, 3811)]
        [DataRow(3815, 3811)] // no data of its own: the newest older version
        [DataRow(3850, 3839)] // newer than the data: the latest
        [DataRow(3700, 3801)] // older than the data: the oldest
        public void ResolvesAGameVersion(int gameVersion, int expected)
        {
            Assert.AreEqual(expected, Index.Resolve(gameVersion));
        }

        [TestMethod]
        public void WithoutAVersionUsesTheLatest()
        {
            Assert.AreEqual(3839, Index.Latest);
            Assert.AreEqual(3839, Index.Resolve(null));
            Assert.IsNull(UnitDataIndex.Empty.Resolve(3839));
        }

        [TestMethod]
        public void RoundTripsThroughJson()
        {
            string json = Index.Serialize();
            UnitDataIndex read = UnitDataIndex.Deserialize(json);

            // one version per line, newest first
            StringAssert.StartsWith(json, "{\"versions\":{\n  \"3839\": \"3839.json\",\n");
            CollectionAssert.AreEqual(new[] { 3839, 3811, 3810, 3801 }, read.Newest.ToArray());
            Assert.AreEqual("3810.json", read.Versions[3811]);
        }
    }
}
