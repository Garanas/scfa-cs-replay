namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// What a new map's water reflects (user story M-2): the sky cube map of its biome.
    /// </summary>
    [TestClass]
    public class NewMapWaterTest
    {
        [TestMethod]
        [DataRow("evergreen", "/textures/environment/skycube_evergreen01a.dds")]
        [DataRow("desert", "/textures/environment/skycube_desert01a.dds")]
        [DataRow("tundra", "/textures/environment/skycube_tundra02a.dds")]
        [DataRow("lava", "/textures/environment/skycube_lava01a.dds")]
        [DataRow("tropical", "/textures/environment/skycube_tropical01a.dds")]
        [DataRow("red-barrens", "/textures/environment/skycube_redrocks05a.dds")]
        public void TheWaterReflectsTheSkyOfItsBiome(string biome, string cubemap)
        {
            Assert.AreEqual(cubemap, NewMap.Create(new NewMapSettings { Biome = biome }).Water.Cubemap);
        }
    }
}
