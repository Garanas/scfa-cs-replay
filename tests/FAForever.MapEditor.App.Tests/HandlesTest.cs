using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The handles of the selection (user story F-11): dragging the move square, the arrows, the
    /// ring and the scale squares with the select tools, each one step of undo; snapping with shift,
    /// one axis with X or Z; the commands that set them, turn, scale and focus the camera; the sun's
    /// arrow in 3D.
    /// </summary>
    [TestClass]
    public class HandlesTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private static async Task<MainViewModel> Open(string tool = "markers.select")
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            await model.OpenAsync(HardFfa);
            model.SelectTool(tool);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        /// <summary>
        /// Two deposits selected, and where their handles lie without a view (two pixels an ogrid).
        /// </summary>
        private static (SelectTool Tool, ObjectRef A, ObjectRef B, HandleLayout Layout) TwoDeposits(MainViewModel model)
        {
            MarkerSet markers = model.ShownDocument!.Markers;
            ObjectRef a = ObjectRef.Marker(markers.Resources[0].Id);
            ObjectRef b = ObjectRef.Marker(markers.Resources[1].Id);
            model.Selection.Set([a, b]);
            SelectTool tool = (SelectTool)model.ActiveTool!.Tool!;
            return (tool, a, b, HandleLayout.Of(tool.Frame!, ToolInput.Default.OgridsPerPixel));
        }

        private static void Drag(IMapTool tool, Vector3 from, Vector2 to, ToolModifiers modifiers = ToolModifiers.None)
        {
            ToolInput input = ToolInput.Default with { Modifiers = modifiers };
            tool.Press(from.X, from.Z, input);
            Vector2 start = new Vector2(from.X, from.Z);
            for (int i = 1; i <= 4; i++)
            {
                Vector2 at = Vector2.Lerp(start, to, i / 4f);
                tool.Drag(at.X, at.Y, input);
            }
            tool.Release();
        }

        private static Vector2 Flat(ObjectRef thing, MapDocument document) => new Vector2(document.PlaceOf(thing)!.Value.X, document.PlaceOf(thing)!.Value.Z);

        [TestMethod]
        public async Task TheArrowsAndXOrZMoveAlongOneAxisAndShiftSnapsTheCentre()
        {
            MainViewModel model = await Open();
            (SelectTool tool, ObjectRef a, _, HandleLayout layout) = TwoDeposits(model);
            MapDocument before = model.ShownDocument!;
            Vector3 arrow = Vector3.Lerp(layout.ArrowX.From, layout.ArrowX.To, 0.5f);

            Assert.AreEqual(new HandlePart(HandleKind.MoveX), tool.HandleAt(arrow.X, arrow.Z, ToolInput.Default));
            Drag(tool, arrow, new Vector2(arrow.X + 10, arrow.Z + 7));

            Assert.AreEqual(Flat(a, before) + new Vector2(10, 0), Flat(a, model.ShownDocument!), "along x only");
            Assert.AreEqual("Move the selection", model.Map!.Session.UndoDescription);
            Assert.AreEqual(2, model.History.Count, "one step");

            // the middle square with Z held and shift: along z, the centre on the grid of the settings
            model.Handles.Snap = model.Handles.SnapOf(4);
            Vector3 centre = tool.Frame!.Centre;
            Drag(tool, centre, new Vector2(centre.X + 9, centre.Z + 9.3f), ToolModifiers.AxisZ | ToolModifiers.Shift);
            Vector3 moved = tool.Frame!.Centre;
            Assert.AreEqual(centre.X, moved.X, 1e-3, "Z held: x stays");
            Assert.AreEqual(0, moved.Z % 4, 1e-3, "on the grid of 4 ogrids");
            Assert.AreEqual(3, model.History.Count);
        }

        [TestMethod]
        public async Task TheRingTurnsAboutTheSelectionsCentreOrEachThingsOwnAndShiftSnaps()
        {
            MainViewModel model = await Open();
            (SelectTool tool, ObjectRef a, ObjectRef b, HandleLayout layout) = TwoDeposits(model);
            MapDocument before = model.ShownDocument!;
            Vector2 middle = tool.Frame!.Middle;
            Vector3 ring = layout.Centre + new Vector3(layout.RingRadius, 0, 0);
            Assert.AreEqual(new HandlePart(HandleKind.Turn), tool.HandleAt(ring.X, ring.Z, ToolInput.Default));

            // a little short of a quarter, clockwise in the top view: shift makes it 90 degrees
            Vector2 to = middle + new Vector2(MathF.Cos(1.45f), MathF.Sin(1.45f)) * layout.RingRadius;
            Drag(tool, ring, to, ToolModifiers.Shift);

            Vector2 d = Flat(a, before) - middle;
            Assert.AreEqual(middle.X - d.Y, Flat(a, model.ShownDocument!).X, 1e-2, "a quarter round the centre");
            Assert.AreEqual(middle.Y + d.X, Flat(a, model.ShownDocument!).Y, 1e-2);
            Assert.AreEqual(90, SelectionInspector.Degrees(model.ShownDocument!.HeadingOf(b)!.Value), 1e-2, "and each turns");
            Assert.AreEqual("Turn the selection", model.Map!.Session.UndoDescription);
            model.Undo();

            model.Handles.Pivot = HandlePivot.Each;
            Drag(tool, ring, to);
            Assert.AreEqual(Flat(a, before), Flat(a, model.ShownDocument!), "each about its own centre: it stays");
            Assert.AreEqual(1.45f, model.ShownDocument!.HeadingOf(a)!.Value, 1e-2, "unsnapped");
        }

        [TestMethod]
        public async Task ACornerScalesPropsAndSpreadsThemAndAClickOnAHandleStillSelects()
        {
            MainViewModel model = await Open("props.select");
            MapDocument document = model.ShownDocument!;
            ObjectRef a = ObjectRef.Prop(document.Props[0].Id);
            ObjectRef b = ObjectRef.Prop(document.Props[1].Id);
            model.Selection.Set([a, b]);
            SelectTool tool = (SelectTool)model.ActiveTool!.Tool!;
            HandleLayout layout = HandleLayout.Of(tool.Frame!, ToolInput.Default.OgridsPerPixel);
            Assert.IsFalse(tool.Frame!.ScalesSides, "props: corners only");
            (HandlePart corner, Vector3 place) = layout.Scales[2];
            Vector2 middle = tool.Frame.Middle;

            Drag(tool, place, middle + (new Vector2(place.X, place.Z) - middle) * 1.5f);

            Prop scaled = (Prop)model.ShownDocument!.Find(a)!;
            float before = document.Props[0].Scale == Vector3.Zero ? 1 : document.Props[0].Scale.X;
            Assert.AreEqual(1.5f * before, scaled.Scale.X, 1e-3, corner.ToString());
            Assert.AreEqual(middle.X + (Flat(a, document).X - middle.X) * 1.5f, Flat(a, model.ShownDocument!).X, 1e-2, "spread from the centre");
            Assert.AreEqual("Scale the selection", model.Map!.Session.UndoDescription);

            // a click on the middle square without a drag clicks what lies there: nothing, so nothing is selected
            int steps = model.History.Count;
            Vector3 centre = tool.Frame!.Centre;
            tool.Press(centre.X, centre.Z, ToolInput.Default with { Picks = true, Picked = null });
            tool.Release();
            Assert.IsTrue(model.Selection.IsEmpty);
            Assert.AreEqual(steps, model.History.Count, "a click changes nothing on the map");
        }

        [TestMethod]
        public async Task HiddenHandlesAreNotTakenAndTheShapesShowTheOneUnderTheMouse()
        {
            MainViewModel model = await Open();
            (SelectTool tool, _, _, HandleLayout layout) = TwoDeposits(model);
            Vector3 centre = layout.Centre;
            ToolView view = ToolView.Flat(0.5f, new Vector2(centre.X, centre.Z));

            IReadOnlyList<ToolShape> shapes = tool.Shapes(view);
            Assert.IsTrue(shapes.OfType<ToolSquare>().Any(square => square.Fill == ToolInk.Hover), "the middle square is lit");
            Assert.AreEqual(new HandlePart(HandleKind.Move), tool.Hovered);
            Assert.IsTrue(shapes.OfType<ToolLine>().Any(line => line.Closed && line.Points.Length == 64), "the ring");

            await Run(model, "select.handles", new JsonObject { ["show"] = false });
            Assert.AreEqual(0, tool.Shapes(view).Count);
            Assert.IsTrue(tool.HandleAt(centre.X, centre.Z, ToolInput.Default).IsNone);
            JsonObject result = (JsonObject)(await Run(model, "select.handles", new JsonObject { ["pivot"] = "each", ["snap"] = 0.4 })).Result!;
            Assert.AreEqual("each", (string?)result["pivot"]);
            Assert.AreEqual(0.5f, model.Handles.Snap.Step, "the nearest grid");
            await Run(model, "select.handles");
            Assert.IsTrue(model.Handles.Show, "without arguments: on and off");
        }

        [TestMethod]
        public async Task TheCommandsTurnScaleAndFocusAndTheStatusBarShowsTheBounds()
        {
            MainViewModel model = await Open();
            (_, ObjectRef a, ObjectRef b, _) = TwoDeposits(model);
            MapDocument before = model.ShownDocument!;
            SelectionFrame frame = SelectionFrame.Of(before, [a, b])!;
            StringAssert.StartsWith(model.SelectionStatus, "2 selected, ");
            StringAssert.Contains(model.SelectionStatus, "ogrids");

            await Run(model, "edit.rotate", new JsonObject { ["degrees"] = 90 });
            Vector2 d = Flat(a, before) - frame.Middle;
            Assert.AreEqual(frame.Middle.X - d.Y, Flat(a, model.ShownDocument!).X, 1e-2, "about the selection's centre, the handles' pivot");
            await Run(model, "edit.rotate", new JsonObject { ["degrees"] = -90, ["about"] = "each" });
            Assert.AreEqual(frame.Middle.X - d.Y, Flat(a, model.ShownDocument!).X, 1e-2, "each: it stays where it is");

            model.Undo();
            model.Undo();
            await Run(model, "edit.scale", new JsonObject { ["factor"] = 2 });
            Assert.AreEqual(frame.Middle.X + 2 * d.X, Flat(a, model.ShownDocument!).X, 0.501, "markers spread from the centre (a deposit to the nearest ogrid centre)");
            Assert.AreEqual("Scale the selection", model.Map!.Session.UndoDescription);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "edit.scale", new JsonObject { ["factor"] = 0 }), "a scale is above 0");

            Assert.IsTrue((await Run(model, "camera.focusSelection")).Ran);
            for (int i = 0; i < 100 && model.Camera.Update(0.1f); i++)
            {
            }
            Vector3 middle = SelectionFrame.Of(model.ShownDocument!, model.Selection.Items)!.Centre;
            Assert.AreEqual(middle.X, model.Camera.Now.X, 0.5, "the camera looks at the selection");
            Assert.AreEqual(middle.Z, model.Camera.Now.Z, 0.5);
            model.Selection.Clear();
            Assert.AreEqual("", model.SelectionStatus);
            Assert.IsFalse(model.Commands.CanRun(model.Commands.Find("camera.focusSelection")!), "nothing selected");
        }

        [TestMethod]
        public async Task TheDecalsSelectToolTakesTheHandlesFirst()
        {
            MainViewModel model = await Open("decals.select");
            CommandRun placed = await Run(model, "decals.place", new JsonObject { ["x"] = 60, ["z"] = 60, ["key"] = "env/evergreen/decals/e_mud001_albedo.dds", ["width"] = 8, ["length"] = 4 });
            ObjectRef decal = ObjectRef.Parse((string)placed.Result!);
            model.Selection.Set([decal]);
            DecalSelectTool tool = model.Tool<DecalSelectTool>();
            HandleLayout layout = HandleLayout.Of(tool.Select.Frame!, ToolInput.Default.OgridsPerPixel);
            (HandlePart side, Vector3 place) = layout.Scales.First(scale => scale.Part == new HandlePart(HandleKind.Scale, 1, 0));
            float width = ((Decal)model.ShownDocument!.Find(decal)!).Scale.X;
            float length = ((Decal)model.ShownDocument!.Find(decal)!).Scale.Z;

            Drag(tool, place, new Vector2(60 + (place.X - 60) * 2, place.Z));

            Decal scaled = (Decal)model.ShownDocument!.Find(decal)!;
            Assert.AreEqual(2 * width, scaled.Scale.X, 1e-2, side.ToString());
            Assert.AreEqual(length, scaled.Scale.Z, 1e-3, "a side: the width only");
            Assert.IsTrue(tool.Shapes(ToolView.Flat(0.5f)).Count > 0, "it draws the handles of the select tool it wraps");
        }

        [TestMethod]
        public async Task TheSunsArrowPointsAtTheSunAndARayTurnsIt()
        {
            MainViewModel model = await Open("lighting.sun");
            MapDocument document = model.ShownDocument!;
            (Vector3 from, Vector3 to) = SunTool.Arrow(document);
            Assert.AreEqual(Vector3.Normalize(document.Lighting.SunDirection).Y, Vector3.Normalize(to - from).Y, 1e-4);

            // a ray from above the middle, down onto the sphere's west side
            Vector3 direction = SunTool.DirectionAt(document, from + new Vector3(-30, 500, 0), new Vector3(0, -1, 0));
            Assert.IsTrue(direction.X < 0, "towards the west");
            Assert.IsTrue(direction.Y > 0);
            Vector3 low = SunTool.DirectionAt(document, from + new Vector3(-1000, 0, 0), new Vector3(0, 0, 1));
            Assert.AreEqual(Math.Sin(SunTool.LowestElevation * Math.PI / 180), low.Y, 1e-3, "never below the lowest elevation");
        }
    }
}
