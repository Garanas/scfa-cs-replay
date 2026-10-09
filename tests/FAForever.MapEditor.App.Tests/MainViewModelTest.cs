using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Tests
{
    [TestClass]
    public class MainViewModelTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "projects");

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                // git's objects are read-only, which stops a delete on Windows
                foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string NewFolder() => Path.Combine(Root, Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Answers the editor's questions as a test says.
        /// </summary>
        private sealed class Answers : IEditorDialogs
        {
            public Queue<string?> Folders { get; } = new Queue<string?>();

            public SaveChoice SaveChoice { get; set; } = SaveChoice.Cancel;

            public int SaveQuestions { get; private set; }

            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult(Folders.Count > 0 ? Folders.Dequeue() : null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName)
            {
                SaveQuestions++;
                return Task.FromResult(SaveChoice);
            }
        }

        [TestMethod]
        public async Task ImportsAMapFolder()
        {
            MainViewModel model = new MainViewModel(new Answers());
            int opened = 0;
            model.MapChanged += (_, _) => opened++;

            await model.OpenAsync(HardFfa);

            Assert.IsTrue(model.HasMap);
            Assert.AreEqual(1, opened);
            StringAssert.StartsWith(model.Title, "HardFFA - not saved as a project");
            Assert.IsTrue(model.Commands.CanRun(model.Commands.Find("file.save")!));
        }

        [TestMethod]
        public async Task CommitsBesideSavingOnceVersionControlIsOn()
        {
            MainViewModel model = new MainViewModel(new Answers());
            await model.OpenAsync(HardFfa);
            VersionControlViewModel versionControl = model.VersionControl;
            Assert.IsFalse(model.Commands.CanRun(model.Commands.Find("file.commit")!), "an imported map is no project yet");
            await versionControl.OpenAsync();
            Assert.IsFalse(versionControl.IsOpen);

            await model.SaveAsAsync(NewFolder());
            Assert.IsTrue(await model.Commands.RunAsync("file.commit"));
            Assert.IsTrue(versionControl.IsOpen);
            Assert.IsTrue(versionControl.IsNotVersioned, "the setup shows before version control is on");
            Assert.IsTrue(versionControl.UseLfs, "LFS is on unless turned off");
            versionControl.AuthorName = "Map maker";
            versionControl.AuthorEmail = "maker@example.org";
            versionControl.UseLfs = false;
            await versionControl.TurnOnCommand.ExecuteAsync(null);
            Assert.IsNull(versionControl.Error);
            Assert.IsTrue(versionControl.IsVersioned);
            Assert.IsFalse(versionControl.IsOpen);

            model.Stroke([(100, 40), (130, 40)]);
            await versionControl.OpenAsync();
            await versionControl.CommitChangesCommand.ExecuteAsync(null);
            Assert.AreEqual("Say what the commit changes", versionControl.Error);
            versionControl.CommitMessage = "Raise a ridge";
            await versionControl.CommitChangesCommand.ExecuteAsync(null);

            Assert.IsNull(versionControl.Error);
            StringAssert.StartsWith(model.Status, "Committed ");
            StringAssert.EndsWith(versionControl.Repository!.LastCommit, "Raise a ridge");
            Assert.AreEqual(0, versionControl.Repository.Changes, "the commit saved the stroke first");
            StringAssert.StartsWith(model.Title, "HardFFA", "saved");
            Assert.IsNull(await versionControl.CommitAsync("Nothing"));
        }

        [TestMethod]
        public async Task PaintsAStrokeThatIsOneStepOfUndo()
        {
            MainViewModel model = new MainViewModel(new Answers());
            await model.OpenAsync(HardFfa);
            float before = model.ShownDocument!.Terrain.Heightmap.GetHeight(128, 40);
            SculptTool sculpt = model.Tool<SculptTool>();
            sculpt.Mode = HeightBrushMode.Raise;
            sculpt.Radius = 10;
            sculpt.Strength = 2;

            sculpt.Press(100, 40);
            sculpt.Drag(130, 40);
            Assert.IsTrue(model.ShownDocument!.Terrain.Heightmap.GetHeight(128, 40) > before, "the stroke shows while it is painted");
            Assert.IsFalse(model.CanUndo, "nothing to undo until the stroke ends");
            sculpt.Release();

            Assert.AreEqual("Raise terrain", model.Map!.Session.UndoDescription);
            StringAssert.StartsWith(model.Title, "* HardFFA");
            Assert.IsTrue(await model.Commands.RunAsync("edit.undo"));
            Assert.AreEqual(before, model.ShownDocument!.Terrain.Heightmap.GetHeight(128, 40));
            Assert.AreEqual("Raise terrain", model.Map!.Session.RedoDescription);
        }

        [TestMethod]
        public async Task JumpsThroughTheHistoryFromThePanel()
        {
            MainViewModel model = new MainViewModel(new Answers());
            await model.OpenAsync(HardFfa);
            model.Stroke([(50, 50), (60, 60)]);
            model.Tool<SculptTool>().Mode = HeightBrushMode.Lower;
            model.Stroke([(100, 100), (110, 110)]);

            CollectionAssert.AreEqual(new[] { "Opened", "Raise terrain", "Lower terrain" }, model.History.Select(entry => entry.Description).ToList());
            Assert.AreEqual(2, model.HistoryIndex);

            model.HistoryIndex = 0;

            Assert.IsFalse(model.Map!.Session.CanUndo, "back at the map as it was opened");
            Assert.IsTrue(model.History.Skip(1).All(entry => entry.IsUndone));
            Assert.AreEqual(0, model.HistoryIndex);
        }

        [TestMethod]
        public async Task PaintsTheMaskOfALayer()
        {
            MainViewModel model = new MainViewModel(new Answers());
            await model.OpenAsync(HardFfa);
            StrataTool strata = model.StrataTool;
            Assert.AreEqual(8, strata.Strata.Count, "eight layers with a mask");
            byte before = model.ShownDocument!.Terrain.StratumMasks.Layers[2][64, 64];
            var heights = model.ShownDocument.Terrain.Heightmap;

            model.SelectTool("paint.strata");
            strata.Choose(3);
            strata.Radius = 10;
            strata.Strength = 1;
            model.Stroke([(120, 128), (136, 128)]);

            Assert.AreEqual("Paint layer 3", model.Map!.Session.UndoDescription);
            Assert.AreEqual(255, model.ShownDocument!.Terrain.StratumMasks.Layers[2][64, 64], "ogrid (128, 128) is pixel (64, 64) of a mask of half the size");
            Assert.AreSame(heights, model.ShownDocument.Terrain.Heightmap, "the heights are untouched");
            model.Undo();
            Assert.AreEqual(before, model.ShownDocument!.Terrain.StratumMasks.Layers[2][64, 64]);
        }

        [TestMethod]
        public async Task SculptsAndPaintsWithABrushOfTheWorkspace()
        {
            MainViewModel model = new MainViewModel(new Answers());
            await model.OpenAsync(HardFfa);
            AssetCatalog catalog = TestAssets.Catalog();
            // brushes of one white pixel: the whole square works fully at its centre
            byte[] white = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGP4DwABAQEAsTj2FAAAAABJRU5ErkJggg==");
            foreach (AssetEntry brush in catalog.Entries.Where(entry => entry.Kind == AssetKind.Brush))
            {
                File.WriteAllBytes(brush.File, white);
            }
            model.Assets.Use(catalog);
            SculptTool sculpt = model.Tool<SculptTool>();
            StrataTool strata = model.StrataTool;
            CollectionAssert.AreEqual(new[] { "Round", "ridge", "craters/crater01" }, sculpt.Shape.Choices.Select(choice => choice.Path).ToArray());
            Assert.AreEqual(5, sculpt.StrengthMaximum);

            model.SelectMode("terrain");
            model.Assets.Select("brushes/craters/crater01.png");
            Assert.AreEqual("brushes/craters/crater01.png", sculpt.Shape.Chosen.Key, "a brush chosen on the shelf is the active tool's shape");
            Assert.IsTrue((await model.Commands.RunAsync("brush.set", new System.Text.Json.Nodes.JsonObject { ["rotation"] = 45, ["radius"] = 10, ["strength"] = 2 }, interactive: false)).Ran);
            Assert.AreEqual(45, sculpt.Shape.Rotation);
            Assert.AreEqual(50, sculpt.StrengthMaximum, "a picture raises a mountain at a click");
            float before = model.ShownDocument!.Terrain.Heightmap.GetHeight(100, 40);
            float far = model.ShownDocument.Terrain.Heightmap.GetHeight(116, 40);
            model.Stroke([(100, 40)]);
            Assert.AreEqual(before + 2, model.ShownDocument!.Terrain.Heightmap.GetHeight(100, 40), 0.05f, "the picture is white at its centre");
            Assert.AreEqual(far, model.ShownDocument.Terrain.Heightmap.GetHeight(116, 40), "nothing past the corner of its square");

            model.SelectTool("paint.strata");
            Assert.IsTrue((await model.Commands.RunAsync("brush.set", new System.Text.Json.Nodes.JsonObject { ["layer"] = 3, ["shape"] = "ridge.png" }, interactive: false)).Ran);
            Assert.AreEqual("brushes/ridge.png", strata.Shape.Chosen.Key);
            Assert.AreEqual("brushes/craters/crater01.png", sculpt.Shape.Chosen.Key, "each tool has its own shape");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                model.Commands.RunAsync("brush.set", new System.Text.Json.Nodes.JsonObject { ["shape"] = "nothing.png" }, interactive: false));

            File.Delete(strata.Shape.Chosen.File!);
            model.Stroke([(50, 50)]);
            StringAssert.StartsWith(model.Status, "The brush ridge cannot be read");
            Assert.AreEqual("Raise terrain", model.Map!.Session.UndoDescription, "no stroke");

            model.Assets.Use(TestAssets.WithoutBrushes());
            Assert.AreEqual(BrushChoice.Round, strata.Shape.Chosen, "a brush the workspace lost is round again");
        }

        [TestMethod]
        public async Task SavesAnImportedMapWhereThePersonSays()
        {
            Answers answers = new Answers();
            MainViewModel model = new MainViewModel(answers);
            await model.OpenAsync(HardFfa);
            model.Stroke([(50, 50), (60, 60)]);
            string project = NewFolder();
            answers.Folders.Enqueue(project);

            await model.RunAsync("file.save");

            Assert.AreEqual(Path.GetFullPath(project), model.Map!.ProjectFolder);
            Assert.IsFalse(model.Map.Session.IsModified);
            Assert.IsTrue(File.Exists(Path.Combine(project, "map.json")));
        }

        [TestMethod]
        public async Task AsksBeforeItLeavesUnsavedChanges()
        {
            Answers answers = new Answers { SaveChoice = SaveChoice.Cancel };
            MainViewModel model = new MainViewModel(answers);
            await model.OpenAsync(HardFfa);
            model.Stroke([(50, 50)]);
            answers.Folders.Enqueue(HardFfa);

            await model.RunAsync("file.import");
            Assert.AreEqual(1, answers.SaveQuestions);
            Assert.IsTrue(model.Map!.Session.IsModified, "cancelled: the changes are still there");

            answers.SaveChoice = SaveChoice.Discard;
            await model.RunAsync("file.import");
            Assert.IsFalse(model.Map!.Session.IsModified, "the map was imported again");
        }

        [TestMethod]
        public async Task ExportsTheMap()
        {
            MainViewModel model = new MainViewModel(new Answers());
            await model.OpenAsync(HardFfa);

            string folder = await model.ExportAsync(NewFolder(), version: 4);

            Assert.AreEqual("HardFFA.v0004", Path.GetFileName(folder));
            StringAssert.StartsWith(model.Status, "Exported");
        }

        [TestMethod]
        public async Task ShowsWhatWentWrongInTheStatus()
        {
            Answers answers = new Answers();
            MainViewModel model = new MainViewModel(answers);
            answers.Folders.Enqueue(Path.Combine(Root, "does-not-exist"));

            await model.RunAsync("file.open");

            Assert.IsFalse(model.HasMap);
            StringAssert.Contains(model.Status, "is missing");
            ConsoleEntry error = model.Diagnostics.ConsoleProblems.Single();
            Assert.AreEqual(LogLevel.Error, error.Level, "logged once, as an error of the operation");
            Assert.AreEqual("Files", error.Category);
            StringAssert.Contains(error.Message, "open failed after");
        }

        [TestMethod]
        public void TheConsoleShowsWhatIsLoggedAndCanShowOnlyProblems()
        {
            Telemetry telemetry = new Telemetry(background: false);
            DiagnosticsViewModel diagnostics = new DiagnosticsViewModel(telemetry, ui: null, showFolder: _ => { });
            ILogger log = telemetry.CreateLogger("Rendering");

            log.LogInformation("The 3D view runs on {Renderer}", "OpenGLES 3.0");
            log.LogWarning("A texture could not be read: {Reason}", "no such file");

            Assert.AreEqual("The 3D view runs on OpenGLES 3.0", diagnostics.Console[^2].Message);
            Assert.AreEqual("warning", diagnostics.Console[^1].LevelText);
            Assert.IsTrue(diagnostics.Console[^1].IsWarning && !diagnostics.Console[^1].IsError, "a warning has its own colour");
            Assert.AreEqual("Console (1)", diagnostics.ConsoleHeader);
            Assert.AreSame(diagnostics.Console, diagnostics.ShownConsole);

            diagnostics.ConsoleShowsProblemsOnly = true;

            Assert.AreEqual("A texture could not be read: no such file", diagnostics.ShownConsole.Single().Message);
        }

        [TestMethod]
        public async Task RemembersTheChoicesAboutDiagnostics()
        {
            UserSettings settings = new UserSettings(Path.Combine(NewFolder(), "settings.json"));
            MainViewModel model = new MainViewModel(new Answers(), new Telemetry(settings: settings.Telemetry, background: false));
            model.Setup.FindFaf = () => null;
            await model.StartAsync(settings);
            DiagnosticsViewModel diagnostics = model.Diagnostics;

            Assert.IsTrue(diagnostics.Record);
            Assert.IsFalse(diagnostics.Send);
            StringAssert.Contains(diagnostics.Summary, "Nothing is sent");

            diagnostics.Endpoint = " http://localhost:4318 ";
            diagnostics.Send = true;

            Assert.AreEqual(new TelemetrySettings(true, true, "http://localhost:4318", settings.Telemetry.InstallId), settings.Telemetry);
            Assert.IsTrue(model.Telemetry.Settings.Send);
            Assert.IsTrue(await model.Commands.RunAsync("help.diagnostics"));
            Assert.IsTrue(diagnostics.IsOpen);
        }

        [TestMethod]
        public async Task TheCameraStartsOverEachMapAndTheTopViewMovesIt()
        {
            MainViewModel model = new MainViewModel(new Answers());
            await model.OpenAsync(HardFfa);
            int moved = 0;
            model.CameraMoved += (_, _) => moved++;

            Assert.AreEqual((128f, 128f, 90f), (model.Camera.Now.X, model.Camera.Now.Z, model.Camera.Now.Pitch), "over the middle of the 256 by 256 map");

            model.MoveCameraTo(40, 60);

            Assert.AreEqual((40f, 60f), (model.Camera.Now.X, model.Camera.Now.Z));
            Assert.AreEqual(1, moved, "the views draw again");
        }
        [TestMethod]
        public async Task TheShelfFollowsTheModeAndItsCommandsSetIt()
        {
            MainViewModel model = new MainViewModel(new Answers());
            model.Assets.Use(TestAssets.Catalog());

            model.SelectMode("decals");
            Assert.AreEqual(AssetKind.Decal, model.Assets.Kind);

            CommandRun browse = await model.Commands.RunAsync("assets.browse",
                new System.Text.Json.Nodes.JsonObject { ["kind"] = "texture", ["scope"] = "everything", ["query"] = "rock" }, interactive: false);
            Assert.IsTrue(browse.Ran);
            Assert.AreEqual(DockTab.Assets, model.Dock);
            Assert.AreEqual("tund_rock", ((System.Text.Json.Nodes.JsonObject)browse.Result!)["tiles"]![0]!["title"]!.GetValue<string>());

            Assert.IsTrue((await model.Commands.RunAsync("assets.select", new System.Text.Json.Nodes.JsonObject { ["key"] = "uec1101" }, interactive: false)).Ran);
            Assert.AreEqual("UEF Residential Building", model.Assets.Shown!.Name);

            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                model.Commands.RunAsync("assets.browse", new System.Text.Json.Nodes.JsonObject { ["kind"] = "tree" }, interactive: false));
        }
    }
}
