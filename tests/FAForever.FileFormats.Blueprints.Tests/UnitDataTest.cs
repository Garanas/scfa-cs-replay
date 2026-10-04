using FAForever.FileFormats.Blueprints;

namespace FAForever.FileFormats.Blueprints.Tests
{
    [TestClass]
    public class UnitDataTest
    {
        private static BlueprintUnit ParseUnit(string source)
        {
            string path = Path.Combine("assets", "blueprints", source.TrimStart('/'));
            return (BlueprintUnit)BlueprintParser.Parse(File.ReadAllText(path), source)[0];
        }

        [TestMethod]
        public void RoundTripsThroughJson()
        {
            UnitData data = UnitData.From(3839, [
                ParseUnit("/units/UEL0201/UEL0201_unit.bp"),
                ParseUnit("/units/UEL0001/UEL0001_unit.bp"),
            ]);

            string json = data.Serialize();
            UnitData read = UnitData.Deserialize(json);

            Assert.AreEqual(3839, read.GameVersion);
            // sorted by id, one unit per line, nulls left out
            CollectionAssert.AreEqual(new[] { "uel0001", "uel0201" }, read.Units.Select(unit => unit.BlueprintId).ToArray());
            Assert.AreEqual(4, json.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            StringAssert.Contains(json, "\"blueprintId\":\"uel0201\"");
            Assert.IsFalse(json.Contains("null"), json);

            UnitSummary? striker = read.GetOrNull("UEL0201");
            Assert.IsNotNull(striker);
            Assert.AreEqual(UnitSummary.From(ParseUnit("/units/UEL0201/UEL0201_unit.bp")).MaxHealth, striker.MaxHealth);
            Assert.AreEqual(24, striker.Weapons[0].Damage);
            CollectionAssert.Contains(striker.Categories.ToList(), "TECH1");
        }

        [TestMethod]
        public void UnknownUnitsAreNull()
        {
            UnitData data = UnitData.From(3839, [ParseUnit("/units/UEL0201/UEL0201_unit.bp")]);

            Assert.IsNull(data.GetOrNull("xxx0000"));
            Assert.IsNull(data.GetOrNull(null));
            Assert.IsNull(data.GetOrNull(""));
        }
    }
}
