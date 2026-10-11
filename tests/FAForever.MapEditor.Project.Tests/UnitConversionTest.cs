using FAForever.FileFormats.Blueprints;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The unit of another faction that stands for a unit (user story U-11), over the blueprints of
    /// 42 real units of the game (<c>assets/unit-catalogue</c>, converted by the workspace's Convert).
    /// </summary>
    [TestClass]
    public sealed class UnitConversionTest
    {
        private static AssetCatalog catalog = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => catalog = AssetCatalog.Load(Path.Combine(AppContext.BaseDirectory, "assets", "unit-catalogue"));

        private static string? Convert(string id, Faction faction) => UnitConversion.Counterpart(catalog.Find(id)!, faction, catalog)?.Key;

        [TestMethod]
        public void TheSeriesLettersAreSwappedForTheOtherFactionsAndTheRestStays()
        {
            CollectionAssert.AreEqual(new[] { "urb1101" }, UnitConversion.SameIds("ueb1101", Faction.Cybran).ToArray());
            CollectionAssert.AreEqual(new[] { "xsb1101" }, UnitConversion.SameIds("uab1101", Faction.Seraphim).ToArray());
            CollectionAssert.AreEqual(new[] { "uab1101" }, UnitConversion.SameIds("xsb1101", Faction.Aeon).ToArray());
            CollectionAssert.AreEqual(new[] { "xra0002", "ura0002" }, UnitConversion.SameIds("xea0002", Faction.Cybran).ToArray(), "a unit of the x series first looks in it");
            Assert.AreEqual(0, UnitConversion.SameIds("opc0001", Faction.Aeon).Count(), "no faction in the id");
            Assert.AreEqual(0, UnitConversion.SameIds("uec", Faction.Aeon).Count());
        }

        [DataTestMethod]
        [DataRow("ueb1101", Faction.Cybran, "urb1101")]
        [DataRow("ueb1101", Faction.Aeon, "uab1101")]
        [DataRow("ueb1101", Faction.Seraphim, "xsb1101")]
        [DataRow("xsb1101", Faction.Uef, "ueb1101")]
        [DataRow("uab0101", Faction.Cybran, "urb0101")]
        [DataRow("urb1103", Faction.Seraphim, "xsb1103")]
        [DataRow("uea0101", Faction.Aeon, "uaa0101")]
        [DataRow("uel0101", Faction.Seraphim, "xsl0101")]
        public void AUnitBecomesTheSameKindOfUnitOfTheOtherFaction(string id, Faction faction, string expected) =>
            Assert.AreEqual(expected, Convert(id, faction));

        [TestMethod]
        public void AUnitOfThatFactionStaysAsItIs() => Assert.AreEqual("ueb1101", Convert("ueb1101", Faction.Uef));

        [DataTestMethod]
        [DataRow("uel0106", Faction.Seraphim, "xsl0201", DisplayName = "the Seraphim have no xsl0106: the T1 land unit with most in common")]
        [DataRow("url0402", Faction.Seraphim, "xsl0401", DisplayName = "no xsl0402: the experimental walker")]
        [DataRow("url0402", Faction.Uef, "uel0401", DisplayName = "no uel0402: the Fatboy")]
        public void AUnitWhoseIdHasNoCounterpartGetsTheUnitWithTheMostCategoriesInCommon(string id, Faction faction, string expected)
        {
            Assert.IsNull(catalog.Find(UnitConversion.SameIds(id, faction).FirstOrDefault() ?? "none"), "the id rule finds nothing");
            Assert.AreEqual(expected, Convert(id, faction));
        }

        [DataTestMethod]
        [DataRow(Faction.Uef)]
        [DataRow(Faction.Aeon)]
        [DataRow(Faction.Seraphim)]
        public void AUnitWithNoCounterpartIsLeftAlone(Faction faction)
        {
            // the Cybran xrl0002 is a T3 "structure" nothing of another faction in the catalogue shares a purpose with
            Assert.IsNull(Convert("xrl0002", faction));
        }

        [TestMethod]
        public void AUnitOfNoFactionHasNoCounterpart() =>
            Assert.IsNull(UnitConversion.Counterpart(catalog.Find("ueb1101")! with { Group = "Other" }, Faction.Cybran, catalog));

        [TestMethod]
        public void TheCategoriesThatSayWhatAUnitIsForSkipTheFactionTechAndLayer()
        {
            string[] purposes = [.. UnitConversion.Purposes(catalog.Find("ueb1101")!).Order()];
            CollectionAssert.AreEqual(new[] { "ECONOMIC", "ENERGYPRODUCTION" }, purposes);
        }
    }
}
