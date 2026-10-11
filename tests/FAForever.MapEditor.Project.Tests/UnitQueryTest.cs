using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Asking a map for its units by army, faction, kind, tech, blueprint and wrecks (user story U-10).
    /// </summary>
    [TestClass]
    public class UnitQueryTest
    {
        private static AssetEntry Entry(string id, string name, string group, string category, int? tech) => new AssetEntry
        {
            Kind = AssetKind.Unit, Key = id, Keys = [id], Name = name, Code = id, Group = group, Category = category, Family = id, File = id + ".json", TechLevel = tech,
        };

        private static readonly Dictionary<string, AssetEntry> Catalogue = new[]
        {
            Entry("ueb0101", "UEF Land Factory", "UEF", "structure", 1),
            Entry("ueb1101", "UEF Mass Extractor", "UEF", "structure", 1),
            Entry("uel0106", "UEF Light Assault Bot", "UEF", "land", 1),
            Entry("uea0102", "UEF Interceptor", "UEF", "air", 1),
            Entry("urb0201", "Cybran Air Factory", "Cybran", "structure", 2),
            Entry("url0402", "Cybran Monkeylord", "Cybran", "land", 4),
            Entry("xsl0201", "Seraphim Medium Tank", "Seraphim", "land", 2),
        }.ToDictionary(entry => entry.Key);

        private static AssetEntry? Find(string blueprint) => Catalogue.GetValueOrDefault(blueprint);

        /// <summary>
        /// Two armies of different factions: ARMY_1 (UEF) with a factory, an extractor, a bot and an air unit, and a wreck of a
        /// Cybran monkeylord; ARMY_2 (Cybran) with an air factory, a Seraphim tank (captured) and a unit the catalogue lacks.
        /// </summary>
        private static MapDocument Map()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 256 });
            document = document with
            {
                Armies = [.. document.Armies.Take(2).Select((army, i) => army with { Name = $"ARMY_{i + 1}", Faction = i == 0 ? 0 : 2 })],
            };
            Unit At(string name, string blueprint, float x) => new Unit { Name = name, Blueprint = blueprint, Position = new Vector3(x, 30, 50) };
            document = document
                .AddUnit("ARMY_1", ["INITIAL"], At("F", "ueb0101", 20))
                .AddUnit("ARMY_1", ["INITIAL"], At("M", "ueb1101", 40))
                .AddUnit("ARMY_1", ["INITIAL"], At("B", "uel0106", 60))
                .AddUnit("ARMY_1", ["INITIAL"], At("A", "uea0102", 80))
                .AddUnit("ARMY_1", ["INITIAL"], At("W", "url0402", 100))
                .AddUnit("ARMY_2", ["INITIAL"], At("AF", "urb0201", 120))
                .AddUnit("ARMY_2", ["INITIAL"], At("S", "xsl0201", 140))
                .AddUnit("ARMY_2", ["INITIAL"], At("X", "zzz9999", 160));
            return document.SetWrecks(["W"], wreck: true, out _);
        }

        private static string[] Ask(UnitQuery query, IReadOnlySet<string>? within = null)
        {
            MapDocument document = Map();
            return [.. query.Select(document, Find, within)];
        }

        [TestMethod]
        public void AQuestionWithNothingAskedGivesTheUnitsThatAreNotWrecks()
        {
            CollectionAssert.AreEqual(new[] { "F", "M", "B", "A", "AF", "S", "X" }, Ask(new UnitQuery()));
        }

        [TestMethod]
        public void WrecksAreLeftOutTakenAloneOrIncluded()
        {
            CollectionAssert.AreEqual(new[] { "W" }, Ask(new UnitQuery { Wrecks = WreckChoice.Only }));
            Assert.AreEqual(8, Ask(new UnitQuery { Wrecks = WreckChoice.Include }).Length);
        }

        [TestMethod]
        public void AnArmyHoldsOnlyItsOwnUnits()
        {
            CollectionAssert.AreEqual(new[] { "AF", "S", "X" }, Ask(new UnitQuery { Army = "ARMY_2" }));
        }

        [TestMethod]
        public void TheFactionIsTheBlueprintsWhateverTheArmyIs()
        {
            CollectionAssert.AreEqual(new[] { "S" }, Ask(new UnitQuery { Factions = ["seraphim"] }), "a Seraphim tank of a Cybran army");
            CollectionAssert.AreEqual(new[] { "F", "M", "B", "A" }, Ask(new UnitQuery { Factions = ["UEF"] }), "in any case");
            CollectionAssert.AreEqual(new[] { "AF", "S", "X" }, Ask(new UnitQuery { Factions = ["cybran", "seraphim"], Army = "ARMY_2" }), "several factions, and X by its army");
        }

        [TestMethod]
        public void AUnitTheCatalogueLacksHasItsArmysFaction()
        {
            // X has no blueprint in the catalogue; its army is Cybran
            CollectionAssert.AreEqual(new[] { "AF", "X" }, Ask(new UnitQuery { Factions = ["cybran"] }));
        }

        [TestMethod]
        public void TheKindIsTheCategoryAndUnknownUnitsAreOther()
        {
            CollectionAssert.AreEqual(new[] { "F", "M", "AF" }, Ask(new UnitQuery { Layers = ["structure"] }));
            CollectionAssert.AreEqual(new[] { "B", "S" }, Ask(new UnitQuery { Layers = ["land"] }));
            CollectionAssert.AreEqual(new[] { "X" }, Ask(new UnitQuery { Layers = ["other"] }));
        }

        [TestMethod]
        public void TechLevelsMatchTheCatalogueAndUnknownUnitsHaveNone()
        {
            CollectionAssert.AreEqual(new[] { "AF", "S" }, Ask(new UnitQuery { Tech = [2] }));
            CollectionAssert.AreEqual(new[] { "W" }, Ask(new UnitQuery { Tech = [4], Wrecks = WreckChoice.Include }));
            CollectionAssert.AreEqual(new[] { "F", "M", "B", "A", "AF", "S" }, Ask(new UnitQuery { Tech = [1, 2] }));
        }

        [TestMethod]
        public void ABlueprintIsAnIdOrPartOfAnIdOrOfTheName()
        {
            CollectionAssert.AreEqual(new[] { "M" }, Ask(new UnitQuery { Blueprint = "ueb1101" }));
            CollectionAssert.AreEqual(new[] { "F", "M", "B", "A" }, Ask(new UnitQuery { Blueprint = "UEF" }), "part of the name");
            CollectionAssert.AreEqual(new[] { "F", "AF" }, Ask(new UnitQuery { Blueprint = "factory" }));
            CollectionAssert.AreEqual(new[] { "F", "M" }, Ask(new UnitQuery { Blueprint = "ueb" }), "part of the id");
        }

        [TestMethod]
        public void TheFiltersTogetherNarrowTheAnswer()
        {
            UnitQuery query = new UnitQuery { Army = "ARMY_1", Factions = ["uef"], Layers = ["structure"], Tech = [1], Blueprint = "mass" };
            CollectionAssert.AreEqual(new[] { "M" }, Ask(query));
        }

        [TestMethod]
        public void OnlyTheUnitsWithinAreAsked()
        {
            CollectionAssert.AreEqual(new[] { "M", "B" }, Ask(new UnitQuery { Layers = ["structure", "land"] }, new HashSet<string> { "M", "B", "W" }));
        }
    }
}
