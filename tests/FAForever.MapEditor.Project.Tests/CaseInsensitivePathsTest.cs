using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Files are found whatever the case of their names, as the game and Windows find them: these
    /// tests prove it on Linux (the test workflow's system), and keep it working on Windows.
    /// </summary>
    [TestClass]
    public sealed class CaseInsensitivePathsTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static string Touch(string folder, string relative, string text = "")
        {
            string path = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        [TestMethod]
        public void AFileIsFoundInTheCaseItHasOnDisk()
        {
            string folder = ProjectFolders.New("case");
            Touch(folder, "GameData/Env.SCD", "archive");

            string found = FilePaths.FindFile(folder, "gamedata/env.scd")!;

            Assert.AreEqual("archive", File.ReadAllText(found));
            if (!OperatingSystem.IsWindows())
            {
                Assert.AreEqual(Path.Combine(folder, "GameData", "Env.SCD"), found, "the name as it is on disk");
            }
            Assert.IsNull(FilePaths.FindFile(folder, "gamedata/units.scd"));
            Assert.AreEqual(File.ReadAllText(FilePaths.InFolder(folder, @"GAMEDATA\env.scd")), "archive", "either separator, any case");
        }

        [TestMethod]
        public void AFileThatIsNotThereGoesIntoTheFolderThatIs()
        {
            string folder = ProjectFolders.New("case");
            Directory.CreateDirectory(Path.Combine(folder, "Presets"));

            string path = FilePaths.Existing(Path.Combine(folder, "presets", "new.json"));

            Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(path)), path);
        }

        [TestMethod]
        public void TheGamesArchivesAreFoundInAnyCase()
        {
            string game = ProjectFolders.New("game");
            Touch(game, "GameData/Env.scd");
            Touch(game, "GameData/UNITS.SCD");
            string faf = ProjectFolders.New("faf");
            Touch(faf, "Env.NX2");

            IReadOnlyList<string> archives = GameArchives.Find(game, faf);

            Assert.AreEqual(3, archives.Count, string.Join(", ", archives));
            Assert.IsTrue(archives.All(File.Exists), string.Join(", ", archives));
            Assert.IsTrue(GameFolders.HasArchives(game));
            Assert.IsFalse(GameFolders.HasArchives(faf));
        }

        [TestMethod]
        public void AMapsFilesAreReadInAnyCase()
        {
            string folder = Path.Combine(ProjectFolders.New("maps"), "HardFFA.v0001");
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(ProjectFolders.HardFfa, "HardFFA.scmap"), Path.Combine(folder, "HardFFA.SCMAP"));
            File.Copy(Path.Combine(ProjectFolders.HardFfa, "HardFFA_scenario.lua"), Path.Combine(folder, "HardFFA_Scenario.LUA"));
            File.Copy(Path.Combine(ProjectFolders.HardFfa, "HardFFA_save.lua"), Path.Combine(folder, "HARDFFA_SAVE.lua"));

            MapDocument document = MapImporter.ImportFolder(folder);

            Assert.AreEqual(EditedMap.ImportMap(ProjectFolders.HardFfa).Document.Armies.Length, document.Armies.Length);
            Assert.AreEqual(0, document.Files.Count, "the map's files are known as its scmap, scenario and save");
        }

        [TestMethod]
        public void APresetOfTheNameInAnotherCaseIsTheSameOne()
        {
            PresetLibrary library = new PresetLibrary(ProjectFolders.New("presets"));
            library.Save(Preset.Of("water", "Deep Blue", new { level = 1 }));

            Assert.IsNotNull(library.Find("water", "deep blue"));
            library.Save(Preset.Of("water", "DEEP BLUE", new { level = 2 }));

            Assert.AreEqual(1, Directory.GetFiles(library.FolderOf("water")).Length, "one file, as on Windows");
            Assert.AreEqual("deep blue 2", library.FreeName("water", "deep blue"));
            Assert.IsTrue(library.Delete("water", "Deep blue"));
            Assert.IsNull(library.Find("water", "Deep Blue"));
        }

        [TestMethod]
        public void AFileNameKeepsToWhatWindowsAllowsOnEverySystem()
        {
            Assert.AreEqual("Hard- copy", FileNames.Clean(" Hard: copy "));
            Assert.AreEqual("a-b-c-d-e-f-g-h-i", FileNames.Clean("a\\b/c<d>e|f?g*h\"i"));
            Assert.AreEqual("", FileNames.Clean("  "));
            Assert.AreEqual("Hard- copy", MapTemplates.FileName("Hard: copy"));
            Assert.IsTrue(Path.GetFileName(new PresetLibrary(ProjectFolders.New("presets")).FileOf("water", "a:b")) == "a-b.json");
        }

        [TestMethod]
        public void TheUsersFoldersAreNeverRelative()
        {
            foreach (string folder in new[] { UserFolders.Home, UserFolders.Documents, UserFolders.Settings, UserFolders.LocalData })
            {
                Assert.IsTrue(Path.IsPathRooted(folder), folder);
            }
            Assert.IsTrue(Path.IsPathRooted(UserSettings.DefaultFile) || Environment.GetEnvironmentVariable(UserSettings.FileVariable) is { Length: > 0 });
            Assert.IsTrue(Path.IsPathRooted(FafInstallation.DefaultFolder), FafInstallation.DefaultFolder);
            Assert.IsTrue(GameFolders.Candidates().All(Path.IsPathRooted));
        }

        [TestMethod]
        public void EveryAssetAMapNamesInMixedCaseResolves()
        {
            Workspace workspace = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/vault");
            string png = Path.Combine(workspace.AssetsFolder, "env", "evergreen", "layers", "grass.png");
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            ProjectImages.WriteRgba(png, 2, 2, new byte[16]);
            MapDocument hardFfa = EditedMap.ImportMap(ProjectFolders.HardFfa).Document;
            MapDocument document = hardFfa with
            {
                Terrain = hardFfa.Terrain with
                {
                    Layers = [.. hardFfa.Terrain.Layers.Take(2).Select((layer, i) => layer with
                    {
                        AlbedoPath = i == 0 ? "/env/Evergreen/Layers/GRASS.dds" : "/env/Evergreen/Layers/Missing.dds",
                        NormalPath = "",
                    })],
                },
                Decals = [],
                Props = [],
            };

            IReadOnlyList<AssetLookup> textures = [.. AssetResolution.Resolve(document, workspace, AssetCatalog.Load(workspace)).Where(lookup => lookup.Kind == "texture")];

            Assert.AreEqual(2, textures.Count, string.Join(", ", textures));
            Assert.AreEqual(png, textures.Single(lookup => lookup.Path.Contains("GRASS", StringComparison.Ordinal)).File);
            Assert.AreEqual("not among the converted assets", textures.Single(lookup => lookup.Path.Contains("Missing", StringComparison.Ordinal)).Reason);
        }
    }
}
