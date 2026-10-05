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
            Assert.AreEqual(3.4, striker.MaxSpeed);
            Assert.AreEqual("UWRC_DirectFire", striker.Weapons[0].RangeCategory);
            Assert.IsFalse(striker.Buildable); // only UnitData.From knows the build tree
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
        public void IgnoresRewritesWithoutAChangeInMeaning()
        {
            // the same unit written two ways, as happens between game versions
            UnitSummary before = UnitSummary.From(Unit("""
                Categories = { "TECH1", "LAND", "MOBILE" },
                Defense = { MaxHealth = 300, RegenRate = 0 },
                Intel = { VisionRadius = 20, RadarRadius = 0 },
                Weapon = { { Damage = 24, DamageRadius = 0, RateOfFire = 0.1667 } },
                """));
            UnitSummary after = UnitSummary.From(Unit("""
                Categories = { "LAND", "MOBILE", "TECH1" },
                Defense = { MaxHealth = 300 },
                Intel = { VisionRadius = 20 },
                Weapon = { { Damage = 24, RateOfFire = 10/60 } },
                """));

            Assert.IsNull(before.RegenRate);
            Assert.IsNull(before.RadarRadius);
            Assert.AreEqual(0.1667, after.Weapons[0].RateOfFire);
            CollectionAssert.AreEqual(new[] { "LAND", "MOBILE", "TECH1" }, before.Categories.ToArray());
            Assert.IsTrue(new UnitData([before]).HasSameUnits(new UnitData([after])));
        }

        private static BlueprintUnit Unit(string lua) =>
            (BlueprintUnit)BlueprintParser.Parse($"UnitBlueprint {{ {lua} }}", "/units/x/x_unit.bp")[0];

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
