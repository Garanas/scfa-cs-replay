using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The reusable stack control (wave 8) and <see cref="ImagePreview"/>: a stack of any target
    /// listed with previews that follow the layers' values, its commands with the layer named, the
    /// pictures made once per version of a grid, and the heightmap's layers in the Terrain panel as
    /// its first user, with a larger preview in each tool tip.
    /// </summary>
    [TestClass]
    public sealed class LayerStackControlTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        /// <summary>
        /// A stack of coverage for a mask of the document, outside any command: what wave 9's stacks
        /// will be.
        /// </summary>
        private static (MapDocument Document, LayerStack Stack) MaskStack()
        {
            MapDocument document = MaskLayers.Add(Project.NewMap.Create(new Project.NewMapSettings { Size = 256 }), "forest");
            LayerStack stack = LayerStack.Empty(LayerTarget.Mask("forest"));
            stack = LayerStackEdits.Add(stack, new Layer { Name = "ground", Source = LayerSource.Painted, Coverage = TiledBuffer<byte>.Create(256, 256, 100) });
            stack = LayerStackEdits.Add(stack, new Layer { Name = "paint", Source = LayerSource.Painted, Combine = LayerCombine.Maximum, Coverage = TiledBuffer<byte>.Create(256, 256) });
            return (document, stack);
        }

        [TestMethod]
        public void AStackOfAnyTargetListsItsLayersAndRunsItsCommands()
        {
            (MapDocument document, LayerStack stack) = MaskStack();
            List<(string Command, JsonObject Arguments)> ran = [];
            LayerStack current = stack;
            LayerStackViewModel model = new LayerStackViewModel((command, arguments) =>
                {
                    ran.Add((command, arguments));
                    return Task.CompletedTask;
                }, _ => current, new LayerStackCommands("masks.setLayer", "masks.moveLayer", "masks.removeLayer", "mask layer", "clip", "Clip"),
                [new LayerAddChoice("Painted", "Add a painted mask layer", "A layer the mask brush paints", "masks.addLayer", new JsonObject { ["source"] = "painted" })]);
            model.Refresh(document);

            CollectionAssert.AreEqual(new[] { "paint", "ground" }, model.Items.Select(item => item.Name).ToArray(), "the highest first");
            Assert.AreEqual("paint", model.Chosen, "the highest painted layer");
            Assert.AreEqual("Mask layers", model.ListTitle);
            Assert.AreEqual("Show the mask layer paint", model.Items[0].ShowLabel);
            Assert.IsInstanceOfType<CoveragePicture>(model.Items[0].Preview);
            Assert.AreEqual("Clip", model.Layer.Settings[3].Label);
            Assert.IsTrue(model.CanMoveDown, "without a base a layer goes down to the bottom");

            model.Items[0].IsLocked = true;
            model.MoveDownCommand.Execute(null);
            model.AddChoices[0].Run.Execute(null);
            Assert.AreEqual(("masks.setLayer", "paint"), (ran[0].Command, ran[0].Arguments["layer"]!.GetValue<string>()));
            Assert.IsTrue(ran[0].Arguments["locked"]!.GetValue<bool>());
            Assert.AreEqual(-1, ran[1].Arguments["by"]!.GetValue<int>());
            Assert.AreEqual(("masks.addLayer", "painted"), (ran[2].Command, ran[2].Arguments["source"]!.GetValue<string>()));

            // a brush's dab makes a new version of a layer's grid: the items stay, the preview follows
            StackLayerItem item = model.Items[0];
            TiledBuffer<byte>.Builder painted = current.Layers[1].Coverage!.ToBuilder();
            painted[10, 10] = 255;
            current = current.With(1, current.Layers[1] with { Coverage = painted.ToBuffer() });
            model.Refresh(document);
            Assert.AreSame(item, model.Items[0], "the list is not made again");
            Assert.AreSame(current.Layers[1].Coverage, ((CoveragePicture)item.Preview!).Grid);

            // a setting makes the list again
            current = LayerStackEdits.SetStrength(current, "paint", 0.5f);
            model.Refresh(document);
            Assert.AreNotSame(item, model.Items[0]);
            StringAssert.Contains(model.Items[0].Summary, "50 %");
        }

        [TestMethod]
        public void PicturesShowGridsAsGreyAndHeightsWithARamp()
        {
            TiledBuffer<byte>.Builder grid = TiledBuffer<byte>.Create(256, 128).ToBuilder();
            for (int z = 0; z < 8; z++)
            {
                for (int x = 0; x < 8; x++)
                {
                    grid[x, z] = 255;
                }
            }
            (uint[] pixels, int width, int height) = new CoveragePicture(grid.ToBuffer()).Render(32)!.Value;
            Assert.AreEqual((32, 16), (width, height), "the grid's proportions");
            Assert.AreEqual(0xFFFFFFFFu, pixels[0]);
            Assert.AreEqual(0xFF000000u, pixels[^1]);

            TiledBuffer<float>.Builder differences = TiledBuffer<float>.Create(65, 65).ToBuilder();
            differences[64, 64] = 10;
            (uint[] grey, _, _) = new HeightPicture(differences.ToBuffer(), Differences: true).Render(64)!.Value;
            Assert.AreEqual(128u, grey[0] & 0xFF, "0 is grey");
            Assert.IsTrue((grey[^1] & 0xFF) > 200, "up is lighter");

            TiledBuffer<float>.Builder heights = TiledBuffer<float>.Create(65, 65).ToBuilder();
            for (int z = 0; z < 65; z++)
            {
                for (int x = 0; x < 65; x++)
                {
                    heights[x, z] = x;
                }
            }
            (uint[] ramp, _, _) = new HeightPicture(heights.ToBuffer()).Render(64)!.Value;
            uint low = ramp[32 * 64];
            uint high = ramp[32 * 64 + 63];
            Assert.IsTrue((low >> 8 & 0xFF) > (low & 0xFF), "low ground is green");
            Assert.IsTrue((high & 0xFF) > (low & 0xFF), "high ground is lighter");
        }

        [TestMethod]
        public Task APictureIsMadeOncePerVersionOfItsGrid() => HeadlessApp.Run(() =>
        {
            TiledBuffer<byte> grid = TiledBuffer<byte>.Create(300, 300, 7);
            int made = ImagePreview.Cache.Made;
            Assert.IsNotNull(ImagePreview.Cache.Get(new CoveragePicture(grid), 32));
            Assert.IsNotNull(ImagePreview.Cache.Get(new CoveragePicture(grid), 30), "a size of the same step");
            Assert.AreEqual(made + 1, ImagePreview.Cache.Made);
            Assert.AreEqual(512, ImagePreview.Cache.Get(new CoveragePicture(grid), 512)!.PixelSize.Width, "the large one apart");
            TiledBuffer<byte>.Builder changed = grid.ToBuilder();
            changed[0, 0] = 1;
            ImagePreview.Cache.Get(new CoveragePicture(changed.ToBuffer()), 32);
            Assert.AreEqual(made + 3, ImagePreview.Cache.Made, "a new version, a new picture");
            Assert.IsNull(ImagePreview.Cache.Get(new FilePicture(Path.Combine(AppContext.BaseDirectory, "no such file.png")), 32));
            return Task.CompletedTask;
        });

        [TestMethod]
        public Task TheTerrainPanelShowsTheHeightLayersWithPreviewsAndALargerOneInTheToolTip() => HeadlessApp.Run(async () =>
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers { SaveChoice = SaveChoice.Discard });
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            model.SelectMode("terrain");
            await model.Commands.RunAsync("terrain.addLayer", [], interactive: false);
            Dispatcher.UIThread.RunJobs();

            List<ImagePreview> previews = [.. window.GetVisualDescendants().OfType<ImagePreview>().Where(preview => preview.IsEffectivelyVisible)];
            CollectionAssert.AreEqual(new[] { "Preview of the height layer Sculpt", "Preview of the height layer Base" },
                previews.Select(preview => Avalonia.Automation.AutomationProperties.GetName(preview)).ToArray());
            ImagePreview basePreview = previews[1];
            Assert.IsInstanceOfType<HeightPicture>(basePreview.Picture);
            Assert.AreEqual(32, basePreview.Bounds.Width, "a small square");

            // the sculpt tool's stroke shows in the sculpt layer's preview, without the list being made again
            StackLayerItem sculpt = model.HeightStack.Items[0];
            PreviewPicture? before = sculpt.Preview;
            model.SelectTool("terrain.sculpt");
            model.Tool<SculptTool>().Strength = 2;
            model.Stroke([(100, 100), (120, 110)]);
            Dispatcher.UIThread.RunJobs();
            Assert.AreSame(sculpt, model.HeightStack.Items[0]);
            Assert.AreNotSame(before, sculpt.Preview);

            // the tool tip holds the same picture larger
            ImagePreview large = (ImagePreview)ToolTip.GetTip(basePreview)!;
            Assert.AreSame(basePreview.Picture, large.Picture);
            Assert.AreEqual(512, large.Width);
            large.Measure(new Size(512, 512));
            large.Arrange(new Rect(0, 0, 512, 512));
            HeadlessApp.SaveCapture(HeadlessApp.Capture(large), "height-layer-preview-large");
            HeadlessApp.SaveCapture(HeadlessApp.Capture(window), "height-layers-panel");
            window.Close();
        });
    }
}
