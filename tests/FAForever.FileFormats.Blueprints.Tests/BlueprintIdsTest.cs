using FAForever.FileFormats.Blueprints;

namespace FAForever.FileFormats.Blueprints.Tests
{
    [TestClass]
    public class BlueprintIdsTest
    {
        [TestMethod]
        [DataRow("ueb0101", Faction.Uef, BlueprintLayer.Structure, 1)]
        [DataRow("ual0303", Faction.Aeon, BlueprintLayer.Land, 3)]
        [DataRow("urs0201", Faction.Cybran, BlueprintLayer.Naval, 2)]
        [DataRow("xsa0402", Faction.Seraphim, BlueprintLayer.Air, 4)]
        [DataRow("XSB0101", Faction.Seraphim, BlueprintLayer.Structure, 1)]
        public void DecodesTheIdConvention(string blueprintId, Faction expectedFaction, BlueprintLayer expectedLayer, int expectedTech)
        {
            Assert.AreEqual(expectedFaction, BlueprintIds.GetFaction(blueprintId));
            Assert.AreEqual(expectedLayer, BlueprintIds.GetLayer(blueprintId));
            Assert.AreEqual(expectedTech, BlueprintIds.GetTechLevel(blueprintId));
        }

        [TestMethod]
        public void UnknownIdsDecodeToNull()
        {
            Assert.IsNull(BlueprintIds.GetFaction(null));
            Assert.IsNull(BlueprintIds.GetFaction("q"));
            Assert.IsNull(BlueprintIds.GetFaction("uxl0001"));
            Assert.IsNull(BlueprintIds.GetLayer("ue"));
            Assert.IsNull(BlueprintIds.GetTechLevel("ueb0001"));
            Assert.IsNull(BlueprintIds.GetTechLevel("mod_unit"));
        }
    }
}
