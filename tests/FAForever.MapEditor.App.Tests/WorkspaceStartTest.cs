using System.IO.Compression;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The editor's start: setting up a workspace, updating its assets, and what the workspace
    /// changes afterwards. The game is a folder of empty archives; the conversion itself is tested
    /// in the library.
    /// </summary>
    [TestClass]
    public sealed class WorkspaceStartTest
    {
        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "workspaces");

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Cancel);
        }

        private sealed record Machine(string Folder, string Game, string Vault, UserSettings Settings)
        {
            public string WorkspaceFolder => Path.Combine(Folder, "workspace");

            public MainViewModel NewEditor() => new MainViewModel(new NoDialogs())
            {
                FindFaf = () => new FafInstallation(Game, Vault, null, "3839"),
            };
        }

        private static Machine NewMachine()
        {
            string folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            string game = Path.Combine(folder, "game");
            Directory.CreateDirectory(Path.Combine(game, "gamedata"));
            foreach (string name in GameArchives.Names)
            {
                using ZipArchive empty = ZipFile.Open(Path.Combine(game, "gamedata", name + ".scd"), ZipArchiveMode.Create);
            }
            string vault = Path.Combine(folder, "vault");
            Directory.CreateDirectory(vault);
            return new Machine(folder, game, vault, new UserSettings(Path.Combine(folder, "settings.json")));
        }

        private static async Task<MainViewModel> SetUp(Machine machine)
        {
            MainViewModel model = machine.NewEditor();
            await model.StartAsync(machine.Settings);
            model.SetupFolder = machine.WorkspaceFolder;
            Assert.IsTrue(await model.SetUpWorkspaceAsync(), model.SetupError);
            return model;
        }

        [TestMethod]
        public async Task HelpShowsTheWorkspaceFolder()
        {
            Machine machine = NewMachine();
            MainViewModel model = machine.NewEditor();
            List<string> shown = [];
            model.ShowFolderRequested += shown.Add;

            await model.StartAsync(machine.Settings);
            model.ShowWorkspaceFolder();
            CollectionAssert.AreEqual(Array.Empty<string>(), shown, "no workspace yet");

            model.SetupFolder = machine.WorkspaceFolder;
            Assert.IsTrue(await model.SetUpWorkspaceAsync(), model.SetupError);
            Assert.IsTrue(await model.Commands.RunAsync("help.workspaceFolder"));

            CollectionAssert.AreEqual(new[] { model.Workspace!.Folder }, shown);
        }

        [TestMethod]
        public async Task StartsWithTheSetupFilledInFromFaf()
        {
            Machine machine = NewMachine();
            MainViewModel model = machine.NewEditor();

            await model.StartAsync(machine.Settings);

            Assert.AreEqual(EditorStage.SetUp, model.Stage);
            Assert.IsFalse(model.IsReady);
            Assert.AreEqual(machine.Game, model.SetupGameFolder);
            Assert.AreEqual(machine.Vault, model.SetupVaultFolder);
        }

        [TestMethod]
        public async Task RefusesAGameFolderWithoutItsArchives()
        {
            Machine machine = NewMachine();
            MainViewModel model = machine.NewEditor();
            await model.StartAsync(machine.Settings);
            model.SetupFolder = machine.WorkspaceFolder;
            model.SetupGameFolder = machine.Vault;

            Assert.IsFalse(await model.SetUpWorkspaceAsync());

            StringAssert.Contains(model.SetupError, "env.scd");
            Assert.AreEqual(EditorStage.SetUp, model.Stage);
            Assert.IsNull(machine.Settings.Workspace);
        }

        [TestMethod]
        public async Task SetsUpConvertsAndRemembersTheWorkspace()
        {
            Machine machine = NewMachine();
            MainViewModel model = await SetUp(machine);

            Assert.AreEqual(EditorStage.Ready, model.Stage);
            Assert.AreEqual(Path.GetFullPath(machine.WorkspaceFolder), model.Workspace!.Folder);
            Assert.AreEqual(Path.GetFullPath(machine.WorkspaceFolder), machine.Settings.Workspace);
            Assert.IsTrue(model.Workspace.CheckAssets().IsCurrent);

            MainViewModel again = machine.NewEditor();
            await again.StartAsync(machine.Settings);
            Assert.AreEqual(EditorStage.Ready, again.Stage, "the next start goes straight to the editor");
        }

        [TestMethod]
        public async Task AsksToUpdateAssetsOfAnotherEditorVersion()
        {
            Machine machine = NewMachine();
            await SetUp(machine);
            string manifest = Path.Combine(machine.WorkspaceFolder, WorkspaceLayout.Assets, WorkspaceLayout.Manifest);
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace($"\"editorVersion\": \"{AssetConverter.EditorVersion}\"", "\"editorVersion\": \"0.0.1\""));

            MainViewModel model = machine.NewEditor();
            await model.StartAsync(machine.Settings, "assets/maps/HardFFA.v0001");

            Assert.AreEqual(EditorStage.UpdateAssets, model.Stage);
            StringAssert.Contains(model.AssetReasons.Single(), "0.0.1");
            Assert.IsFalse(model.HasMap, "the map waits for the assets");

            Assert.IsTrue(await model.ConvertAssetsAsync());
            Assert.AreEqual(EditorStage.Ready, model.Stage);
        }

        [TestMethod]
        public async Task ExportsIntoTheVault()
        {
            Machine machine = NewMachine();
            MainViewModel model = await SetUp(machine);
            await model.OpenAsync("assets/maps/HardFFA.v0001");

            await model.RunAsync("file.export");

            Assert.IsTrue(Directory.EnumerateDirectories(Path.Combine(machine.Vault, "maps")).Any(), model.Status);
        }
    }
}
