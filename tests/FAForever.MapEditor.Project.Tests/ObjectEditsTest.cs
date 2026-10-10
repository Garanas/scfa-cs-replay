using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class ObjectEditsTest
    {
        private static MapDocument original = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        /// <summary>
        /// The map with one of each kind at known places: a prop, a decal, a unit and an area.
        /// </summary>
        private static MapDocument WithOneOfEach(out ObjectRef prop, out ObjectRef decal, out ObjectRef unit, out ObjectRef area)
        {
            Heightmap heights = original.Terrain.Heightmap;
            MapDocument document = original
                .AddProp(new Prop { Id = 0, Blueprint = "/env/rock.bp", Position = new Vector3(50, heights.HeightAt(50, 60), 60), RotationX = Vector3.UnitX, RotationY = Vector3.UnitY, RotationZ = Vector3.UnitZ, Scale = Vector3.One })
                .AddDecal(new Decal { Type = DecalType.Albedo, Textures = ["/env/rock.dds"], Scale = new Vector3(8, 1, 4), Position = new Vector3(100, heights.HeightAt(100, 100), 100) })
                .AddUnit(original.Armies[0].Name, ["INITIAL"], new Unit { Name = "", Blueprint = "uec1101", Position = new Vector3(70, heights.HeightAt(70, 80), 80) })
                .AddArea(new Area { Name = "AREA_1", X0 = 10, Z0 = 10, X1 = 30, Z1 = 20 }, out Area added);
            prop = ObjectRef.Prop(document.Props[^1].Id);
            decal = ObjectRef.Decal(document.Decals[^1].Id);
            unit = ObjectRef.Unit("UNIT_1");
            area = ObjectRef.Area(added.Name);
            return document;
        }

        [TestMethod]
        public void AReferenceIsWrittenAndReadAsText()
        {
            foreach (ObjectRef thing in new[] { ObjectRef.Marker(12), ObjectRef.Prop(5), ObjectRef.Decal(3), ObjectRef.Unit("UNIT_5"), ObjectRef.Area("AREA_1") })
            {
                Assert.AreEqual(thing, ObjectRef.Parse(thing.ToString()));
            }
            Assert.AreEqual("unit:UNIT_5", ObjectRef.Unit("UNIT_5").ToString());
            Assert.ThrowsException<FormatException>(() => ObjectRef.Parse("tree:1"));
            Assert.ThrowsException<FormatException>(() => ObjectRef.Parse("prop:pine"));
        }

        [TestMethod]
        public void FindsWhatLiesUnderAPlace()
        {
            MapDocument document = WithOneOfEach(out ObjectRef prop, out ObjectRef decal, out ObjectRef unit, out ObjectRef area);
            Spawn spawn = document.Markers.Spawns[0];

            Assert.AreEqual(prop, document.Nearest(50.5f, 60, 0.25f, ObjectKinds.All));
            Assert.IsNull(document.Nearest(50.5f, 60, 0.25f, ObjectKinds.Markers), "only the chosen kinds");
            Assert.AreEqual(unit, document.Nearest(70, 80.5f, 0.25f, ObjectKinds.All));
            Assert.AreEqual(ObjectRef.Marker(spawn.Id), document.Nearest(spawn.Position.X + 1, spawn.Position.Z, 0.25f, ObjectKinds.All));
            Assert.AreEqual(decal, document.Nearest(104, 102, 0.25f, ObjectKinds.All), "inside the decal's rectangle");
            Assert.AreEqual(area, document.Nearest(20, 10.5f, 0.25f, ObjectKinds.All), "near the area's edge");
            Assert.IsNull(document.Nearest(20, 15, 0.25f, ObjectKinds.Areas), "not in the middle of an area");
            Assert.IsNull(document.Nearest(50.5f, 63, 0.25f, ObjectKinds.Props), "too far for a quarter ogrid a pixel");
            Assert.AreEqual(prop, document.Nearest(50.5f, 63, 1, ObjectKinds.Props), "near enough when zoomed out");
        }

        [TestMethod]
        public void FindsWhatLiesInABox()
        {
            MapDocument document = WithOneOfEach(out ObjectRef prop, out ObjectRef decal, out ObjectRef unit, out ObjectRef area);

            ObjectRef[] inside = [.. document.Inside(110, 110, 40, 50, ObjectKinds.Decals | ObjectKinds.Units | ObjectKinds.Areas)];

            CollectionAssert.AreEquivalent(new[] { unit, decal }, inside, "the area's centre lies outside");
            CollectionAssert.Contains(document.Inside(110, 110, 40, 50, ObjectKinds.Props).ToArray(), prop);
            CollectionAssert.Contains(document.Inside(0, 0, 40, 40, ObjectKinds.Areas).ToArray(), area);
        }

        [TestMethod]
        public void MovesThingsOfEveryKindAndKeepsThemOnTheTerrain()
        {
            MapDocument document = WithOneOfEach(out ObjectRef prop, out ObjectRef decal, out ObjectRef unit, out ObjectRef area);
            Spawn spawn = document.Markers.Spawns[0];
            ObjectRef marker = ObjectRef.Marker(spawn.Id);
            Heightmap heights = document.Terrain.Heightmap;

            MapDocument moved = document.MoveObjects([prop, decal, unit, area, marker], 7, -3);

            foreach (ObjectRef thing in new[] { prop, decal, unit, area, marker })
            {
                Vector3 before = document.PlaceOf(thing)!.Value;
                Vector3 after = moved.PlaceOf(thing)!.Value;
                Assert.AreEqual(before.X + 7, after.X, 1e-4, thing.ToString());
                Assert.AreEqual(before.Z - 3, after.Z, 1e-4, thing.ToString());
            }
            Vector3 propAt = moved.PlaceOf(prop)!.Value;
            Assert.AreEqual(heights.HeightAt(propAt.X, propAt.Z), propAt.Y, 1e-3, "on the ground where it lay on the ground");
            Assert.AreSame(document.Markers.Spawns[1], moved.Markers.Spawns[1], "what was not moved stays the same object");
        }

        [TestMethod]
        public void KeepsHowHighAThingWasAboveTheTerrain()
        {
            MapDocument document = WithOneOfEach(out ObjectRef prop, out _, out _, out _);
            document = document.UpdateProp(prop.Id, p => p with { Position = p.Position - new Vector3(0, 0.5f, 0) });

            MapDocument moved = document.MoveObjects([prop], 20, 20);

            Vector3 at = moved.PlaceOf(prop)!.Value;
            Assert.AreEqual(document.Terrain.Heightmap.HeightAt(at.X, at.Z) - 0.5f, at.Y, 1e-3, "a sunken rock stays sunken");
        }

        [TestMethod]
        public void PlacesEachThingsCentre()
        {
            MapDocument document = WithOneOfEach(out ObjectRef prop, out ObjectRef decal, out _, out ObjectRef area);

            MapDocument placed = document.PlaceObjects([prop, decal, area], 64, null);

            Assert.AreEqual(64, placed.PlaceOf(prop)!.Value.X, 1e-4);
            Assert.AreEqual(64, placed.PlaceOf(decal)!.Value.X, 1e-4);
            Assert.AreEqual(64, placed.PlaceOf(area)!.Value.X, 1e-4);
            Assert.AreEqual(document.PlaceOf(prop)!.Value.Z, placed.PlaceOf(prop)!.Value.Z, 1e-4, "z stays");
        }

        [TestMethod]
        public void TurnsEachThingAboutItsOwnCentre()
        {
            MapDocument document = WithOneOfEach(out ObjectRef prop, out ObjectRef decal, out ObjectRef unit, out ObjectRef area);
            float quarter = MathF.PI / 2;

            MapDocument turned = document.RotateObjects([prop, decal, unit, area], quarter);

            Assert.AreEqual(quarter, turned.HeadingOf(prop)!.Value, 1e-4);
            Assert.AreEqual(quarter, turned.HeadingOf(decal)!.Value, 1e-4);
            Assert.AreEqual(quarter, turned.HeadingOf(unit)!.Value, 1e-4);
            Vector3 centre = document.PlaceOf(decal)!.Value;
            Vector3 turnedCentre = turned.PlaceOf(decal)!.Value;
            Assert.AreEqual(centre.X, turnedCentre.X, 1e-3, "a decal turns about its centre, not its corner");
            Assert.AreEqual(centre.Z, turnedCentre.Z, 1e-3);
            Assert.AreEqual(document.Markers.Areas[^1], turned.Markers.Areas[^1], "areas do not turn");

            MapDocument facing = turned.SetHeading([prop, decal, unit], -1);
            Assert.AreEqual(-1, facing.HeadingOf(prop)!.Value, 1e-4);
            Assert.AreEqual(-1, facing.HeadingOf(decal)!.Value, 1e-4);
            Assert.AreEqual(-1, facing.HeadingOf(unit)!.Value, 1e-4);
        }

        [TestMethod]
        public void TurningAPropKeepsItsTilt()
        {
            MapDocument document = WithOneOfEach(out ObjectRef prop, out _, out _, out _);
            // tilted forwards about x by 30 degrees
            (float sin, float cos) = MathF.SinCos(MathF.PI / 6);
            document = document.UpdateProp(prop.Id, p => p with { RotationX = Vector3.UnitX, RotationY = new Vector3(0, cos, -sin), RotationZ = new Vector3(0, sin, cos) });

            MapDocument turned = document.RotateObjects([prop], MathF.PI / 4).RotateObjects([prop], MathF.PI / 4);

            Prop before = (Prop)document.Find(prop)!;
            Prop after = (Prop)turned.Find(prop)!;
            Assert.AreEqual(before.RotationY, after.RotationY, "the up row stays: the tilt is kept");
            Assert.AreEqual(1, after.RotationX.Length(), 1e-5, "still a rotation");
            Assert.AreEqual(0, Vector3.Dot(after.RotationX, after.RotationZ), 1e-5);
            Assert.AreEqual(MathF.PI / 2, MapObjects.PropHeading(after), 1e-4);
        }

        [TestMethod]
        public void RemovesThingsOfEveryKind()
        {
            MapDocument document = WithOneOfEach(out ObjectRef prop, out ObjectRef decal, out ObjectRef unit, out ObjectRef area);
            ObjectRef marker = ObjectRef.Marker(document.Markers.Resources[0].Id);

            MapDocument removed = document.RemoveObjects([prop, decal, unit, area, marker, ObjectRef.Prop(99999)]);

            foreach (ObjectRef thing in new[] { prop, decal, unit, area, marker })
            {
                Assert.IsFalse(removed.Exists(thing), thing.ToString());
            }
            Assert.AreEqual(original.Props.Length, removed.Props.Length);
        }

        [TestMethod]
        public void PastesCopiesUnderNewIdsAndNames()
        {
            MapDocument document = WithOneOfEach(out ObjectRef prop, out ObjectRef decal, out ObjectRef unit, out ObjectRef area);
            document = document
                .AddMarker(new PathNode { Id = 0, Name = "LandPN1", Layer = PathLayer.Land, Position = new Vector3(10, 0, 10) }, out Marker a)
                .AddMarker(new PathNode { Id = 0, Name = "LandPN2", Layer = PathLayer.Land, Position = new Vector3(20, 0, 10) }, out Marker b);
            document = document.UpdateMarker(a.Id, node => ((PathNode)node) with { AdjacentTo = [b.Id] });
            ObjectRef mass = ObjectRef.Marker(document.Markers.Resources[0].Id);
            ObjectRef[] copied = [prop, decal, unit, area, mass, ObjectRef.Marker(a.Id), ObjectRef.Marker(b.Id)];

            ObjectClipboard clipboard = document.CopyObjects(copied);
            MapDocument pasted = document.Paste(clipboard, 5, 5, out ImmutableArray<ObjectRef> added);

            Assert.AreEqual(copied.Length, added.Length);
            for (int i = 0; i < copied.Length; i++)
            {
                Assert.AreNotEqual(copied[i], added[i], "a new id or name");
                Assert.AreEqual(document.PlaceOf(copied[i])!.Value.X + 5, pasted.PlaceOf(added[i])!.Value.X, 1e-3, copied[i].ToString());
            }
            Assert.AreEqual(pasted.FindUnit(unit.Name!)!.Army, pasted.FindUnit(added[2].Name!)!.Army, "into the same army");
            PathNode newA = (PathNode)pasted.Find(added[5])!;
            CollectionAssert.AreEqual(new[] { added[6].Id }, newA.AdjacentTo.ToArray(), "the links between copied nodes follow them");
            Assert.AreEqual(clipboard.Centre.X, copied.Select(t => document.PlaceOf(t)!.Value.X).Average(), 1e-3);
        }
    }
}
