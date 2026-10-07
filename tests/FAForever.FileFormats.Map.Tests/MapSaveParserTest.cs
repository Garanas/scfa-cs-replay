using System.Numerics;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class MapSaveParserTest
    {
        private static MapSave ParseAsset(string map) => MapSaveParser.Parse(File.ReadAllText(map + "_save.lua"));

        [TestMethod]
        [DataRow(ScmapParserTest.ThetaPassage, 609, 24, 2, 1, 0, 104)]
        [DataRow(ScmapParserTest.HardFfa, 149, 48, 16, 0, 0, 0)]
        [DataRow(ScmapParserTest.CoopR01, 204, 36, 0, 18, 23, 1386)]
        public void ParsesRealSaveFiles(string map, int markers, int mass, int hydrocarbons, int areas, int chains, int units)
        {
            MapSave save = ParseAsset(map);

            Assert.AreEqual(markers, save.Markers.Count);
            Assert.AreEqual(mass, save.GetMarkers("Mass").Count());
            Assert.AreEqual(hydrocarbons, save.GetMarkers("Hydrocarbon").Count());
            Assert.AreEqual(areas, save.Areas.Count);
            Assert.AreEqual(chains, save.Chains.Count);
            Assert.AreEqual(units, save.Armies.Sum(army => army.Units.AllUnits.Count()));
        }

        [TestMethod]
        public void ReadsMarkers()
        {
            MapSave save = ParseAsset(ScmapParserTest.HardFfa);

            MapMarker? start = save.GetMarker("ARMY_1");
            Assert.IsNotNull(start);
            Assert.AreEqual("Blank Marker", start.Type);
            Assert.AreEqual(new Vector3(129.5f, 2.63f, 124.5f), start.Position);

            MapMarker mass = save.GetMarkers("Mass").First();
            Assert.IsTrue(mass.Raw.TryGetNumberValue("amount", out double? amount));
            Assert.AreEqual(100, amount);
            Assert.IsNull(save.GetMarker("ARMY_99"));
        }

        [TestMethod]
        public void ReadsArmiesAndTheirUnits()
        {
            MapSave save = ParseAsset(ScmapParserTest.ThetaPassage);

            CollectionAssert.AreEqual(new[] { "ARMY_1", "ARMY_2", "ARMY_17", "NEUTRAL_CIVILIAN" }, save.Armies.Select(army => army.Name).ToList());

            MapArmy civilians = save.Armies.Single(army => army.Name == "NEUTRAL_CIVILIAN");
            Assert.AreEqual("Units", civilians.Units.Name);
            CollectionAssert.AreEqual(new[] { "INITIAL", "WRECKAGE" }, civilians.Units.Groups.Select(group => group.Name).ToList());
            Assert.AreEqual(0, civilians.Units.Units.Count, "the root group holds only groups");

            MapUnit unit = civilians.Units.AllUnits.First();
            Assert.AreEqual("UNIT_445", unit.Name);
            Assert.AreEqual("urb5101", unit.BlueprintId);
            Assert.AreEqual(new Vector3(129.5f, 3.324422f, 100.5f), unit.Position);
        }

        [TestMethod]
        public void ReadsNestedGroupsAlliancesAreasAndChains()
        {
            MapSave save = ParseAsset(ScmapParserTest.CoopR01);

            MapArmy symbiont = save.Armies.Single(army => army.Name == "Symbiont");
            Assert.AreEqual("Ally", symbiont.Alliances["Player1"]);
            MapUnitGroup m3 = symbiont.Units.Groups.Single(group => group.Name == "M3Symbionts");
            CollectionAssert.IsSubsetOf(new[] { "M3SymbiontBuildings", "M3SymbiontStructures", "M3SymbiontUnits" }, m3.Groups.Select(group => group.Name).ToList());

            Assert.AreEqual(new MapArea("CameraArea_2", 100.5, 189.5, 149.5, 230.5), save.Areas[0]);

            MapChain patrol = save.Chains[0];
            Assert.AreEqual("AeonBaseAirPatrol_Chain", patrol.Name);
            CollectionAssert.AreEqual(new[] { "M3AeonBaseAirPatrol2", "M3AeonBaseAirPatrol3", "M3AeonBaseAirPatrol1" }, patrol.Markers.ToList());
        }

        [TestMethod]
        public void ReadsTheCategoriesOfPlatoonBuilders()
        {
            // co-op missions name categories of the game in their build conditions
            MapSave save = MapSaveParser.Parse("""
                Scenario = {
                    Armies = {
                        ['Aeon'] = {
                            PlatoonBuilders = {
                                Builders = {
                                    ['Defense'] = { BuildConditions = { { 'default_brain', 7, categories.ual0105, false } } },
                                },
                            },
                        },
                    },
                }
                """);

            LuaData.Table builder = (LuaData.Table)((LuaData.Table)((LuaData.Table)save.Armies[0].Raw.Value["PlatoonBuilders"]).Value["Builders"]).Value["Defense"];
            LuaData.Table condition = (LuaData.Table)((LuaData.Table)builder.Value["BuildConditions"]).Value["1"];
            Assert.AreEqual(new LuaData.String("ual0105"), condition.Value["3"]);
        }

        [TestMethod]
        public void RejectsAFileWithoutAScenario()
        {
            Assert.ThrowsException<FormatException>(() => MapSaveParser.Parse("Other = {}"));
        }
    }
}
