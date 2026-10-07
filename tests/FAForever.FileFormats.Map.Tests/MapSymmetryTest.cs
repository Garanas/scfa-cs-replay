using System.Numerics;
using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class MapSymmetryTest
    {
        private static Vector3 P(float x, float z) => new Vector3(x, 0, z);

        [TestMethod]
        public void FindsTheSymmetryOfThetaPassage()
        {
            MapSave save = MapSaveParser.Parse(File.ReadAllText(ScmapParserTest.ThetaPassage + "_save.lua"));
            List<Vector3> positions = save.Markers
                .Where(marker => marker.Type is "Mass" or "Hydrocarbon" || marker.Name.StartsWith("ARMY_", StringComparison.Ordinal))
                .Where(marker => marker.Name != "ARMY_17")
                .Select(marker => marker.Position)
                .ToList();

            Assert.IsNotNull(MapSymmetry.Find(256, 256, positions));
        }

        [TestMethod]
        public void TellsTheKindsApart()
        {
            Assert.AreEqual(MapSymmetryKind.Rotational, MapSymmetry.Find(100, 100, [P(10, 20), P(90, 80)]));
            Assert.AreEqual(MapSymmetryKind.MirroredLeftRight, MapSymmetry.Find(100, 100, [P(10, 20), P(90, 20)]));
            Assert.AreEqual(MapSymmetryKind.MirroredTopBottom, MapSymmetry.Find(100, 100, [P(10, 20), P(10, 80)]));
            Assert.AreEqual(MapSymmetryKind.MirroredDiagonal, MapSymmetry.Find(100, 100, [P(10, 30), P(30, 10)]));
            Assert.IsNull(MapSymmetry.Find(100, 100, [P(10, 20), P(70, 40)]));
        }

        [TestMethod]
        public void AllowsSmallDifferences()
        {
            Assert.AreEqual(MapSymmetryKind.Rotational, MapSymmetry.Find(100, 100, [P(10, 20), P(91, 79)]));
            Assert.IsNull(MapSymmetry.Find(100, 100, [P(10, 20), P(95, 75)]));
        }
    }
}
