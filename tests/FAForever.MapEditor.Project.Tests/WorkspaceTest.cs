using FAForever.MapEditor.Project.Workspaces;

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
        public void AnUnreadableSettingsFileRemembersNothing()
        {
            UserSettings settings = new UserSettings(Path.Combine(ProjectFolders.New("settings"), "settings.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(settings.File)!);
            File.WriteAllText(settings.File, "{ not json");

            Assert.IsNull(settings.Workspace);
        }
    }
}
