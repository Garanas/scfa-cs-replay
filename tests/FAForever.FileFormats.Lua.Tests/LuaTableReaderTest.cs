using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Lua.Tests
{
    [TestClass]
    public class LuaTableReaderTest
    {
        private static LuaTableReader Read(string source) =>
            new LuaTableReader((LuaData.Table)LuaSourceParser.ParseExpression(source));

        [TestMethod]
        public void ReadsScalars()
        {
            LuaTableReader t = Read("{ Name = 'Mass', Amount = 100.5, Count = 3, Resource = true }");

            Assert.AreEqual("Mass", t.String("Name"));
            Assert.AreEqual(100.5, t.Number("Amount"));
            Assert.AreEqual(3, t.Integer("Count"));
            Assert.AreEqual(true, t.Bool("Resource"));
        }

        [TestMethod]
        public void ReadsAMissingOrMistypedFieldAsAbsent()
        {
            LuaTableReader t = Read("{ Name = 'Mass', Amount = 'many' }");

            Assert.IsNull(t.Number("Amount"));
            Assert.IsNull(t.String("Missing"));
            Assert.IsNull(t.Section("Name", section => section.Table));
            Assert.AreEqual(0, t.Strings("Missing").Count);
            Assert.AreEqual(0, t.Dictionary("Name", entry => entry.Table).Count);
            Assert.AreEqual(0, t.SectionOrEmpty("Missing", section => section.Table.Value.Count));
        }

        [TestMethod]
        public void ReadsListsUpToTheFirstGap()
        {
            LuaTableReader t = Read("{ Armies = { 'ARMY_1', 'ARMY_2', [4] = 'ARMY_4' }, Size = { 512, 256 } }");

            CollectionAssert.AreEqual(new[] { "ARMY_1", "ARMY_2" }, t.Strings("Armies").ToList());
            CollectionAssert.AreEqual(new[] { 512.0, 256.0 }, t.Numbers("Size").ToList());
        }

        [TestMethod]
        public void ReadsNestedTables()
        {
            LuaTableReader t = Read("""
                {
                    Teams = { { name = 'FFA' }, { name = 'Other' } },
                    Areas = { AREA_1 = { size = 1 }, AREA_2 = { size = 2 }, Note = 'not a table' },
                    Props = { ExtraArmies = 'ARMY_9', Count = 1 },
                    Caps = { Move = true, Attack = false },
                }
                """);

            CollectionAssert.AreEqual(new[] { "FFA", "Other" }, t.List("Teams", team => team.String("name")).ToList());
            IReadOnlyDictionary<string, double?> areas = t.Dictionary("Areas", area => area.Number("size"));
            Assert.AreEqual(2, areas.Count);
            Assert.AreEqual(2, areas["AREA_2"]);
            Assert.AreEqual("ARMY_9", t.StringDictionary("Props").Single().Value);
            CollectionAssert.AreEqual(new[] { "Move" }, t.Flags("Caps").ToList());
        }
    }
}
