using System.Text.Json.Nodes;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// One presets folder of plural kinds (user story F-10): the old kind folders and the map
    /// templates move into it once, when a workspace opens.
    /// </summary>
    [TestClass]
    public class PresetFoldersTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static void Write(string folder, string path, string text = "{}")
        {
            string file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        private static string BrushPreset(string name, string tool) =>
            new JsonObject { ["formatVersion"] = 1, ["kind"] = "brush", ["name"] = name, ["settings"] = new JsonObject { ["tool"] = tool, ["radius"] = 12 } }.ToJsonString();

        [TestMethod]
        public void TheOldFoldersAndTemplatesMoveIntoPluralKindsOnce()
        {
            string workspace = ProjectFolders.New("workspace");
            Write(workspace, "presets/brush/Raise 12.json", BrushPreset("Raise 12", "sculpt"));
            Write(workspace, "presets/brush/Crater.json", BrushPreset("Crater", "stamp"));
            Write(workspace, "presets/props-palette/Forest.json");
            Write(workspace, "presets/props/Forest.json");
            Write(workspace, "presets/selection/Village.json");
            Write(workspace, "presets/lighting/Dusk.json");
            Write(workspace, "templates/Eight.json");
            Write(workspace, "templates/Whole/map.json");
            Write(workspace, "brushes/stamps/hill.png", "");

            IReadOnlyList<PresetMove> moves = PresetFolders.Move(workspace);

            string presets = Path.Combine(workspace, "presets");
            Assert.IsTrue(File.Exists(Path.Combine(presets, "brushes", "Raise 12.json")));
            Assert.IsTrue(File.Exists(Path.Combine(presets, "stamps", "Crater.json")), "a brush preset of the stamp tool is a stamp preset");
            Assert.IsTrue(File.Exists(Path.Combine(presets, "props", "Forest.json")));
            Assert.IsTrue(File.Exists(Path.Combine(presets, "props", "Forest 2.json")), "a name the new folder holds gets a number");
            Assert.IsTrue(File.Exists(Path.Combine(presets, "selections", "Village.json")));
            Assert.IsTrue(File.Exists(Path.Combine(presets, "lighting", "Dusk.json")), "a kind of the same name stays");
            Assert.IsTrue(File.Exists(Path.Combine(presets, "maps", "Eight.json")));
            Assert.IsTrue(File.Exists(Path.Combine(presets, "maps", "Whole", "map.json")), "a whole map's template moves as a folder");
            Assert.IsTrue(File.Exists(Path.Combine(workspace, "brushes", "stamps", "hill.png")), "a stamp's picture is a brush, not a preset");
            foreach (string old in new[] { "presets/brush", "presets/props-palette", "presets/selection", "templates" })
            {
                Assert.IsFalse(Directory.Exists(Path.Combine(workspace, old)), $"{old} is gone");
            }
            Assert.AreEqual(7, moves.Count, string.Join("; ", moves));
            Assert.AreEqual(0, PresetFolders.Move(workspace).Count, "once: nothing is left to move");
        }

        [TestMethod]
        public void AWorkspaceKeepsItsTemplatesAndPresetsInOneFolder()
        {
            Workspace workspace = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/vault");

            Assert.AreEqual(Path.Combine(workspace.PresetsFolder, "maps"), workspace.TemplatesFolder);
            Assert.IsFalse(Directory.Exists(Path.Combine(workspace.Folder, "templates")), "a new workspace has no templates folder of its own");
            Assert.AreEqual("brushes", PresetKinds.Of("brush"));
            Assert.AreEqual("props", PresetKinds.Of("props-palette"));
            Assert.AreEqual("selections", PresetKinds.Of("selection"));
            Assert.AreEqual("lighting", PresetKinds.Of("lighting"));
            CollectionAssert.AreEqual(new[] { "brushes", "stamps", "lighting", "water", "sky", "props", "units", "palettes", "markers", "selections", "maps" }, PresetKinds.All.ToArray());
        }
    }
}
