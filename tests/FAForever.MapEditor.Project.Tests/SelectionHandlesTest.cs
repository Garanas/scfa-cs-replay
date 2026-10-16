using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The handles of the selection (user story F-11): the box they sit on, where they lie for a
    /// view, which one the mouse takes, and what dragging them does to the things.
    /// </summary>
    [TestClass]
    public class SelectionHandlesTest
    {
        private static MapDocument original = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        private static MapDocument WithThings(out ObjectRef prop, out ObjectRef decal, out ObjectRef unit, out ObjectRef area)
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
        public void TheBoxHoldsTheThingsAndSaysWhichHandlesTheyHave()
        {
            MapDocument document = WithThings(out ObjectRef prop, out ObjectRef decal, out ObjectRef unit, out ObjectRef area);

            SelectionFrame both = SelectionFrame.Of(document, [prop, unit])!;
            Assert.AreEqual(new Vector2(60, 70), both.Middle, "the middle of their bounds");
            Assert.AreEqual(new Vector2(10, 10), both.HalfSize);
            Assert.IsTrue(both.Turns && both.Scales && !both.ScalesSides, "a prop scales alike along every axis");

            SelectionFrame units = SelectionFrame.Of(document, [unit])!;
            Assert.IsFalse(units.Scales, "a unit does not scale");
            Assert.IsTrue(units.Turns);

            SelectionFrame oneArea = SelectionFrame.Of(document, [area])!;
            Assert.IsFalse(oneArea.Turns, "an area does not turn");
            Assert.IsTrue(oneArea.ScalesSides);
            Assert.AreEqual(new Vector2(20, 15), oneArea.Middle);

            Assert.IsNull(SelectionFrame.Of(document, [ObjectRef.Prop(99999)]), "nothing that exists");
        }

        [TestMethod]
        public void TheBoxOfDecalsThatFaceOneWayIsTurnedWithThem()
        {
            MapDocument document = WithThings(out _, out ObjectRef decal, out _, out _);
            document = document.SetHeading([decal], MathF.PI / 6);

            SelectionFrame frame = SelectionFrame.Of(document, [decal])!;

            Assert.AreEqual(MathF.PI / 6, frame.Heading, 1e-5);
            Assert.AreEqual(4, frame.HalfSize.X, 1e-3, "half the width along its own axis");
            Assert.AreEqual(2, frame.HalfSize.Y, 1e-3);
            Vector3 centre = document.PlaceOf(decal)!.Value;
            Assert.AreEqual(centre.X, frame.Centre.X, 1e-3);
            Vector2 local = frame.ToLocal(new Vector2(centre.X, centre.Z) + new Vector2(MathF.Cos(MathF.PI / 6), MathF.Sin(MathF.PI / 6)) * 3);
            Assert.AreEqual(3, local.X, 1e-4, "its own x axis");
            Assert.AreEqual(0, local.Y, 1e-4);
        }

        [TestMethod]
        public void TheHandlesKeepTheirSizeOnScreenAndTheMouseFindsThem()
        {
            SelectionFrame frame = new SelectionFrame(new Vector3(100, 5, 100), 0, new Vector2(2, 1), Turns: true, Scales: true, ScalesSides: true);
            // two pixels an ogrid: a view like the top view, the map's origin at the view's
            Vector2? Screen(Vector3 place) => new Vector2(place.X * 2, place.Z * 2);

            HandleLayout near = HandleLayout.Of(frame, 0.5f);
            HandleLayout far = HandleLayout.Of(frame, 2f);
            Assert.AreEqual(4 * near.RingRadius, far.RingRadius, 1e-3, "four times as large a pixel: four times as far on the map");
            Assert.AreEqual(18, near.Half.X, 1e-4, "at least 36 pixels across");

            Vector2 centre = new Vector2(200, 200);
            Assert.AreEqual(new HandlePart(HandleKind.Move), near.HitTest(centre + new Vector2(3, -2), Screen));
            Assert.AreEqual(new HandlePart(HandleKind.MoveX), near.HitTest(centre + new Vector2(20, 1), Screen));
            Assert.AreEqual(new HandlePart(HandleKind.MoveZ), near.HitTest(centre + new Vector2(0, -20), Screen), "z points up the view");
            Assert.AreEqual(new HandlePart(HandleKind.Scale, 1, 1), near.HitTest(centre + new Vector2(36, 36), Screen));
            Assert.AreEqual(new HandlePart(HandleKind.Scale, -1, 0), near.HitTest(centre + new Vector2(-36, 1), Screen));
            float ring = near.RingRadius * 2;
            Assert.AreEqual(new HandlePart(HandleKind.Turn), near.HitTest(centre + new Vector2(0, ring + 3), Screen));
            Assert.IsTrue(near.HitTest(centre + new Vector2(ring + 30, 0), Screen).IsNone);

            HandleLayout props = HandleLayout.Of(frame with { ScalesSides = false }, 0.5f);
            Assert.AreEqual(4, props.Scales.Count, "corners only");
            HandleLayout units = HandleLayout.Of(frame with { Scales = false, Turns = false }, 0.5f);
            Assert.AreEqual(0, units.Scales.Count);
            Assert.IsTrue(units.HitTest(centre + new Vector2(0, ring), Screen).IsNone, "no ring");
        }

        [TestMethod]
        public void AMoveGoesAlongOneAxisAndSnapsTheCentreToTheGrid()
        {
            Vector2 centre = new Vector2(10.3f, 20.2f);

            Assert.AreEqual(new Vector2(3.4f, -1.2f), HandleMaths.MoveDelta(centre, new Vector2(3.4f, -1.2f), HandleAxis.Both, 0));
            Assert.AreEqual(new Vector2(3.4f, 0), HandleMaths.MoveDelta(centre, new Vector2(3.4f, -1.2f), HandleAxis.X, 0));
            Assert.AreEqual(new Vector2(0, -1.2f), HandleMaths.MoveDelta(centre, new Vector2(3.4f, -1.2f), HandleAxis.Z, 0));

            Vector2 snapped = HandleMaths.MoveDelta(centre, new Vector2(3.4f, -1.2f), HandleAxis.Both, 1);
            Assert.AreEqual(14, centre.X + snapped.X, 1e-4, "the centre on a whole ogrid");
            Assert.AreEqual(19, centre.Y + snapped.Y, 1e-4);
            Vector2 half = HandleMaths.MoveDelta(centre, new Vector2(3.4f, 0), HandleAxis.X, 0.5f);
            Assert.AreEqual(13.5f, centre.X + half.X, 1e-4);
            Assert.AreEqual(0, half.Y, "the locked axis stays");
            Vector2 four = HandleMaths.MoveDelta(centre, new Vector2(3.4f, 9), HandleAxis.Both, 4);
            Assert.AreEqual(12, centre.X + four.X, 1e-4);
            Assert.AreEqual(28, centre.Y + four.Y, 1e-4);
        }

        [TestMethod]
        public void ATurnIsTheAngleTheMouseWentAboutTheCentreSnappedToSteps()
        {
            Vector2 centre = new Vector2(10, 10);
            float quarter = MathF.PI / 2;

            Assert.AreEqual(quarter, HandleMaths.TurnAngle(centre, new Vector2(20, 10), new Vector2(10, 20), 0), 1e-5, "from +x towards +z");
            Assert.AreEqual(-quarter, HandleMaths.TurnAngle(centre, new Vector2(20, 10), new Vector2(10, 0), 0), 1e-5);
            float step = 15 * MathF.PI / 180;
            float raw = HandleMaths.TurnAngle(centre, new Vector2(20, 10), centre + new Vector2(MathF.Cos(0.4f), MathF.Sin(0.4f)) * 7, 0);
            Assert.AreEqual(0.4f, raw, 1e-5);
            Assert.AreEqual(2 * step, HandleMaths.TurnAngle(centre, new Vector2(20, 10), centre + new Vector2(MathF.Cos(0.4f), MathF.Sin(0.4f)) * 7, step), 1e-5, "23 degrees snaps to 30");
            Assert.AreEqual(0, HandleMaths.TurnAngle(centre, centre, new Vector2(3, 3), 0), "on the centre: no turn");
        }

        [TestMethod]
        public void ScalingFollowsTheSideOrCornerDragged()
        {
            SelectionFrame frame = new SelectionFrame(new Vector3(100, 0, 100), MathF.PI / 2, new Vector2(4, 2), true, true, true);
            // turned a quarter: the box's own x runs along the map's z
            Vector2 side = HandleMaths.ScaleFactors(frame, new HandlePart(HandleKind.Scale, 1, 0), new Vector2(100, 104), new Vector2(100, 106), 0);
            Assert.AreEqual(1.5f, side.X, 1e-4);
            Assert.AreEqual(1, side.Y);

            SelectionFrame level = frame with { Heading = 0 };
            Vector2 corner = HandleMaths.ScaleFactors(level, new HandlePart(HandleKind.Scale, 1, 1), new Vector2(104, 102), new Vector2(108, 104), 0);
            Assert.AreEqual(2, corner.X, 1e-4, "both alike");
            Assert.AreEqual(2, corner.Y, 1e-4);
            Vector2 snapped = HandleMaths.ScaleFactors(level, new HandlePart(HandleKind.Scale, 0, -1), new Vector2(100, 98), new Vector2(100, 96.75f), 0.1f);
            Assert.AreEqual(1.6f, snapped.Y, 1e-4, "1.625 snaps to a tenth");
            Vector2 inverted = HandleMaths.ScaleFactors(level, new HandlePart(HandleKind.Scale, 1, 0), new Vector2(104, 100), new Vector2(96, 100), 0);
            Assert.AreEqual(0.01f, inverted.X, 1e-6, "never through the centre");
        }

        [TestMethod]
        public void ARayMeetsTheLevelOfTheHandles()
        {
            Vector2 hit = HandleMaths.PlaneHit(new Vector3(10, 50, 0), Vector3.Normalize(new Vector3(0, -45, 20)), 5)!.Value;
            Assert.AreEqual(10, hit.X, 1e-3);
            Assert.AreEqual(20, hit.Y, 1e-3);
            Assert.IsNull(HandleMaths.PlaneHit(new Vector3(0, 50, 0), new Vector3(0, 1, 0), 5), "away from it");
            Assert.IsNull(HandleMaths.PlaneHit(new Vector3(0, 50, 0), new Vector3(1, 0, 0), 5), "along it");
        }

        [TestMethod]
        public void TurningAboutTheSelectionsCentreTakesTheThingsRound()
        {
            MapDocument document = WithThings(out ObjectRef prop, out ObjectRef decal, out ObjectRef unit, out ObjectRef area);
            Vector2 pivot = new Vector2(60, 70);
            float quarter = MathF.PI / 2;

            MapDocument turned = document.RotateObjectsAbout([prop, unit, decal, area], quarter, pivot);

            Vector3 propAt = turned.PlaceOf(prop)!.Value;
            Assert.AreEqual(70, propAt.X, 1e-3, "(50, 60) goes a quarter round (60, 70) to (70, 60)");
            Assert.AreEqual(60, propAt.Z, 1e-3);
            Assert.AreEqual(turned.Terrain.Heightmap.HeightAt(70, 60), propAt.Y, 1e-3, "on the ground");
            Assert.AreEqual(quarter, turned.HeadingOf(prop)!.Value, 1e-4, "and it turns itself");
            Vector3 unitAt = turned.PlaceOf(unit)!.Value;
            Assert.AreEqual(50, unitAt.X, 1e-3);
            Assert.AreEqual(80, unitAt.Z, 1e-3);
            Assert.AreEqual(quarter, turned.HeadingOf(unit)!.Value, 1e-4);
            Vector3 decalCentre = document.PlaceOf(decal)!.Value;
            Vector3 decalAt = turned.PlaceOf(decal)!.Value;
            Assert.AreEqual(pivot.X - (decalCentre.Z - pivot.Y), decalAt.X, 1e-3);
            Assert.AreEqual(pivot.Y + (decalCentre.X - pivot.X), decalAt.Z, 1e-3);
            Area areaAfter = (Area)turned.Find(area)!;
            Assert.AreEqual(20, areaAfter.X1 - areaAfter.X0, 1e-6, "an area keeps its size and goes round");
            Assert.AreEqual(pivot.X - (15 - pivot.Y), (areaAfter.X0 + areaAfter.X1) / 2, 1e-3);

            MapDocument each = document.RotateObjectsAbout([prop], quarter, new Vector2(50, 60));
            Assert.AreEqual(document.PlaceOf(prop)!.Value.X, each.PlaceOf(prop)!.Value.X, 1e-3, "about its own centre it stays");
        }

        [TestMethod]
        public void ScalingSpreadsFromThePivotOrScalesEachAboutItsCentre()
        {
            MapDocument document = WithThings(out ObjectRef prop, out ObjectRef decal, out ObjectRef unit, out ObjectRef area);
            Vector2 pivot = new Vector2(60, 70);

            MapDocument spread = document.ScaleObjects([prop, unit, decal, area], 2, 2, 0, pivot);

            Assert.AreEqual(2, ((Prop)spread.Find(prop)!).Scale.X, 1e-5);
            Assert.AreEqual(40, spread.PlaceOf(prop)!.Value.X, 1e-3, "twice as far from the pivot");
            Assert.AreEqual(80, spread.PlaceOf(unit)!.Value.X, 1e-3, "units spread too");
            Decal scaled = (Decal)spread.Find(decal)!;
            Assert.AreEqual(16, scaled.Scale.X, 1e-4);
            Assert.AreEqual(8, scaled.Scale.Z, 1e-4);
            Area grown = (Area)spread.Find(area)!;
            Assert.AreEqual(40, grown.X1 - grown.X0, 1e-6);

            MapDocument each = document.ScaleObjects([prop, unit, decal, area], 1.5f, 1, 0);
            Assert.AreEqual(1.5f, ((Prop)each.Find(prop)!).Scale.Y, 1e-5, "a prop alike along every axis");
            Assert.AreEqual(document.PlaceOf(prop)!.Value.X, each.PlaceOf(prop)!.Value.X, 1e-4, "about its own centre");
            Assert.AreEqual(document.PlaceOf(unit), each.PlaceOf(unit), "a unit stays");
            Assert.AreEqual(12, ((Decal)each.Find(decal)!).Scale.X, 1e-4, "the width only");
            Assert.AreEqual(4, ((Decal)each.Find(decal)!).Scale.Z, 1e-4);
            Assert.AreEqual(document.PlaceOf(decal)!.Value.X, each.PlaceOf(decal)!.Value.X, 1e-3);
            Area wider = (Area)each.Find(area)!;
            Assert.AreEqual(5, wider.X0, 1e-6);
            Assert.AreEqual(35, wider.X1, 1e-6);
        }
    }
}
