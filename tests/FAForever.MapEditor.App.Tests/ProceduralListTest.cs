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
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Procedural mode as a list (the panel lists, the inspector edits): an entry per generated
    /// result with how it stands, a picture and Generate, the chosen one's settings and details in the
    /// inspector, the entries with a tool picking it, and the export last.
    /// </summary>
    [TestClass]
    public sealed class ProceduralListTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.TryRunAsync(id, arguments ?? [], interactive: false);

        private static async Task Until(Func<bool> done)
        {
            for (int i = 0; i < 500 && !done(); i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.IsTrue(done(), "it did not finish");
        }

        /// <summary>
        /// A generator a feature registers after the list was made (as the sky cube map does).
        /// </summary>
        private sealed class LateGenerator : IGenerator
        {
            public string Id => "lateTest";

            public string Title => "Late result";

            public string Story => "F-7";

            public IReadOnlyList<GeneratedInput> Inputs { get; } = [GeneratedInput.Heightmap, GeneratedInput.Lighting];

            public object Generate(MapDocument document) => document.Width;
        }

        [TestMethod]
        public async Task TheListHasAnEntryPerResultUnderHeadingsAndTheExportLast()
        {
            MainViewModel model = await Open();

            CollectionAssert.AreEqual(new[]
            {
                "procedural.walkability", "procedural.navigationMesh", "procedural.pathNetwork",
                "procedural.normalMap", "procedural.normalDecal", "procedural.shadowDecal",
                "procedural.masksFromRules", "procedural.propsFromRules", "procedural.preview", "procedural.skyCube", "procedural.export",
            }, model.ProceduralList.Parts.Items.Select(part => part.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "Movement", "Terrain surface", "From rules", "Pictures", "Export" },
                model.ProceduralList.Groups.Select(group => group.Title).ToArray());
            Assert.AreEqual("procedural.walkability", model.ProceduralList.Parts.Chosen.Id, "the first is chosen until another is");
            Assert.AreSame(model.ProceduralList.Parts, model.PanelPartLists["procedural"], "view.inspectPart knows them");
            Assert.AreEqual("Missing", model.ProceduralList.Parts.Find("procedural.walkability")!.Detail);
            Assert.AreEqual("none", model.ProceduralList.Parts.Find("procedural.shadowDecal")!.Detail, "a decal not generated says its source");
            Assert.IsInstanceOfType<ColourGridPicture>(model.ProceduralList.PartOf(NormalMapGenerator.GeneratorId)!.Picture, "the normal map has its picture");
            Assert.AreSame(model.PathNodes, model.ProceduralList.PartOf(PathNetworkGenerator.GeneratorId)!.Details);
        }

        [TestMethod]
        public async Task AnEntrySaysHowItStandsWhatItIsMadeFromAndWhenItWasMade()
        {
            MainViewModel model = await Open();
            ProceduralPart walkability = model.ProceduralList.PartOf(WalkabilityGenerator.GeneratorId)!;
            Assert.AreEqual("Not made in this session", walkability.MadeText);
            StringAssert.StartsWith(walkability.ReadsText, "Made from heightmap, terrain types");
            Assert.IsNull(walkability.Picture);

            await walkability.GenerateCommand.ExecuteAsync(null);
            await Until(() => !model.Generation.IsRunning(WalkabilityGenerator.GeneratorId) && model.Walkability.Navigation is not null);

            Assert.AreEqual("Current", model.ProceduralList.Parts.Find(walkability.PartId)!.Detail);
            Assert.AreEqual("Generate again", walkability.GenerateText);
            StringAssert.StartsWith(walkability.MadeText, "Made at ");
            Assert.IsInstanceOfType<NavGridPicture>(walkability.Picture);
            Assert.IsTrue(walkability.CanAuto && !walkability.AutoOn);

            await walkability.ToggleAutoCommand.ExecuteAsync(null);
            Assert.IsTrue(model.Map!.Document.Procedural.RegeneratesOnItsOwn(WalkabilityGenerator.GeneratorId), "its check box runs generate.auto");
            Assert.IsTrue(walkability.AutoOn);

            ProceduralPart navigation = model.ProceduralList.PartOf(NavigationMeshGenerator.GeneratorId)!;
            StringAssert.EndsWith(navigation.ReadsText, "and the last walkability", "a result it reads is named");
            ProceduralPart normalMap = model.ProceduralList.PartOf(NormalMapGenerator.GeneratorId)!;
            Assert.IsTrue(normalMap.ShowsExportToggle && normalMap.OnExport, "the normal map's export check box");
            Assert.AreEqual("Taken from the map as it was opened", model.ProceduralList.PartOf(MapPreview.GeneratorId)!.MadeText);
        }

        [TestMethod]
        public async Task AnEntryWithAToolPicksItAndTheToolShowsItsEntry()
        {
            MainViewModel model = await Open();
            model.SelectMode("procedural");
            Assert.AreEqual("procedural.walkability", model.ProceduralList.Parts.Chosen.Id, "coming in with the first tool keeps the chosen entry");

            await Run(model, "view.inspectPart", new JsonObject { ["part"] = "procedural.pathNetwork" });
            Assert.AreEqual("procedural.pathNodes", model.ActiveTool!.Definition.Id);

            model.SelectTool("procedural.regions");
            Assert.AreEqual("procedural.navigationMesh", model.ProceduralList.Parts.Chosen.Id, "the tool shows its entry");

            model.ProceduralList.Parts.Choose("procedural.normalMap");
            Assert.AreEqual("procedural.regions", model.ActiveTool!.Definition.Id, "an entry without a tool leaves the tool");

            model.SelectMode("terrain");
            model.SelectMode("procedural");
            Assert.AreEqual("procedural.normalMap", model.ProceduralList.Parts.Chosen.Id, "coming back keeps the chosen entry");
        }

        [TestMethod]
        public async Task AGeneratorRegisteredLaterIsListedBeforeTheExportAndCanBeDescribed()
        {
            MainViewModel model = await Open();

            model.Generation.Register(new LateGenerator(), "generate.walkability");
            Assert.AreEqual("procedural.lateTest", model.ProceduralList.Parts.Items[^2].Id);
            Assert.AreEqual(ProceduralListViewModel.OtherGroup, model.ProceduralList.Parts.Items[^2].Group);
            Assert.AreEqual("procedural.export", model.ProceduralList.Parts.Items[^1].Id);
            Assert.AreEqual("Made from heightmap and lighting", model.ProceduralList.PartOf("lateTest")!.ReadsText);

            model.ProceduralList.Describe(new ProceduralEntry("lateTest", "Pictures") { Facts = _ => ["A fact"] });
            CollectionAssert.AreEqual(new[] { "procedural.preview", "procedural.skyCube", "procedural.lateTest", "procedural.export" },
                model.ProceduralList.Parts.Items.TakeLast(4).Select(part => part.Id).ToArray(), "under its heading");
            CollectionAssert.AreEqual(new[] { "A fact" }, model.ProceduralList.PartOf("lateTest")!.Facts.ToArray());
            Assert.IsFalse(model.ProceduralList.Groups.Any(group => group.Title == ProceduralListViewModel.OtherGroup));

            await Run(model, "view.inspectPart", new JsonObject { ["part"] = "procedural.lateTest" });
            Assert.AreEqual("procedural.lateTest", model.ProceduralList.Parts.Chosen.Id);
        }

        [TestMethod]
        public void GenerationTimingsReadShort()
        {
            Assert.AreEqual("340 ms", GenerationTiming.Duration(TimeSpan.FromMilliseconds(340.4)));
            Assert.AreEqual("2.3 s", GenerationTiming.Duration(TimeSpan.FromMilliseconds(2345)));
            StringAssert.EndsWith(new GenerationTiming(DateTimeOffset.Now, TimeSpan.FromSeconds(1), GenerationWay.OnItsOwn).Text, "in 1.0 s, on its own");
        }

        #region In the window

        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> OpenWindow()
        {
            MainViewModel model = await Open();
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            model.SelectMode("procedural");
            Dispatcher.UIThread.RunJobs();
            return (window, model, new AutomationServer(window, model, port: 0));
        }

        private static IEnumerable<T> Named<T>(MainWindow window, string name) where T : Control =>
            window.GetVisualDescendants().OfType<T>().Where(control => Avalonia.Automation.AutomationProperties.GetName(control) == name && control.IsEffectivelyVisible);

        private static void Click(MainWindow window, Control control)
        {
            control.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private static string Inspected(MainWindow window) =>
            window.GetVisualDescendants().OfType<TextBlock>().Single(text => Avalonia.Automation.AutomationProperties.GetName(text) == "Inspected part" && text.IsEffectivelyVisible).Text!;

        [TestMethod]
        public Task ClickingAnEntryShowsItsPartInTheInspectorAndTheStateSaysWhich() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await OpenWindow();
            Assert.AreEqual("Walkability", Inspected(window));
            Assert.AreEqual(1, Named<ComboBox>(window, "Walkability of the units of a layer").Count(), "walkability's choices");

            Click(window, Named<Button>(window, "Navigation mesh").Single());
            Assert.AreEqual("Navigation mesh", Inspected(window));
            Assert.AreEqual(1, Named<ComboBox>(window, "Connected regions of the units of a layer").Count(), "the navigation mesh's choices and regions");
            Assert.AreEqual("procedural.regions", model.ActiveTool!.Definition.Id);

            Click(window, Named<Button>(window, "Path networks").Single());
            Assert.AreEqual(1, Named<Button>(window, "Generate path networks").Count(), "the path networks' own part");
            Assert.AreEqual(0, Named<Button>(window, "Generate the path networks from the inspector").Count(), "with their own Generate only");
            Assert.AreEqual("procedural.pathNodes", model.ActiveTool!.Definition.Id);

            Click(window, Named<Button>(window, "Shadow decal").Single());
            Assert.AreEqual("Shadow decal", Inspected(window));
            Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Source" && text.IsEffectivelyVisible), "the slot's settings");

            Click(window, Named<Button>(window, "Export").Single());
            Assert.AreEqual(1, Named<Button>(window, "Export to the vault").Count(), "the export's steps last");

            JsonObject state = (JsonObject)await automation.HandleAsync("state", new JsonObject { ["paths"] = new JsonArray("procedural.list", "panelParts.procedural.chosen") });
            Assert.AreEqual("procedural.export", state["procedural.list"]!["chosen"]!.GetValue<string>());
            Assert.AreEqual("procedural.export", state["panelParts.procedural.chosen"]!.GetValue<string>());
            Assert.AreEqual("Missing", ((JsonArray)state["procedural.list"]!["entries"]!)[0]!["state"]!.GetValue<string>());
            window.Close();
        });

        [TestMethod]
        public Task AnEntrysGenerateGeneratesItAndTheEntrySaysSo() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await OpenWindow();

            Click(window, Named<Button>(window, "Generate the normal map").Single());
            await Until(() => !model.Generation.IsRunning(NormalMapGenerator.GeneratorId) && model.Generation.TimingOf(NormalMapGenerator.GeneratorId) is not null);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual("Current", model.ProceduralList.Parts.Find("procedural.normalMap")!.Detail);
            Assert.AreEqual("procedural.walkability", model.ProceduralList.Parts.Chosen.Id, "Generate does not choose the entry");
            Assert.IsNull(model.Map!.Session.UndoDescription, "a derived result: no step of undo");
            Assert.IsTrue(model.Map.Session.IsModified);
            window.Close();
        });

        #endregion
    }
}
