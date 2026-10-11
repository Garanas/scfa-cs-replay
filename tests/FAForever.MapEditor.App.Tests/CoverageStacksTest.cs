using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The stacks of coverage in the window (wave 9B; user stories K-1, K-2, S-1, W-2): the Masks
    /// mode's list and the chosen mask's stack, the Paint mode's stratum stack, the water maps'
    /// stacks; the <c>layers.*</c> commands; each brush painting its stack's chosen painted layer;
    /// the props brush taking a mask by any of its names.
    /// </summary>
    [TestClass]
    public sealed class CoverageStacksTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers { SaveChoice = SaveChoice.Discard });
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static async Task<object?> Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            var run = await model.Commands.RunAsync(id, arguments ?? [], interactive: false);
            Assert.IsTrue(run.Ran, $"{id}: {model.Status}");
            return run.Result;
        }

        [TestMethod]
        public async Task TheMasksModeListsMasksAndEditsTheChosenOnesStack()
        {
            MainViewModel model = await Open();
            model.SelectMode("masks");
            Assert.AreEqual("masks.paint", model.ActiveTool!.Definition.Id, "the mask brush lives in the Masks mode");
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "forest" });
            Assert.IsNotNull(model.Masks.Items.Single().Preview, "each mask has a preview");
            Assert.AreEqual("mask-forest", model.MaskStack.Target?.Key);
            Assert.AreEqual(CoverageLayers.PaintedName, model.MaskStack.Items.Single().Name, "a mask is its own painted layer until it has more");

            // painting: into the chosen painted layer
            MaskPaintTool brush = model.Tool<MaskPaintTool>();
            brush.Strength = 1;
            brush.Falloff = BrushFalloff.Sharp;
            model.Stroke([(60, 60)]);
            Assert.AreEqual(255, model.ShownDocument!.Masks["forest"][60, 60]);
            byte[] before = model.ShownDocument.Masks["forest"].ToArray();

            // a painted layer above: nothing until painted, then a clearing
            Assert.AreEqual("Painted 2", await Run(model, "layers.add", new JsonObject { ["source"] = "painted" }));
            Assert.AreEqual("Painted 2", model.MaskStack.Chosen, "the new layer is chosen");
            CollectionAssert.AreEqual(before, model.ShownDocument!.Masks["forest"].ToArray(), "an untouched painted layer changes nothing");
            Assert.AreEqual("Painted 2", model.Map!.Session.PaintLayers["mask-forest"], "the brush paints the chosen layer");
            brush.Erase = true;
            model.Stroke([(60, 60)]);
            Assert.AreEqual(0, model.ShownDocument!.Masks["forest"][60, 60], "the clearing");
            Assert.AreEqual(255, CoverageLayers.StackOf(model.ShownDocument, LayerTarget.Mask("forest")).Find(CoverageLayers.PaintedName)!.Coverage![60, 60],
                "the layer below keeps its forest");

            // a mask reference, set up in the inspector's fields
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "rivers" });
            await Run(model, "paint.chooseMask", new JsonObject { ["name"] = "forest" });
            Assert.AreEqual("rivers", await Run(model, "layers.add", new JsonObject { ["source"] = "mask" }), "the first other mask, named after it");
            Assert.IsTrue(model.MaskStack.IsReference);
            await Run(model, "layers.set", new JsonObject { ["reads"] = "Layer 2", ["threshold"] = 0.5, ["invert"] = true });
            MaskRefSource reference = (MaskRefSource)CoverageLayers.StackOf(model.ShownDocument!, LayerTarget.Mask("forest")).Find("rivers")!.Source;
            Assert.AreEqual("stratum:2", reference.Key, "the rules' name of a stratum, read the one way");
            Assert.AreEqual(0.5f, reference.Threshold);
            Assert.IsTrue(reference.Invert);
            Assert.AreEqual(3, model.MaskStack.Reference.Settings.Count, "what it reads, its threshold, inverted");

            // duplicate, rename: the stack goes along
            Assert.AreEqual("forest copy", await Run(model, "paint.duplicateMask"));
            Assert.AreEqual(3, CoverageLayers.StackOf(model.ShownDocument!, LayerTarget.Mask("forest copy")).Count);
            Assert.AreEqual("forest copy", model.Masks.Chosen);
            Assert.AreEqual("3 layers", model.Masks.Items.Single(item => item.Name == "forest copy").Layers);

            // the last layer stays
            await Run(model, "paint.chooseMask", new JsonObject { ["name"] = "rivers" });
            Assert.IsFalse(model.MaskStack.CanRemove);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => model.Commands.RunAsync("layers.remove", [], interactive: false));
        }

        [TestMethod]
        public async Task ARulesLayerShowsItsRulesAndItsPreview()
        {
            MainViewModel model = await Open();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "high" });
            model.SelectMode("masks");
            await Run(model, "layers.add", new JsonObject { ["source"] = "rules", ["name"] = "Hills" });
            Assert.IsTrue(model.MaskStack.IsRules);
            Assert.AreEqual("high", model.MaskRules.Editing);
            Assert.AreEqual("Hills", model.MaskRules.Layer);
            await Run(model, "paint.addMaskRule", new JsonObject { ["kind"] = "Height", ["min"] = 0, ["max"] = 1000 });
            Assert.AreEqual(1, model.ShownDocument!.LayerRulesOf(LayerTarget.Mask("high"), "Hills").Length, "the rule went into the chosen rules layer");
            for (int i = 0; i < 200 && model.MaskRules.PreviewOverlay is null; i++)
            {
                await Task.Delay(20);
            }
            Assert.AreSame(model.MaskRules.PreviewOverlay, model.ShownGrid, "the views show what the rules make");

            // choosing the painted layer closes the rules
            model.MaskStack.Choose(CoverageLayers.PaintedName);
            Assert.IsNull(model.MaskRules.Editing);
        }

        [TestMethod]
        public async Task TheInspectorSaysWhichFilesTheEditorWritesAndWhichAreTheMapsOwn()
        {
            MainViewModel model = await Open();
            model.SelectMode("paint");
            model.LayerTextures.Choose(2);
            StringAssert.Contains(model.StratumStack.ResultNote, "Saved as stratum-masks/stratum-2.png, the map's own", "no stack yet: one painted layer");
            StringAssert.Contains(model.StratumStack.FileNote, "the map's own");

            await Run(model, "layers.add", new JsonObject { ["source"] = "rules", ["name"] = "Hills" });
            StringAssert.Contains(model.StratumStack.ResultNote, "Makes stratum-masks/stratum-2.gen.png, the editor's");
            StringAssert.Contains(model.StratumStack.FileNote, "stacks/stratum-2/Hills.gen.png, the editor's: made again from its rules");
            model.StratumStack.Choose(CoverageLayers.PaintedName);
            StringAssert.Contains(model.StratumStack.FileNote, $"stacks/stratum-2/{CoverageLayers.PaintedName}.png, the map's own");

            await Run(model, "layers.add", new JsonObject { ["source"] = "stratum", ["mask"] = "stratum 1" });
            Assert.AreEqual("No file: it reads stratum 1", model.StratumStack.FileNote);
        }

        [TestMethod]
        public async Task TheStrataToolPaintsTheChosenStratumsPaintedLayer()
        {
            MainViewModel model = await Open();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "forest" });
            model.SelectMode("paint");
            model.SelectTool("paint.strata");
            model.LayerTextures.Choose(2);
            Assert.AreEqual("stratum-2", model.StratumStack.Target?.Key);
            Assert.AreEqual("stratum-2", model.ShownCoverageTarget?.Key);

            await Run(model, "layers.add", new JsonObject { ["source"] = "stratum", ["mask"] = "stratum 1" });
            await Run(model, "layers.add", new JsonObject { ["source"] = "painted", ["name"] = "Paths" });
            StrataTool strata = model.StrataTool;
            strata.Strength = 1;
            strata.Falloff = BrushFalloff.Sharp;
            model.Stroke([(100, 100)]);
            Layer paths = CoverageLayers.StackOf(model.ShownDocument!, LayerTarget.Stratum(2)).Find("Paths")!;
            TiledBuffer<byte> mask = model.ShownDocument!.Terrain.StratumMasks.Layers[1];
            int x = 100 * mask.Width / model.ShownDocument.Width;
            Assert.AreEqual(255, paths.Alpha![x, x], "painted into the chosen layer");
            Assert.AreEqual(255, mask[x, x]);

            // every layer locked: the brush says why it cannot paint
            await Run(model, "layers.set", new JsonObject { ["layer"] = "Paths", ["locked"] = true });
            await Run(model, "layers.set", new JsonObject { ["layer"] = CoverageLayers.PaintedName, ["locked"] = true });
            int steps = model.History.Count;
            model.Stroke([(150, 150)]);
            Assert.AreEqual(steps, model.History.Count);
            StringAssert.Contains(model.Status, "locked");

            JsonObject state = (JsonObject)model.GetType().GetMethod("DescribeCoverageStacksState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(model, null)!;
            Assert.AreEqual("stratum-2", (string?)state["stratum"]!["target"]);
            Assert.AreEqual(3, ((JsonArray)state["stratum"]!["layers"]!).Count);
        }

        [TestMethod]
        public async Task EachWaterMapIsAStackTheWaterBrushPaints()
        {
            MainViewModel model = await Open();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "shore" });
            await Run(model, "view.inspectPart", new JsonObject { ["part"] = "water.foam" });
            WaterMapPart part = (WaterMapPart)model.WaterSettings.Parts.Chosen.Content;
            Assert.IsTrue(part.HasStack);
            Assert.AreEqual("water-foam", part.Stack!.Target?.Key);
            await Run(model, "layers.add", new JsonObject { ["target"] = "flatness", ["source"] = "painted" });
            Assert.IsTrue(model.ShownDocument!.CoverageStacks.ContainsKey("water-flatness"));
            await Run(model, "layers.add", new JsonObject { ["source"] = "mask", ["mask"] = "shore" });
            Assert.AreEqual(2, CoverageLayers.StackOf(model.ShownDocument!, LayerTarget.Water(WaterMask.Foam)).Count, "the shown map: foam");
        }

        [TestMethod]
        public async Task ThePropsBrushTakesAMaskByAnyOfItsNames()
        {
            MainViewModel model = await Open();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "forest" });
            model.SelectMode("props");
            PropPaintTool props = model.Tool<PropPaintTool>();
            Assert.AreEqual("Mask: forest", props.LayerNamed("mask:forest").Name);
            Assert.AreEqual("Mask: forest", props.LayerNamed("forest").Name);
            StringAssert.StartsWith(props.LayerNamed("stratum:1").Name, "Layer 1");
        }

        [TestMethod]
        public Task TheMasksPanelAndTheInspectorsStackByMouse() => HeadlessApp.Run(async () =>
        {
            MainViewModel model = await Open();
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "forest" });
            model.SelectMode("masks");
            Dispatcher.UIThread.RunJobs();

            Labelled<Control>(window, "Preview of the mask forest");
            Click(window, Labelled<Button>(window, "Duplicate the chosen mask"));
            Assert.AreEqual("forest copy", model.Masks.Chosen);
            Click(window, Labelled<Button>(window, "Add a painted mask layer"));
            Assert.AreEqual(2, CoverageLayers.StackOf(model.ShownDocument!, LayerTarget.Mask("forest copy")).Count);
            Labelled<Control>(window, "Preview of the mask layer Painted 2");
            Click(window, Labelled<Button>(window, "Add a mask layer that reads a mask"));
            Dispatcher.UIThread.RunJobs();
            Labelled<ComboBox>(window, "Mask");
            window.Close();
        });

        private static T Labelled<T>(MainWindow window, string label) where T : Control => window.GetVisualDescendants().OfType<T>()
            .First(control => control.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(control) == label);

        private static void Click(MainWindow window, Control control)
        {
            control.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            for (int i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }
        }
    }
}
