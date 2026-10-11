using System.Text.Json.Nodes;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Pictures in the panels' lists: the Water panel's maps with their count of layers (issue 23 of
    /// the wave 10 walkthrough), the Procedural list's path networks, masks and props from rules
    /// (issue 24).
    /// </summary>
    [TestClass]
    public sealed class PanelPicturesTest
    {
        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            model.Assets.Use(TestAssets.Catalog());
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static async Task Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            Commands.CommandRun run = await model.Commands.TryRunAsync(id, arguments, interactive: false);
            Assert.IsTrue(run.Ran, $"{id}: {run.Why}");
            Dispatcher.UIThread.RunJobs();
        }

        [TestMethod]
        public Task TheWaterMapsHaveAPictureAndTheirLayersAndTheInspectorLeavesTheBrushToTheOptions() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectMode("water");
            Dispatcher.UIThread.RunJobs();
            PanelPart foam = model.WaterSettings.Parts.Find("water.foam")!;
            Assert.IsInstanceOfType<CoveragePicture>(foam.Preview);
            Assert.AreEqual("1 layer", foam.Detail);

            await Run(model, "layers.add", new JsonObject { ["target"] = "water:foam", ["source"] = "painted" });

            Assert.AreEqual("2 layers", foam.Detail);
            string ui = UiTree.Describe(window, hidden: false).ToJsonString();
            Assert.IsTrue(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Controls.ImagePreview>()
                .Any(preview => preview.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(preview) == "Preview of Foam"), "the panel's entry shows its picture");
            Assert.IsFalse(ui.Contains("Water brush radius in ogrids", StringComparison.Ordinal), "the radius is in the options row only");
            window.Close();
        });

        [TestMethod]
        public Task TheProceduralListHasPicturesOfPathsAndOfWhatRulesMade() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            ProceduralPart Entry(string id) => (ProceduralPart)model.ProceduralList.Parts.Items.Single(part => part.Id == $"procedural.{id}").Content;
            Assert.IsNull(Entry("pathNetwork").Picture);
            Assert.IsNull(Entry("masksFromRules").Picture);
            Assert.IsNull(Entry("propsFromRules").Picture);

            await Run(model, "generate.navigation");
            await Run(model, "markers.connectPaths");
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "highlands" });
            await Run(model, "layers.add", new JsonObject { ["target"] = "mask:highlands", ["source"] = "rules" });
            await Run(model, "paint.addMaskRule", new JsonObject { ["kind"] = "height", ["min"] = 0, ["mask"] = "highlands" });
            await Run(model, "generate.masks");
            await Run(model, "props.addRule", new JsonObject { ["name"] = "rocks", ["layer"] = "highlands", ["density"] = 0.5 });
            await Run(model, "props.setRulePalette", new JsonObject { ["props"] = new JsonArray("/env/evergreen/props/trees/pine06_prop.bp") });
            await Run(model, "generate.props");

            Assert.IsInstanceOfType<PathNetworkPicture>(Entry("pathNetwork").Picture);
            Assert.IsInstanceOfType<CoveragePicture>(Entry("masksFromRules").Picture);
            RulePropsPicture props = (RulePropsPicture)Entry("propsFromRules").Picture!;
            Assert.IsTrue(props.Places.Count > 0);
            Assert.IsNotNull(props.Render(32), "it draws");
            Assert.IsNotNull(Entry("pathNetwork").Picture!.Render(32));
            window.Close();
        });
    }
}
