using FAForever.FileFormats.Blueprints;

namespace FAForever.FileFormats.Blueprints.Tests
{
    [TestClass]
    public class UnitSummaryTest
    {
        private static BlueprintUnit ParseUnit(string source)
        {
            string path = Path.Combine("assets", "blueprints", source.TrimStart('/'));
            return (BlueprintUnit)BlueprintParser.Parse(File.ReadAllText(path), source)[0];
        }

        [TestMethod]
        public void SummarisesAUnit()
        {
            UnitSummary striker = UnitSummary.From(ParseUnit("/units/UEL0201/UEL0201_unit.bp"));

            Assert.AreEqual("uel0201", striker.BlueprintId);
            Assert.AreEqual("MA12 Striker", striker.Name);
            Assert.AreEqual("Medium Tank", striker.Description);
            Assert.AreEqual("UEF", striker.Faction);
            Assert.AreEqual(1, striker.TechLevel);
            Assert.AreEqual("RULEUMT_Land", striker.MotionType);
            Assert.IsTrue(striker.HasCategory("DIRECTFIRE"));
            Assert.AreEqual(300, striker.MaxHealth);
            Assert.AreEqual(56, striker.BuildCostMass);
            Assert.IsNull(striker.ShieldMaxHealth);

            UnitSummaryWeapon gun = striker.Weapons[0];
            Assert.AreEqual("Gauss Cannon", gun.DisplayName);
            Assert.AreEqual(24, gun.Damage);
            Assert.AreEqual(18, gun.MaxRadius);
            Assert.AreEqual(1, gun.RateOfFire);
            Assert.AreEqual(1, gun.MuzzleSalvoSize);
        }

        [TestMethod]
        public void SummarisesTheCommander()
        {
            UnitSummary acu = UnitSummary.From(ParseUnit("/units/UEL0001/UEL0001_unit.bp"));

            Assert.AreEqual(10, acu.BuildRate);
            Assert.IsNull(acu.TechLevel);
            Assert.AreEqual(7, acu.Weapons.Count);
            Assert.AreEqual("TacticalMissile", acu.Weapons[3].EnabledByEnhancement);
            Assert.IsTrue(acu.Weapons.Any(weapon => weapon.WeaponCategory == "Death"));
        }

        [TestMethod]
        public void SummarisesShieldsAndIntel()
        {
            UnitSummary continental = UnitSummary.From(ParseUnit("/units/XEA0306/XEA0306_unit.bp"));

            Assert.AreEqual(3000, continental.ShieldMaxHealth);
            Assert.AreEqual("RULEUMT_Air", continental.MotionType);
            Assert.IsNotNull(continental.VisionRadius);
        }

        [TestMethod]
        [DataRow("<LOC uel0201_name>MA12 Striker", "MA12 Striker")]
        [DataRow("Plain text", "Plain text")]
        [DataRow("<LOC x_desc>", null)]
        [DataRow("", null)]
        [DataRow(null, null)]
        public void DropsLocalizationTags(string? text, string? expected)
        {
            Assert.AreEqual(expected, UnitSummary.WithoutLocalization(text));
        }
    }
}
