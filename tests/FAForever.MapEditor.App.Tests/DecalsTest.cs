using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Decals mode (user stories D-1, D-2): choosing and placing decals, scaling them, their
    /// distances and order, and the decal groups shown or hidden.
    /// </summary>
    [TestClass]
    public class DecalsTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        /// <summary>
        /// A decal of the test assets, an albedo of one pixel.
        /// </summary>
        private const string Mud = "env/evergreen/decals/e_mud001_albedo.dds";

        /// <summary>
        /// A normal decal twice as wide as long, added to the test assets.
        /// </summary>
        private const string Erosion = "env/common/decals/erosion018_normals.dds";

        private static async Task<(MainViewModel Model, MainViewModelTest.Answers Answers)> Open()
        {
            MainViewModelTest.Answers answers = new MainViewModelTest.Answers();
            MainViewModel model = new MainViewModel(answers);
            await model.OpenAsync(HardFfa);
            string folder = TestAssets.Folder();
            Directory.CreateDirectory(Path.Combine(folder, "env/common/decals"));
            File.WriteAllBytes(Path.Combine(folder, "env/common/decals/erosion018_normals.png"), PngHeader(64, 32));
            model.Assets.Use(AssetCatalog.Load(folder));
            model.SelectMode("decals");
            return (model, answers);
        }

        /// <summary>
        /// The start of a PNG of a size: what the place tool reads its shape from.
        /// </summary>
        private static byte[] PngHeader(int width, int height)
        {
            byte[] bytes = new byte[33];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }.CopyTo(bytes, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
            return bytes;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static Decal DecalOf(MainViewModel model, ObjectRef thing) => (Decal)model.ShownDocument!.Find(thing)!;

        private static async Task<ObjectRef> Place(MainViewModel model, float x, float z, string key = Mud, float width = 8)
        {
            CommandRun run = await Run(model, "decals.place", new JsonObject { ["key"] = key, ["x"] = x, ["z"] = z, ["width"] = width });
            Assert.IsTrue(run.Ran);
            return ObjectRef.Parse((string)run.Result!);
        }

        [TestMethod]
        public async Task TheModeHasItsSelectAndPlaceTools()
        {
            (MainViewModel model, _) = await Open();

            Assert.IsInstanceOfType<DecalSelectTool>(model.ActiveTool!.Tool, "the select tool first");
            Assert.AreEqual(ObjectKinds.Decals, model.SelectableKinds);
            model.SelectTool("decals.place");
            Assert.IsInstanceOfType<DecalPlaceTool>(model.ActiveTool!.Tool);
            Assert.IsFalse(model.ActiveTool.Definition.IsIncomplete);
        }

        [TestMethod]
        public async Task ADecalChosenWithThePickerIsPlacedByAClickAndSelected()
        {
            (MainViewModel model, MainViewModelTest.Answers answers) = await Open();
            int before = model.ShownDocument!.Decals.Length;
            DecalPlaceTool tool = model.DecalPlaceTool;

            model.SelectTool("decals.place");
            tool.Press(50, 50);
            tool.Release();
            Assert.AreEqual(before, model.ShownDocument.Decals.Length, "nothing chosen, nothing placed");
            StringAssert.Contains(model.Status, "Choose a decal");

            // the options' Choose asks the picker for a decal
            answers.Assets.Enqueue(Erosion);
            await model.RunAsync("decals.choose");
            Assert.AreEqual(AssetKind.Decal, answers.AssetQuestions.Single().Kind);
            Assert.AreEqual(Erosion, tool.Decal);
            Assert.AreEqual(0.5f, tool.Aspect, "half as long as wide, as its picture");

            tool.Width = 20;
            tool.Heading = 90;
            tool.Press(100, 120);
            Assert.AreEqual(before + 1, model.ShownDocument!.Decals.Length, "it shows while the mouse is down");
            Assert.IsFalse(model.CanUndo, "one step, when the mouse is up");
            tool.Release();

            Decal placed = model.ShownDocument!.Decals[^1];
            ObjectRef thing = ObjectRef.Decal(placed.Id);
            Assert.AreEqual(DecalType.Normals, placed.Type, "a normal map's texture makes a normal decal");
            Assert.AreEqual("/" + Erosion, placed.Textures[0]);
            Assert.AreEqual(new Vector3(20, 20, 10), placed.Scale);
            Assert.AreEqual(MathF.PI / 2, placed.Rotation.Y, 1e-4);
            Assert.AreEqual(DecalEdits.DefaultCutOffLod, placed.CutOffLod);
            Vector3 centre = model.ShownDocument.PlaceOf(thing)!.Value;
            Assert.AreEqual((100f, 120f), (MathF.Round(centre.X, 3), MathF.Round(centre.Z, 3)));
            CollectionAssert.AreEqual(new[] { thing }, model.Selection.Items.ToArray(), "the new decal is selected");
            StringAssert.StartsWith(model.Map!.Session.UndoDescription, "Place the decal");
            Assert.IsTrue(model.Undo());
            Assert.AreEqual(before, model.ShownDocument!.Decals.Length);
        }

        [TestMethod]
        public async Task DraggingWhilePlacingSetsTheWidthAndTurn()
        {
            (MainViewModel model, _) = await Open();
            await Run(model, "decals.choose", new JsonObject { ["key"] = Mud });
            DecalPlaceTool tool = model.DecalPlaceTool;
            Assert.AreSame(tool, model.ActiveTool!.Tool, "choosing a decal makes the place tool active");

            tool.Press(60, 60);
            tool.Drag(70, 60);
            tool.Drag(60, 70);
            tool.Release();

            Decal placed = model.ShownDocument!.Decals[^1];
            Assert.AreEqual(10, placed.Scale.X, 1e-3, "from the press to the mouse");
            Assert.AreEqual(10, placed.Scale.Z, 1e-3, "a square picture: as long as wide");
            Assert.AreEqual(MathF.PI / 2, placed.Rotation.Y, 1e-4, "its width along the drag");
            Assert.AreEqual(60, model.ShownDocument.PlaceOf(ObjectRef.Decal(placed.Id))!.Value.X, 1e-3, "centred half way along the drag");
            Assert.AreEqual(65, model.ShownDocument.PlaceOf(ObjectRef.Decal(placed.Id))!.Value.Z, 1e-3);
            Assert.AreEqual(2, model.History.Count, "opened, and the decal");
        }

        [TestMethod]
        public async Task ADecalChosenOnTheShelfOrDroppedOnTheMapIsPlaced()
        {
            (MainViewModel model, _) = await Open();
            int before = model.ShownDocument!.Decals.Length;

            // on the shelf, in the Decals mode: the place tool takes it and becomes active
            model.Assets.Select(Mud);
            Assert.AreEqual(Mud, model.DecalPlaceTool.Decal);
            Assert.AreEqual("decals.place", model.ActiveTool!.Definition.Id);

            // dropped while the place tool is active, and while another tool of another mode is
            Assert.IsTrue(model.DropAsset(Erosion, 40, 60));
            Assert.AreEqual(Erosion.Insert(0, "/"), model.ShownDocument!.Decals[^1].Textures[0]);
            model.SelectMode("terrain");
            Assert.IsTrue(model.DropAsset(Mud, 80, 90));
            Assert.AreEqual(before + 2, model.ShownDocument!.Decals.Length);
            Vector3 centre = model.ShownDocument.PlaceOf(ObjectRef.Decal(model.ShownDocument.Decals[^1].Id))!.Value;
            Assert.AreEqual((80f, 90f), (MathF.Round(centre.X, 3), MathF.Round(centre.Z, 3)));
            CollectionAssert.AreEqual(new[] { ObjectRef.Decal(model.ShownDocument.Decals[^1].Id) }, model.Selection.Items.ToArray(), "the last one placed is selected");
        }

        [TestMethod]
        public async Task TheSelectionIsScaledWithCommandsKeysTheInspectorAndADrag()
        {
            (MainViewModel model, _) = await Open();
            ObjectRef a = await Place(model, 60, 60);
            ObjectRef b = await Place(model, 120, 60, width: 4);
            model.Selection.Set([a, b]);

            await Run(model, "decals.scale", new JsonObject { ["factor"] = 2 });
            Assert.AreEqual((16f, 8f), (DecalOf(model, a).Scale.X, DecalOf(model, b).Scale.X));
            await Run(model, "decals.scale", new JsonObject { ["z"] = 0.5 });
            Assert.AreEqual((16f, 8f), (DecalOf(model, a).Scale.X, DecalOf(model, a).Scale.Z), "the length alone");
            Assert.AreEqual(60, model.ShownDocument!.PlaceOf(a)!.Value.X, 1e-3, "about each centre");

            DecalSelection inspector = (DecalSelection)model.Selection.Inspector.Sections.Single();
            Assert.AreEqual(SelectionInspector.Mixed, inspector.Width, "they differ");
            inspector.Width = "10";
            Assert.AreEqual((10f, 10f), (DecalOf(model, a).Scale.X, DecalOf(model, b).Scale.X));
            Assert.AreEqual("10", inspector.Width);
            inspector.Length = "x";
            Assert.AreEqual(SelectionInspector.Mixed, inspector.Length, "a typo changes nothing");

            DecalSelectTool tool = model.Tool<DecalSelectTool>();
            Assert.IsTrue(tool.Key("OemPlus", ToolModifiers.None));
            Assert.AreEqual(11, DecalOf(model, a).Scale.X, 1e-3, "plus: a tenth larger");
            Assert.IsTrue(tool.Key("S", ToolModifiers.None));
            Assert.AreEqual(DecalDrag.Scale, tool.DragMode, "S: the next drag mode");
            tool.Key("S", ToolModifiers.None);
            Assert.AreEqual(DecalDrag.Width, tool.DragMode);
            tool.Key("OemMinus", ToolModifiers.None);
            Assert.AreEqual(10, DecalOf(model, a).Scale.X, 1e-3, "minus, in the width mode: the width");

            // a drag in the scale mode: twice as far from the selection's middle, twice the size
            model.Selection.Set([a]);
            tool.DragMode = DecalDrag.Scale;
            Vector3 centre = model.ShownDocument!.PlaceOf(a)!.Value;
            float length = DecalOf(model, a).Scale.Z;
            int steps = model.History.Count;
            tool.Press(centre.X + 2, centre.Z);
            tool.Drag(centre.X + 3, centre.Z);
            tool.Drag(centre.X + 4, centre.Z);
            tool.Release();
            Assert.AreEqual(20, DecalOf(model, a).Scale.X, 1e-3);
            Assert.AreEqual(2 * length, DecalOf(model, a).Scale.Z, 1e-3);
            Assert.AreEqual(centre.X, model.ShownDocument!.PlaceOf(a)!.Value.X, 1e-3, "about its centre");
            Assert.AreEqual(steps + 1, model.History.Count, "one step");
            Assert.AreEqual("Scale the decals", model.Map!.Session.UndoDescription);
        }

        [TestMethod]
        public async Task TheSelectToolOfTheModeSelectsAndMovesDecals()
        {
            (MainViewModel model, _) = await Open();
            ObjectRef a = await Place(model, 60, 60);
            model.Selection.Clear();
            model.SelectTool("decals.select");
            IMapTool tool = model.ActiveTool!.Tool!;

            tool.Press(61, 60);
            tool.Drag(66, 60);
            tool.Drag(71, 60);
            tool.Release();

            Assert.IsTrue(model.Selection.Contains(a));
            Assert.AreEqual(70, model.ShownDocument!.PlaceOf(a)!.Value.X, 1e-3, "dragged along");
            Assert.AreEqual("Move the selection", model.Map!.Session.UndoDescription);
        }

        [TestMethod]
        public async Task TheDistancesAreSetForManyDecalsAtOnce()
        {
            (MainViewModel model, _) = await Open();
            ObjectRef a = await Place(model, 60, 60);
            ObjectRef b = await Place(model, 120, 60);
            model.Selection.Set([a]);
            await Run(model, "decals.lod", new JsonObject { ["cutOff"] = 800 });
            model.Selection.Set([a, b]);
            DecalSelection inspector = (DecalSelection)model.Selection.Inspector.Sections.Single();
            Assert.AreEqual(SelectionInspector.Mixed, inspector.CutOff);
            Assert.AreEqual("0", inspector.NearCutOff);

            inspector.CutOff = "1250";
            inspector.NearCutOff = "20";

            Assert.AreEqual((1250f, 20f), (DecalOf(model, a).CutOffLod, DecalOf(model, a).NearCutOffLod));
            Assert.AreEqual((1250f, 20f), (DecalOf(model, b).CutOffLod, DecalOf(model, b).NearCutOffLod));
            Assert.AreEqual("albedo", inspector.Types);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "decals.lod", new JsonObject { ["cutOff"] = -1 }));
        }

        [TestMethod]
        public async Task TheOrderCommandsMoveTheSelectionUpAndDown()
        {
            (MainViewModel model, _) = await Open();
            ObjectRef a = await Place(model, 60, 60);
            ObjectRef b = await Place(model, 62, 60);
            ObjectRef c = await Place(model, 64, 60);
            int Index(ObjectRef thing) => model.ShownDocument!.Decals.IndexOf(DecalOf(model, thing));
            int top = model.ShownDocument!.Decals.Length - 1;

            model.Selection.Set([a]);
            await Run(model, "decals.forward");
            Assert.AreEqual(top - 1, Index(a));
            await Run(model, "decals.toFront");
            Assert.AreEqual(top, Index(a));
            Assert.AreEqual(a, model.PickAt(62, 61, 0.5f), "the one on top is picked");
            await Run(model, "decals.backward");
            Assert.AreEqual(top - 1, Index(a));
            await Run(model, "decals.toBack");
            Assert.AreEqual(0, Index(a));
            Assert.AreEqual(c, model.PickAt(62, 61, 0.5f));
            Assert.AreEqual("Send to back", model.Map!.Session.UndoDescription);

            Assert.AreEqual(top, Index(c), "b and c kept their order");
            Assert.AreEqual(top - 1, Index(b));
            model.Selection.Set([ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[0].Id)]);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "decals.forward"), "no decal selected");
        }

        [TestMethod]
        public async Task GroupsAreMadeFromTheSelectionAndHiddenInTheViews()
        {
            (MainViewModel model, _) = await Open();
            ObjectRef a = await Place(model, 60, 60);
            ObjectRef b = await Place(model, 120, 60);
            model.Selection.Set([a, b]);

            int id = (int)(await Run(model, "decals.addGroup", new JsonObject { ["name"] = "Roads" })).Result!;
            DecalGroupItem item = model.Decals.Groups.Single(group => group.Id == id);
            Assert.AreEqual(("Roads", 2, true), (item.Name, item.Count, item.IsShown));

            item.Name = "Paths";
            Assert.AreEqual("Paths", model.ShownDocument!.FindDecalGroup(id)!.Name, "typing a name renames it");
            Assert.AreEqual("Rename a decal group", model.Map!.Session.UndoDescription);

            // hidden: not picked, not selectable, out of the selection; the map keeps it
            item.IsShown = false;
            Assert.IsFalse(item.IsShown);
            CollectionAssert.AreEquivalent(new[] { a.Id, b.Id }, model.Decals.HiddenDecals.ToArray());
            Assert.IsTrue(model.Selection.IsEmpty, "what is hidden cannot stay selected");
            Assert.AreNotEqual(a, model.PickAt(60, 60, 0.5f));
            model.SelectAll();
            Assert.IsFalse(model.Selection.Contains(a));
            Assert.AreEqual(0, (int)(await Run(model, "decals.selectGroup", new JsonObject { ["group"] = id })).Result!);
            Assert.IsTrue(model.ShownDocument!.Exists(a));
            Assert.IsFalse(model.Map!.Session.UndoDescription!.Contains("show", StringComparison.OrdinalIgnoreCase), "showing is no step of undo");

            await Run(model, "decals.showGroup", new JsonObject { ["group"] = id });
            Assert.IsTrue(item.IsShown, "left out: the other way");
            Assert.AreEqual(2, (int)(await Run(model, "decals.selectGroup", new JsonObject { ["group"] = id })).Result!);

            // take out, add, delete
            model.Selection.Set([b]);
            await Run(model, "decals.removeFromGroup", new JsonObject { ["group"] = id });
            Assert.AreEqual(1, item.Count);
            await Run(model, "decals.addToGroup", new JsonObject { ["group"] = id });
            Assert.AreEqual(2, item.Count);
            await Run(model, "decals.deleteGroup", new JsonObject { ["group"] = id });
            Assert.IsFalse(model.Decals.HasGroups);
            Assert.IsTrue(model.ShownDocument!.Exists(a), "its decals stay");
            model.Undo();
            Assert.AreEqual(id, model.Decals.Groups.Single().Id, "undo brings the group back");
        }

        [TestMethod]
        public async Task AnotherMapShowsEveryGroupAgain()
        {
            (MainViewModel model, _) = await Open();
            ObjectRef a = await Place(model, 60, 60);
            model.Selection.Set([a]);
            int id = (int)(await Run(model, "decals.addGroup")).Result!;
            await Run(model, "decals.showGroup", new JsonObject { ["group"] = id, ["on"] = false });
            Assert.AreEqual(1, model.Decals.HiddenDecals.Count);

            await model.OpenAsync(HardFfa);

            Assert.AreEqual(0, model.Decals.HiddenDecals.Count);
            Assert.AreEqual(0, model.Decals.HiddenGroups.Count);
        }
    }
}
