using FAForever.MapEditor.Project.Workspaces;
using FAForever.MapEditor.Project.Workspaces.Serialized;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public sealed class WorkspaceTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        [TestMethod]
        public void CreatesTheFoldersAndKeepsTheAssetsOutOfGit()
        {
            string folder = ProjectFolders.New("workspace");
            Workspace workspace = Workspace.Create(folder, "C:/game", null, "D:/faf-vault");

            Assert.IsTrue(Workspace.Exists(folder));
            Assert.IsTrue(Directory.Exists(workspace.ProjectsFolder));
            Assert.IsTrue(Directory.Exists(Path.Combine(folder, WorkspaceLayout.Brushes)));
            StringAssert.Contains(File.ReadAllText(Path.Combine(folder, WorkspaceLayout.GitIgnore)), "/assets/");
            Assert.AreEqual(Path.Combine(Path.GetFullPath("D:/faf-vault"), "maps"), workspace.MapsFolder);
            Assert.AreEqual(workspace, Workspace.Open(folder));
        }

        [TestMethod]
        public void AScratchWorkspaceLinksTheAssetsAndKeepsItsOwnFolders()
        {
            Workspace source = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/faf-vault");
            string texture = Path.Combine(source.AssetsFolder, "env", "evergreen", "layers", "eg_grass001_albedo.png");
            Directory.CreateDirectory(Path.GetDirectoryName(texture)!);
            File.WriteAllBytes(texture, [1, 2, 3]);
            Directory.CreateDirectory(source.BrushesFolder);
            File.WriteAllBytes(Path.Combine(source.BrushesFolder, "ridge.png"), [4]);

            (Workspace scratch, ScratchWorkspace.AssetLink link) = ScratchWorkspace.Create(ProjectFolders.New("scratch"), source);

            Assert.AreEqual(OperatingSystem.IsWindows() ? ScratchWorkspace.AssetLink.Junction : ScratchWorkspace.AssetLink.SymbolicLink, link);
            Assert.IsTrue(File.Exists(Path.Combine(scratch.AssetsFolder, "env", "evergreen", "layers", "eg_grass001_albedo.png")), "the assets show through the link");
            Assert.IsTrue(File.Exists(Path.Combine(scratch.BrushesFolder, "ridge.png")), "the brushes are copied");
            Assert.AreEqual(Path.Combine(scratch.Folder, "vault"), scratch.VaultFolder, "exports stay inside");
            Assert.AreEqual(source.GameFolder, scratch.GameFolder);
            Assert.AreEqual("/env/evergreen/layers/eg_grass001_albedo.dds", EditedMap.OpenProject(SandboxMap.FolderOf(scratch)).Document.Terrain.Layers[0].AlbedoPath,
                "its sandbox is made of the linked assets");

            File.WriteAllBytes(Path.Combine(scratch.BrushesFolder, "mine.png"), [5]);
            Assert.IsFalse(File.Exists(Path.Combine(source.BrushesFolder, "mine.png")), "a brush saved there stays there");
            ScratchWorkspace.Delete(scratch.Folder);
            Assert.IsFalse(Directory.Exists(scratch.Folder));
            Assert.IsTrue(File.Exists(texture), "deleting the scratch workspace leaves the assets alone");
        }

        [TestMethod]
        public void AScratchWorkspaceCanHaveAssetsOfItsOwnToConvertInto()
        {
            Workspace source = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/faf-vault");
            string texture = Path.Combine(source.AssetsFolder, "env", "grass.png");
            Directory.CreateDirectory(Path.GetDirectoryName(texture)!);
            File.WriteAllBytes(texture, [1, 2, 3]);
            string into = Path.Combine(ProjectFolders.New("converted"), "assets");

            (Workspace scratch, ScratchWorkspace.AssetLink link) = ScratchWorkspace.Create(ProjectFolders.New("scratch"), source, into);

            Assert.AreEqual(OperatingSystem.IsWindows() ? ScratchWorkspace.AssetLink.Junction : ScratchWorkspace.AssetLink.SymbolicLink, link);
            Assert.IsTrue(Directory.Exists(into), "the folder is made");
            Assert.IsFalse(File.Exists(Path.Combine(scratch.AssetsFolder, "env", "grass.png")), "the source's assets are not linked");
            File.WriteAllBytes(Path.Combine(scratch.AssetsFolder, "converted.png"), [4]);
            Assert.IsTrue(File.Exists(Path.Combine(into, "converted.png")), "what is converted lands in the folder of its own");
            Assert.IsFalse(File.Exists(Path.Combine(source.AssetsFolder, "converted.png")));
            Assert.ThrowsException<MapProjectException>(() => ScratchWorkspace.Create(ProjectFolders.New("scratch"), source, source.AssetsFolder),
                "never the source's own assets");
            ScratchWorkspace.Delete(scratch.Folder);
            Assert.IsTrue(File.Exists(Path.Combine(into, "converted.png")), "deleting the scratch workspace leaves them");
        }

        [TestMethod]
        public void RefusesAFolderWithOtherFiles()
        {
            string folder = ProjectFolders.New("workspace");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");

            Assert.ThrowsException<MapProjectException>(() => Workspace.Create(folder, "C:/game", null, "D:/vault"));
        }

        [TestMethod]
        public void SettingUpAgainKeepsTheProjects()
        {
            string folder = ProjectFolders.New("workspace");
            Workspace workspace = Workspace.Create(folder, "C:/game", null, "D:/vault");
            string project = Path.Combine(workspace.ProjectsFolder, "mine", "map.json");
            Directory.CreateDirectory(Path.GetDirectoryName(project)!);
            File.WriteAllText(project, "{}");

            Workspace again = Workspace.Create(folder, "C:/game", "C:/faf", "E:/vault");

            Assert.IsTrue(File.Exists(project));
            Assert.AreEqual(Path.GetFullPath("E:/vault"), Workspace.Open(folder).VaultFolder);
            Assert.AreEqual(Path.GetFullPath("C:/faf"), again.FafDataFolder);
        }

        [TestMethod]
        public void FindsAssetsByTheGamesPath()
        {
            Workspace workspace = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/vault");
            string png = Path.Combine(workspace.AssetsFolder, "env", "evergreen", "layers", "eg_grass001_albedo.png");
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            File.WriteAllText(png, "");

            Assert.AreEqual(png, workspace.FindAsset("/env/Evergreen/layers/Eg_Grass001_albedo.dds"));
            Assert.IsNull(workspace.FindAsset("/env/Evergreen/layers/missing.dds"));
        }

        [TestMethod]
        public void ReadsFafsPathFile()
        {
            string folder = ProjectFolders.New("faforever");
            Directory.CreateDirectory(Path.Combine(folder, "gamedata"));
            File.WriteAllText(Path.Combine(folder, FafInstallation.PathFile), """
                fa_path = "D:/SteamLibrary/steamapps/common/Supreme Commander Forged Alliance"
                custom_vault_path = "D:/faf-vault"
                GameType = "faf"
                GameVersion = "3839"
                """);

            FafInstallation faf = FafInstallation.Find(folder)!;

            Assert.AreEqual(Path.GetFullPath("D:/SteamLibrary/steamapps/common/Supreme Commander Forged Alliance"), faf.GameFolder);
            Assert.AreEqual(Path.GetFullPath("D:/faf-vault"), faf.VaultFolder);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(folder, "gamedata")), faf.DataFolder);
            Assert.AreEqual("3839", faf.GameVersion);
            Assert.IsNull(FafInstallation.Find(ProjectFolders.New("nothing")));
        }

        [TestMethod]
        public void RemembersTheWorkspace()
        {
            UserSettings settings = new UserSettings(Path.Combine(ProjectFolders.New("settings"), "settings.json"));
            Assert.IsNull(settings.Workspace);

            settings.SetWorkspace("D:/maps/workspace");

            Assert.AreEqual(Path.GetFullPath("D:/maps/workspace"), settings.Workspace);
        }

        [TestMethod]
        public void RemembersWhetherResultsRegenerateOnTheirOwn()
        {
            UserSettings settings = new UserSettings(Path.Combine(ProjectFolders.New("settings"), "settings.json"));
            Assert.IsTrue(settings.RegenerateOnTheirOwn, "on until the user turns it off");
            settings.SetWorkspace("D:/maps/workspace");

            settings.SetRegenerateOnTheirOwn(false);

            Assert.IsFalse(settings.RegenerateOnTheirOwn);
            Assert.AreEqual(Path.GetFullPath("D:/maps/workspace"), settings.Workspace, "the rest stays");
            settings.SetRegenerateOnTheirOwn(true);
            Assert.IsTrue(settings.RegenerateOnTheirOwn);
            Assert.IsFalse(File.ReadAllText(settings.File).Contains("generation", StringComparison.OrdinalIgnoreCase), "the default writes nothing");
        }

        [TestMethod]
        public void RemembersTheChoicesAboutDiagnosticsBesideTheWorkspace()
        {
            UserSettings settings = new UserSettings(Path.Combine(ProjectFolders.New("settings"), "settings.json"));

            TelemetrySettings first = settings.Telemetry;
            Assert.IsTrue(first.Record, "recorded on disk unless the user turns it off");
            Assert.IsFalse(first.Send, "sent only when the user turns it on");
            Assert.AreEqual(32, first.InstallId.Length);
            Assert.AreEqual(first.InstallId, settings.Telemetry.InstallId, "the install id is remembered");

            settings.SetWorkspace("D:/maps/workspace");
            settings.SetTelemetry(first with { Send = true, Endpoint = " http://localhost:4318 " });
            settings.SetWorkspace("D:/maps/other");

            Assert.AreEqual(first with { Send = true, Endpoint = "http://localhost:4318" }, settings.Telemetry);
            Assert.AreEqual(Path.GetFullPath("D:/maps/other"), settings.Workspace);
        }

        [TestMethod]
        [DataRow("""{ "workspace": "D:/maps/workspace", "telemetry": { "record": true, "send": false } }""")]
        [DataRow("""{ "workspace": "D:/maps/workspace", "telemetry": { } }""")]
        [DataRow("""{ "workspace": "D:/maps/workspace", "telemetry": { "installId": null, "endpoint": null } }""")]
        [DataRow("""{ "workspace": "D:/maps/workspace", "telemetry": null, "layout": { "modePanel": false } }""")]
        [DataRow("""{ "workspace": "D:/maps/workspace" }""")]
        public void ASettingsFileWithoutAnInstallIdIsReadAsItIs(string text)
        {
            UserSettings settings = new UserSettings(Path.Combine(ProjectFolders.New("settings"), "settings.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(settings.File)!);
            File.WriteAllText(settings.File, text);

            Assert.AreEqual("D:/maps/workspace", settings.Workspace);
            TelemetrySettings telemetry = settings.Telemetry;
            Assert.AreEqual(32, telemetry.InstallId.Length);
            Assert.AreEqual("D:/maps/workspace", settings.Workspace, "giving it an install id keeps the rest");
            Assert.AreEqual(telemetry.InstallId, settings.Telemetry.InstallId);
        }

        [TestMethod]
        public void APartOfTheSettingsThatCannotBeReadLosesOnlyItself()
        {
            UserSettings settings = new UserSettings(Path.Combine(ProjectFolders.New("settings"), "settings.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(settings.File)!);
            File.WriteAllText(settings.File, """{ "workspace": "D:/maps/workspace", "layout": { "modePanel": "no" }, "telemetry": { "send": true } }""");

            Assert.AreEqual("D:/maps/workspace", settings.Workspace);
            StringAssert.Contains(settings.Problem, "layout");
            Assert.IsTrue(settings.Layout.ModePanel, "the part that cannot be read is the default");
            Assert.IsTrue(settings.Telemetry.Send, "the other parts are read");
        }

        [TestMethod]
        public void ASettingsFileThatIsNotJsonIsNeverWrittenOver()
        {
            UserSettings settings = new UserSettings(Path.Combine(ProjectFolders.New("settings"), "settings.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(settings.File)!);
            File.WriteAllText(settings.File, "{ not json");

            Assert.ThrowsException<MapProjectException>(() => settings.SetLayout(new LayoutSettingsDto { Dock = false }));
            Assert.AreEqual("{ not json", File.ReadAllText(settings.File));
            Assert.AreEqual(32, settings.Telemetry.InstallId.Length, "an install id for this session all the same");
            Assert.AreEqual("{ not json", File.ReadAllText(settings.File));
        }

        [TestMethod]
        public void AnUnreadableSettingsFileRemembersNothing()
        {
            UserSettings settings = new UserSettings(Path.Combine(ProjectFolders.New("settings"), "settings.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(settings.File)!);
            File.WriteAllText(settings.File, "{ not json");

            Assert.IsNull(settings.Workspace);
        }
    }
}
