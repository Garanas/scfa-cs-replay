using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The map's size, playable area and version (user stories M-3, M-8): resizing as one step of
    /// undo, the size tool and its rectangle, the playable area chosen in the inspector, raising the
    /// version and rendering the preview on export.
    /// </summary>
    [TestClass]
    public class MapSizeTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "map-size");

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string NewFolder() => Path.Combine(Root, Guid.NewGuid().ToString("N"));

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) => model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        [TestMethod]
        public async Task ResizesTheMapAsOneStepOfUndo()
        {
            MainViewModel model = await Open();
            int maps = 0;
            model.MapChanged += (_, _) => maps++;
            int steps = model.History.Count;

            CommandRun run = await Run(model, "map.resize", new JsonObject { ["width"] = 512, ["height"] = 1024, ["anchor"] = "TopLeft" });

            Assert.IsTrue(run.Ran, model.Status);
            Assert.AreEqual((512, 1024), (model.Map!.Document.Width, model.Map.Document.Height));
            Assert.AreEqual(steps + 1, model.History.Count);
            Assert.AreEqual("Resize the map to 512 by 1024", model.History[^1].Description);
            StringAssert.StartsWith(model.Status, "Resized the map to 512 by 1024 ogrids (10 by 20 km)");
            Assert.AreEqual(1, maps, "the views start over with a map of another size");
            Assert.AreEqual(512, model.Tool<MapSizeTool>().NewWidth, "the tool takes the new size");
            Assert.AreEqual(GeneratedStatus.Stale, model.Generation.States.Single(state => state.Generator.Id == MapPreview.GeneratorId).Status, "the preview is out of date");
            JsonObject state = model.DescribeMapSize();
            Assert.AreEqual(1024, state["height"]!.GetValue<int>());

            model.Undo();
            Assert.AreEqual(256, model.Map.Document.Width);
            Assert.AreEqual(2, maps);
        }

        [TestMethod]
        public async Task TheSizeToolShowsTheNewMapAndApplies()
        {
            MainViewModel model = await Open();

            CommandRun chosen = await Run(model, "map.resize");
            Assert.IsTrue(chosen.Ran, model.Status);
            Assert.AreEqual("map.size", model.ActiveTool!.Definition.Id, "without a size: the tool");
            MapSizeTool tool = model.Tool<MapSizeTool>();
            Assert.AreEqual((256, 256), (tool.NewWidth, tool.NewHeight));

            tool.NewWidth = 1024;
            tool.NewHeight = 512;
            Assert.AreEqual((384, 128), (tool.OffsetX, tool.OffsetZ), "the centre");
            Assert.AreEqual((-384f, -128f, 640f, 384f), tool.Box);
            Assert.AreEqual("1024 × 512", tool.Marks!.Label);
            tool.Anchor = MapAnchor.BottomRight;
            Assert.AreEqual((768, 256), (tool.OffsetX, tool.OffsetZ));

            // dragging the rectangle moves it by 4 ogrids at a time
            tool.Press(100, 100);
            tool.Drag(110, 93);
            tool.Release();
            Assert.AreEqual((760, 264), (tool.OffsetX, tool.OffsetZ));
            Assert.IsTrue(tool.CustomOffset);
            Assert.AreEqual(760, tool.Arguments()["offsetX"]!.GetValue<int>());

            tool.FillWithHeight = true;
            tool.FillHeight = 20;
            await tool.ApplyCommand.ExecuteAsync(null);
            Assert.AreEqual((1024, 512), (model.Map!.Document.Width, model.Map.Document.Height));
            Assert.AreEqual(20, model.Map.Document.Terrain.Heightmap.GetHeight(0, 0), 0.01);
            Assert.IsFalse(tool.CustomOffset, "after a resize the tool starts from the anchor again");
        }

        [TestMethod]
        public async Task CroppingSaysWhatWasRemoved()
        {
            MainViewModel model = await Open();
            await Run(model, "map.resize", new JsonObject { ["width"] = 512, ["height"] = 512, ["anchor"] = "TopLeft" });
            MapDocument grown = model.Map!.Document;
            model.EditMap("Add a prop", d => d.AddProp(new Prop { Id = d.NextPropId(), Blueprint = "/env/rock.bp", Position = new System.Numerics.Vector3(400, 0, 400) }));

            CommandRun run = await Run(model, "map.resize", new JsonObject { ["width"] = 256, ["height"] = 256, ["offsetX"] = 0, ["offsetZ"] = 0 });

            Assert.AreEqual("1 prop", run.Result!.ToString());
            StringAssert.Contains(model.Status, "removed what lay outside: 1 prop");
            Assert.AreEqual(grown.Markers.Resources.Length, model.Map.Document.Markers.Resources.Length);
        }

        [TestMethod]
        public async Task ThePlayableAreaIsChosenInTheInspector()
        {
            MainViewModel model = await Open();
            await Run(model, "map.addArea", new JsonObject { ["name"] = "AREA_1", ["x0"] = 0, ["z0"] = 0, ["x1"] = 256, ["z1"] = 256 });
            await Run(model, "map.addArea", new JsonObject { ["name"] = "INNER", ["x0"] = 32, ["z0"] = 32, ["x1"] = 224, ["z1"] = 224 });
            Assert.IsTrue(model.AreaList.Items[0].IsPlayable, "the first, by convention");

            AreaSelection inspector = model.Selection.Inspector.Sections.OfType<AreaSelection>().Single();
            Assert.IsFalse(inspector.Playable, "INNER is selected");
            inspector.Playable = true;
            await Task.Yield();

            Assert.AreEqual("INNER", model.Map!.Document.Scenario.PlayableArea);
            Assert.AreEqual("Make INNER the playable area", model.History[^1].Description);
            Assert.IsTrue(inspector.Playable);
            Assert.IsTrue(model.AreaList.Items[1].IsPlayable);
            Assert.IsFalse(model.AreaList.Items[0].IsPlayable);
            Assert.AreEqual("INNER", model.DescribeMapSize()["playableArea"]!.GetValue<string>());

            await Run(model, "map.setPlayableArea");
            Assert.IsNull(model.Map.Document.Scenario.PlayableArea);
            Assert.IsTrue(model.AreaList.Items[0].IsPlayable);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "map.setPlayableArea", new JsonObject { ["name"] = "NONE" }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "map.resize", new JsonObject { ["width"] = 512, ["anchor"] = "Middle" }));
        }

        [TestMethod]
        public async Task ExportRaisesTheVersionAndRendersAStalePreview()
        {
            MainViewModel model = await Open();
            Assert.AreEqual(GeneratedStatus.Current, model.Generation.States.Single(state => state.Generator.Id == MapPreview.GeneratorId).Status,
                "the map's own preview fits the map as opened");
            model.Stroke([(100, 40), (130, 40)]);
            Assert.AreEqual(GeneratedStatus.Stale, model.Generation.States.Single(state => state.Generator.Id == MapPreview.GeneratorId).Status);
            string output = NewFolder();

            CommandRun run = await Run(model, "file.export", new JsonObject { ["folder"] = output, ["raise"] = true });

            Assert.IsTrue(run.Ran, model.Status);
            Assert.AreEqual("HardFFA.v0002", Path.GetFileName(run.Result!.ToString()));
            Assert.AreEqual(2, model.Map!.Document.Version, "the map keeps its new version");
            Assert.IsTrue(model.History.Any(entry => entry.Description == "Raise the version to 2"));
            Assert.AreEqual(GeneratedStatus.Current, model.Generation.States.Single(state => state.Generator.Id == MapPreview.GeneratorId).Status, "rendered before the export");
            StringAssert.Contains(model.Status, "(version 2); exporting with raise makes version 3");
            StringAssert.Contains(File.ReadAllText(Path.Combine(run.Result!.ToString()!, "HardFFA_scenario.lua")), "map_version = 2");

            CommandRun again = await Run(model, "file.export", new JsonObject { ["folder"] = output });
            Assert.AreEqual("HardFFA.v0002", Path.GetFileName(again.Result!.ToString()), "without raise the version stays");
        }

        [TestMethod]
        public Task DraggingInTheTopViewMovesTheNewMap() => HeadlessApp.Run(async () =>
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers { SaveChoice = SaveChoice.Discard });
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            model.SelectTool("map.size");
            MapSizeTool tool = model.Tool<MapSizeTool>();
            tool.NewWidth = 512;
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(128, tool.OffsetX);

            Control view = window.MapView;
            Point middle = view.TranslatePoint(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2), window)!.Value;
            window.MouseMove(middle);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseMove(middle + new Vector(40, 0));
            window.MouseMove(middle + new Vector(80, 0));
            window.MouseUp(middle + new Vector(80, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(tool.OffsetX < 128, "the rectangle went right, so the old map lies further left in it");
            Assert.AreEqual(0, tool.OffsetX % 4);
            Assert.AreEqual(256, model.Map!.Document.Width, "dragging changes nothing until Apply");

            JsonArray ui = UiTree.Describe(window, hidden: false);
            StringAssert.Contains(ui.ToJsonString(), "\"label\":\"New width in ogrids\"");
            window.Close();
        });
    }
}
