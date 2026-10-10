using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The armies' trees of unit groups (user stories U-2, U-3, U-4), templates of units, and the
    /// build grid structures snap to.
    /// </summary>
    [TestClass]
    public class UnitGroupEditsTest
    {
        private static MapDocument original = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        private static Unit NewUnit(float x, float z, string blueprint = "uec1101") => new Unit { Name = "", Blueprint = blueprint, Position = new Vector3(x, 0, z) };

        [TestMethod]
        public void GroupsAreAddedRenamedNestedAndRemoved()
        {
            MapDocument document = original.AddGroup("ARMY_1", ["INITIAL"], "Base", out ImmutableArray<string> basePath);
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base" }, basePath.ToArray());
            document = document.AddGroup("ARMY_1", ["INITIAL"], "Base", out ImmutableArray<string> second);
            Assert.AreEqual("Base 1", second[^1], "a sibling's name gets a number");
            document = document.AddGroup("ARMY_1", basePath, null, out ImmutableArray<string> nested);
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base", "GROUP_1" }, nested.ToArray());

            MapDocument renamed = document.RenameGroup("ARMY_1", second, "Outpost");
            Assert.IsNotNull(renamed.FindGroup("ARMY_1", ["INITIAL", "Outpost"]));
            Assert.ThrowsException<ArgumentException>(() => renamed.RenameGroup("ARMY_1", ["INITIAL", "Outpost"], "Base"), "a sibling has it");
            Assert.ThrowsException<ArgumentException>(() => renamed.RenameGroup("ARMY_1", [], "Root"), "the root keeps its name");
            Assert.ThrowsException<ArgumentException>(() => renamed.AddGroup("ARMY_1", [], "a/b", out _));
            Assert.ThrowsException<KeyNotFoundException>(() => renamed.AddGroup("ARMY_1", ["NOWHERE"], "x", out _));

            MapDocument moved = renamed.MoveGroup("ARMY_1", ["INITIAL", "Outpost"], "ARMY_2", ["INITIAL"], out ImmutableArray<string> movedPath);
            Assert.IsNull(moved.FindGroup("ARMY_1", ["INITIAL", "Outpost"]));
            CollectionAssert.AreEqual(new[] { "INITIAL", "Outpost" }, movedPath.ToArray());
            Assert.IsNotNull(moved.FindGroup("ARMY_2", movedPath), "into another army");
            Assert.ThrowsException<ArgumentException>(() => moved.MoveGroup("ARMY_1", basePath, "ARMY_1", nested, out _), "not into itself");
        }

        [TestMethod]
        public void ARemovedGroupLeavesItsUnitsToTheGroupAboveOrTakesThemAlong()
        {
            MapDocument document = original
                .AddUnit("ARMY_1", ["INITIAL", "Base"], NewUnit(10, 10) with { Name = "UNIT_1" })
                .AddUnit("ARMY_1", ["INITIAL", "Base", "Walls"], NewUnit(12, 10) with { Name = "UNIT_2" });

            MapDocument kept = document.RemoveGroup("ARMY_1", ["INITIAL", "Base"], keepUnits: true);
            CollectionAssert.AreEqual(new[] { "INITIAL" }, kept.FindUnit("UNIT_1")!.Group.ToArray());
            CollectionAssert.AreEqual(new[] { "INITIAL", "Walls" }, kept.FindUnit("UNIT_2")!.Group.ToArray(), "its groups go up too");

            MapDocument removed = document.RemoveGroup("ARMY_1", ["INITIAL", "Base"], keepUnits: false);
            Assert.IsNull(removed.FindUnit("UNIT_1"));
            Assert.IsNull(removed.FindUnit("UNIT_2"));
            Assert.ThrowsException<ArgumentException>(() => document.RemoveGroup("ARMY_1", [], true));

            Assert.AreEqual(2, document.UnitsIn("ARMY_1", ["INITIAL", "Base"]).Count());
            Assert.AreEqual(2, UnitGroupEdits.CountUnits(document.FindGroup("ARMY_1", ["INITIAL"])!) - UnitGroupEdits.CountUnits(original.FindGroup("ARMY_1", ["INITIAL"])!));
        }

        [TestMethod]
        public void UnitsMoveToAnotherArmyOrGroup()
        {
            MapDocument document = original
                .AddUnit("ARMY_1", ["INITIAL"], NewUnit(10, 10) with { Name = "UNIT_1" })
                .AddUnit("ARMY_1", ["INITIAL"], NewUnit(12, 10) with { Name = "UNIT_2" });

            MapDocument moved = document.MoveUnits(["UNIT_1", "UNIT_2", "NOBODY"], "NEUTRAL_CIVILIAN", ["INITIAL", "Town"], out int count);
            Assert.AreEqual(2, count);
            PlacedUnit unit = moved.FindUnit("UNIT_1")!;
            Assert.AreEqual("NEUTRAL_CIVILIAN", unit.Army);
            CollectionAssert.AreEqual(new[] { "INITIAL", "Town" }, unit.Group.ToArray(), "the group is made");
            Assert.AreEqual(new Vector3(10, 0, 10), unit.Unit.Position, "it stays where it is");

            moved.MoveUnits(["UNIT_1"], "NEUTRAL_CIVILIAN", ["INITIAL", "Town"], out int again);
            Assert.AreEqual(0, again, "there already");
            Assert.ThrowsException<KeyNotFoundException>(() => document.MoveUnits(["UNIT_1"], "ARMY_99", [], out _));
        }

        [TestMethod]
        public void ATemplateOfUnitsIsPlacedForAnotherArmyTurnedAndOnTheTerrain()
        {
            Heightmap heights = original.Terrain.Heightmap;
            MapDocument document = original
                .AddUnit("ARMY_1", ["INITIAL", "Base"], NewUnit(100, 100) with { Name = "UNIT_1", Position = new Vector3(100, heights.HeightAt(100, 100) + 1, 100) })
                .AddUnit("ARMY_1", ["INITIAL", "Base", "Walls"], NewUnit(110, 100, "uec1201") with { Name = "UNIT_2", Position = new Vector3(110, heights.HeightAt(110, 100), 100) });

            UnitGroup template = document.CaptureUnits(["UNIT_1", "UNIT_2"], "Town");
            Assert.AreEqual("Town", template.Name);
            Assert.AreEqual(new Vector3(-5, 1, 0), template.Units.Single().Position, "relative to the middle, with its height above the terrain");
            Assert.AreEqual("Walls", template.Groups.Single().Name, "the groups below the one holding them all");
            Assert.AreEqual(5, template.Groups[0].Units[0].Position.X, 1e-4);

            MapDocument placed = document.PlaceUnits(template, "ARMY_2", ["INITIAL"], 50, 60, MathF.PI / 2, out ImmutableArray<string> names, out ImmutableArray<string> group);
            CollectionAssert.AreEqual(new[] { "INITIAL", "Town" }, group.ToArray());
            CollectionAssert.AreEqual(new[] { "UNIT_3", "UNIT_4" }, names.ToArray(), "free names");
            PlacedUnit first = placed.FindUnit("UNIT_3")!;
            Assert.AreEqual("ARMY_2", first.Army);
            Assert.AreEqual(50, first.Unit.Position.X, 1e-3, "a quarter turn clockwise: -x becomes -z");
            Assert.AreEqual(55, first.Unit.Position.Z, 1e-3);
            Assert.AreEqual(placed.Terrain.Heightmap.HeightAt(50, 55) + 1, first.Unit.Position.Y, 1e-3);
            Assert.AreEqual(MathF.PI / 2, first.Unit.Orientation.Y, 1e-4, "turned with it");
            CollectionAssert.AreEqual(new[] { "INITIAL", "Town", "Walls" }, placed.FindUnit("UNIT_4")!.Group.ToArray());

            placed.PlaceUnits(template, "ARMY_2", ["INITIAL"], 0, 0, 0, out _, out ImmutableArray<string> again);
            Assert.AreEqual("Town 1", again[^1], "a second town gets its own group");
            Assert.ThrowsException<ArgumentException>(() => document.CaptureUnits(["NOBODY"], "x"));
        }

        [TestMethod]
        public void StructuresSnapToTheBuildGrid()
        {
            Assert.AreEqual((10.5f, 20.5f), BuildGrid.Snap(10.9f, 20.2f, 1, 1, 0), "odd sizes on half ogrids");
            Assert.AreEqual((11f, 20f), BuildGrid.Snap(10.6f, 20.2f, 2, 2, 0), "even on whole ogrids");
            Assert.AreEqual((10.5f, 20f), BuildGrid.Snap(10.6f, 20.2f, 3, 2, 0));
            Assert.AreEqual((11f, 20.5f), BuildGrid.Snap(10.6f, 20.2f, 3, 2, MathF.PI / 2), "a quarter turn swaps the sizes");
            Assert.AreEqual(MathF.PI / 2, BuildGrid.SnapHeading(1.4f), 1e-6);
            Assert.AreEqual(0, BuildGrid.SnapHeading(0.7f), 1e-6);
        }
    }
}
