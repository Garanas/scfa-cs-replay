using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class StrataEditsTest
    {
        private static MapDocument HardFfa() => MapImporter.ImportFolder(ProjectFolders.HardFfa);

        [TestMethod]
        public void MovesAStratumWithItsMask()
        {
            MapDocument document = HardFfa();
            TerrainLayer second = document.Terrain.Layers[2];
            TiledBuffer<byte> secondMask = document.Terrain.StratumMasks.Layers[1];
            TerrainLayer first = document.Terrain.Layers[1];

            MapDocument moved = document.MoveStratum(2, 4);

            Assert.AreEqual(second, moved.Terrain.Layers[4]);
            Assert.AreSame(secondMask, moved.Terrain.StratumMasks.Layers[3], "the mask moves with its layer");
            Assert.AreEqual(document.Terrain.Layers[3], moved.Terrain.Layers[2], "the ones between move up");
            Assert.AreEqual(first, moved.Terrain.Layers[1]);
            Assert.AreEqual(document.Terrain.Layers[0], moved.Terrain.Layers[0]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => document.MoveStratum(0, 2), "the lower layer is no stratum");
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => document.MoveStratum(2, 9), "nor the upper one");
        }

        [TestMethod]
        public void AddsAndRemovesStrataWithinTheVersionsCount()
        {
            MapDocument document = HardFfa();
            Assert.AreEqual(4, document.Terrain.Layers.Count(layer => layer.IsUsed()) - 2, "HardFFA uses four strata besides the lower and upper layers");

            MapDocument added = document.AddStratum("/env/tundra/layers/tund_rock_albedo.dds", "/env/tundra/layers/tund_rock_normal.dds", 6, out int layer);
            Assert.AreEqual(5, layer, "the first unused stratum");
            Assert.AreEqual(6, added.Terrain.Layers[5].AlbedoScale);
            Assert.AreEqual(0, added.Terrain.StratumMasks.Layers[4][10, 10]);

            MapDocument removed = added.RemoveStratum(1);
            Assert.AreEqual(document.Terrain.Layers[2], removed.Terrain.Layers[1], "the strata after it move down");
            Assert.AreSame(added.Terrain.StratumMasks.Layers[1], removed.Terrain.StratumMasks.Layers[0]);
            Assert.IsFalse(removed.Terrain.Layers[8].IsUsed());
            Assert.AreEqual(10, removed.Terrain.Layers.Length);
            Assert.AreEqual(8, removed.Terrain.StratumMasks.Layers.Length);

            MapDocument full = document;
            for (int i = 0; i < 4; i++)
            {
                full = full.AddStratum("/a.dds", "", 4, out _);
            }
            Assert.ThrowsException<InvalidOperationException>(() => full.AddStratum("/b.dds", "", 4, out _), "eight strata in version 56");
        }

        [TestMethod]
        public void KeepsFourStrataInVersion53()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.CoopR01);
            Assert.AreEqual(6, document.Terrain.Layers.Length);
            Assert.IsTrue(document.Terrain.IsStratum(4));
            Assert.IsFalse(document.Terrain.IsStratum(5));
            Assert.ThrowsException<InvalidOperationException>(() => document.AddStratum("/b.dds", "", 4, out _));
            Assert.AreEqual(6, document.RemoveStratum(2).Terrain.Layers.Length);
        }

        [TestMethod]
        public void SavesLightingAndWaterAsPresets()
        {
            MapDocument document = HardFfa();
            MapDocument other = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);

            Preset lighting = AtmospherePresets.OfLighting("Geothermal", document.Lighting);
            Assert.AreEqual(document.Lighting, AtmospherePresets.ReadLighting(lighting));

            Preset water = AtmospherePresets.OfWater("Geothermal water", document.Water);
            Water applied = AtmospherePresets.ApplyWater(water, other.Water);
            Assert.AreEqual(document.Water.SurfaceColor, applied.SurfaceColor);
            Assert.AreEqual(document.Water.Cubemap, applied.Cubemap);
            Assert.AreEqual(document.Water.WaveTextures.Length, applied.WaveTextures.Length);
            Assert.AreEqual(other.Water.Elevation, applied.Elevation, "the elevations are the map's");
            Assert.AreEqual(other.Water.HasWater, applied.HasWater);
            Assert.AreSame(other.Water.FoamMask, applied.FoamMask);
        }
    }
}
