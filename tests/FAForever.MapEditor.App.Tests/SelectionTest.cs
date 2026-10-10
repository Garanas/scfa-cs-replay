using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Selecting things on the map and changing them (user story F-11): the select tool, the
    /// commands of the Edit and Select menus, the inspector's shared fields.
    /// </summary>
    [TestClass]
    public class SelectionTest
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

        private static IMapTool ActiveTool(MainViewModel model) => model.ActiveTool!.Tool!;

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        [TestMethod]
        public async Task WhatThe3DViewPicksIsSelectedWhereverTheGroundIs()
        {
            MainViewModel model = await Open();
            Spawn far = model.ShownDocument!.Markers.Spawns[^1];
            ObjectRef thing = ObjectRef.Marker(far.Id);
            IMapTool tool = ActiveTool(model);

            // the ray met the spawn; the ground behind it is elsewhere
            tool.Press(1, 1, new ToolInput(ToolModifiers.None, 0.5f, Picks: true, Picked: thing));
            tool.Release();
            CollectionAssert.AreEqual(new[] { thing }, model.Selection.Items.ToArray());

            tool.Press(far.Position.X, far.Position.Z, new ToolInput(ToolModifiers.None, 0.5f, Picks: true, Picked: null));
            tool.Release();
            Assert.IsTrue(model.Selection.IsEmpty, "the ray met nothing: the nearest on the ground does not count");

            tool.Press(1, 1, new ToolInput(ToolModifiers.None, 0.5f, Picks: true, Picked: ObjectRef.Prop(model.ShownDocument.Props[0].Id)));
            tool.Release();
            Assert.IsTrue(model.Selection.IsEmpty, "the Markers mode selects no props");
        }

        [TestMethod]
        public async Task ABoxInThe3DViewSelectsWhatShowsInsideTheRectangleOnScreen()
        {
            MainViewModel model = await Open();
            SelectTool tool = (SelectTool)ActiveTool(model);
            // a view that is not straight down: a place shows at (x * 3 + z, z * 2), so its box on the
            // ground would be wrong
            ToolView ViewWith(Vector2 mouse) => new ToolView(false, mouse, place => new Vector2(place.X * 3 + place.Z, place.Z * 2), _ => 0.5f);
            Vector2 Screen(Vector3 place) => new Vector2(place.X * 3 + place.Z, place.Z * 2);
            Spawn spawn = model.ShownDocument!.Markers.Spawns[0];
            Vector2 at = Screen(spawn.Position);
            Vector2 from = at - new Vector2(4, 4);
            Vector2 to = at + new Vector2(4, 4);

            tool.Press(0, 0, new ToolInput(ToolModifiers.None, 0.5f, Picks: true, Picked: null, View: ViewWith(from)));
            tool.Drag(5, 5, new ToolInput(ToolModifiers.None, 0.5f, View: ViewWith(to)));
            Assert.AreEqual((from, to), tool.ScreenBox);
            Assert.IsNull(tool.Box, "no box on the ground");
            IReadOnlyList<ToolShape> shapes = tool.Shapes(ViewWith(to));
            Assert.IsTrue(shapes.OfType<ToolLine>().Any(line => line.Closed && line.Points.Contains(from) && line.Points.Contains(to)), "the rectangle on screen");
            Assert.IsFalse(tool.Shapes(ToolView.Flat(0.5f)).OfType<ToolLine>().Any(line => line.Points.Contains(from)), "the top view draws no 3D box");
            tool.Release();

            ObjectRef[] expected = [.. model.ShownDocument.All(model.SelectableKinds, model.ShownMarkerKinds)
                .Where(thing => model.ShownDocument.PlaceOf(thing) is Vector3 place && Vector2.Clamp(Screen(place), from, to) == Screen(place))];
            CollectionAssert.Contains(expected, ObjectRef.Marker(spawn.Id));
            CollectionAssert.AreEquivalent(expected, model.Selection.Items.ToArray());
            Assert.IsNull(tool.ScreenBox, "gone after the release");
        }

        [TestMethod]
        public async Task AClickSelectsAndADragMovesTheSelectionAsOneStepOfUndo()
        {
            MainViewModel model = await Open();
            Spawn spawn = model.ShownDocument!.Markers.Spawns[0];
            ObjectRef thing = ObjectRef.Marker(spawn.Id);
            IMapTool tool = ActiveTool(model);

            tool.Press(spawn.Position.X + 1, spawn.Position.Z);
            CollectionAssert.AreEqual(new[] { thing }, model.Selection.Items.ToArray());
            tool.Drag(spawn.Position.X + 6, spawn.Position.Z);
            tool.Drag(spawn.Position.X + 11, spawn.Position.Z + 5);
            Assert.AreEqual(spawn.Position.X + 10, model.ShownDocument!.PlaceOf(thing)!.Value.X, 1e-3, "it shows while it is dragged");
            Assert.IsFalse(model.CanUndo, "one step, when the mouse is up");
            tool.Release();

            Vector3 moved = model.ShownDocument!.PlaceOf(thing)!.Value;
            Assert.AreEqual(spawn.Position.Z + 5, moved.Z, 1e-3);
            Assert.AreEqual("Move the selection", model.Map!.Session.UndoDescription);
            Assert.AreEqual(2, model.History.Count, "opened, and the move");
            Assert.IsTrue(model.Undo());
            Assert.AreEqual(spawn.Position, model.ShownDocument!.PlaceOf(thing));
            Assert.IsTrue(model.Selection.Contains(thing), "it stays selected");
        }

        [TestMethod]
        public async Task ShiftAddsControlTogglesAndABoxSelectsWhatLiesInIt()
        {
            MainViewModel model = await Open();
            TwoSpawns(model, out Spawn a, out Spawn b);
            IMapTool tool = ActiveTool(model);
            ToolInput shift = ToolInput.Default with { Modifiers = ToolModifiers.Shift };
            ToolInput control = ToolInput.Default with { Modifiers = ToolModifiers.Control };

            tool.Press(a.Position.X, a.Position.Z);
            tool.Release();
            tool.Press(b.Position.X, b.Position.Z, shift);
            tool.Release();
            Assert.AreEqual(2, model.Selection.Count);
            tool.Press(a.Position.X, a.Position.Z, control);
            tool.Release();
            CollectionAssert.AreEqual(new[] { ObjectRef.Marker(b.Id) }, model.Selection.Items.ToArray());

            // a click on nothing selects nothing; a box selects what lies in it
            tool.Press(-50, -50);
            tool.Release();
            Assert.IsTrue(model.Selection.IsEmpty);
            tool.Press(-1, -1);
            tool.Drag(1000, 1000);
            Assert.IsNotNull(tool.Box);
            tool.Release();
            Assert.IsNull(tool.Box);
            int markers = model.ShownDocument!.Markers.All.Count(marker => marker is not PathNode) + model.ShownDocument.Markers.Areas.Length;
            Assert.AreEqual(markers, model.Selection.Count, "the markers and areas, without the hidden path nodes");
            Assert.IsFalse(model.CanUndo, "selecting changes nothing on the map");
        }

        private static void TwoSpawns(MainViewModel model, out Spawn a, out Spawn b)
        {
            a = model.ShownDocument!.Markers.Spawns[0];
            b = model.ShownDocument.Markers.Spawns[1];
        }

        [TestMethod]
        public async Task TheKeysOfTheSelectToolTurnMoveAndClear()
        {
            MainViewModel model = await Open();
            ResourceMarker mass = model.ShownDocument!.Markers.Resources[0];
            ObjectRef thing = ObjectRef.Marker(mass.Id);
            model.Selection.Set([thing]);
            IMapTool tool = ActiveTool(model);

            Assert.IsTrue(tool.Key("E", ToolModifiers.None));
            Assert.IsTrue(tool.Key("Right", ToolModifiers.Shift));
            Assert.IsFalse(tool.Key("Right", ToolModifiers.Control), "control and an arrow are not the tool's");
            await Task.Yield();

            Assert.AreEqual(15, SelectionInspector.Degrees(model.ShownDocument!.HeadingOf(thing)!.Value), 1e-3);
            Assert.AreEqual(mass.Position.X + 8, model.ShownDocument!.PlaceOf(thing)!.Value.X, 1e-3);
            Assert.AreEqual(3, model.History.Count, "opened, a turn, a move");
            Assert.IsTrue(tool.Key("Escape", ToolModifiers.None));
            Assert.IsTrue(model.Selection.IsEmpty);
            Assert.IsFalse(tool.Key("Escape", ToolModifiers.None), "nothing to clear: the key is not used");
        }

        [TestMethod]
        public async Task CopiesPastesDuplicatesAndDeletesAsStepsOfUndo()
        {
            MainViewModel model = await Open();
            MapDocument opened = model.ShownDocument!;
            ResourceMarker mass = opened.Markers.Resources[0];
            Assert.IsFalse(model.Commands.CanRun(model.Commands.Find("edit.delete")!), "nothing selected");
            Assert.IsTrue((await Run(model, "select.set", new JsonObject { ["refs"] = new JsonArray($"marker:{mass.Id}") })).Ran);
            Assert.IsTrue(model.Commands.CanRun(model.Commands.Find("edit.delete")!));

            Assert.AreEqual(1, (await Run(model, "edit.copy")).Result);
            CommandRun pasted = await Run(model, "edit.paste", new JsonObject { ["x"] = 30, ["z"] = 40 });
            string[] copies = (string[])pasted.Result!;
            Assert.AreEqual(1, copies.Length);
            ObjectRef copy = ObjectRef.Parse(copies[0]);
            Assert.AreEqual(30, model.ShownDocument!.PlaceOf(copy)!.Value.X, 1e-3);
            Assert.AreEqual(40, model.ShownDocument!.PlaceOf(copy)!.Value.Z, 1e-3);
            CollectionAssert.AreEqual(new[] { copy }, model.Selection.Items.ToArray(), "the copies are selected");
            Assert.AreEqual("Paste", model.Map!.Session.UndoDescription);

            string[] duplicates = (string[])(await Run(model, "edit.duplicate")).Result!;
            ObjectRef duplicate = ObjectRef.Parse(duplicates[0]);
            Assert.AreEqual(30 + DuplicateHandler.Offset, model.ShownDocument!.PlaceOf(duplicate)!.Value.X, 1e-3);
            Assert.AreEqual(opened.Markers.Resources.Length + 2, model.ShownDocument!.Markers.Resources.Length);

            Assert.IsTrue((await Run(model, "edit.delete")).Ran);
            Assert.IsFalse(model.ShownDocument!.Exists(duplicate));
            Assert.IsTrue(model.Selection.IsEmpty);

            Assert.IsTrue(model.Undo());
            Assert.IsTrue(model.ShownDocument!.Exists(duplicate));
            Assert.IsTrue(model.Undo());
            Assert.IsTrue(model.Undo());
            Assert.AreSame(opened, model.ShownDocument, "three steps: paste, duplicate, delete");
        }

        [TestMethod]
        public async Task SelectAllTakesTheModesKindsThatTheViewsShow()
        {
            MainViewModel model = await Open();
            MarkerSet markers = model.ShownDocument!.Markers;

            Assert.IsTrue((await Run(model, "select.all")).Ran);
            Assert.AreEqual(markers.All.Count(marker => marker is not PathNode) + markers.Areas.Length, model.Selection.Count);

            model.ShowPathNodes = true;
            await Run(model, "select.all");
            Assert.AreEqual(markers.All.Count() + markers.Areas.Length, model.Selection.Count, "path nodes too, once they show");

            model.SelectTool("units.select");
            Assert.AreEqual(ObjectKinds.Units, model.SelectableKinds);
            await Run(model, "select.all");
            Assert.AreEqual(0, model.Selection.Count, "HardFFA has no units");

            Assert.IsTrue((await Run(model, "select.none")).Ran);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "select.set", new JsonObject { ["refs"] = new JsonArray("prop:999999") }));
        }

        [TestMethod]
        public async Task TheInspectorShowsWhatTheSelectionSharesAndChangesEveryOne()
        {
            MainViewModel model = await Open();
            ResourceMarker[] masses = [.. model.ShownDocument!.Markers.Resources.Where(r => r.Kind == ResourceKind.Mass).Take(2)];
            model.Selection.Set(masses.Select(mass => ObjectRef.Marker(mass.Id)));
            SelectionInspector inspector = model.Selection.Inspector;

            Assert.AreEqual("2 selected", inspector.Title);
            Assert.AreEqual("2 markers", inspector.Kinds);
            Assert.AreEqual(SelectionInspector.Mixed, inspector.X);
            Assert.AreEqual("0", inspector.Heading);
            Assert.AreEqual(1, inspector.Sections.Count);
            Assert.IsInstanceOfType(inspector.Sections[0], typeof(MarkerSelection));
            StringAssert.Contains(inspector.Sections[0].Lines[0], "(mass)");

            inspector.X = "100";
            await Task.Yield();
            Assert.AreEqual("100", inspector.X);
            foreach (ResourceMarker mass in masses)
            {
                Assert.AreEqual(100, model.ShownDocument!.PlaceOf(ObjectRef.Marker(mass.Id))!.Value.X, 1e-3);
            }
            inspector.Heading = "90";
            await Task.Yield();
            Assert.AreEqual("90", inspector.Heading);
            Assert.AreEqual(3, model.History.Count, "a step per field");

            inspector.Z = "not a number";
            Assert.AreEqual(3, model.History.Count, "a typo changes nothing");
            Assert.AreNotEqual("not a number", inspector.Z);
        }

        [TestMethod]
        public async Task SelectingOnTheMapAndChoosingAnAssetTakeTheInspectorFromEachOther()
        {
            MainViewModel model = await Open();
            model.Assets.Use(TestAssets.Catalog());

            model.Assets.Select("uec1101");
            model.Selection.Set([ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[0].Id)]);
            Assert.IsFalse(model.Assets.HasSelection, "the shelf's choice is cleared");

            model.Assets.Select("uec1101");
            Assert.IsTrue(model.Selection.IsEmpty, "the selection is cleared");
        }

        [TestMethod]
        public async Task AnUndoThatTakesASelectedThingAwayDropsIt()
        {
            MainViewModel model = await Open();
            ObjectRef spawn = ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[0].Id);
            model.Selection.Set([spawn]);
            ObjectRef copy = ObjectRef.Parse(((string[])(await Run(model, "edit.duplicate")).Result!)[0]);
            Assert.IsTrue(model.Selection.Contains(copy));

            model.Undo();

            // the copy is gone; the selection is the one before the step again
            CollectionAssert.AreEqual(new[] { spawn }, model.Selection.Items.ToArray());
        }
    }
}
