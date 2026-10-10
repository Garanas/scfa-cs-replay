using System.IO.Compression;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

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

            public MainViewModel NewEditor()
            {
                MainViewModel model = new MainViewModel(new NoDialogs());
                model.Setup.FindFaf = FindFaf;
                return model;
            }

            /// <summary>
            /// The setup page without the editor around it.
            /// </summary>
            public WorkspaceSetupViewModel NewSetup() =>
                new WorkspaceSetupViewModel(new NoDialogs(), NullLogger.Instance, _ => { }) { FindFaf = FindFaf };

            private FafInstallation FindFaf() => new FafInstallation(Game, Vault, null, "3839");
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
            model.Setup.Folder = machine.WorkspaceFolder;
            Assert.IsTrue(await model.Setup.SetUpAsync(), model.Setup.Error);
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
            Assert.IsFalse(await model.Commands.RunAsync("help.workspaceFolder"), "no workspace yet");
            CollectionAssert.AreEqual(Array.Empty<string>(), shown);

            model.Setup.Folder = machine.WorkspaceFolder;
            Assert.IsTrue(await model.Setup.SetUpAsync(), model.Setup.Error);
            Assert.IsTrue(await model.Commands.RunAsync("help.workspaceFolder"));

            CollectionAssert.AreEqual(new[] { model.Setup.Workspace!.Folder }, shown);
        }

        [TestMethod]
        public async Task StartsWithTheSetupFilledInFromFaf()
        {
            Machine machine = NewMachine();
            WorkspaceSetupViewModel setup = machine.NewSetup();

            await setup.StartAsync(machine.Settings);

            Assert.AreEqual(EditorStage.SetUp, setup.Stage);
            Assert.IsFalse(setup.IsReady);
            Assert.AreEqual(machine.Game, setup.GameFolder);
            Assert.AreEqual(machine.Vault, setup.VaultFolder);
        }

        [TestMethod]
        public async Task RefusesAGameFolderWithoutItsArchives()
        {
            Machine machine = NewMachine();
            WorkspaceSetupViewModel setup = machine.NewSetup();
            await setup.StartAsync(machine.Settings);
            setup.Folder = machine.WorkspaceFolder;
            setup.GameFolder = machine.Vault;

            Assert.IsFalse(await setup.SetUpAsync());

            StringAssert.Contains(setup.Error, "env.scd");
            Assert.AreEqual(EditorStage.SetUp, setup.Stage);
            Assert.IsNull(machine.Settings.Workspace);
        }

        [TestMethod]
        public async Task SetsUpConvertsAndRemembersTheWorkspace()
        {
            Machine machine = NewMachine();
            MainViewModel model = await SetUp(machine);

            Assert.AreEqual(EditorStage.Ready, model.Setup.Stage);
            Assert.AreEqual(Path.GetFullPath(machine.WorkspaceFolder), model.Setup.Workspace!.Folder);
            Assert.AreEqual(Path.GetFullPath(machine.WorkspaceFolder), machine.Settings.Workspace);
            Assert.IsTrue(model.Setup.Workspace.CheckAssets().IsCurrent);

            MainViewModel again = machine.NewEditor();
            await again.StartAsync(machine.Settings);
            Assert.AreEqual(EditorStage.Ready, again.Setup.Stage, "the next start goes straight to the editor");
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

            Assert.AreEqual(EditorStage.UpdateAssets, model.Setup.Stage);
            StringAssert.Contains(model.Setup.AssetReasons.Single(), "0.0.1");
            Assert.IsFalse(model.HasMap, "the map waits for the assets");

            Assert.IsTrue(await model.Commands.RunAsync("workspace.convert"));
            Assert.AreEqual(EditorStage.Ready, model.Setup.Stage);
            for (int i = 0; i < 100 && !model.HasMap; i++)
            {
                await Task.Delay(50);
            }
            Assert.IsTrue(model.HasMap, "and opens once they are converted");
        }

        [TestMethod]
        public async Task StartingWithoutAFolderOpensTheSandboxWhichExportsWithAWarning()
        {
            Machine machine = NewMachine();
            await SetUp(machine);
            MainViewModel model = machine.NewEditor();
            model.OpenSandboxWhenReady = true;

            await model.StartAsync(machine.Settings);
            for (int i = 0; i < 100 && !model.HasMap; i++)
            {
                await Task.Delay(50);
            }

            Assert.IsTrue(model.HasMap, model.Status);
            Assert.AreEqual(SandboxMap.FolderOf(model.Setup.Workspace!), model.Map!.ProjectFolder);
            Assert.IsFalse(model.Map.Document.IsPublishable);
            StringAssert.Contains(model.Title, "sandbox");
            StringAssert.Contains(model.Status, "sandbox");

            await model.RunAsync("file.export");
            Assert.IsTrue(Directory.EnumerateDirectories(Path.Combine(machine.Vault, "maps")).Any(), model.Status);
            StringAssert.Contains(model.Status, "cannot be uploaded");
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

        [TestMethod]
        public async Task TheWorkspaceOpensWithItsPresetsAndReadsThemAgainWhenTheyChange()
        {
            Machine machine = NewMachine();
            MainViewModel model = machine.NewEditor();
            List<string> opened = [];
            model.Events.WorkspaceOpened += workspace => opened.Add(workspace.Folder);
            await model.StartAsync(machine.Settings);
            model.Setup.Folder = machine.WorkspaceFolder;
            Assert.IsTrue(await model.Setup.SetUpAsync(), model.Setup.Error);

            CollectionAssert.AreEqual(new[] { Path.GetFullPath(machine.WorkspaceFolder) }, opened);
            Assert.AreEqual(Path.Combine(Path.GetFullPath(machine.WorkspaceFolder), WorkspaceLayout.Presets), model.Presets.Library!.Folder);
            Assert.AreEqual("brush", model.Presets.Kind);
            Assert.AreEqual(0, model.Presets.Shown.Count);

            // another tool, or another editor, writes a preset into the workspace
            model.Setup.Workspace!.Presets.Save(new Preset("brush", "Shared ridge", new System.Text.Json.Nodes.JsonObject { ["tool"] = "sculpt" }));
            for (int i = 0; i < 100 && model.Presets.Shown.Count == 0; i++)
            {
                await Task.Delay(50);
            }
            Assert.AreEqual("Shared ridge", model.Presets.Shown.Single().Name);
        }
    }
}
