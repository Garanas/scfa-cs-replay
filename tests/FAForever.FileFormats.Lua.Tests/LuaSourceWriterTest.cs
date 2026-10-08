using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Lua.Tests
{
    [TestClass]
    public class LuaSourceWriterTest
    {
        private static LuaData Reparse(LuaData value) => LuaSourceParser.ParseExpression(LuaSourceWriter.Format(value));

        private static LuaData.Table Table(params (string Key, LuaData Value)[] entries) =>
            new LuaData.Table(entries.ToDictionary(entry => entry.Key, entry => entry.Value));

        [TestMethod]
        [DataRow("")]
        [DataRow("plain")]
        [DataRow("it's a \"quote\"")]
        [DataRow(@"back\slash")]
        [DataRow("lines\nand\r\nreturns\tand tabs")]
        [DataRow("\0\u0001\u001f\u007f then 1")]
        [DataRow("<LOC HardFFA_Description>FFA hardcore")]
        [DataRow("non-ASCII: é ü Ω")]
        public void WritesStringsThatReadBack(string text)
        {
            string literal = LuaSourceWriter.Literal(text);

            Assert.AreEqual(text, ((LuaData.String)LuaSourceParser.ParseExpression(literal)).Value);
            Assert.IsFalse(literal.Any(c => c < ' '), "control characters are escaped");
        }

        [TestMethod]
        [DataRow(0.0)]
        [DataRow(256.0)]
        [DataRow(-5.0)]
        [DataRow(0.1)]
        [DataRow(4271.7)]
        [DataRow(1e-7)]
        [DataRow(-2.5e300)]
        [DataRow(Math.PI)]
        public void WritesDoublesThatReadBack(double number)
        {
            Assert.AreEqual(number, ((LuaData.Number)LuaSourceParser.ParseExpression(LuaSourceWriter.Literal(number))).Value);
        }

        [TestMethod]
        public void WritesFloatsAsTheirShortestText()
        {
            Assert.AreEqual("2.70329", LuaSourceWriter.Literal(2.70329f));
            Assert.AreEqual("143.5", LuaSourceWriter.Literal(143.5f));
            Assert.AreEqual("0", LuaSourceWriter.Literal(0f));
            Assert.AreEqual(4.3535156f, (float)((LuaData.Number)LuaSourceParser.ParseExpression(LuaSourceWriter.Literal(4.3535156f))).Value);
        }

        [TestMethod]
        public void RefusesNumbersWithoutALiteral()
        {
            Assert.ThrowsException<ArgumentException>(() => LuaSourceWriter.Literal(double.NaN));
            Assert.ThrowsException<ArgumentException>(() => LuaSourceWriter.Literal(float.PositiveInfinity));
        }

        [TestMethod]
        [DataRow("size", "size")]
        [DataRow("_MASTERCHAIN_", "_MASTERCHAIN_")]
        [DataRow("Mass 08", "['Mass 08']")]
        [DataRow("end", "['end']")]
        [DataRow("2nd", "['2nd']")]
        [DataRow("5", "[5]")]
        [DataRow("-3", "[-3]")]
        [DataRow("05", "['05']")]
        [DataRow("true", "[true]")]
        public void WritesKeys(string key, string expected)
        {
            Assert.AreEqual(expected, LuaSourceWriter.Key(key));
        }

        [TestMethod]
        public void WritesTablesThatReadBack()
        {
            LuaData.Table table = Table(
                ("1", new LuaData.String("first")),
                ("2", new LuaData.Number(2)),
                ("name", new LuaData.String("x")),
                ("Mass 08", Table(("amount", new LuaData.Number(100)), ("resource", new LuaData.Bool(true)))),
                ("7", new LuaData.Bool(false)),
                ("true", new LuaData.String("a boolean key")),
                ("empty", Table()),
                ("size", Table(("1", new LuaData.Number(256)), ("2", new LuaData.Number(512)))));

            LuaData reparsed = Reparse(table);

            Assert.AreEqual(LuaSourceWriter.Format(table), LuaSourceWriter.Format(reparsed));
            CollectionAssert.AreEquivalent(table.Value.Keys.ToList(), ((LuaData.Table)reparsed).Value.Keys.ToList());
        }

        [TestMethod]
        public void SpreadsTablesOverLines()
        {
            LuaData.Table table = Table(
                ("size", Table(("1", new LuaData.Number(256)), ("2", new LuaData.Number(256)))),
                ("Economy", Table(("mass", new LuaData.Number(0)))));

            Assert.AreEqual("""
                {
                    size = { 256, 256 },
                    Economy = {
                        mass = 0,
                    },
                }
                """.ReplaceLineEndings("\n"), LuaSourceWriter.Format(table));
        }

        [TestMethod]
        public void WritesListsInTheirOrder()
        {
            LuaData.Table table = Table(("2", new LuaData.String("b")), ("1", new LuaData.String("a")));

            Assert.AreEqual("{ 'a', 'b' }", LuaSourceWriter.Format(table));
        }

        [TestMethod]
        public void LetsTheCallerWriteValuesItKnows()
        {
            static string? Inline(LuaData value) => value switch
            {
                LuaData.String text => $"STRING( {LuaSourceWriter.Literal(text.Value)} )",
                _ => null,
            };
            LuaSourceWriter writer = new LuaSourceWriter();

            writer.OpenField("Markers").Field("Mass 08", Table(("type", new LuaData.String("Mass")), ("amount", new LuaData.Number(100))), Inline).Close();

            Assert.AreEqual("""
                Markers = {
                    ['Mass 08'] = {
                        type = STRING( 'Mass' ),
                        amount = 100,
                    },
                },

                """.ReplaceLineEndings("\n"), writer.ToString());
        }
    }
}
