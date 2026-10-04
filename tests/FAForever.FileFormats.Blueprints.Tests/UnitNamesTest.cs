using FAForever.FileFormats.Blueprints;

namespace FAForever.FileFormats.Blueprints.Tests
{
    [TestClass]
    public class UnitNamesTest
    {
        [TestMethod]
        [DataRow("uel0001", "Armored Command Unit")]
        [DataRow("ueb0101", "Land Factory")]
        [DataRow("XSB0101", "Land Factory")]
        public void ResolvesKnownBlueprints(string blueprintId, string expectedName)
        {
            Assert.AreEqual(expectedName, UnitNames.GetOrNull(blueprintId));
        }

        [TestMethod]
        public void UnknownBlueprintsResolveToNull()
        {
            Assert.IsNull(UnitNames.GetOrNull("mod_unit_without_name"));
            Assert.IsNull(UnitNames.GetOrNull(null));
            Assert.IsNull(UnitNames.GetOrNull(""));
        }
    }
}
