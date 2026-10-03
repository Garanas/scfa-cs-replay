using FAForever.Replay;

namespace FAForever.Replay.Test
{
    [TestClass]
    public class BlueprintsTest
    {
        [TestMethod]
        [DataRow("ueb0101", Faction.Uef, BlueprintLayer.Structure, 1)]
        [DataRow("ual0303", Faction.Aeon, BlueprintLayer.Land, 3)]
        [DataRow("urs0201", Faction.Cybran, BlueprintLayer.Naval, 2)]
        [DataRow("xsa0402", Faction.Seraphim, BlueprintLayer.Air, 4)]
        [DataRow("XSB0101", Faction.Seraphim, BlueprintLayer.Structure, 1)]
        public void DecodesTheIdConvention(string blueprintId, Faction expectedFaction, BlueprintLayer expectedLayer, int expectedTech)
        {
            Assert.AreEqual(expectedFaction, Blueprints.GetFaction(blueprintId));
            Assert.AreEqual(expectedLayer, Blueprints.GetLayer(blueprintId));
            Assert.AreEqual(expectedTech, Blueprints.GetTechLevel(blueprintId));
        }

        [TestMethod]
        public void UnknownIdsDecodeToNull()
        {
            Assert.IsNull(Blueprints.GetFaction(null));
            Assert.IsNull(Blueprints.GetFaction("q"));
            Assert.IsNull(Blueprints.GetFaction("uxl0001"));
            Assert.IsNull(Blueprints.GetLayer("ue"));
            Assert.IsNull(Blueprints.GetTechLevel("ueb0001"));
            Assert.IsNull(Blueprints.GetTechLevel("mod_unit"));
        }
    }
}
