using FAForever.FileFormats.Blueprints;

namespace FAForever.FileFormats.Blueprints.Tests
{
    [TestClass]
    public class UnitDataIndexTest
    {
        private static UnitDataIndex.Entry Entry(string file, int? reusedFrom = null, UnitDataIndex.UnitChanges? changes = null) =>
            new(file, 606, reusedFrom, changes, Commit: null, Released: null, Generated: new DateOnly(2026, 10, 5));

        private static readonly UnitDataIndex Index = UnitDataIndex.Empty
            .With(3801, Entry("3801.json"))
            .With(3810, Entry("3810.json", changes: new(3801, ["uel0201"], [], [])))
            .With(3811, Entry("3810.json", reusedFrom: 3810, changes: new(3810, [], [], [])))
            .With(3839, Entry("3839.json", changes: new(3811, ["uel0201", "url0107"], ["xsl0402"], ["opc1001"])));

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
        public void FindsThePreviousVersion()
        {
            Assert.AreEqual(3811, Index.Previous(3839));
            Assert.AreEqual(3811, Index.Previous(3815));
            Assert.IsNull(Index.Previous(3801));
        }

        [TestMethod]
        public void RoundTripsThroughJson()
        {
            UnitDataIndex index = Index.With(3838, new UnitDataIndex.Entry(
                "3810.json", 606, 3810, new(3811, [], [], []), "a1b2c3", new DateOnly(2026, 8, 25), new DateOnly(2026, 10, 5)));
            string json = index.Serialize();
            UnitDataIndex read = UnitDataIndex.Deserialize(json);

            // one version per line, newest first, nulls left out
            string[] lines = json.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(index.Versions.Count + 2, lines.Length);
            StringAssert.StartsWith(lines[1], "\"3839\":{\"file\":\"3839.json\"");
            StringAssert.Contains(lines[2], "\"released\":\"2026-08-25\"");
            Assert.IsFalse(lines[1].Contains("commit"), lines[1]);

            CollectionAssert.AreEqual(new[] { 3839, 3838, 3811, 3810, 3801 }, read.Newest.ToArray());
            UnitDataIndex.Entry entry = read.Versions[3838];
            Assert.AreEqual("3810.json", entry.File);
            Assert.AreEqual(3810, entry.ReusedFrom);
            Assert.AreEqual("a1b2c3", entry.Commit);
            Assert.AreEqual(new DateOnly(2026, 8, 25), entry.Released);
            Assert.IsTrue(entry.Changes!.IsEmpty);
            CollectionAssert.AreEqual(new[] { "xsl0402" }, read.Versions[3839].Changes!.Added.ToArray());
        }
    }
}
