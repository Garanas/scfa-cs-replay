using System.Numerics;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class ExtractorLayoutTest
    {
        private static MapMarker Marker(string name, string type, float x, float z) =>
            new MapMarker(name, type, new Vector3(x, 10, z), Vector3.Zero, new LuaData.Table(new Dictionary<string, LuaData>()));

        /// <summary>A flat dry map of 256 by 256 ogrids with these markers.</summary>
        private static (MapNavigation, IReadOnlyList<MapMarker> Starts, IReadOnlyList<MapMarker> Mass) Flat(params MapMarker[] markers)
        {
            ushort[] samples = new ushort[257 * 257];
            Array.Fill(samples, (ushort)1280);
            byte[] types = new byte[256 * 256];
            Array.Fill(types, (byte)1);
            MapNavigation navigation = NavGenerator.Generate(new ScmapHeightmap(256, 256, 1f / 128, samples), null, types, markers);
            return (navigation, markers.Where(m => m.Type == "Blank Marker").ToList(), markers.Where(m => m.Type == "Mass").ToList());
        }

        [TestMethod]
        public void SortsThetaPassageAsMapMakersDo()
        {
            Scmap scmap = ScmapParser.Parse(File.ReadAllBytes(ScmapParserTest.ThetaPassage + ".scmap"));
            MapSave save = MapSaveParser.Parse(File.ReadAllText(ScmapParserTest.ThetaPassage + "_save.lua"));
            MapScenario scenario = MapScenarioParser.Parse(File.ReadAllText(ScmapParserTest.ThetaPassage + "_scenario.lua"));
            MapNavigation navigation = NavGenerator.Generate(scmap, save, NavGenerator.FindPlayableArea(scenario, save));
            List<MapMarker> starts = scenario.PlayerArmies.Select(army => save.GetMarker(army)!).ToList();

            ExtractorDistances distances = ExtractorLayout.Measure(navigation, starts, save.GetMarkers("Mass").ToList());
            ExtractorLayout layout = ExtractorLayout.Classify(distances);

            Assert.AreEqual(NavLayer.Land, distances.Layer);
            Assert.AreEqual(new NearestOrigins(0, 0, 1, distances.NearestToStarts[0].SecondDistance), distances.NearestToStarts[0]);
            Assert.AreEqual(distances.NearestToStarts[0].SecondDistance, distances.NearestToStarts[1].SecondDistance, 0.01, "the route is as long both ways");
            Assert.IsTrue(distances.NearestToStarts[0].SecondDistance >= Vector2.Distance(new Vector2(starts[0].Position.X, starts[0].Position.Z), new Vector2(starts[1].Position.X, starts[1].Position.Z)));
            Assert.IsTrue(distances.Routed.All(routed => routed));
            foreach (IReadOnlyDictionary<ExtractorRole, double> counts in layout.PerPlayer)
            {
                Assert.AreEqual(4, counts[ExtractorRole.Safe]);
                Assert.AreEqual(0, counts[ExtractorRole.Expandable], "the spots of Theta Passage come alone or in pairs");
                Assert.AreEqual(7, counts[ExtractorRole.Raidable]);
                Assert.AreEqual(1, counts[ExtractorRole.Contestable]);
            }
            Assert.AreEqual(2, layout.GuidelinesMet);
            Assert.IsTrue(layout.Meets(ExtractorGuideline.All[0]));
            Assert.IsFalse(layout.Meets(ExtractorGuideline.All[1]));
        }

        [TestMethod]
        public void GivesEveryRoleItsSpots()
        {
            (MapNavigation navigation, IReadOnlyList<MapMarker> starts, IReadOnlyList<MapMarker> mass) = Flat(
                Marker("ARMY_1", "Blank Marker", 20.5f, 128.5f),
                Marker("ARMY_2", "Blank Marker", 235.5f, 128.5f),
                // in the base of ARMY_1
                Marker("base", "Mass", 30.5f, 128.5f),
                // an expansion of three on the side of ARMY_1
                Marker("e1", "Mass", 90.5f, 40.5f), Marker("e2", "Mass", 96.5f, 40.5f), Marker("e3", "Mass", 93.5f, 46.5f),
                // a lone spot on that side
                Marker("lone", "Mass", 90.5f, 220.5f),
                // halfway
                Marker("middle", "Mass", 128.5f, 200.5f));

            ExtractorLayout layout = ExtractorLayout.Classify(ExtractorLayout.Measure(navigation, starts, mass));
            Dictionary<string, ExtractorSpot> spots = layout.Spots.ToDictionary(spot => spot.Marker.Name);

            Assert.AreEqual(ExtractorRole.Safe, spots["base"].Role);
            Assert.AreEqual(ExtractorRole.Expandable, spots["e1"].Role);
            Assert.AreEqual(ExtractorRole.Expandable, spots["e3"].Role);
            Assert.AreEqual(ExtractorRole.Raidable, spots["lone"].Role);
            Assert.AreEqual(ExtractorRole.Contestable, spots["middle"].Role);
            Assert.AreEqual(0, spots["base"].Owner);
            Assert.AreEqual(1, layout.PerPlayer[0][ExtractorRole.Safe]);
            Assert.AreEqual(0.5, layout.PerPlayer[1][ExtractorRole.Contestable], "contestable spots are shared");
        }

        [TestMethod]
        public void TheThresholdsMoveSpotsBetweenRoles()
        {
            (MapNavigation navigation, IReadOnlyList<MapMarker> starts, IReadOnlyList<MapMarker> mass) = Flat(
                Marker("ARMY_1", "Blank Marker", 20.5f, 128.5f),
                Marker("ARMY_2", "Blank Marker", 235.5f, 128.5f),
                Marker("near", "Mass", 90.5f, 128.5f));
            ExtractorDistances distances = ExtractorLayout.Measure(navigation, starts, mass);

            Assert.AreEqual(ExtractorRole.Raidable, ExtractorLayout.Classify(distances).Spots[0].Role);
            Assert.AreEqual(ExtractorRole.Safe, ExtractorLayout.Classify(distances, new ExtractorThresholds(BaseRadius: 80)).Spots[0].Role);
            Assert.AreEqual(ExtractorRole.Contestable, ExtractorLayout.Classify(distances, new ExtractorThresholds(ContestedWithin: 1)).Spots[0].Role);
        }

        [TestMethod]
        [DataRow(4, null, 3, false)]
        [DataRow(4, null, 9, true)]
        [DataRow(2, 6, 6, true)]
        [DataRow(2, 6, 7, false)]
        [DataRow(1, 2, 0.5, false)]
        [DataRow(1, 2, 1.5, true)]
        public void ChecksTheGuideline(int min, int? max, double count, bool met)
        {
            Assert.AreEqual(met, new ExtractorGuideline(ExtractorRole.Raidable, min, max).IsMetBy(count));
        }
    }
}
