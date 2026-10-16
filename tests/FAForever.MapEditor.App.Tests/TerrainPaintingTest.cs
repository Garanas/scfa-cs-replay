using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The brushes on the terrain's grids (user stories T-1, S-1, T-6, W-2, K-1): the operations as
    /// commands and tools of the view model, and one tool by mouse in the real window.
    /// </summary>
    [TestClass]
    public class TerrainPaintingTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "painting");

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static async Task Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            var result = await model.Commands.RunAsync(id, arguments ?? [], interactive: false);
            Assert.IsTrue(result.Ran, $"{id} ran");
        }

        [TestMethod]
        public async Task SculptsWithTheNewModesFalloffsAndLimits()
        {
            MainViewModel model = await Open();
            SculptTool sculpt = model.Tool<SculptTool>();

            await Run(model, "brush.set", new JsonObject { ["layer"] = 0, ["mode"] = "setHeight", ["target"] = 42, ["falloff"] = "sharp", ["radius"] = 6 });
            Assert.AreEqual(HeightBrushMode.SetHeight, sculpt.Mode);
            Assert.AreEqual(BrushFalloff.Sharp, sculpt.Falloff);
            Assert.IsTrue(sculpt.UsesTarget);
            Assert.IsFalse(sculpt.UsesStrength, "setting goes all the way");
            model.Stroke([(60, 60)]);
            Heightmap heights = model.ShownDocument!.Terrain.Heightmap;
            Assert.AreEqual(42f, heights.GetHeight(60, 60), 0.01f);
            Assert.AreEqual(42f, heights.GetHeight(65, 60), 0.01f, "a sharp brush sets as much at its edge");
            Assert.AreEqual("Set terrain height", model.Map!.Session.UndoDescription);

            await Run(model, "brush.set", new JsonObject { ["mode"] = "raise", ["strength"] = 5, ["maxHeight"] = 44, ["falloff"] = "smooth" });
            Assert.IsTrue(sculpt.Limited, "a limit turns the limits on");
            model.Stroke([(60, 60), (60, 61), (60, 62)]);
            Assert.AreEqual(44f, model.ShownDocument!.Terrain.Heightmap.GetHeight(60, 60), 0.01f, "raised no higher than the limit");

            await Run(model, "brush.set", new JsonObject { ["mode"] = "noise", ["strength"] = 3, ["noiseScale"] = 4, ["limits"] = false });
            Assert.IsFalse(sculpt.Limited);
            Assert.IsTrue(sculpt.IsNoise);
            MapDocument before = model.ShownDocument!;
            model.Stroke([(150, 150)]);
            Assert.AreEqual("Add noise to terrain", model.Map.Session.UndoDescription);
            Assert.AreNotSame(before.Terrain.Heightmap.Samples, model.ShownDocument!.Terrain.Heightmap.Samples);

            await Run(model, "brush.set", new JsonObject { ["mode"] = "erode", ["strength"] = 1, ["radius"] = 4 });
            float edge = model.ShownDocument!.Terrain.Heightmap.GetHeight(65, 60);
            float foot = model.ShownDocument.Terrain.Heightmap.GetHeight(67, 60);
            model.Stroke([(66, 60), (66, 60.5f), (66, 61)]);
            Assert.AreEqual("Erode terrain", model.Map.Session.UndoDescription);
            Assert.IsTrue(model.ShownDocument!.Terrain.Heightmap.GetHeight(65, 60) < edge, "the edge of the plateau wears down");
            Assert.IsTrue(model.ShownDocument.Terrain.Heightmap.GetHeight(67, 60) > foot, "its foot fills up");

            await Run(model, "brush.set", new JsonObject { ["mode"] = "scale", ["factor"] = 2, ["target"] = 0 });
            Assert.IsTrue(sculpt.IsScaling);
            Assert.AreEqual(2, sculpt.Factor);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => model.Commands.RunAsync("brush.set", new JsonObject { ["falloff"] = "spiky" }, interactive: false));
        }

        [TestMethod]
        public async Task PaintsStrataOnlyWithinTheLimits()
        {
            MainViewModel model = await Open();
            StrataTool strata = model.StrataTool;
            await Run(model, "brush.set", new JsonObject { ["layer"] = 3, ["radius"] = 10, ["strength"] = 1, ["maxHeight"] = -1 });
            Assert.IsTrue(strata.Limiting.ByHeight);
            MapDocument before = model.ShownDocument!;

            model.Stroke([(120, 128), (136, 128)]);

            Assert.AreSame(before, model.ShownDocument, "no ground is that low: nothing painted, no step kept");

            await Run(model, "brush.set", new JsonObject { ["limits"] = false, ["minSlope"] = 0, ["maxSlope"] = 90 });
            Assert.IsFalse(strata.Limiting.ByHeight);
            Assert.IsTrue(strata.Limiting.BySlope, "a slope turns the slope limit on");
            model.Stroke([(120, 128), (136, 128)]);
            Assert.AreEqual(255, model.ShownDocument!.Terrain.StratumMasks.Layers[2][64, 64], "every slope lies between 0 and 90 degrees");
            Assert.AreEqual("Paint layer 3", model.Map!.Session.UndoDescription);
        }

        [TestMethod]
        public async Task PaintsTheTerrainTypesAndShowsThemInTheirColours()
        {
            MainViewModel model = await Open();
            TerrainTypeTool types = model.Tool<TerrainTypeTool>();
            List<string?> properties = [];
            model.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

            await Run(model, "brush.set", new JsonObject { ["terrainType"] = "Lava01", ["radius"] = 4 });

            Assert.AreEqual("terrain.types", model.ActiveTool?.Definition.Id);
            Assert.IsTrue(properties.Contains(nameof(MainViewModel.ShownGrid)), "the top view lays the types over the terrain");
            Assert.AreEqual(GridOverlay.TerrainTypes.Key, model.ShownGrid?.Key);
            Assert.IsNotNull(model.ShownGrid?.Palette, "a colour per type");
            Assert.AreEqual(0, model.ShownMask, "no stratum's mask");
            Assert.IsTrue(types.Legend.Count > 0, "the legend lists the types the map uses");
            Assert.IsFalse(types.Legend.Any(entry => entry.Type.Name == "Lava01"));
            Assert.AreEqual(BrushFalloff.Sharp, types.Falloff, "a type is set, not blended: a hard edge");

            model.Stroke([(100, 100), (110, 100)]);

            byte lava = (byte)TerrainTypes.All.Single(type => type.Name == "Lava01").TypeCode;
            Assert.AreEqual(lava, model.ShownDocument!.Terrain.Types[105, 100]);
            Assert.AreEqual("Paint terrain type Lava01", model.Map!.Session.UndoDescription);
            Assert.IsTrue(types.Legend.Any(entry => entry.Type.Name == "Lava01" && entry.Count > 0), "the legend counts it");
            Assert.ThrowsException<ArgumentException>(() => types.Choose("Marshmallow"));
            types.Choose("152");
            Assert.AreEqual("Rocky02", types.Chosen.Type.Name, "by code too");
            model.Undo();
            Assert.AreNotEqual(lava, model.ShownDocument!.Terrain.Types[105, 100]);
        }

        [TestMethod]
        public async Task TheLegendNamesCodeZeroAsNoTypeInAQuietColour()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(TestAssets.SandboxProject());
            model.SelectTool("terrain.types");

            TerrainTypeChoice none = model.Tool<TerrainTypeTool>().Legend.Single(entry => entry.Code == 0);
            Assert.AreEqual("None", none.Type.Name, "the sandbox's ogrids have no type");
            StringAssert.Contains(none.Type.Description, "Default");
            Assert.AreNotEqual(0xFFFF00FFu, none.Colour, "not the magenta of a code the game does not know");
            Assert.AreEqual(0xFFFF00FFu, TerrainTypeColours.Of(255), "an unknown code stays magenta");
        }

        [TestMethod]
        public async Task PaintsTheWatersMasks()
        {
            MainViewModel model = await Open();
            MapDocument before = model.ShownDocument!;

            await Run(model, "brush.set", new JsonObject { ["waterMask"] = "flatness", ["radius"] = 6, ["strength"] = 1, ["falloff"] = "sharp" });
            Assert.AreEqual("water.paint", model.ActiveTool?.Definition.Id);
            Assert.AreEqual(WaterMask.Flatness, model.Tool<WaterPaintTool>().Painted.Mask);
            Assert.IsNotNull(model.ShownGrid, "the top view shows the chosen mask");
            Assert.IsNull(model.ShownGrid!.Palette);
            model.Stroke([(128, 128)]);

            MapDocument painted = model.ShownDocument!;
            Assert.AreEqual(255, painted.Water.FlatnessMask[64, 64], "half the map's size");
            Assert.AreSame(before.Water.FoamMask, painted.Water.FoamMask);
            Assert.AreEqual("Paint water flatness", model.Map!.Session.UndoDescription);

            string shown = model.ShownGrid.Key;
            await Run(model, "brush.set", new JsonObject { ["waterMask"] = "depthBias", ["erase"] = true });
            Assert.AreNotEqual(shown, model.ShownGrid!.Key, "another mask shows");
            model.Stroke([(128, 128)]);
            Assert.AreEqual(0, model.ShownDocument!.Water.DepthBiasMask[64, 64]);
        }

        [TestMethod]
        public async Task AddsPaintsRenamesAndDeletesMasksForTools()
        {
            MainViewModel model = await Open();
            MasksViewModel masks = model.Masks;
            Assert.IsTrue(masks.IsEmpty);
            model.SelectTool("masks.paint");
            Assert.IsNull(model.Tool<MaskPaintTool>().Target, "nothing to paint without a mask");
            model.Stroke([(50, 50)]);
            Assert.IsFalse(model.CanUndo);

            await Run(model, "paint.addMask", new JsonObject { ["name"] = "rivers", ["half"] = true });
            Assert.AreEqual("rivers", masks.Chosen, "a new mask is chosen");
            CollectionAssert.AreEqual(new[] { "rivers" }, masks.Items.Select(item => item.Name).ToArray());
            Assert.AreEqual("128 × 128", masks.Items[0].Size);
            Assert.AreEqual("Add a mask", model.Map!.Session.UndoDescription);
            Assert.IsNotNull(model.ShownGrid, "the masks tool shows the chosen mask");

            model.Tool<MaskPaintTool>().Strength = 1;
            model.Tool<MaskPaintTool>().Falloff = BrushFalloff.Sharp;
            model.Stroke([(100, 100)]);
            Assert.AreEqual(255, model.ShownDocument!.Masks["rivers"][50, 50]);
            Assert.AreEqual("Paint mask rivers", model.Map.Session.UndoDescription);

            masks.NewName = "forest";
            await Run(model, "paint.addMask");
            Assert.AreEqual("forest", masks.Chosen, "the name in the panel's field");
            Assert.AreEqual("", masks.NewName);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => model.Commands.RunAsync("paint.addMask", new JsonObject { ["name"] = "Rivers" }, interactive: false));
            await Run(model, "paint.chooseMask", new JsonObject { ["name"] = "rivers" });

            await Run(model, "paint.renameMask", new JsonObject { ["newName"] = "water" });
            Assert.AreEqual("water", masks.Chosen, "the chosen mask keeps being chosen under its new name");
            Assert.AreEqual(255, model.ShownDocument!.Masks["water"][50, 50]);
            Assert.AreEqual("water", model.Tool<MaskPaintTool>().Target is PaintTarget target ? target.Title.Replace("mask ", "") : null);

            await Run(model, "paint.deleteMask");
            CollectionAssert.AreEqual(new[] { "forest" }, masks.Items.Select(item => item.Name).ToArray());
            Assert.AreEqual("forest", masks.Chosen, "another mask is chosen");
            model.Undo();
            CollectionAssert.AreEqual(new[] { "forest", "water" }, masks.Items.Select(item => item.Name).ToArray(), "undo brings it back");

            // the overlay shows the chosen mask while no painting tool shows its own
            model.SelectTool("terrain.sculpt");
            Assert.IsNull(model.ShownGrid);
            await Run(model, "view.overlay.mask", new JsonObject { ["on"] = true });
            Assert.IsNotNull(model.ShownGrid);

            string folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            await model.SaveAsAsync(folder);
            Assert.IsTrue(File.Exists(Path.Combine(folder, MapProjectLayout.Mask("water"))));
            Assert.IsTrue(File.Exists(Path.Combine(folder, MapProjectLayout.Mask("forest"))));
            Assert.AreEqual(255, MapProjectStore.Load(folder).Masks["water"][50, 50]);
        }

        [TestMethod]
        public async Task SavesAndAppliesPresetsOfThePaintingTools()
        {
            MainViewModel model = await Open();
            await Run(model, "brush.set", new JsonObject { ["terrainType"] = "Snowy02", ["radius"] = 9 });
            (string name, JsonObject settings) = BrushPreset.Capture(model);
            Assert.AreEqual("Snowy02 9", name);

            await Run(model, "brush.set", new JsonObject { ["layer"] = 2, ["minSlope"] = 10, ["maxSlope"] = 40, ["radius"] = 30 });
            (string strataName, JsonObject strataSettings) = BrushPreset.Capture(model);
            model.Tool<TerrainTypeTool>().Choose("Lava01");
            model.StrataTool.Limiting.BySlope = false;

            BrushPreset.Apply(model, new Preset(BrushPreset.Kind, name, settings));
            Assert.AreEqual("terrain.types", model.ActiveTool?.Definition.Id);
            Assert.AreEqual("Snowy02", model.Tool<TerrainTypeTool>().Chosen.Type.Name);
            Assert.AreEqual(9, model.Tool<TerrainTypeTool>().Radius);

            BrushPreset.Apply(model, new Preset(BrushPreset.Kind, strataName, strataSettings));
            Assert.AreEqual("paint.strata", model.ActiveTool?.Definition.Id);
            Assert.IsTrue(model.StrataTool.Limiting.BySlope);
            Assert.AreEqual(40, model.StrataTool.Limiting.MaxSlope);
        }

        [TestMethod]
        public Task PaintsTerrainTypesWithTheMouseInTheTopView() => HeadlessApp.Run(async () =>
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            model.Tool<TerrainTypeTool>().Choose("Lava01");
            byte[] plain = Png(window.CaptureRenderedFrame());

            // the tool's button in the Terrain panel
            Button tool = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Terrain types");
            Point at = Middle(tool, window);
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("terrain.types", model.ActiveTool?.Definition.Id);
            CollectionAssert.AreNotEqual(plain, Png(window.CaptureRenderedFrame()), "the top view shows the types in their colours");

            Point middle = Middle(window.MapView, window);
            window.MouseMove(middle);
            window.MouseDown(middle, MouseButton.Left);
            for (int i = 1; i <= 5; i++)
            {
                window.MouseMove(middle + new Vector(i * 6, 0));
            }
            window.MouseUp(middle + new Vector(30, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            byte lava = (byte)TerrainTypes.All.Single(type => type.Name == "Lava01").TypeCode;
            TiledBuffer<byte> types = model.ShownDocument!.Terrain.Types;
            int painted = types.ToArray().Count(code => code == lava);
            Assert.IsTrue(painted > 20, $"the stroke painted lava ({painted} ogrids)");
            Assert.AreEqual(2, model.History.Count, "opened, and the stroke");
            Assert.AreEqual("Paint terrain type Lava01", model.History[^1].Description);
            window.Close();
        });

        private static Point Middle(Control control, Window window) =>
            control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        private static byte[] Png(Bitmap? frame)
        {
            Assert.IsNotNull(frame, "the window drew a frame");
            using MemoryStream stream = new MemoryStream();
            frame.Save(stream, new PngBitmapEncoderOptions());
            return stream.ToArray();
        }
    }
}
