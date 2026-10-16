namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class MapFolderNameTest
    {
        [TestMethod]
        [DataRow("theta_passage.v0004", "theta_passage", 4)]
        [DataRow("SCCA_Coop_R01.v0020", "SCCA_Coop_R01", 20)]
        [DataRow("8 - badlands_v4.v0001", "8 - badlands_v4", 1)]
        [DataRow("my_map", "my_map", null)]
        public void SplitsTheVersionOff(string folder, string name, int? version)
        {
            MapFolderName parsed = MapFolderName.Parse(folder);

            Assert.AreEqual(new MapFolderName(name, version), parsed);
            Assert.AreEqual(folder, parsed.ToString());
        }

        [TestMethod]
        public void PointsPathsAtItsFolder()
        {
            MapFolderName folder = new MapFolderName("theta_passage", 5);

            Assert.AreEqual("'/maps/theta_passage.v0005/theta_passage_save.lua'", folder.RewritePaths("'/maps/theta_passage.v0004/theta_passage_save.lua'"));
            Assert.AreEqual("/maps/theta_passage.v0005/env/rock.dds", folder.RewritePaths("/maps/Theta_Passage.V0001/env/rock.dds"));
            Assert.AreEqual("/maps/theta_passage.v0005/x.lua", folder.RewritePaths("/maps/theta_passage/x.lua"), "a path without a version");
            Assert.AreEqual("/maps/theta_passage_2.v0001/x.lua", folder.RewritePaths("/maps/theta_passage_2.v0001/x.lua"), "another map");
            Assert.AreEqual("/env/common/props/rock.bp", folder.RewritePaths("/env/common/props/rock.bp"));
        }

        [TestMethod]
        public void LeavesTheVersionOutWhenThereIsNone()
        {
            Assert.AreEqual("/maps/my_map/my_map.scmap", new MapFolderName("my_map", null).RewritePaths("/maps/my_map.v0003/my_map.scmap"));
        }
    }
}
