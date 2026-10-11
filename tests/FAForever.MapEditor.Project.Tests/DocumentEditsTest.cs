using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class DocumentEditsTest
    {
        private static MapDocument original = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        private static Decal NewDecal(float x, float z) => new Decal
        {
            Type = DecalType.Albedo,
            Textures = ["/env/evergreen/decals/rock.dds"],
            Scale = new Vector3(8, 1, 4),
            Position = new Vector3(x, 0, z),
            OwnerArmy = -1,
        };

        [TestMethod]
        public void AddsChangesAndRemovesADecalByItsId()
        {
            MapDocument added = original.AddDecal(NewDecal(10, 20) with { Id = 1 });
            Decal decal = added.Decals[^1];
            Assert.AreEqual(original.NextDecalId(), decal.Id, "a new id, whatever the decal had");

            MapDocument grouped = added with { DecalGroups = [new DecalGroup { Id = 1, Name = "rocks", DecalIds = [decal.Id] }] };
            MapDocument moved = grouped.UpdateDecal(decal.Id, d => d with { Position = new Vector3(30, 0, 40) });
            Assert.AreEqual(new Vector3(30, 0, 40), moved.Decals[^1].Position);

            MapDocument removed = moved.RemoveDecal(decal.Id);
            Assert.AreEqual(original.Decals.Length, removed.Decals.Length);
            Assert.IsTrue(removed.DecalGroups[0].DecalIds.IsEmpty, "the group no longer names it");
            Assert.ThrowsException<KeyNotFoundException>(() => removed.RemoveDecal(decal.Id));
        }

        [TestMethod]
        public void AddsAUnitIntoItsArmysGroupAndFindsItByName()
        {
            string army = original.Armies[0].Name;
            MapDocument document = original
                .AddUnit(army, ["INITIAL"], new Unit { Name = "", Blueprint = "uec1101", Position = new Vector3(5, 0, 6) })
                .AddUnit(army, ["INITIAL", "BASE"], new Unit { Name = "UNIT_7", Blueprint = "uec1201" });

            PlacedUnit first = document.FindUnit("UNIT_1")!;
            Assert.AreEqual(army, first.Army);
            CollectionAssert.AreEqual(new[] { "INITIAL" }, first.Group.ToArray());
            PlacedUnit second = document.FindUnit("UNIT_7")!;
            CollectionAssert.AreEqual(new[] { "INITIAL", "BASE" }, second.Group.ToArray(), "a group that is missing is made");
            Assert.AreEqual("UNIT_8", document.NextUnitName());

            MapDocument renamed = document.AddUnit(army, [], new Unit { Name = "UNIT_7", Blueprint = "uec1201" });
            Assert.IsNotNull(renamed.FindUnit("UNIT_8"), "a name that is taken is replaced");

            MapDocument turned = document.UpdateUnit("UNIT_7", unit => unit with { Orientation = new Vector3(0, 1, 0) });
            Assert.AreEqual(1, turned.FindUnit("UNIT_7")!.Unit.Orientation.Y);
            Assert.AreSame(document.FindUnit("UNIT_1")!.Unit, turned.FindUnit("UNIT_1")!.Unit, "the other units stay as they are");

            MapDocument removed = turned.RemoveUnit("UNIT_7");
            Assert.IsNull(removed.FindUnit("UNIT_7"));
            Assert.IsNotNull(removed.FindUnit("UNIT_1"));
            Assert.ThrowsException<KeyNotFoundException>(() => removed.UpdateUnit("UNIT_7", unit => unit));
            Assert.ThrowsException<KeyNotFoundException>(() => original.AddUnit("NOBODY", [], new Unit { Name = "", Blueprint = "uec1101" }));
        }

        [TestMethod]
        public void AddsManyUnitsIntoOneGroupAsOneVersionWithFreeNames()
        {
            string army = original.Armies[0].Name;
            MapDocument document = original.AddUnit(army, ["INITIAL"], new Unit { Name = "UNIT_3", Blueprint = "uec1101" });
            Unit[] units =
            [
                new Unit { Name = "", Blueprint = "ueb5101", Position = new Vector3(5, 0, 6) },
                new Unit { Name = "UNIT_3", Blueprint = "ueb5101", Position = new Vector3(6, 0, 6) },
                new Unit { Name = "UNIT_9", Blueprint = "ueb5101", Position = new Vector3(7, 0, 6) },
                new Unit { Name = "", Blueprint = "ueb5101", Position = new Vector3(8, 0, 6) },
            ];
            MapDocument added = document.AddUnits(army, ["INITIAL", "Walls"], units, out System.Collections.Immutable.ImmutableArray<string> names);
            CollectionAssert.AreEqual(new[] { "UNIT_4", "UNIT_5", "UNIT_9", "UNIT_6" }, names.ToArray(), "empty and taken names get the next free ones, a free name is kept");
            foreach (string name in names)
            {
                PlacedUnit placed = added.FindUnit(name)!;
                Assert.AreEqual(army, placed.Army);
                CollectionAssert.AreEqual(new[] { "INITIAL", "Walls" }, placed.Group.ToArray(), "the group is made");
            }
            Assert.AreEqual(4, added.FindGroup(army, ["INITIAL", "Walls"])!.Units.Length);
            Assert.AreEqual("UNIT_10", added.NextUnitName());

            MapDocument none = document.AddUnits(army, ["INITIAL"], [], out System.Collections.Immutable.ImmutableArray<string> noNames);
            Assert.AreSame(document, none, "nothing to add changes nothing");
            Assert.IsTrue(noNames.IsEmpty);
            Assert.ThrowsException<KeyNotFoundException>(() => original.AddUnits("NOBODY", [], units, out _));
            Assert.ThrowsException<KeyNotFoundException>(() => original.AddUnits("NOBODY", [], [], out _));
        }

        [TestMethod]
        public void AddsMarkersOfEveryKindUnderANewIdAndAFreeName()
        {
            ResourceMarker mass = original.Markers.Resources.First(r => r.Kind == ResourceKind.Mass);
            MapDocument document = original.AddMarker(mass, out Marker copy);

            Assert.AreEqual(original.Markers.NextId(), copy.Id);
            Assert.AreNotEqual(mass.Name, copy.Name, "the name is taken");
            Assert.IsInstanceOfType(document.FindMarker(copy.Id), typeof(ResourceMarker));
            Assert.AreEqual(original.Markers.Resources.Length + 1, document.Markers.Resources.Length);

            MapDocument more = document.AddMarker(new AiMarker { Id = 0, Name = "Rally Point 01", Type = "Rally Point" }, out Marker rally);
            Assert.AreEqual("Rally Point 01", rally.Name, "a free name stays");
            Assert.AreEqual(1, more.Markers.AiMarkers.Count(m => m.Name == "Rally Point 01"));
        }

        [TestMethod]
        public void FindsAFreeName()
        {
            Assert.AreEqual("Mass 12", DocumentEdits.FreeName(["Mass 08", "Mass 11"], "Mass 08"));
            Assert.AreEqual("ARMY_10", DocumentEdits.FreeName(["ARMY_9"], "ARMY_9"));
            Assert.AreEqual("Rally 1", DocumentEdits.FreeName(["Rally"], "Rally"));
            Assert.AreEqual("Mass 08", DocumentEdits.FreeName(["Mass 07"], "Mass 08"));
        }

        [TestMethod]
        public void RemovingAMarkerTakesAwayEveryLinkToIt()
        {
            MapDocument document = original
                .AddMarker(new PathNode { Id = 0, Name = "LandPN1", Layer = PathLayer.Land, Position = new Vector3(10, 0, 10) }, out Marker a)
                .AddMarker(new PathNode { Id = 0, Name = "LandPN2", Layer = PathLayer.Land, Position = new Vector3(20, 0, 10) }, out Marker b);
            document = document
                .UpdateMarker(a.Id, node => ((PathNode)node) with { AdjacentTo = [b.Id] })
                .UpdateMarker(b.Id, node => ((PathNode)node) with { AdjacentTo = [a.Id] });
            document = document with { Markers = document.Markers with { Chains = [new Chain { Name = "patrol", MarkerIds = [a.Id, b.Id] }] } };

            MapDocument removed = document.RemoveMarker(b.Id);

            Assert.IsNull(removed.FindMarker(b.Id));
            Assert.IsTrue(((PathNode)removed.FindMarker(a.Id)!).AdjacentTo.IsEmpty);
            CollectionAssert.AreEqual(new[] { a.Id }, removed.Markers.Chains[0].MarkerIds.ToArray());
            Assert.ThrowsException<KeyNotFoundException>(() => removed.RemoveMarker(b.Id));
        }

        [TestMethod]
        public void AnUpdateOfAMarkerKeepsItsKindAndId()
        {
            Spawn spawn = original.Markers.Spawns[0];

            MapDocument moved = original.UpdateMarker(spawn.Id, marker => marker with { Position = new Vector3(1, 2, 3) });

            Assert.AreEqual(new Vector3(1, 2, 3), moved.Markers.Spawns[0].Position);
            Assert.ThrowsException<InvalidOperationException>(() => original.UpdateMarker(spawn.Id, marker => new AiMarker { Id = marker.Id, Name = marker.Name, Type = "Rally Point" }));
            Assert.ThrowsException<InvalidOperationException>(() => original.UpdateMarker(spawn.Id, marker => marker with { Id = 9999 }));
        }

        [TestMethod]
        public void AddsChangesAndRemovesAnAreaByItsName()
        {
            MapDocument document = original
                .AddArea(new Area { Name = "AREA_1", X0 = 0, Z0 = 0, X1 = 10, Z1 = 10 }, out Area first)
                .AddArea(new Area { Name = "AREA_1", X0 = 5, Z0 = 5, X1 = 20, Z1 = 20 }, out Area second);
            Assert.AreNotEqual(first.Name, second.Name);

            MapDocument wider = document.UpdateArea(second.Name, area => area with { X1 = 40 });
            Assert.AreEqual(40, wider.Markers.Areas.Single(a => a.Name == second.Name).X1);

            MapDocument removed = wider.RemoveArea(first.Name);
            Assert.IsFalse(removed.Markers.Areas.Any(a => a.Name == first.Name));
            Assert.ThrowsException<KeyNotFoundException>(() => removed.RemoveArea(first.Name));
        }

        [TestMethod]
        public void ReadsTheHeightBetweenTheCorners()
        {
            Heightmap heights = original.Terrain.Heightmap;
            float a = heights.GetHeight(10, 10);
            float b = heights.GetHeight(11, 10);

            Assert.AreEqual(a, heights.HeightAt(10, 10), 1e-4);
            Assert.AreEqual((a + b) / 2, heights.HeightAt(10.5f, 10), 1e-3);
            Assert.AreEqual(heights.GetHeight(0, 0), heights.HeightAt(-5, -5), 1e-4, "outside: at the nearest edge");
            Assert.AreEqual(heights.GetHeight(heights.Width, heights.Height), heights.HeightAt(1e6f, 1e6f), 1e-4);
        }
    }
}
