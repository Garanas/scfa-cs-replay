using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Lua.Tests
{
    [TestClass]
    public class LuaSourceParserTest
    {
        private static LuaData.Table ParseTable(string source) => (LuaData.Table)LuaSourceParser.ParseExpression(source);

        [TestMethod]
        [DataRow("nil", "nil")]
        [DataRow("true", "true")]
        [DataRow("false", "false")]
        [DataRow("42", "42")]
        [DataRow("-1.5", "-1.5")]
        [DataRow(".5", "0.5")]
        [DataRow("1e-007", "0")]
        [DataRow("0x10", "16")]
        [DataRow("10/20", "0.5")]
        [DataRow("2 + 3 * 4", "14")]
        [DataRow("(2 + 3) * 4", "20")]
        [DataRow("2 ^ 3 ^ 2", "512")]
        [DataRow("-2 ^ 2", "-4")]
        [DataRow("7 % 3", "1")]
        [DataRow("'a' .. 'b' .. 1", "'ab1'")]
        [DataRow("1 < 2 and 'yes' or 'no'", "'yes'")]
        [DataRow("not nil", "true")]
        [DataRow("1 != 1", "false")]
        public void EvaluatesExpressions(string source, string expected)
        {
            Assert.AreEqual(expected, LuaDataFormatter.Format(LuaSourceParser.ParseExpression(source)));
        }

        [TestMethod]
        [DataRow("'it''s'")]
        [DataRow("{ 1, 2")]
        [DataRow("1 +")]
        [DataRow("function() end")]
        [DataRow("'unfinished")]
        [DataRow("3x")]
        public void RejectsInvalidSource(string source)
        {
            Assert.ThrowsException<LuaSyntaxException>(() => LuaSourceParser.ParseExpression(source));
        }

        [TestMethod]
        public void ParsesStrings()
        {
            Assert.AreEqual("a\"b'c\n\\A", ((LuaData.String)LuaSourceParser.ParseExpression("\"a\\\"b'c\\n\\\\\\65\"")).Value);
            Assert.AreEqual("/textures/ring.dds", ((LuaData.String)LuaSourceParser.ParseExpression("[[/textures/ring.dds]]")).Value);
            // the newline right after the opening bracket is dropped
            Assert.AreEqual("a]]b\n", ((LuaData.String)LuaSourceParser.ParseExpression("[==[\na]]b\n]==]")).Value);
        }

        [TestMethod]
        public void ParsesTableConstructors()
        {
            LuaData.Table table = ParseTable("""
                {
                    'first',   -- a comment
                    Name = "x"; [10] = true,
                    --[[ a block
                         comment ]]
                    ['Key With Spaces'] = { },
                    'second',
                    Skipped = nil,
                }
                """);

            Assert.AreEqual("{1='first', Name='x', 10=true, Key With Spaces={}, 2='second'}", LuaDataFormatter.Format(table));
        }

        [TestMethod]
        public void LaterKeysOverrideEarlierOnes()
        {
            Assert.AreEqual("{A=2}", LuaDataFormatter.Format(ParseTable("{ A = 1, A = 2 }")));
            Assert.AreEqual("{}", LuaDataFormatter.Format(ParseTable("{ A = 1, A = nil }")));
        }

        [TestMethod]
        public void CallsFunctionsAndAssignsVariables()
        {
            List<LuaData> calls = new List<LuaData>();
            Dictionary<string, LuaFunction> functions = new Dictionary<string, LuaFunction>
            {
                ["Define"] = arguments => { calls.Add(arguments[0]); return new LuaData.Nil(); },
                ["math.max"] = arguments => new LuaData.Number(arguments.Max(argument => ((LuaData.Number)argument).Value)),
            };

            IReadOnlyDictionary<string, LuaData> globals = LuaSourceParser.Execute("""
                local speed = math.max(1, 3, 2)
                Define { Speed = speed * 2 };
                Define "named"
                Copy = ({ Speed = speed }).Speed
                """, functions);

            Assert.AreEqual("{Speed=6}", LuaDataFormatter.Format(calls[0]));
            Assert.AreEqual("'named'", LuaDataFormatter.Format(calls[1]));
            Assert.AreEqual("3", LuaDataFormatter.Format(globals["Copy"]));
        }

        [TestMethod]
        public void ReadsTheGlobalsOfTheCaller()
        {
            Dictionary<string, LuaData> predefined = new Dictionary<string, LuaData>
            {
                ["categories"] = new LuaData.Table(new Dictionary<string, LuaData> { ["ual0105"] = new LuaData.String("ual0105") }),
            };

            IReadOnlyDictionary<string, LuaData> globals = LuaSourceParser.Execute(
                "Unit = categories.ual0105",
                new Dictionary<string, LuaFunction>(),
                predefined);

            Assert.AreEqual("'ual0105'", LuaDataFormatter.Format(globals["Unit"]));
            Assert.IsFalse(globals.ContainsKey("categories"), "only what the chunk assigned is returned");
        }

        [TestMethod]
        public void ReportsTheLineOfAnError()
        {
            LuaSyntaxException exception = Assert.ThrowsException<LuaSyntaxException>(() =>
                LuaSourceParser.Execute("Define {\n  A = 1,\n  B = Unknown(2),\n}", new Dictionary<string, LuaFunction> { ["Define"] = _ => new LuaData.Nil() }));

            Assert.AreEqual(3, exception.Line);
            StringAssert.Contains(exception.Message, "Unknown");
        }
    }
}
