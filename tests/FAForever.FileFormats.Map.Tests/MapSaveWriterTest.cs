using System.Numerics;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class MapSaveWriterTest
    {
        private static MapSave ParseAsset(string map) => MapSaveParser.Parse(File.ReadAllText(map + "_save.lua"));

        [TestMethod]
        [DataRow(ScmapParserTest.ThetaPassage)]
        [DataRow(ScmapParserTest.HardFfa)]
        [DataRow(ScmapParserTest.CoopR01)]
        public void WritesASaveThatParsesToTheSameContent(string map)
        {
            MapSave save = ParseAsset(map);

            string written = MapSaveWriter.Write(save);
            MapSave reparsed = MapSaveParser.Parse(written);

            Assert.AreEqual(MapDump.Save(save), MapDump.Save(reparsed));
            Assert.AreEqual(written, MapSaveWriter.Write(reparsed), "writing again changes nothing");
        }

        [TestMethod]
        public void WritesMarkersAsTheMapEditorsDo()
        {
            string written = MapSaveWriter.Write(ParseAsset(ScmapParserTest.ThetaPassage));

            StringAssert.Contains(written, """
                                ['Mass 08'] = {
                                    size = FLOAT( 1 ),
                                    resource = BOOLEAN( true ),
                                    amount = FLOAT( 100 ),
                                    color = STRING( 'ff808080' ),
                                    editorIcon = STRING( '' ),
                                    prop = STRING( '/env/common/props/markers/M_Blank_prop.bp' ),
                                    type = STRING( 'Mass' ),
                                    orientation = VECTOR3( 0, 0, 0 ),
                                    position = VECTOR3( 14.5, 5.98474, 31.5 ),
                                },
                """.ReplaceLineEndings("\n"));
            StringAssert.Contains(written, "rectangle = RECTANGLE( 0, 0, 256, 256 ),");
            StringAssert.Contains(written, "Units = GROUP {");
        }

        [TestMethod]
        public void LeavesThePlatoonBuildersOut()
        {
            MapSave save = ParseAsset(ScmapParserTest.CoopR01);
            Assert.IsTrue(save.Armies.Any(army => Builders(army).Count > 0), "the co-op mission has platoon builders");

            MapSave reparsed = MapSaveParser.Parse(MapSaveWriter.Write(save));

            Assert.IsTrue(reparsed.Armies.All(army => Builders(army).Count == 0));
            Assert.IsTrue(reparsed.Raw.TryGetTableValue("Platoons", out LuaData.Table? platoons) && platoons!.Value.Count == 0);
        }

        private static Dictionary<string, LuaData> Builders(MapArmy army) =>
            army.Raw.TryGetTableValue("PlatoonBuilders", out LuaData.Table? builders) && builders!.TryGetTableValue("Builders", out LuaData.Table? list)
                ? list!.Value
                : [];

        [TestMethod]
        public void RaisesTheEditorsCountersPastTheNamesInUse()
        {
            MapSave save = ParseAsset(ScmapParserTest.ThetaPassage);
            MapArmy army = save.Armies[0];
            MapUnit unit = new MapUnit("UNIT_999", "uel0001", new Vector3(1, 2, 3), Vector3.Zero, "", "");
            MapSave changed = save with
            {
                Areas = [.. save.Areas, new MapArea("AREA_12", 0, 0, 10, 10)],
                Armies = [army with { Units = army.Units with { Units = [unit] } }, .. save.Armies.Skip(1)],
            };

            MapSave reparsed = MapSaveParser.Parse(MapSaveWriter.Write(changed));

            Assert.IsTrue(reparsed.Raw.TryGetStringValue("next_unit_id", out string? nextUnit));
            Assert.AreEqual("1000", nextUnit);
            Assert.IsTrue(reparsed.Raw.TryGetStringValue("next_area_id", out string? nextArea));
            Assert.AreEqual("13", nextArea);
            Assert.AreEqual(unit, reparsed.Armies[0].Units.Units.Single());
        }

        [TestMethod]
        public void LeavesOutWhatTheArmyDoesNotHave()
        {
            MapSave save = ParseAsset(ScmapParserTest.HardFfa);
            MapSave changed = save with { Armies = [save.Armies[0] with { Personality = null, Color = null, Economy = null }] };

            MapArmy army = MapSaveParser.Parse(MapSaveWriter.Write(changed)).Armies.Single();

            Assert.IsNull(army.Personality);
            Assert.IsNull(army.Color);
            Assert.IsNull(army.Economy);
            Assert.AreEqual(save.Armies[0].Faction, army.Faction);
        }
    }
}
