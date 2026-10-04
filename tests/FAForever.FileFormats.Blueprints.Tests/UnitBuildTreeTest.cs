using FAForever.FileFormats.Blueprints;

namespace FAForever.FileFormats.Blueprints.Tests
{
    [TestClass]
    public class UnitBuildTreeTest
    {
        private static BlueprintUnit Unit(string id, string lua) =>
            (BlueprintUnit)BlueprintParser.Parse($"UnitBlueprint {{ {lua} }}", $"/units/{id}/{id}_unit.bp")[0];

        // A small tree: a commander that builds a factory (and an engineer after an enhancement),
        // a factory that builds a tank by category and a transport by an unusual category, an
        // upgrade, a unit named by its id, and a campaign unit nobody builds.
        private static readonly BlueprintUnit[] Units =
        [
            Unit("acu", """
                Categories = { "COMMAND", "UEF" },
                Economy = { BuildableCategory = { "BUILTBYCOMMANDER UEF" } },
                Enhancements = {
                    Slots = { Back = {} },
                    AdvancedEngineering = { BuildableCategoryAdds = "BUILTBYTIER2COMMANDER UEF", Slot = "LCH" },
                },
                """),
            Unit("factory", """
                Categories = { "BUILTBYCOMMANDER", "UEF", "FACTORY" },
                Economy = { BuildableCategory = { "BUILTBYTIER1FACTORY UEF MOBILE", "TRANSPORTBUILTBYTIER1FACTORY UEF", "special" } },
                General = { UpgradesTo = "factoryhq" },
                """),
            Unit("factoryhq", """Categories = { "UEF", "FACTORY" }, General = { UpgradesFrom = "factory" },"""),
            Unit("tank", """Categories = { "BUILTBYTIER1FACTORY", "UEF", "MOBILE", "LAND" },"""),
            Unit("cybrantank", """Categories = { "BUILTBYTIER1FACTORY", "CYBRAN", "MOBILE", "LAND" },"""),
            Unit("transport", """Categories = { "TRANSPORTBUILTBYTIER1FACTORY", "UEF", "MOBILE", "AIR" },"""),
            Unit("special", """Categories = { "UEF" },"""),
            Unit("engineer", """Categories = { "BUILTBYTIER2COMMANDER", "UEF", "MOBILE" },"""),
            Unit("civilian", """Categories = { "UEF", "STRUCTURE" },"""),
        ];

        [TestMethod]
        public void MatchesAllCategoriesOfAnExpression()
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> builds = UnitBuildTree.Builds(Units);

            // the Cybran tank lacks UEF; the unit named "special" matches its own id
            CollectionAssert.AreEqual(new[] { "special", "tank", "transport" }, builds["factory"].ToArray());
        }

        [TestMethod]
        public void CommandersBuildWhatTheirEnhancementsAdd()
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> builds = UnitBuildTree.Builds(Units);

            CollectionAssert.AreEqual(new[] { "engineer", "factory" }, builds["acu"].ToArray());
            Assert.IsFalse(builds.ContainsKey("tank"));
        }

        [TestMethod]
        public void BuildableUnitsAreReachableFromACommander()
        {
            IReadOnlySet<string> buildable = UnitBuildTree.Buildable(Units, UnitBuildTree.Builds(Units));

            CollectionAssert.AreEquivalent(
                new[] { "acu", "factory", "factoryhq", "tank", "transport", "special", "engineer" },
                buildable.ToArray());
        }

        [TestMethod]
        public void UnitDataCarriesTheTree()
        {
            UnitData data = UnitData.From(3839, Units);

            Assert.IsTrue(data.GetOrNull("factoryhq")!.Buildable);
            Assert.IsFalse(data.GetOrNull("civilian")!.Buildable);
            Assert.AreEqual("factoryhq", data.GetOrNull("factory")!.UpgradesTo);
            Assert.AreEqual("factory", data.GetOrNull("factoryhq")!.UpgradesFrom);
            CollectionAssert.AreEqual(new[] { "factory" }, data.GetBuilders("tank").Select(unit => unit.BlueprintId).ToArray());
            Assert.AreEqual(0, data.GetBuilders("civilian").Count);

            // and survives the file format
            UnitData read = UnitData.Deserialize(data.Serialize());
            CollectionAssert.AreEqual(data.GetOrNull("factory")!.Builds.ToArray(), read.GetOrNull("factory")!.Builds.ToArray());
            Assert.IsTrue(read.GetOrNull("tank")!.Buildable);
        }
    }
}
