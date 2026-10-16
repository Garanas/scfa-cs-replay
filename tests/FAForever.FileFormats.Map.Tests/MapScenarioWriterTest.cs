using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class MapScenarioWriterTest
    {
        private static MapScenario ParseAsset(string map) => MapScenarioParser.Parse(File.ReadAllText(map + "_scenario.lua"));

        [TestMethod]
        [DataRow(ScmapParserTest.ThetaPassage)]
        [DataRow(ScmapParserTest.HardFfa)]
        [DataRow(ScmapParserTest.CoopR01)]
        public void WritesAScenarioThatParsesToTheSameContent(string map)
        {
            MapScenario scenario = ParseAsset(map);

            string written = MapScenarioWriter.Write(scenario);
            MapScenario reparsed = MapScenarioParser.Parse(written);

            Assert.AreEqual(MapDump.Scenario(scenario), MapDump.Scenario(reparsed));
            Assert.AreEqual(written, MapScenarioWriter.Write(reparsed), "writing again changes nothing");
        }

        [TestMethod]
        public void KeepsTheKeysWithoutAField()
        {
            MapScenario reparsed = MapScenarioParser.Parse(MapScenarioWriter.Write(ParseAsset(ScmapParserTest.HardFfa)));

            Assert.IsTrue(reparsed.Raw.TryGetNumberValue("norushoffsetX_ARMY_1", out double? offset));
            Assert.AreEqual(-35, offset);
        }

        [TestMethod]
        public void WritesTheScenarioAsTheMapEditorsDo()
        {
            string written = MapScenarioWriter.Write(ParseAsset(ScmapParserTest.ThetaPassage));

            Assert.IsTrue(written.StartsWith("version = 3"));
            StringAssert.Contains(written, """
                    Configurations = {
                        standard = {
                            teams = {
                                {
                                    name = 'FFA',
                                    armies = { 'ARMY_1', 'ARMY_2' },
                                },
                            },
                            customprops = {
                                ExtraArmies = 'ARMY_17 NEUTRAL_CIVILIAN',
                            },
                        },
                    },
                """.ReplaceLineEndings("\n"));
            StringAssert.Contains(written, "size = { 256, 256 },");
            StringAssert.Contains(written, "map_version = 1,");
        }

        [TestMethod]
        public void WritesAChangedVersion()
        {
            MapScenario scenario = ParseAsset(ScmapParserTest.CoopR01) with
            {
                MapVersion = 21,
                Map = "/maps/SCCA_Coop_R01.v0021/SCCA_Coop_R01.scmap",
            };

            MapScenario reparsed = MapScenarioParser.Parse(MapScenarioWriter.Write(scenario));

            Assert.AreEqual(21, reparsed.MapVersion);
            Assert.AreEqual("/maps/SCCA_Coop_R01.v0021/SCCA_Coop_R01.scmap", reparsed.Map);
            StringAssert.StartsWith(reparsed.Description, "All of the Symbionts on Theban 2 have been enslaved by the UEF's loyalty programming.");
        }
    }
}
