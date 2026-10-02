using FAForever.Replay;

namespace FAForever.Replay.Test
{
    [TestClass]
    public class LuaDataFormatterTest
    {
        [TestMethod]
        public void FormatsScalars()
        {
            Assert.AreEqual("nil", LuaDataFormatter.Format(new LuaData.Nil()));
            Assert.AreEqual("true", LuaDataFormatter.Format(new LuaData.Bool(true)));
            Assert.AreEqual("1.25", LuaDataFormatter.Format(new LuaData.Number(1.25)));
            Assert.AreEqual("'glhf'", LuaDataFormatter.Format(new LuaData.String("glhf")));
        }

        [TestMethod]
        public void TruncatesLongStrings()
        {
            string formatted = LuaDataFormatter.Format(new LuaData.String(new string('a', 100)), maxStringLength: 10);
            Assert.AreEqual("'aaaaaaaaaa…'", formatted);
        }

        [TestMethod]
        public void FormatsNestedTablesWithDepthCap()
        {
            LuaData.Table inner = new(new Dictionary<string, LuaData> { ["text"] = new LuaData.String("hi") });
            LuaData.Table outer = new(new Dictionary<string, LuaData>
            {
                ["To"] = new LuaData.Number(3),
                ["Msg"] = inner,
            });

            Assert.AreEqual("{To=3, Msg={text='hi'}}", LuaDataFormatter.Format(outer));
            Assert.AreEqual("{To=3, Msg={…}}", LuaDataFormatter.Format(outer, maxDepth: 1));
        }

        [TestMethod]
        public void CapsEntriesPerTable()
        {
            LuaData.Table table = new(Enumerable.Range(1, 5).ToDictionary(i => i.ToString(), i => (LuaData)new LuaData.Number(i)));

            Assert.AreEqual("{1=1, 2=2, 3=3, …}", LuaDataFormatter.Format(table, maxEntriesPerTable: 3));
        }
    }
}
