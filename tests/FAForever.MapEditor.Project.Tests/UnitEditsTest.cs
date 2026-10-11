using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Editing many units at once (user story U-11): group, replace, distribute, and moves that keep
    /// structures on the build grid.
    /// </summary>
    [TestClass]
    public sealed class UnitEditsTest
    {
        private static MapDocument original = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        private static Unit At(string name, float x, float z, string blueprint = "ueb1101") => new Unit { Name = name, Blueprint = blueprint, Position = new Vector3(x, 0, z) };

        private static (double, double)? Footprints(string blueprint) => blueprint switch
        {
            "ueb1101" => (1, 1),
            "ueb0101" => (3, 3),
            "uab0101" => (4, 4),
            _ => null,
        };

        private static MapDocument WithUnits(params (string Army, string[] Group, Unit Unit)[] units)
        {
            MapDocument document = original;
            foreach ((string army, string[] group, Unit unit) in units)
            {
                document = document.AddUnit(army, group, unit);
            }
            return document;
        }

        [TestMethod]
        public void SelectedUnitsGoIntoANewGroupUnderTheDeepestGroupThatHoldsThemAll()
        {
            MapDocument document = WithUnits(
                ("ARMY_1", ["INITIAL", "Base", "Power"], At("U_1", 10.5f, 10.5f)),
                ("ARMY_1", ["INITIAL", "Base"], At("U_2", 12.5f, 10.5f)),
                ("ARMY_1", ["INITIAL", "Base", "Power"], At("U_3", 14.5f, 10.5f)));

            MapDocument grouped = document.GroupUnits(["U_1", "U_2"], "Pair", out ImmutableArray<(string Army, ImmutableArray<string> Path)> groups);

            Assert.AreEqual(1, groups.Length);
            Assert.AreEqual("ARMY_1", groups[0].Army);
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base", "Pair" }, groups[0].Path.ToArray());
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base", "Pair" }, grouped.FindUnit("U_1")!.Group.ToArray());
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base", "Pair" }, grouped.FindUnit("U_2")!.Group.ToArray());
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base", "Power" }, grouped.FindUnit("U_3")!.Group.ToArray(), "what is not selected stays");
            Assert.AreEqual(2, UnitGroupEdits.CountUnits(grouped.FindGroup("ARMY_1", groups[0].Path)!));
        }

        [TestMethod]
        public void UnitsOfSeveralArmiesGetAGroupEachWithTheSameNameAndANameTakenGetsTheNextNumber()
        {
            MapDocument document = WithUnits(("ARMY_1", ["INITIAL"], At("U_1", 10, 10)), ("ARMY_2", ["INITIAL"], At("U_2", 20, 10)));

            MapDocument grouped = document.GroupUnits(["U_1", "U_2"], null, out ImmutableArray<(string Army, ImmutableArray<string> Path)> groups);

            CollectionAssert.AreEqual(new[] { "ARMY_1", "ARMY_2" }, groups.Select(g => g.Army).ToArray());
            CollectionAssert.AreEqual(new[] { "INITIAL", "GROUP_1" }, groups[0].Path.ToArray());
            CollectionAssert.AreEqual(new[] { "INITIAL", "GROUP_1" }, groups[1].Path.ToArray());

            grouped = grouped.GroupUnits(["U_1"], null, out groups);
            CollectionAssert.AreEqual(new[] { "INITIAL", "GROUP_1", "GROUP_1" }, groups[0].Path.ToArray(), "inside the group they are in");
            Assert.ThrowsException<ArgumentException>(() => grouped.GroupUnits(["NO_SUCH_UNIT"], null, out _));
        }

        [TestMethod]
        public void UngroupingGivesTheUnitsToTheGroupAbove()
        {
            MapDocument document = WithUnits(("ARMY_1", ["INITIAL"], At("U_1", 10, 10)), ("ARMY_1", ["INITIAL"], At("U_2", 12, 10)))
                .GroupUnits(["U_1", "U_2"], "Pair", out ImmutableArray<(string Army, ImmutableArray<string> Path)> groups);

            MapDocument ungrouped = document.RemoveGroup("ARMY_1", groups[0].Path, keepUnits: true);

            CollectionAssert.AreEqual(new[] { "INITIAL" }, ungrouped.FindUnit("U_1")!.Group.ToArray());
            Assert.IsNull(ungrouped.FindGroup("ARMY_1", groups[0].Path));
        }

        [TestMethod]
        public void ReplacingABlueprintKeepsTheNamePlaceHeadingAndGroupAndSnapsAStructureToItsOwnGrid()
        {
            MapDocument document = WithUnits(
                ("ARMY_1", ["INITIAL", "Base"], At("U_1", 10.5f, 20.5f) with { Orientation = new Vector3(0, 0.3f, 0) }),
                ("ARMY_1", ["INITIAL"], At("U_2", 30.5f, 20.5f)));

            MapDocument replaced = document.ReplaceUnits(["U_1", "U_2", "MISSING"], unit => unit.Unit.Name == "U_1" ? new UnitReplacement("ueb0101", (3, 3)) : null, out int changed);

            Assert.AreEqual(1, changed);
            PlacedUnit one = replaced.FindUnit("U_1")!;
            Assert.AreEqual("ueb0101", one.Unit.Blueprint);
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base" }, one.Group.ToArray());
            Assert.AreEqual("ARMY_1", one.Army);
            Assert.AreEqual((10.5f, 20.5f), (one.Unit.Position.X, one.Unit.Position.Z), "an odd footprint stays on its half ogrid");
            Assert.AreEqual(0, one.Unit.Orientation.Y, 1e-6, "its heading turns to the nearest quarter turn");
            Assert.AreEqual("ueb1101", replaced.FindUnit("U_2")!.Unit.Blueprint, "a null answer leaves the unit");

            MapDocument offGrid = WithUnits(("ARMY_1", ["INITIAL"], At("U_1", 10.8f, 20.2f, "uel0101")));
            MapDocument even = offGrid.ReplaceUnits(["U_1"], _ => new UnitReplacement("uab0101", (4, 4)), out _);
            Assert.AreEqual((11f, 20f), (even.FindUnit("U_1")!.Unit.Position.X, even.FindUnit("U_1")!.Unit.Position.Z), "an even footprint moves to a whole ogrid");

            MapDocument same = document.ReplaceUnits(["U_1"], _ => new UnitReplacement("UEB1101", null), out int none);
            Assert.AreEqual(0, none, "the same blueprint is no change");
            Assert.AreSame(document, same);
        }

        [TestMethod]
        public void ANonStructureKeepsItsPlaceAndHeadingWhenReplaced()
        {
            MapDocument document = WithUnits(("ARMY_1", ["INITIAL"], At("U_1", 10.3f, 20.7f, "uel0101") with { Orientation = new Vector3(0, 0.3f, 0) }));

            MapDocument replaced = document.ReplaceUnits(["U_1"], _ => new UnitReplacement("url0101", null), out _);

            Assert.AreEqual(document.FindUnit("U_1")!.Unit.Position, replaced.FindUnit("U_1")!.Unit.Position);
            Assert.AreEqual(0.3f, replaced.FindUnit("U_1")!.Unit.Orientation.Y);
        }

        [TestMethod]
        public void DistributingSpreadsTheUnitsEvenlyBetweenTheEndsAlongTheLongerExtentKeepingTheirOrder()
        {
            MapDocument document = WithUnits(
                ("ARMY_1", ["INITIAL"], At("U_A", 10, 30, "uel0101")),
                ("ARMY_1", ["INITIAL"], At("U_D", 50, 32, "uel0101")),
                ("ARMY_1", ["INITIAL"], At("U_B", 15, 31, "uel0101")),
                ("ARMY_1", ["INITIAL"], At("U_C", 22, 29, "uel0101")));

            MapDocument spread = document.DistributeUnits(["U_A", "U_B", "U_C", "U_D"], null, _ => null, out int moved);

            Assert.AreEqual(2, moved);
            Assert.AreEqual(10, spread.FindUnit("U_A")!.Unit.Position.X);
            Assert.AreEqual(50, spread.FindUnit("U_D")!.Unit.Position.X);
            Assert.AreEqual(10 + 40f / 3, spread.FindUnit("U_B")!.Unit.Position.X, 1e-4);
            Assert.AreEqual(10 + 80f / 3, spread.FindUnit("U_C")!.Unit.Position.X, 1e-4);
            Assert.AreEqual(31, spread.FindUnit("U_B")!.Unit.Position.Z, "the other axis stays");

            // along z the order is C (29), A (30), B (31), D (32): already even
            document.DistributeUnits(["U_A", "U_B", "U_C", "U_D"], false, _ => null, out int alongZ);
            Assert.AreEqual(0, alongZ);
        }

        [TestMethod]
        public void DistributingStructuresPutsThemOnTheBuildGridAndTwoUnitsStayWhereTheyAre()
        {
            MapDocument document = WithUnits(
                ("ARMY_1", ["INITIAL"], At("U_1", 10.5f, 10.5f)),
                ("ARMY_1", ["INITIAL"], At("U_2", 11.5f, 10.5f)),
                ("ARMY_1", ["INITIAL"], At("U_3", 18.5f, 10.5f)));

            MapDocument spread = document.DistributeUnits(["U_1", "U_2", "U_3"], true, Footprints, out _);

            Assert.AreEqual(14.5f, spread.FindUnit("U_2")!.Unit.Position.X, "the middle, on a half ogrid");
            Assert.AreSame(document, document.DistributeUnits(["U_1", "U_3"], true, Footprints, out int moved));
            Assert.AreEqual(0, moved);
        }

        [TestMethod]
        public void AMoveIsRoundedSoTheFirstStructureLandsOnTheGridAndEveryThingMovesByTheSame()
        {
            MapDocument document = WithUnits(
                ("ARMY_1", ["INITIAL"], At("U_1", 10.5f, 10.5f, "uel0101")),
                ("ARMY_1", ["INITIAL"], At("U_2", 20, 20, "uab0101")),
                ("ARMY_1", ["INITIAL"], At("U_3", 30.5f, 30.5f, "uel0101")),
                ("ARMY_1", ["INITIAL"], At("U_4", 40.5f, 40.5f, "ueb0101")));
            ObjectRef[] things = [ObjectRef.Unit("U_1"), ObjectRef.Unit("U_2"), ObjectRef.Unit("U_3")];

            (float dx, float dz) = BuildGrid.KeepOnGrid(document, things, 0.3f, -2.4f, Footprints);

            // U_1 has no footprint (no structure), the first structure is U_2 (4 by 4: whole ogrids)
            Assert.AreEqual((20f, 18f), (20 + dx, 20 + dz));
            Assert.AreEqual((0.3f, -2.4f), BuildGrid.KeepOnGrid(document, [ObjectRef.Unit("U_1"), ObjectRef.Unit("U_3")], 0.3f, -2.4f, Footprints), "no structure: the offset as it is");
            (float fx, float fz) = BuildGrid.KeepOnGrid(document, [ObjectRef.Unit("U_4")], 1.2f, 0.9f, Footprints);
            Assert.AreEqual((41.5f, 41.5f), (40.5f + fx, 40.5f + fz), "a 3 by 3 stays on half ogrids");
        }
    }
}
