using FAForever.MapEditor.App.Diagnostics;
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
            Assert.IsTrue(model.SaveProjectCommand.CanExecute(null));
        }

        [TestMethod]
        public async Task PaintsAStrokeThatIsOneStepOfUndo()
        {
            MainViewModel model = new MainViewModel(new Answers());
            await model.OpenAsync(HardFfa);
            float before = model.ShownDocument!.Terrain.Heightmap.GetHeight(128, 40);
            model.BrushMode = HeightBrushMode.Raise;
            model.BrushRadius = 10;
            model.BrushStrength = 2;

            model.BeginStroke(100, 40);
            model.ContinueStroke(130, 40);
            Assert.IsTrue(model.ShownDocument!.Terrain.Heightmap.GetHeight(128, 40) > before, "the stroke shows while it is painted");
            Assert.IsFalse(model.CanUndo, "nothing to undo until the stroke ends");
            model.EndStroke();

            Assert.AreEqual("_Undo Raise terrain", model.UndoHeader);
            StringAssert.StartsWith(model.Title, "* HardFFA");
            model.UndoEditCommand.Execute(null);
            Assert.AreEqual(before, model.ShownDocument!.Terrain.Heightmap.GetHeight(128, 40));
            Assert.AreEqual("_Redo Raise terrain", model.RedoHeader);
        }

        [TestMethod]
        public async Task JumpsThroughTheHistoryFromThePanel()
        {
            MainViewModel model = new MainViewModel(new Answers());
            await model.OpenAsync(HardFfa);
            model.Stroke([(50, 50), (60, 60)]);
            model.BrushMode = HeightBrushMode.Lower;
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
            Assert.AreEqual(9, model.PaintTargets.Count, "the heights and eight layers");
            byte before = model.ShownDocument!.Terrain.StratumMasks.Layers[2][64, 64];
            var heights = model.ShownDocument.Terrain.Heightmap;

            model.SelectPaintTarget(3);
            model.BrushRadius = 10;
            model.MaskStrength = 1;
            model.Stroke([(120, 128), (136, 128)]);

            Assert.AreEqual("_Undo Paint layer 3", model.UndoHeader);
            Assert.AreEqual(255, model.ShownDocument!.Terrain.StratumMasks.Layers[2][64, 64], "ogrid (128, 128) is pixel (64, 64) of a mask of half the size");
            Assert.AreSame(heights, model.ShownDocument.Terrain.Heightmap, "the heights are untouched");
            model.Undo();
            Assert.AreEqual(before, model.ShownDocument!.Terrain.StratumMasks.Layers[2][64, 64]);
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

            await model.SaveProjectCommand.ExecuteAsync(null);

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

            await model.ImportMapCommand.ExecuteAsync(null);
            Assert.AreEqual(1, answers.SaveQuestions);
            Assert.IsTrue(model.Map!.Session.IsModified, "cancelled: the changes are still there");

            answers.SaveChoice = SaveChoice.Discard;
            await model.ImportMapCommand.ExecuteAsync(null);
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

            await model.OpenProjectCommand.ExecuteAsync(null);

            Assert.IsFalse(model.HasMap);
            StringAssert.Contains(model.Status, "is missing");
            ConsoleEntry error = model.ConsoleProblems.Single();
            Assert.AreEqual(LogLevel.Error, error.Level, "logged once, as an error of the operation");
            Assert.AreEqual("Files", error.Category);
            StringAssert.Contains(error.Message, "open failed after");
        }

        [TestMethod]
        public void TheConsoleShowsWhatIsLoggedAndCanShowOnlyProblems()
        {
            MainViewModel model = new MainViewModel(new Answers());
            ILogger log = model.Logger("Rendering");

            log.LogInformation("The 3D view runs on {Renderer}", "OpenGLES 3.0");
            log.LogWarning("A texture could not be read: {Reason}", "no such file");

            Assert.AreEqual("The 3D view runs on OpenGLES 3.0", model.Console[^2].Message);
            Assert.AreEqual("warning", model.Console[^1].LevelText);
            Assert.IsTrue(model.Console[^1].IsWarning && !model.Console[^1].IsError, "a warning has its own colour");
            Assert.AreEqual("Console (1)", model.ConsoleHeader);
            Assert.AreSame(model.Console, model.ShownConsole);

            model.ConsoleShowsProblemsOnly = true;

            Assert.AreEqual("A texture could not be read: no such file", model.ShownConsole.Single().Message);
        }

        [TestMethod]
        public async Task RemembersTheChoicesAboutDiagnostics()
        {
            UserSettings settings = new UserSettings(Path.Combine(NewFolder(), "settings.json"));
            MainViewModel model = new MainViewModel(new Answers(), new Telemetry(settings: settings.Telemetry, background: false)) { FindFaf = () => null };
            await model.StartAsync(settings);

            Assert.IsTrue(model.RecordDiagnostics);
            Assert.IsFalse(model.SendDiagnostics);
            StringAssert.Contains(model.DiagnosticsSummary, "Nothing is sent");

            model.DiagnosticsEndpoint = " http://localhost:4318 ";
            model.SendDiagnostics = true;

            Assert.AreEqual(new TelemetrySettings(true, true, "http://localhost:4318", settings.Telemetry.InstallId), settings.Telemetry);
            Assert.IsTrue(model.Telemetry.Settings.Send);
            Assert.IsTrue(await model.Commands.RunAsync("help.diagnostics"));
            Assert.IsTrue(model.IsDiagnosticsOpen);
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
    }
}
