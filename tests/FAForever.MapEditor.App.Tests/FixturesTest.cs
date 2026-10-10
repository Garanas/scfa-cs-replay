using System.Text.Json.Nodes;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The test fixtures themselves: the converted assets with models of their own (made here,
    /// none of the game's) and the sandbox built from them, with one of every kind of thing.
    /// </summary>
    [TestClass]
    public sealed class FixturesTest
    {
        [TestMethod]
        public void TheTestUnitHasAModelThatDraws()
        {
            AssetEntry unit = TestAssets.Catalog(unitModel: true).Find("uec1901")!;
            Assert.IsTrue(unit.IsCivilian);
            Assert.IsNotNull(unit.Model, "its mesh and albedo");

            FileFormats.Mesh.RgbaImage picture = AssetPreview.Render(unit.Model, 64);

            Assert.IsTrue(Enumerable.Range(0, picture.Width * picture.Height).Any(pixel => picture.Pixels[pixel * 4 + 3] > 0), "the box shows in its picture");
        }

        [TestMethod]
        public Task TheHelperCapturesOneControlSuchAsThePicker() => HeadlessApp.Run(async () =>
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            model.Assets.Use(TestAssets.Catalog(unitModel: true));
            await model.OpenAsync(TestAssets.SandboxProject());
            window.DataContext = model;
            window.Show();
            model.SelectMode("units");
            _ = model.RunAsync("units.pick");
            Dispatcher.UIThread.RunJobs();
            Avalonia.Controls.Control picker = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Border>()
                .First(border => Avalonia.Automation.AutomationProperties.GetName(border) == "Picker");

            byte[] png = HeadlessApp.Capture(picker);

            using Avalonia.Media.Imaging.Bitmap bitmap = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png));
            Assert.AreEqual((int)picker.Bounds.Width, bitmap.PixelSize.Width, "the control's own size, not the window's");
            Assert.IsTrue(picker.Bounds.Width < window.Bounds.Width);
            HeadlessApp.SaveCapture(png, "picker");
            model.Picker.Cancel();
            window.Close();
        });

        [TestMethod]
        public Task TheSandboxHasOneOfEachKindAndItsThingsCanBeClicked() => HeadlessApp.Run(async () =>
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            model.Assets.Use(TestAssets.Catalog(unitModel: true));
            await model.OpenAsync(TestAssets.SandboxProject());
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            MapDocument document = model.ShownDocument!;

            Assert.IsFalse(document.IsPublishable);
            Assert.AreEqual(2, document.Markers.Spawns.Length);
            Assert.IsTrue(document.Markers.Resources.Any(marker => marker.Kind == Project.Files.ResourceKind.Hydrocarbon));
            Assert.IsTrue(document.Markers.PathNodes.All(node => node.AdjacentTo.Length > 0), "the path nodes are linked");
            Assert.AreEqual(1, document.Markers.AiMarkers.Length);
            Assert.AreEqual(1, document.Markers.Areas.Length);
            Assert.AreEqual(1, document.Markers.Chains.Length);
            Assert.AreEqual(1, document.Decals.Length);
            Assert.AreEqual(1, document.DecalGroups.Length);
            Assert.IsTrue(document.Props.Any(prop => prop.Blueprint == "/env/evergreen/props/rocks/rock01_prop.bp"), "the rock with its model");
            PlacedUnit unit = document.AllUnits().First(placed => placed.Unit.Blueprint == "uec1901");
            Assert.AreEqual("NEUTRAL_CIVILIAN", unit.Army);

            // a click on the unit's pixel in the top view selects it
            AutomationServer automation = new AutomationServer(window, model, port: 0);
            model.View = MapViewTab.Top;
            model.SelectTool("units.select");
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            ObjectRef thing = ObjectRef.Unit(unit.Unit.Name);
            JsonObject point = (JsonObject)(await automation.HandleAsync("view-point", new JsonObject { ["ref"] = thing.ToString() }))["point"]!["top"]!;
            await automation.HandleAsync("mouse", new JsonObject { ["view"] = "top", ["x"] = point["x"]!.DeepClone(), ["y"] = point["y"]!.DeepClone() });
            Dispatcher.UIThread.RunJobs();

            CollectionAssert.AreEqual(new[] { thing }, model.Selection.Items.ToArray());
            window.Close();
        });

        [TestMethod]
        public void TheSlopeMapHasACleanRamp()
        {
            Heightmap heights = FAForever.MapEditor.Project.MapProjectStore.Load(TestMaps.SlopeProject(degrees: 30)).Terrain.Heightmap;

            Assert.AreEqual(TestMaps.Low, heights.GetHeight(10, 10), 0.01, "flat low ground");
            Assert.AreEqual(TestMaps.High(30), heights.GetHeight(200, 250), 0.01, "flat high ground");
            for (int x = TestMaps.RampStart; x < TestMaps.RampEnd; x += 7)
            {
                float rise = heights.GetHeight(x + 1, 128) - heights.GetHeight(x, 128);
                Assert.AreEqual(TestMaps.Rise(30), rise, 1e-4, $"the same rise at x = {x}");
                Assert.AreEqual(30, MathF.Atan(rise) * 180 / MathF.PI, 0.05, "30 degrees");
            }
            Assert.AreEqual(heights.GetHeight(120, 3), heights.GetHeight(120, 250), "the same along z");
        }
    }
}
