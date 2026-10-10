using System.Numerics;
using FAForever.MapEditor.Project.Files;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Ways to select more easily (user story F-11): select similar, invert, inside an area, the
    /// selection filter (locked kinds), hidden kinds, isolating the selection, R and a drag to turn,
    /// and a box that leaves the playable area alone.
    /// </summary>
    [TestClass]
    public class SelectionToolsTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";
        private const string Rock = "env/evergreen/props/rocks/rock01_prop.bp";
        private const string Pine = "env/evergreen/props/trees/pine06_prop.bp";
        private const string PineVariant = "env/evergreen/props/trees/pine06_v1_prop.bp";

        private static async Task<MainViewModel> Open(string tool = "markers.select")
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Assets.Use(TestAssets.Catalog());
            await model.OpenAsync(HardFfa);
            model.SelectTool(tool);
            return model;
        }

        private static async Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            CommandRun run = await model.Commands.RunAsync(id, arguments ?? [], interactive: false);
            Assert.IsTrue(run.Ran, id);
            return run;
        }

        private static async Task<ObjectRef> Place(MainViewModel model, string key, float x, float z)
        {
            await Run(model, "props.place", new JsonObject { ["key"] = key, ["x"] = x, ["z"] = z, ["heading"] = 0 });
            return model.Selection.Items.Single();
        }

        [TestMethod]
        public async Task ABoxInTheMarkersModeLeavesThePlayableAreaAlone()
        {
            MainViewModel model = await Open();
            await Run(model, "map.addArea", new JsonObject { ["x0"] = 0, ["z0"] = 0, ["x1"] = 256, ["z1"] = 256 });
            model.SelectTool("markers.select");
            model.Selection.Clear();

            model.SelectInside(-10, -10, 270, 270, SelectionChange.Replace);
            Assert.IsTrue(model.Selection.HasAny);
            Assert.IsFalse(model.Selection.Items.Any(thing => thing.Kind == ObjectKind.Area), "a box over markers takes no area");
            await Run(model, "select.all");
            Assert.IsFalse(model.Selection.Items.Any(thing => thing.Kind == ObjectKind.Area), "nor does Select all");

            model.SelectTool("map.areas");
            model.SelectInside(-10, -10, 270, 270, SelectionChange.Replace);
            CollectionAssert.AreEqual(new[] { ObjectRef.Area("AREA_1") }, model.Selection.Items.ToArray(), "the Map mode's tools pick areas alone: a box takes them");
        }

        [TestMethod]
        public async Task SimilarThingsAreSelectedByBlueprintOrFamily()
        {
            MainViewModel model = await Open("props.select");
            ObjectRef pine = await Place(model, Pine, 40, 40);
            ObjectRef pine2 = await Place(model, Pine, 60, 40);
            ObjectRef variant = await Place(model, PineVariant, 80, 40);
            ObjectRef rock = await Place(model, Rock, 100, 40);

            model.Selection.Set([pine]);
            await Run(model, "select.similar");
            CollectionAssert.IsSubsetOf(new[] { pine, pine2 }, model.Selection.Items.ToArray());
            Assert.IsFalse(model.Selection.Contains(variant) || model.Selection.Contains(rock), "the same blueprint only");

            model.Selection.Set([pine]);
            await Run(model, "select.similar", new JsonObject { ["family"] = true });
            Assert.IsTrue(model.Selection.Contains(variant), "the family: every pine06");
            Assert.IsFalse(model.Selection.Contains(rock));

            // deposits of the same resource, in the Markers mode
            model.SelectTool("markers.select");
            ResourceMarker mass = model.ShownDocument!.Markers.Resources.First(resource => resource.Kind == ResourceKind.Mass);
            model.Selection.Set([ObjectRef.Marker(mass.Id)]);
            int masses = model.ShownDocument.Markers.Resources.Count(resource => resource.Kind == ResourceKind.Mass);
            Assert.AreEqual(masses, (await Run(model, "select.similar")).Result);
        }

        [TestMethod]
        public async Task TheSelectionIsInvertedAndSelectedInsideAnArea()
        {
            MainViewModel model = await Open();
            int markers = model.ShownDocument!.Markers.All.Count(marker => marker is not PathNode);
            ObjectRef first = ObjectRef.Marker(model.ShownDocument.Markers.Spawns[0].Id);
            model.Selection.Set([first]);
            await Run(model, "select.invert");
            Assert.AreEqual(markers - 1, model.Selection.Count, "every other marker");
            Assert.IsFalse(model.Selection.Contains(first));

            Spawn spawn = model.ShownDocument.Markers.Spawns[0];
            await Run(model, "map.addArea", new JsonObject { ["name"] = "BASE", ["x0"] = spawn.Position.X - 30, ["z0"] = spawn.Position.Z - 30, ["x1"] = spawn.Position.X + 30, ["z1"] = spawn.Position.Z + 30 });
            model.SelectMode("markers");
            model.Selection.Set([ObjectRef.Area("BASE")]);
            await Run(model, "select.insideArea");
            Assert.IsTrue(model.Selection.Contains(ObjectRef.Marker(spawn.Id)), "the spawn inside the selected area");
            Assert.IsTrue(model.Selection.Items.All(thing => model.ShownDocument.PlaceOf(thing) is Vector3 place
                && MathF.Abs(place.X - spawn.Position.X) <= 30.01f && MathF.Abs(place.Z - spawn.Position.Z) <= 30.01f), "only what lies inside");
            Assert.IsFalse(model.Selection.Contains(ObjectRef.Area("BASE")));
        }

        [TestMethod]
        public async Task ALockedKindIsNotPickedAndTheFilterShowsIt()
        {
            MainViewModel model = await Open();
            SelectTool tool = (SelectTool)model.ActiveTool!.Tool!;
            CollectionAssert.AreEqual(new[] { "Markers", "Path nodes", "Areas" }, tool.Filters.Select(item => item.Title).ToArray(), "the kinds the Markers mode's tool picks");
            Spawn spawn = model.ShownDocument!.Markers.Spawns[0];
            model.Selection.Set([ObjectRef.Marker(spawn.Id)]);

            await Run(model, "select.lockKind", new JsonObject { ["kind"] = "markers" });
            Assert.IsTrue(model.LockedKinds.HasFlag(ObjectKinds.Markers));
            Assert.IsFalse(tool.Filters[0].IsOn, "its check box is unchecked");
            Assert.IsTrue(model.Selection.IsEmpty, "what is locked leaves the selection");
            Assert.IsNull(model.PickAt(spawn.Position.X, spawn.Position.Z, 0.5f), "a click passes it by");
            model.SelectInside(0, 0, 256, 256, SelectionChange.Replace);
            Assert.IsFalse(model.Selection.Items.Any(thing => !model.ShownDocument.IsPathNode(thing)), "a box too");

            tool.Filters[0].IsOn = true;
            await Task.Yield();
            Assert.IsFalse(model.LockedKinds.HasFlag(ObjectKinds.Markers), "checked again: unlocked");
            Assert.AreEqual(ObjectRef.Marker(spawn.Id), model.PickAt(spawn.Position.X, spawn.Position.Z, 0.5f));
        }

        [TestMethod]
        public async Task HiddenDecalsAreNeitherDrawnNorPicked()
        {
            MainViewModel model = await Open("decals.select");
            await Run(model, "decals.place", new JsonObject { ["key"] = "env/common/decals/mud.dds", ["x"] = 60, ["z"] = 40, ["width"] = 10 });
            ObjectRef decal = model.Selection.Items.Single();
            Assert.AreEqual(decal, model.PickAt(60, 40, 0.5f));

            Assert.IsTrue((bool)(await Run(model, "select.hideKind", new JsonObject { ["kind"] = "decals" })).Result!);
            Assert.IsTrue(model.HiddenDecalIds.Contains(decal.Id), "the 3D view leaves it out");
            Assert.IsNull(model.PickAt(60, 40, 0.5f), "and it is not picked");
            Assert.IsTrue(model.Selection.IsEmpty);

            await Run(model, "select.hideKind", new JsonObject { ["kind"] = "decals", ["hidden"] = false });
            Assert.AreEqual(0, model.HiddenDecalIds.Count);
            await Run(model, "select.hideKind", new JsonObject { ["kind"] = "props" });
            Assert.IsFalse(model.ShowProps, "the other kinds are the overlays");
        }

        [TestMethod]
        public async Task AnIsolatedSelectionAloneShowsAndCanBePicked()
        {
            MainViewModel model = await Open();
            MarkerSet markers = model.ShownDocument!.Markers;
            ObjectRef a = ObjectRef.Marker(markers.Spawns[0].Id);
            ObjectRef b = ObjectRef.Marker(markers.Spawns[1].Id);
            // the spawn furthest from both
            Spawn other = markers.Spawns.Skip(2).MaxBy(spawn => MathF.Min(Vector3.Distance(spawn.Position, markers.Spawns[0].Position), Vector3.Distance(spawn.Position, markers.Spawns[1].Position)))!;
            model.Selection.Set([a, b]);

            Assert.AreEqual(2, (await Run(model, "select.isolate")).Result);
            Assert.IsNull(model.PickAt(other.Position.X, other.Position.Z, 0.05f), "the others are not picked",", model.Isolated!)} other {other.Id} {other.Position}");
            await Run(model, "select.all");
            CollectionAssert.AreEquivalent(new[] { a, b }, model.Selection.Items.ToArray(), "Select all takes the isolated alone");
            Assert.AreEqual(2, model.DrawnMarkers(model.ShownDocument).All.Count(), "the top view draws them alone");
            Assert.IsFalse(model.IsDrawn(ObjectRef.Marker(other.Id)));

            Assert.AreEqual(0, (await Run(model, "select.isolate")).Result, "again: everything shows");
            Assert.IsNull(model.Isolated);
            Assert.AreEqual(ObjectRef.Marker(other.Id), model.PickAt(other.Position.X, other.Position.Z, 0.05f));
            model.Selection.Clear();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => model.Commands.RunAsync("select.isolate", [], interactive: false), "nothing to isolate");
        }

        [TestMethod]
        public async Task RAndADragTurnTheSelection()
        {
            MainViewModel model = await Open("props.select");
            ObjectRef a = await Place(model, Rock, 100, 100);
            ObjectRef b = await Place(model, Rock, 120, 100);
            model.Selection.Set([a, b]);
            await Run(model, "select.handles", new JsonObject { ["pivot"] = "selection" });
            SelectTool tool = (SelectTool)model.ActiveTool!.Tool!;
            ToolInput turning = ToolInput.Default with { Modifiers = ToolModifiers.Turn };

            // from right of the middle (110, 100) to below it: a quarter turn clockwise in the top view
            tool.Press(140, 100, turning);
            for (int i = 1; i <= 8; i++)
            {
                float angle = i / 8f * MathF.PI / 2;
                tool.Drag(110 + 30 * MathF.Cos(angle), 100 + 30 * MathF.Sin(angle), turning);
            }
            tool.Release();

            Assert.AreEqual("Turn the selection", model.Map!.Session.UndoDescription);
            Vector3 turned = model.ShownDocument!.PlaceOf(a)!.Value;
            Assert.AreEqual(110, turned.X, 0.05, "about the selection's middle");
            Assert.AreEqual(90, turned.Z, 0.05);
            Assert.AreEqual(MathF.PI / 2, model.ShownDocument.HeadingOf(a)!.Value, 1e-3);
        }
    }

    /// <summary>
    /// The window's side of the selection tools: R held in the top view turns the selection with a
    /// drag, and the Markers mode's select tool shows its filter in the options.
    /// </summary>
    [TestClass]
    public sealed class SelectionToolsWindowTest
    {
        [TestMethod]
        public Task RHeldInTheTopViewTurnsTheSelectionWithADrag() => HeadlessApp.Run(async () =>
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            model.View = MapViewTab.Top;
            model.SelectTool("markers.select");
            Dispatcher.UIThread.RunJobs();
            MarkerSet markers = model.ShownDocument!.Markers;
            ObjectRef a = ObjectRef.Marker(markers.Spawns[0].Id);
            ObjectRef b = ObjectRef.Marker(markers.Spawns[1].Id);
            model.Selection.Set([a, b]);
            await model.Commands.RunAsync("select.handles", new JsonObject { ["pivot"] = "selection", ["show"] = false }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            SelectTool tool = (SelectTool)model.ActiveTool!.Tool!;
            Vector2 middle = tool.Frame!.Middle;
            Point Window(float x, float z) => window.MapView.TranslatePoint(window.MapView.MapToControl(x, z), window)!.Value;
            float before = model.ShownDocument.HeadingOf(a)!.Value;

            // the view has the keyboard after a click on it: on a selected spawn, which keeps the selection
            Vector3 spawnA = model.ShownDocument.PlaceOf(a)!.Value;
            Point onA = Window(spawnA.X, spawnA.Z);
            window.MouseMove(onA);
            window.MouseDown(onA, MouseButton.Left);
            window.MouseUp(onA, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(2, model.Selection.Count);
            Point start = Window(middle.X + 20, middle.Y);
            window.MouseMove(start);
            window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
            window.MouseDown(start, MouseButton.Left);
            for (int i = 1; i <= 6; i++)
            {
                float angle = i / 6f * MathF.PI / 2;
                window.MouseMove(Window(middle.X + 20 * MathF.Cos(angle), middle.Y + 20 * MathF.Sin(angle)));
            }
            window.MouseUp(Window(middle.X, middle.Y + 20), MouseButton.Left);
            window.KeyRelease(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual("Turn the selection", model.Map!.Session.UndoDescription);
            Assert.AreEqual(90, (model.ShownDocument!.HeadingOf(a)!.Value - before) * 180 / MathF.PI, 2, "a quarter turn, as the mouse went");

            // the options of the Markers mode's select tool: its filter
            CheckBox areas = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Select areas");
            Assert.IsTrue(areas.IsChecked);
            areas.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(model.LockedKinds.HasFlag(ObjectKinds.Areas), "unchecked: areas are locked");
            window.Close();
        });
    }
}
