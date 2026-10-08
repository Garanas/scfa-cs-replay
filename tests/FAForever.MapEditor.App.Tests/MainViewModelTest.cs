using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;

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

            public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folders.Count > 0 ? Folders.Dequeue() : null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName)
            {
                SaveQuestions++;
                return Task.FromResult(SaveChoice);
            }
        }

        [TestMethod]
        public void ImportsAMapFolder()
        {
            MainViewModel model = new MainViewModel(new Answers());
            int opened = 0;
            model.WorkspaceChanged += (_, _) => opened++;

            model.Open(HardFfa);

            Assert.IsTrue(model.HasMap);
            Assert.AreEqual(1, opened);
            StringAssert.StartsWith(model.Title, "HardFFA - not saved as a project");
            Assert.IsTrue(model.SaveProjectCommand.CanExecute(null));
        }

        [TestMethod]
        public void PaintsAStrokeThatIsOneStepOfUndo()
        {
            MainViewModel model = new MainViewModel(new Answers());
            model.Open(HardFfa);
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
        public async Task SavesAnImportedMapWhereThePersonSays()
        {
            Answers answers = new Answers();
            MainViewModel model = new MainViewModel(answers);
            model.Open(HardFfa);
            model.Stroke([(50, 50), (60, 60)]);
            string project = NewFolder();
            answers.Folders.Enqueue(project);

            await model.SaveProjectCommand.ExecuteAsync(null);

            Assert.AreEqual(Path.GetFullPath(project), model.Workspace!.ProjectFolder);
            Assert.IsFalse(model.Workspace.Session.IsModified);
            Assert.IsTrue(File.Exists(Path.Combine(project, "map.json")));
        }

        [TestMethod]
        public async Task AsksBeforeItLeavesUnsavedChanges()
        {
            Answers answers = new Answers { SaveChoice = SaveChoice.Cancel };
            MainViewModel model = new MainViewModel(answers);
            model.Open(HardFfa);
            model.Stroke([(50, 50)]);
            answers.Folders.Enqueue(HardFfa);

            await model.ImportMapCommand.ExecuteAsync(null);
            Assert.AreEqual(1, answers.SaveQuestions);
            Assert.IsTrue(model.Workspace!.Session.IsModified, "cancelled: the changes are still there");

            answers.SaveChoice = SaveChoice.Discard;
            await model.ImportMapCommand.ExecuteAsync(null);
            Assert.IsFalse(model.Workspace!.Session.IsModified, "the map was imported again");
        }

        [TestMethod]
        public void ExportsTheMap()
        {
            MainViewModel model = new MainViewModel(new Answers());
            model.Open(HardFfa);

            string folder = model.Export(NewFolder(), version: 4);

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
        }
    }
}
