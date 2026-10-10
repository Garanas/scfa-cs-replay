using System.Text.Json.Nodes;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class PresetLibraryTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private enum Mode
        {
            Raise,
            Flatten,
        }

        private sealed record BrushSettings(Mode Mode, double Radius, string? Shape = null);

        [TestMethod]
        public void SavesListsReadsAndDeletesPresetsOfAKind()
        {
            Workspace workspace = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/vault");
            Assert.IsTrue(Directory.Exists(workspace.PresetsFolder), "a new workspace has the folder");
            PresetLibrary presets = workspace.Presets;

            string file = presets.Save(Preset.Of("brushes", "Big crater", new BrushSettings(Mode.Flatten, 40, "craters/crater01.png")));
            presets.Save(Preset.Of("brushes", "a small hill", new BrushSettings(Mode.Raise, 6)));
            presets.Save(new Preset("lighting", "Evergreen dusk", new JsonObject { ["sunColor"] = "#ffcc8844" }));

            Assert.AreEqual(Path.Combine(workspace.PresetsFolder, "brushes", "Big crater.json"), file);
            StringAssert.Contains(File.ReadAllText(file), "\"mode\": \"Flatten\"", "the editor's JSON: camel case, enums as text");
            CollectionAssert.AreEqual(new[] { "brushes", "lighting", "units" }, presets.Kinds().ToArray(), "units: the example unit presets of a new workspace");
            CollectionAssert.AreEqual(new[] { "a small hill", "Big crater" }, presets.List("brushes").Select(preset => preset.Name).ToArray());
            Assert.AreEqual(new BrushSettings(Mode.Flatten, 40, "craters/crater01.png"), presets.Find("brushes", "Big crater")!.Read<BrushSettings>());
            Assert.IsNull(presets.Find("brushes", "nothing"));
            Assert.AreEqual(0, presets.List("water").Count);

            presets.Save(Preset.Of("brushes", "Big crater", new BrushSettings(Mode.Raise, 41)));
            Assert.AreEqual(41, presets.Find("brushes", "Big crater")!.Read<BrushSettings>().Radius, "saved again under its name");
            Assert.AreEqual("Big crater 2", presets.FreeName("brushes", "Big crater"));
            Assert.AreEqual("Water", presets.FreeName("brushes", "Water"));

            Assert.IsTrue(presets.Delete("brushes", "Big crater"));
            Assert.IsFalse(presets.Delete("brushes", "Big crater"));
            Assert.AreEqual(1, presets.List("brushes").Count);
            Assert.AreEqual(1, presets.List("brush").Count, "an old kind name means its new folder");
            Assert.AreEqual(Path.Combine(workspace.PresetsFolder, "brushes"), presets.FolderOf("brush"));
        }

        [TestMethod]
        public void NamesThatAreNoFileNamesStillWork()
        {
            PresetLibrary presets = new PresetLibrary(ProjectFolders.New("presets"));

            string file = presets.Save(Preset.Of("brush", "Ridge: 50/50?", new BrushSettings(Mode.Raise, 10)));

            Assert.AreEqual("Ridge- 50-50-.json", Path.GetFileName(file));
            Assert.AreEqual("Ridge: 50/50?", presets.List("brush").Single().Name, "the name is the file's, not its file name");
            Assert.IsNotNull(presets.Find("brush", "Ridge: 50/50?"));
            Assert.ThrowsException<ArgumentException>(() => presets.Save(Preset.Of("Brush", "x", new BrushSettings(Mode.Raise, 1))), "kinds are lower case");
            Assert.ThrowsException<ArgumentException>(() => presets.Save(Preset.Of("../brush", "x", new BrushSettings(Mode.Raise, 1))));
            Assert.ThrowsException<ArgumentException>(() => presets.Save(Preset.Of("brush", " ", new BrushSettings(Mode.Raise, 1))));
        }

        [TestMethod]
        public void LeavesOutAFileItCannotRead()
        {
            PresetLibrary presets = new PresetLibrary(ProjectFolders.New("presets"));
            presets.Save(Preset.Of("brush", "Good", new BrushSettings(Mode.Raise, 10)));
            File.WriteAllText(Path.Combine(presets.Folder, "brushes", "broken.json"), "{ not json");
            List<string> unreadable = [];

            IReadOnlyList<Preset> listed = presets.List("brush", unreadable);

            Assert.AreEqual("Good", listed.Single().Name);
            StringAssert.Contains(unreadable.Single(), "broken.json");
            Assert.ThrowsException<MapProjectException>(() => new Preset("brush", "Wrong", new JsonObject { ["radius"] = "far" }).Read<BrushSettings>());
        }
    }
}
