using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class MapScenarioParserTest
    {
        private static MapScenario ParseAsset(string map) => MapScenarioParser.Parse(File.ReadAllText(map + "_scenario.lua"));

        [TestMethod]
        public void ReadsASkirmishMap()
        {
            MapScenario scenario = ParseAsset(ScmapParserTest.ThetaPassage);

            Assert.AreEqual("Theta Passage - FAF version", scenario.Name);
            Assert.AreEqual("skirmish", scenario.Type);
            Assert.AreEqual(true, scenario.Starts);
            Assert.AreEqual((256, 256), scenario.Size);
            Assert.AreEqual(1, scenario.MapVersion);
            Assert.AreEqual(70, scenario.NoRushRadius);
            Assert.AreEqual((4271.7, 1976.0), scenario.Reclaim);
            Assert.AreEqual("/maps/theta_passage_-_faf_version.v0001/theta_passage_-_faf_version.scmap", scenario.Map);
            Assert.AreEqual("/maps/theta_passage_-_faf_version.v0001/theta_passage_-_faf_version_save.lua", scenario.Save);

            MapConfiguration standard = scenario.Configurations.Single();
            Assert.AreEqual("standard", standard.Name);
            Assert.AreEqual("FFA", standard.Teams.Single().Name);
            CollectionAssert.AreEqual(new[] { "ARMY_1", "ARMY_2" }, scenario.PlayerArmies.ToList());
            CollectionAssert.AreEqual(new[] { "ARMY_17", "NEUTRAL_CIVILIAN" }, standard.ExtraArmies.ToList());
        }

        [TestMethod]
        public void ReadsATableWithoutSpaces()
        {
            // written by a tool, not by the map editor: unquoted keys and no STRING( ... )
            MapScenario scenario = ParseAsset(ScmapParserTest.HardFfa);

            Assert.AreEqual("HardFFA", scenario.Name);
            Assert.AreEqual(8, scenario.PlayerArmies.Count);
            CollectionAssert.AreEqual(new[] { "ARMY_9", "NEUTRAL_CIVILIAN" }, scenario.Configurations[0].ExtraArmies.ToList());
            Assert.IsNull(scenario.Reclaim);
        }

        [TestMethod]
        public void ReadsACoopMission()
        {
            MapScenario scenario = ParseAsset(ScmapParserTest.CoopR01);

            Assert.AreEqual("Cybran Mission 1 - Liberation", scenario.Name);
            Assert.AreEqual("campaign_coop", scenario.Type);
            Assert.AreEqual((512, 512), scenario.Size);
            Assert.AreEqual(20, scenario.MapVersion);
            Assert.AreEqual(10, scenario.PlayerArmies.Count);
            Assert.AreEqual(0, scenario.Configurations[0].ExtraArmies.Count);
        }

        [TestMethod]
        public void MatchesItsSaveFile()
        {
            // every army a player can take has a start position in the save file
            MapScenario scenario = ParseAsset(ScmapParserTest.ThetaPassage);
            MapSave save = MapSaveParser.Parse(File.ReadAllText(ScmapParserTest.ThetaPassage + "_save.lua"));

            foreach (string army in scenario.PlayerArmies)
            {
                Assert.IsNotNull(save.GetMarker(army), army);
            }
        }

        [TestMethod]
        public void RejectsAFileWithoutScenarioInfo()
        {
            Assert.ThrowsException<FormatException>(() => MapScenarioParser.Parse("version = 3"));
        }
    }
}
